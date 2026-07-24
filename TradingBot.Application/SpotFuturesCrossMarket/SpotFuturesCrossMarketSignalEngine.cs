using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.SpotFuturesCrossMarket;

/// <summary>
/// Multi-timeframe, cross-market trend-following engine. The higher timeframe determines
/// direction; the execution timeframe supplies a pullback/reclaim or range breakout; spot
/// volume, futures taker flow and live futures microstructure must confirm the entry.
/// </summary>
public sealed class SpotFuturesCrossMarketSignalEngine(
    ITrendStateService trendStateService,
    IAtrService atrService,
    ILogger<SpotFuturesCrossMarketSignalEngine> logger)
{
    public CrossMarketDecision Evaluate(
        SpotFuturesCrossMarketSettings settings,
        CrossMarketSnapshot snapshot,
        OrderSide? openPositionSide)
    {
        if (!snapshot.MarketsInSync || snapshot.Spot is null || snapshot.Futures is null ||
            snapshot.RegimeSpot is null || snapshot.RegimeFutures is null)
        {
            return NoTrade($"MarketsOutOfSync: {snapshot.SyncIssue ?? "missing synchronized execution/regime data"}");
        }

        var spotTrend = trendStateService.Analyze(snapshot.Spot, settings.ShortMaPeriod, settings.LongMaPeriod);
        var futuresTrend = trendStateService.Analyze(snapshot.Futures, settings.ShortMaPeriod, settings.LongMaPeriod);
        if (!spotTrend.IsValid || !futuresTrend.IsValid)
            return NoTrade($"TrendUnavailable(spotValid={spotTrend.IsValid}, futuresValid={futuresTrend.IsValid})");

        var spotMomentumPercent = ChangePercent(snapshot.Spot.ClosePrices, settings.MomentumLookbackCandles);
        var futuresAtrPercent = atrService.Calculate(
            snapshot.Futures.HighPrices,
            snapshot.Futures.LowPrices,
            snapshot.Futures.ClosePrices,
            normalize: true,
            currentPrice: snapshot.FuturesClose) * 100m;

        CrossMarketDecision Build(
            CrossMarketAction action,
            string reason,
            decimal expectedMovePercent = 0m,
            decimal? stopLoss = null,
            decimal? takeProfit = null) => new()
            {
                Action = action,
                Reason = reason,
                SpotTrendState = spotTrend.CurrentTrendState,
                SpotTrendConfidenceScore = spotTrend.ConfidenceScore,
                SpotShortMaSlopePercent = spotTrend.ShortMaSlopePercent,
                SpotTrendStrengthPercent = spotTrend.TrendStrengthPercent,
                SpotMomentumPercent = spotMomentumPercent,
                FuturesTrendState = futuresTrend.CurrentTrendState,
                FuturesTrendConfidenceScore = futuresTrend.ConfidenceScore,
                FuturesShortMaSlopePercent = futuresTrend.ShortMaSlopePercent,
                FuturesTrendStrengthPercent = futuresTrend.TrendStrengthPercent,
                FuturesAtrPercent = futuresAtrPercent,
                ExpectedMovePercent = expectedMovePercent,
                StopLossPrice = stopLoss,
                TakeProfitPrice = takeProfit
            };

        // Position exits deliberately need agreement from both markets or a full higher-
        // timeframe reversal. The event-driven rolling worker handles fast profit/loss exits.
        if (openPositionSide is not null)
        {
            var exit = EvaluateSignalExit(settings, snapshot, spotTrend, futuresTrend, openPositionSide.Value);
            if (exit is null)
            {
                var side = openPositionSide == OrderSide.BUY ? "Long" : "Short";
                return Build(CrossMarketAction.NoTrade, $"Hold{side}: trend structure remains valid.");
            }

            return Build(
                openPositionSide == OrderSide.BUY ? CrossMarketAction.CloseLong : CrossMarketAction.CloseShort,
                exit);
        }

        if (futuresAtrPercent <= 0m)
            return Build(CrossMarketAction.NoTrade, "AtrUnavailable: cannot build a price-anchored risk plan.");

        var longSetup = EvaluateEntrySetup(settings, snapshot, isLong: true, futuresAtrPercent);
        var shortSetup = EvaluateEntrySetup(settings, snapshot, isLong: false, futuresAtrPercent);
        var setup = longSetup.Allowed ? longSetup : shortSetup.Allowed ? shortSetup : null;
        if (setup is null && settings.EnableTestnetEvidenceEntries)
        {
            setup = BuildTestnetEvidenceEntry(settings, snapshot, longSetup.Reason, shortSetup.Reason);
        }

        if (setup is null)
        {
            var decision = Build(CrossMarketAction.NoTrade,
                $"NoQualifiedTrendEntry: long[{longSetup.Reason}] short[{shortSetup.Reason}]");
            logger.LogDebug(
                "SpotFutures no entry {Symbol}. Long={LongReason} Short={ShortReason}",
                snapshot.Symbol,
                longSetup.Reason,
                shortSetup.Reason);
            return decision;
        }

        var maxBasisPercent = setup.IsTestnetEvidence
            ? settings.TestnetEvidenceMaxAbsBasisPercent
            : settings.MaxAbsBasisPercentForEntry;
        if (Math.Abs(snapshot.BasisPercent) > maxBasisPercent)
        {
            return Build(CrossMarketAction.NoTrade,
                $"BasisDislocation: |{snapshot.BasisPercent:F3}%| > {maxBasisPercent:F3}% " +
                $"(mode={(setup.IsTestnetEvidence ? "testnet-evidence" : "strategy")})");
        }

        if (snapshot.FundingRate is not null)
        {
            if (setup.IsLong && snapshot.FundingRate.Value > settings.MaxAbsFundingRateForEntry)
                return Build(CrossMarketAction.NoTrade, $"FundingTooExpensiveForLong({snapshot.FundingRate.Value:F6})");
            if (!setup.IsLong && snapshot.FundingRate.Value < -settings.MaxAbsFundingRateForEntry)
                return Build(CrossMarketAction.NoTrade, $"FundingTooExpensiveForShort({snapshot.FundingRate.Value:F6})");
        }

        var entryPrice = snapshot.MarkPrice is > 0m ? snapshot.MarkPrice.Value : snapshot.FuturesClose;
        var atrPrice = snapshot.FuturesClose * futuresAtrPercent / 100m;
        var structurePrice = setup.IsLong
            ? snapshot.Futures.LowPrices.TakeLast(settings.EntryPullbackLookbackCandles + 2).Min()
            : snapshot.Futures.HighPrices.TakeLast(settings.EntryPullbackLookbackCandles + 2).Max();
        var atrRisk = atrPrice * settings.AtrStopMultiplier;
        var structureRisk = setup.IsLong
            ? entryPrice - structurePrice + atrPrice * 0.10m
            : structurePrice - entryPrice + atrPrice * 0.10m;
        var rawRisk = Math.Max(atrRisk, structureRisk);
        var minRisk = entryPrice * settings.MinStopPercent / 100m;
        var maxRisk = entryPrice * settings.MaxStopPercent / 100m;
        var riskDistance = Math.Clamp(rawRisk, minRisk, maxRisk);
        var stopPercent = riskDistance / entryPrice * 100m;
        var targetPercent = stopPercent * settings.MinRewardRiskRatio;
        var netExpectedMovePercent = targetPercent - settings.FeeAndSpreadPercent;

        if (netExpectedMovePercent < settings.MinNetExpectedMovePercent)
        {
            return Build(CrossMarketAction.NoTrade,
                $"ExpectedMoveTooSmall: target={targetPercent:F3}% costs={settings.FeeAndSpreadPercent:F3}% net={netExpectedMovePercent:F3}%");
        }

        var stopLoss = setup.IsLong ? entryPrice - riskDistance : entryPrice + riskDistance;
        var takeProfitDistance = riskDistance * settings.MinRewardRiskRatio;
        var takeProfit = setup.IsLong ? entryPrice + takeProfitDistance : entryPrice - takeProfitDistance;
        var action = setup.IsLong ? CrossMarketAction.OpenLong : CrossMarketAction.OpenShort;
        var reason = $"{action}: {setup.Reason}; risk={stopPercent:F3}%, target={targetPercent:F3}% ({settings.MinRewardRiskRatio:F2}R), netEdge={netExpectedMovePercent:F3}%.";

        return Build(action, reason, targetPercent, stopLoss, takeProfit);

        CrossMarketDecision NoTrade(string reason) => new() { Action = CrossMarketAction.NoTrade, Reason = reason };
    }

    private static EntrySetup EvaluateEntrySetup(
        SpotFuturesCrossMarketSettings settings,
        CrossMarketSnapshot snapshot,
        bool isLong,
        decimal futuresAtrPercent)
    {
        var spot = snapshot.Spot!;
        var futures = snapshot.Futures!;
        var regimeSpot = snapshot.RegimeSpot!;
        var regimeFutures = snapshot.RegimeFutures!;

        var regimeSpotFast = EmaSeries(regimeSpot.ClosePrices, settings.RegimeShortMaPeriod);
        var regimeSpotSlow = EmaSeries(regimeSpot.ClosePrices, settings.RegimeLongMaPeriod);
        var regimeFuturesFast = EmaSeries(regimeFutures.ClosePrices, settings.RegimeShortMaPeriod);
        var regimeFuturesSlow = EmaSeries(regimeFutures.ClosePrices, settings.RegimeLongMaPeriod);
        var executionSpotFast = EmaSeries(spot.ClosePrices, settings.ShortMaPeriod);
        var executionSpotSlow = EmaSeries(spot.ClosePrices, settings.LongMaPeriod);
        var executionFuturesFast = EmaSeries(futures.ClosePrices, settings.ShortMaPeriod);
        var executionFuturesSlow = EmaSeries(futures.ClosePrices, settings.LongMaPeriod);

        if (new[] { regimeSpotFast.Count, regimeSpotSlow.Count, regimeFuturesFast.Count, regimeFuturesSlow.Count,
                    executionSpotFast.Count, executionSpotSlow.Count, executionFuturesFast.Count, executionFuturesSlow.Count }.Any(x => x == 0))
        {
            return Reject(isLong, "indicator warmup incomplete");
        }

        var direction = isLong ? 1m : -1m;
        var regimeSpotAdx = CalculateAdx(regimeSpot, settings.RsiPeriod);
        var regimeFuturesAdx = CalculateAdx(regimeFutures, settings.RsiPeriod);
        var regimeAligned = DirectionalGreater(regimeSpotFast[^1], regimeSpotSlow[^1], isLong) &&
                            DirectionalGreater(regimeFuturesFast[^1], regimeFuturesSlow[^1], isLong) &&
                            (regimeSpotFast[^1] - regimeSpotFast[^4]) * direction > 0m &&
                            (regimeFuturesFast[^1] - regimeFuturesFast[^4]) * direction > 0m;
        if (!regimeAligned)
            return Reject(isLong, $"{settings.RegimeInterval} spot/futures EMA regime not aligned");
        if (Math.Min(regimeSpotAdx, regimeFuturesAdx) < settings.MinRegimeAdx)
            return Reject(isLong, $"regime ADX weak spot={regimeSpotAdx:F1} futures={regimeFuturesAdx:F1} < {settings.MinRegimeAdx:F1}");

        var executionAligned = DirectionalGreater(executionSpotFast[^1], executionSpotSlow[^1], isLong) &&
                               DirectionalGreater(executionFuturesFast[^1], executionFuturesSlow[^1], isLong) &&
                               (spot.ClosePrices[^1] - executionSpotSlow[^1]) * direction > 0m &&
                               (futures.ClosePrices[^1] - executionFuturesSlow[^1]) * direction > 0m;
        if (!executionAligned)
            return Reject(isLong, $"{settings.Interval} spot/futures execution trend not aligned");

        var rsi = CalculateRsi(spot.ClosePrices, settings.RsiPeriod);
        var rsiAllowed = isLong
            ? rsi >= settings.LongRsiMin && rsi <= settings.LongRsiMax
            : rsi >= settings.ShortRsiMin && rsi <= settings.ShortRsiMax;
        if (!rsiAllowed)
            return Reject(isLong, $"RSI {rsi:F1} outside {(isLong ? $"{settings.LongRsiMin:F0}-{settings.LongRsiMax:F0}" : $"{settings.ShortRsiMin:F0}-{settings.ShortRsiMax:F0}")}");

        var spotVolumeRatio = LatestVolumeRatio(spot.Volumes);
        var futuresVolumeRatio = LatestVolumeRatio(futures.Volumes);
        if (Math.Min(spotVolumeRatio, futuresVolumeRatio) < settings.MinEntryVolumeRatio)
            return Reject(isLong, $"volume not participating spot={spotVolumeRatio:F2}x futures={futuresVolumeRatio:F2}x");

        var takerBuyRatio = futures.Volumes[^1] > 0m && futures.TakerBuyBaseVolumes.Count == futures.Volumes.Count
            ? futures.TakerBuyBaseVolumes[^1] / futures.Volumes[^1]
            : 0.5m;
        if (isLong && takerBuyRatio < settings.MinLongTakerBuyRatio)
            return Reject(isLong, $"futures taker-buy ratio {takerBuyRatio:F3} < {settings.MinLongTakerBuyRatio:F3}");
        if (!isLong && takerBuyRatio > settings.MaxShortTakerBuyRatio)
            return Reject(isLong, $"futures taker-buy ratio {takerBuyRatio:F3} > {settings.MaxShortTakerBuyRatio:F3}");

        var latest = spot.ClosePrices.Count - 1;
        var previousHigh = spot.HighPrices[^2];
        var previousLow = spot.LowPrices[^2];
        var candleConfirms = spot.OpenPrices.Count == spot.ClosePrices.Count && futures.OpenPrices.Count == futures.ClosePrices.Count &&
                             (spot.ClosePrices[^1] - spot.OpenPrices[^1]) * direction > 0m &&
                             (futures.ClosePrices[^1] - futures.OpenPrices[^1]) * direction > 0m;
        if (settings.RequireEntryClosedCandleDirectionConfirmation && !candleConfirms)
            return Reject(isLong, "latest closed spot/futures candles do not confirm direction");

        var pullbackStart = Math.Max(0, latest - settings.EntryPullbackLookbackCandles);
        var pulledBack = false;
        for (var i = pullbackStart; i < latest; i++)
        {
            if (isLong && spot.LowPrices[i] <= executionSpotFast[i] * 1.001m)
                pulledBack = true;
            if (!isLong && spot.HighPrices[i] >= executionSpotFast[i] * 0.999m)
                pulledBack = true;
        }

        var reclaimed = isLong
            ? spot.ClosePrices[^1] > previousHigh && futures.ClosePrices[^1] > futures.ClosePrices[^2]
            : spot.ClosePrices[^1] < previousLow && futures.ClosePrices[^1] < futures.ClosePrices[^2];

        var breakoutLookback = Math.Min(settings.EntryBreakoutLookbackCandles, latest);
        var priorExtreme = isLong
            ? spot.HighPrices.Skip(latest - breakoutLookback).Take(breakoutLookback).Max()
            : spot.LowPrices.Skip(latest - breakoutLookback).Take(breakoutLookback).Min();
        var breakout = isLong ? spot.ClosePrices[^1] > priorExtreme : spot.ClosePrices[^1] < priorExtreme;
        var trigger = breakout ? "breakout" : pulledBack && reclaimed ? "pullback-reclaim" : null;
        if (trigger is null)
            return Reject(isLong, "waiting for pullback-reclaim or confirmed range breakout");

        var atrPrice = snapshot.FuturesClose * futuresAtrPercent / 100m;
        var extension = atrPrice > 0m
            ? Math.Abs(futures.ClosePrices[^1] - executionFuturesFast[^1]) / atrPrice
            : decimal.MaxValue;
        if (extension > settings.MaxEntryExtensionAtr)
            return Reject(isLong, $"entry extended {extension:F2} ATR from fast EMA > {settings.MaxEntryExtensionAtr:F2}");

        var micro = snapshot.Microstructure;
        var microScore = CalculateMicrostructureScore(micro, isLong);
        if (settings.RequireMicrostructureConfirmation)
        {
            if (micro is null || !micro.IsFresh)
                return Reject(isLong, $"live microstructure unavailable ({micro?.DegradedReason ?? "not subscribed"})");
            if (micro.SpreadBps > settings.MaxEntrySpreadBps)
                return Reject(isLong, $"spread {micro.SpreadBps:F2}bps > {settings.MaxEntrySpreadBps:F2}bps");
            if (microScore < settings.MinEntryMicrostructureScore)
                return Reject(isLong, $"microstructure score {microScore:F1} < {settings.MinEntryMicrostructureScore:F1}");
        }

        return new EntrySetup(
            true,
            isLong,
            $"{settings.RegimeInterval} regime confirmed (ADX spot={regimeSpotAdx:F1}, futures={regimeFuturesAdx:F1}); " +
            $"{trigger}; RSI={rsi:F1}; volume spot={spotVolumeRatio:F2}x/futures={futuresVolumeRatio:F2}x; " +
            $"takerBuy={takerBuyRatio:F3}; extension={extension:F2}ATR; micro={microScore:F1}");
    }

    private static string? EvaluateSignalExit(
        SpotFuturesCrossMarketSettings settings,
        CrossMarketSnapshot snapshot,
        TrendAnalysisResult spotTrend,
        TrendAnalysisResult futuresTrend,
        OrderSide side)
    {
        var isLong = side == OrderSide.BUY;
        var opposingSpot = isLong ? spotTrend.IsBearishTrendConfirmed : spotTrend.IsBullishTrendConfirmed;
        var opposingFutures = isLong ? futuresTrend.IsBearishTrendConfirmed : futuresTrend.IsBullishTrendConfirmed;
        if (opposingSpot && opposingFutures &&
            Math.Min(spotTrend.ConfidenceScore, futuresTrend.ConfidenceScore) >= settings.MinExitTrendConfidenceScore)
        {
            return $"Close{(isLong ? "Long" : "Short")}: both execution markets confirmed the opposing trend.";
        }

        var regimeSpotFast = EmaSeries(snapshot.RegimeSpot!.ClosePrices, settings.RegimeShortMaPeriod);
        var regimeSpotSlow = EmaSeries(snapshot.RegimeSpot.ClosePrices, settings.RegimeLongMaPeriod);
        var regimeFuturesFast = EmaSeries(snapshot.RegimeFutures!.ClosePrices, settings.RegimeShortMaPeriod);
        var regimeFuturesSlow = EmaSeries(snapshot.RegimeFutures.ClosePrices, settings.RegimeLongMaPeriod);
        var regimeReversed = !DirectionalGreater(regimeSpotFast[^1], regimeSpotSlow[^1], isLong) &&
                             !DirectionalGreater(regimeFuturesFast[^1], regimeFuturesSlow[^1], isLong);
        if (regimeReversed)
            return $"Close{(isLong ? "Long" : "Short")}: synchronized higher-timeframe regime reversed.";

        return null;
    }

    private static EntrySetup BuildTestnetEvidenceEntry(
        SpotFuturesCrossMarketSettings settings,
        CrossMarketSnapshot snapshot,
        string normalLongRejection,
        string normalShortRejection)
    {
        var spot = snapshot.Spot!;
        var futures = snapshot.Futures!;
        var regimeSpot = snapshot.RegimeSpot!;
        var regimeFutures = snapshot.RegimeFutures!;

        var spotFast = EmaSeries(spot.ClosePrices, settings.ShortMaPeriod);
        var spotSlow = EmaSeries(spot.ClosePrices, settings.LongMaPeriod);
        var futuresFast = EmaSeries(futures.ClosePrices, settings.ShortMaPeriod);
        var futuresSlow = EmaSeries(futures.ClosePrices, settings.LongMaPeriod);
        var regimeSpotFast = EmaSeries(regimeSpot.ClosePrices, settings.RegimeShortMaPeriod);
        var regimeSpotSlow = EmaSeries(regimeSpot.ClosePrices, settings.RegimeLongMaPeriod);
        var regimeFuturesFast = EmaSeries(regimeFutures.ClosePrices, settings.RegimeShortMaPeriod);
        var regimeFuturesSlow = EmaSeries(regimeFutures.ClosePrices, settings.RegimeLongMaPeriod);

        decimal score = 0m;
        score += Vote(futuresFast[^1] - futuresSlow[^1], 3m);
        score += Vote(futures.ClosePrices[^1] - futures.ClosePrices[^2], 2m);
        score += Vote(ChangePercent(futures.ClosePrices, settings.MomentumLookbackCandles), 2m);
        score += Vote(spotFast[^1] - spotSlow[^1], 2m);
        score += Vote(ChangePercent(spot.ClosePrices, settings.MomentumLookbackCandles), 1m);
        score += Vote(regimeFuturesFast[^1] - regimeFuturesSlow[^1], 2m);
        score += Vote(regimeSpotFast[^1] - regimeSpotSlow[^1], 1m);

        if (futures.Volumes[^1] > 0m && futures.TakerBuyBaseVolumes.Count == futures.Volumes.Count)
            score += Vote(futures.TakerBuyBaseVolumes[^1] / futures.Volumes[^1] - 0.5m, 1m);

        var isLong = score >= 0m;
        // Avoid deliberately paying extreme funding in evidence mode. The normal funding
        // guard below still validates the selected direction before any order is attempted.
        if (snapshot.FundingRate > settings.MaxAbsFundingRateForEntry)
            isLong = false;
        else if (snapshot.FundingRate < -settings.MaxAbsFundingRateForEntry)
            isLong = true;

        return new EntrySetup(
            true,
            isLong,
            $"TESTNET_EVIDENCE_FALLBACK: directionalScore={score:F1}; normalLong=[{normalLongRejection}]; normalShort=[{normalShortRejection}]",
            IsTestnetEvidence: true);
    }

    private static decimal CalculateMicrostructureScore(AdaptiveRollingMarketDataSnapshot? snapshot, bool isLong)
    {
        if (snapshot is null || !snapshot.IsFresh)
            return -100m;

        var direction = isLong ? 1m : -1m;
        var velocity = Math.Clamp(snapshot.VelocityBps * direction / 10m, -1m, 1m) * 35m;
        var flow = Math.Clamp(snapshot.AggressiveFlowImbalance * direction, -1m, 1m) * 35m;
        var book = Math.Clamp(snapshot.OrderBookImbalance * direction, -1m, 1m) * 20m;
        var microprice = Math.Clamp(snapshot.MicropricePressureBps * direction / 10m, -1m, 1m) * 10m;
        return Math.Clamp(velocity + flow + book + microprice, -100m, 100m);
    }

    private static IReadOnlyList<decimal> EmaSeries(IReadOnlyList<decimal> values, int period)
    {
        if (values.Count < period || period <= 1)
            return [];

        var result = new decimal[values.Count];
        var alpha = 2m / (period + 1m);
        result[0] = values[0];
        for (var i = 1; i < values.Count; i++)
            result[i] = alpha * values[i] + (1m - alpha) * result[i - 1];
        return result;
    }

    private static decimal CalculateRsi(IReadOnlyList<decimal> closes, int period)
    {
        if (closes.Count < period + 1)
            return 50m;

        decimal gains = 0m, losses = 0m;
        for (var i = closes.Count - period; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            if (change >= 0m) gains += change;
            else losses -= change;
        }

        if (losses == 0m)
            return gains > 0m ? 100m : 50m;
        var rs = gains / losses;
        return 100m - 100m / (1m + rs);
    }

    private static decimal CalculateAdx(MarketSnapshot snapshot, int period)
    {
        var count = Math.Min(snapshot.HighPrices.Count, Math.Min(snapshot.LowPrices.Count, snapshot.ClosePrices.Count));
        if (count < period * 2 + 1)
            return 0m;

        var dxValues = new List<decimal>();
        for (var end = count - period; end < count; end++)
        {
            decimal trSum = 0m, plusDmSum = 0m, minusDmSum = 0m;
            var start = Math.Max(1, end - period + 1);
            for (var i = start; i <= end; i++)
            {
                var up = snapshot.HighPrices[i] - snapshot.HighPrices[i - 1];
                var down = snapshot.LowPrices[i - 1] - snapshot.LowPrices[i];
                if (up > down && up > 0m) plusDmSum += up;
                if (down > up && down > 0m) minusDmSum += down;
                trSum += Math.Max(
                    snapshot.HighPrices[i] - snapshot.LowPrices[i],
                    Math.Max(
                        Math.Abs(snapshot.HighPrices[i] - snapshot.ClosePrices[i - 1]),
                        Math.Abs(snapshot.LowPrices[i] - snapshot.ClosePrices[i - 1])));
            }

            if (trSum <= 0m)
                continue;
            var plusDi = plusDmSum / trSum * 100m;
            var minusDi = minusDmSum / trSum * 100m;
            var sum = plusDi + minusDi;
            if (sum > 0m)
                dxValues.Add(Math.Abs(plusDi - minusDi) / sum * 100m);
        }

        return dxValues.Count > 0 ? dxValues.Average() : 0m;
    }

    private static decimal LatestVolumeRatio(IReadOnlyList<decimal> volumes)
    {
        if (volumes.Count < 3)
            return 0m;
        var lookback = Math.Min(20, volumes.Count - 1);
        var average = volumes.Skip(volumes.Count - lookback - 1).Take(lookback).Average();
        return average > 0m ? volumes[^1] / average : 0m;
    }

    private static decimal ChangePercent(IReadOnlyList<decimal> closes, int lookback)
    {
        if (closes.Count < lookback + 1 || closes[^(lookback + 1)] <= 0m)
            return 0m;
        return (closes[^1] - closes[^(lookback + 1)]) / closes[^(lookback + 1)] * 100m;
    }

    private static bool DirectionalGreater(decimal fast, decimal slow, bool isLong)
        => isLong ? fast > slow : fast < slow;

    private static decimal Vote(decimal value, decimal weight)
        => value > 0m ? weight : value < 0m ? -weight : 0m;

    private static EntrySetup Reject(bool isLong, string reason) => new(false, isLong, reason);

    private sealed record EntrySetup(bool Allowed, bool IsLong, string Reason, bool IsTestnetEvidence = false);
}
