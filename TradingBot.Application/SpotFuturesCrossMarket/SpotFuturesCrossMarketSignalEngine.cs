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
    private static readonly string[] EntryGateOrder =
    [
        "Sync",
        "Freshness",
        "Trend",
        "Atr",
        "Warmup",
        "Regime",
        "RegimeAdx",
        "ExecutionTrend",
        "Volume",
        "TakerFlow",
        "CandleDirection",
        "Trigger",
        "Rsi",
        "Extension",
        "Microstructure",
        "EvidenceFallback",
        "Basis",
        "Funding",
        "ExpectedMove"
    ];

    public CrossMarketDecision Evaluate(
        SpotFuturesCrossMarketSettings settings,
        CrossMarketSnapshot snapshot,
        OrderSide? openPositionSide)
    {
        var longTrace = new EntryGateTraceBuilder(isLong: true);
        var shortTrace = new EntryGateTraceBuilder(isLong: false);
        var isEntryEvaluation = openPositionSide is null;

        CrossMarketEntryGateTrace CurrentTrace()
            => new(longTrace.Snapshot(), shortTrace.Snapshot());

        string WithTrace(string reason)
            => isEntryEvaluation ? $"{reason} | gates={CurrentTrace().ToCompactString()}" : reason;

        CrossMarketDecision EarlyNoTrade(string reason)
            => new()
            {
                Action = CrossMarketAction.NoTrade,
                Reason = WithTrace(reason),
                EntryGateTrace = isEntryEvaluation ? CurrentTrace() : null
            };

        if (!snapshot.MarketsInSync || snapshot.Spot is null || snapshot.Futures is null ||
            snapshot.RegimeSpot is null || snapshot.RegimeFutures is null)
        {
            var detail = snapshot.SyncIssue ?? "missing synchronized execution/regime data";
            longTrace.Fail("Sync", detail);
            shortTrace.Fail("Sync", detail);
            return EarlyNoTrade($"MarketsOutOfSync: {detail}");
        }

        longTrace.Pass("Sync", "execution and regime spot/futures snapshots are aligned");
        shortTrace.Pass("Sync", "execution and regime spot/futures snapshots are aligned");

        var freshness = EvaluateCandleFreshness(settings, snapshot);
        if (!freshness.Allowed)
        {
            longTrace.Fail("Freshness", freshness.Reason);
            shortTrace.Fail("Freshness", freshness.Reason);
            return EarlyNoTrade($"MarketDataStale: {freshness.Reason}");
        }

        longTrace.Pass("Freshness", freshness.Reason);
        shortTrace.Pass("Freshness", freshness.Reason);

        var spotTrend = trendStateService.Analyze(snapshot.Spot, settings.ShortMaPeriod, settings.LongMaPeriod);
        var futuresTrend = trendStateService.Analyze(snapshot.Futures, settings.ShortMaPeriod, settings.LongMaPeriod);
        if (!spotTrend.IsValid || !futuresTrend.IsValid)
        {
            var detail = $"spotValid={spotTrend.IsValid}, futuresValid={futuresTrend.IsValid}";
            longTrace.Fail("Trend", detail);
            shortTrace.Fail("Trend", detail);
            return EarlyNoTrade($"TrendUnavailable({detail})");
        }

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
            decimal? takeProfit = null,
            bool includeEntryTrace = true) => new()
            {
                Action = action,
                Reason = includeEntryTrace ? WithTrace(reason) : reason,
                EntryGateTrace = includeEntryTrace ? CurrentTrace() : null,
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
                return Build(CrossMarketAction.NoTrade, $"Hold{side}: trend structure remains valid.", includeEntryTrace: false);
            }

            return Build(
                openPositionSide == OrderSide.BUY ? CrossMarketAction.CloseLong : CrossMarketAction.CloseShort,
                exit,
                includeEntryTrace: false);
        }

        if (futuresAtrPercent <= 0m)
        {
            longTrace.Fail("Atr", "normalized futures ATR is unavailable or non-positive");
            shortTrace.Fail("Atr", "normalized futures ATR is unavailable or non-positive");
            return Build(CrossMarketAction.NoTrade, "AtrUnavailable: cannot build a price-anchored risk plan.");
        }

        longTrace.Pass("Atr", $"{futuresAtrPercent:F3}%");
        shortTrace.Pass("Atr", $"{futuresAtrPercent:F3}%");

        var longSetup = EvaluateEntrySetup(
            settings, snapshot, spotTrend, futuresTrend, spotMomentumPercent,
            isLong: true, futuresAtrPercent, longTrace);
        var shortSetup = EvaluateEntrySetup(
            settings, snapshot, spotTrend, futuresTrend, spotMomentumPercent,
            isLong: false, futuresAtrPercent, shortTrace);
        var setup = longSetup.Allowed ? longSetup : shortSetup.Allowed ? shortSetup : null;
        if (setup is null && settings.EnableTestnetEvidenceEntries)
        {
            setup = BuildTestnetEvidenceEntry(settings, snapshot, longSetup, shortSetup);
            setup.Trace.Pass("EvidenceFallback", "testnet-only directional fallback selected after normal setup rejection");
        }
        else if (setup is null)
        {
            const string disabled = "disabled after normal long/short setup rejection";
            longTrace.Fail("EvidenceFallback", disabled);
            shortTrace.Fail("EvidenceFallback", disabled);
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
            setup.Trace.Fail("Basis", $"|{snapshot.BasisPercent:F3}%| > {maxBasisPercent:F3}%");
            return Build(CrossMarketAction.NoTrade,
                $"BasisDislocation: |{snapshot.BasisPercent:F3}%| > {maxBasisPercent:F3}% " +
                $"(mode={(setup.IsTestnetEvidence ? "testnet-evidence" : "strategy")})");
        }

        setup.Trace.Pass("Basis", $"|{snapshot.BasisPercent:F3}%| <= {maxBasisPercent:F3}%");

        if (snapshot.FundingRate is not null)
        {
            if (setup.IsLong && snapshot.FundingRate.Value > settings.MaxAbsFundingRateForEntry)
            {
                setup.Trace.Fail("Funding", $"{snapshot.FundingRate.Value:F6} too expensive for long");
                return Build(CrossMarketAction.NoTrade, $"FundingTooExpensiveForLong({snapshot.FundingRate.Value:F6})");
            }
            if (!setup.IsLong && snapshot.FundingRate.Value < -settings.MaxAbsFundingRateForEntry)
            {
                setup.Trace.Fail("Funding", $"{snapshot.FundingRate.Value:F6} too expensive for short");
                return Build(CrossMarketAction.NoTrade, $"FundingTooExpensiveForShort({snapshot.FundingRate.Value:F6})");
            }

            setup.Trace.Pass("Funding", $"{snapshot.FundingRate.Value:F6} accepted for {(setup.IsLong ? "long" : "short")}");
        }
        else
        {
            setup.Trace.Pass("Funding", "unavailable; existing degraded-mode behavior permits entry");
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
            setup.Trace.Fail("ExpectedMove", $"net {netExpectedMovePercent:F3}% < {settings.MinNetExpectedMovePercent:F3}%");
            return Build(CrossMarketAction.NoTrade,
                $"ExpectedMoveTooSmall: target={targetPercent:F3}% costs={settings.FeeAndSpreadPercent:F3}% net={netExpectedMovePercent:F3}%");
        }

        setup.Trace.Pass("ExpectedMove", $"net {netExpectedMovePercent:F3}% >= {settings.MinNetExpectedMovePercent:F3}%");

        var stopLoss = setup.IsLong ? entryPrice - riskDistance : entryPrice + riskDistance;
        var takeProfitDistance = riskDistance * settings.MinRewardRiskRatio;
        var takeProfit = setup.IsLong ? entryPrice + takeProfitDistance : entryPrice - takeProfitDistance;
        var action = setup.IsLong ? CrossMarketAction.OpenLong : CrossMarketAction.OpenShort;
        var reason = $"{action}: {setup.Reason}; risk={stopPercent:F3}%, target={targetPercent:F3}% ({settings.MinRewardRiskRatio:F2}R), netEdge={netExpectedMovePercent:F3}%.";

        return Build(action, reason, targetPercent, stopLoss, takeProfit);
    }

    private static EntrySetup EvaluateEntrySetup(
        SpotFuturesCrossMarketSettings settings,
        CrossMarketSnapshot snapshot,
        TrendAnalysisResult spotTrend,
        TrendAnalysisResult futuresTrend,
        decimal spotMomentumPercent,
        bool isLong,
        decimal futuresAtrPercent,
        EntryGateTraceBuilder trace)
    {
        var spot = snapshot.Spot!;
        var futures = snapshot.Futures!;
        var regimeSpot = snapshot.RegimeSpot!;
        var regimeFutures = snapshot.RegimeFutures!;

        var trendDetail = $"spot={spotTrend.CurrentTrendState}/{spotTrend.ConfidenceScore}, " +
                          $"futures={futuresTrend.CurrentTrendState}/{futuresTrend.ConfidenceScore}";
        if (settings.EnableEntryQualityFilters)
        {
            var spotSupportsDirection = isLong
                ? spotTrend.IsBullishTrendConfirmed
                : spotTrend.IsBearishTrendConfirmed;
            var futuresSupportsDirection = isLong
                ? futuresTrend.IsBullishTrendConfirmed
                : futuresTrend.IsBearishTrendConfirmed;
            var spotOpposesDirection = isLong
                ? spotTrend.IsBearishTrendConfirmed
                : spotTrend.IsBullishTrendConfirmed;
            var futuresOpposesDirection = isLong
                ? futuresTrend.IsBearishTrendConfirmed
                : futuresTrend.IsBullishTrendConfirmed;
            var minimumConfidence = Math.Min(spotTrend.ConfidenceScore, futuresTrend.ConfidenceScore);
            var directionalSpotMomentum = spotMomentumPercent * (isLong ? 1m : -1m);

            if (minimumConfidence < settings.MinEntryTrendConfidenceScore)
            {
                return Reject(
                    isLong,
                    trace,
                    "Trend",
                    $"entry confidence {minimumConfidence} < {settings.MinEntryTrendConfidenceScore}; {trendDetail}");
            }

            if (spotOpposesDirection || futuresOpposesDirection ||
                (!spotSupportsDirection && !futuresSupportsDirection))
            {
                return Reject(
                    isLong,
                    trace,
                    "Trend",
                    $"spot/futures trend does not support {(isLong ? "long" : "short")}; {trendDetail}");
            }

            if (directionalSpotMomentum < settings.MinEntrySpotMomentumAbsPercent)
            {
                return Reject(
                    isLong,
                    trace,
                    "Trend",
                    $"directional spot momentum {directionalSpotMomentum:F3}% < {settings.MinEntrySpotMomentumAbsPercent:F3}%; {trendDetail}");
            }

            var support = spotSupportsDirection && futuresSupportsDirection
                ? "both markets confirmed"
                : "one market confirmed and the other remained neutral";
            trace.Pass(
                "Trend",
                $"{support}; directionalMomentum={directionalSpotMomentum:F3}%, {trendDetail}");
        }
        else
        {
            trace.Pass("Trend", $"entry quality filters disabled; {trendDetail}");
        }

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
            return Reject(isLong, trace, "Warmup", "indicator warmup incomplete");
        }

        trace.Pass("Warmup", "all execution/regime EMA series are available");

        var direction = isLong ? 1m : -1m;
        var regimeSpotAdx = CalculateAdx(regimeSpot, settings.RsiPeriod);
        var regimeFuturesAdx = CalculateAdx(regimeFutures, settings.RsiPeriod);
        var regimeAligned = DirectionalGreater(regimeSpotFast[^1], regimeSpotSlow[^1], isLong) &&
                            DirectionalGreater(regimeFuturesFast[^1], regimeFuturesSlow[^1], isLong) &&
                            (regimeSpotFast[^1] - regimeSpotFast[^4]) * direction > 0m &&
                            (regimeFuturesFast[^1] - regimeFuturesFast[^4]) * direction > 0m;
        if (!regimeAligned)
            return Reject(isLong, trace, "Regime", $"{settings.RegimeInterval} spot/futures EMA regime not aligned");

        trace.Pass("Regime", $"{settings.RegimeInterval} spot/futures EMA direction and slope aligned");
        if (Math.Min(regimeSpotAdx, regimeFuturesAdx) < settings.MinRegimeAdx)
            return Reject(isLong, trace, "RegimeAdx", $"regime ADX weak spot={regimeSpotAdx:F1} futures={regimeFuturesAdx:F1} < {settings.MinRegimeAdx:F1}");

        trace.Pass("RegimeAdx", $"spot={regimeSpotAdx:F1}, futures={regimeFuturesAdx:F1}, min={settings.MinRegimeAdx:F1}");

        var spotFastDistanceBps = DistanceBps(executionSpotFast[^1], executionSpotSlow[^1]);
        var spotCloseDistanceBps = DistanceBps(spot.ClosePrices[^1], executionSpotSlow[^1]);
        var futuresFastDistanceBps = DistanceBps(executionFuturesFast[^1], executionFuturesSlow[^1]);
        var futuresCloseDistanceBps = DistanceBps(futures.ClosePrices[^1], executionFuturesSlow[^1]);
        var spotExecutionAligned = DirectionalWithinTolerance(
                                       executionSpotFast[^1],
                                       executionSpotSlow[^1],
                                       isLong,
                                       settings.ExecutionTrendSpotToleranceBps) &&
                                   DirectionalWithinTolerance(
                                       spot.ClosePrices[^1],
                                       executionSpotSlow[^1],
                                       isLong,
                                       settings.ExecutionTrendSpotToleranceBps);
        var futuresExecutionAligned = DirectionalGreater(executionFuturesFast[^1], executionFuturesSlow[^1], isLong) &&
                                      (futures.ClosePrices[^1] - executionFuturesSlow[^1]) * direction > 0m;
        var executionAligned = spotExecutionAligned && futuresExecutionAligned;
        if (!executionAligned)
        {
            return Reject(
                isLong,
                trace,
                "ExecutionTrend",
                $"{settings.Interval} execution trend not aligned: spotFast={spotFastDistanceBps:F2}bps spotClose={spotCloseDistanceBps:F2}bps tolerance={settings.ExecutionTrendSpotToleranceBps:F2}bps; futuresFast={futuresFastDistanceBps:F2}bps futuresClose={futuresCloseDistanceBps:F2}bps strict");
        }

        trace.Pass(
            "ExecutionTrend",
            $"{settings.Interval} futures strict and spot within tolerance; spotFast={spotFastDistanceBps:F2}bps spotClose={spotCloseDistanceBps:F2}bps tolerance={settings.ExecutionTrendSpotToleranceBps:F2}bps; futuresFast={futuresFastDistanceBps:F2}bps futuresClose={futuresCloseDistanceBps:F2}bps");
        var rsi = CalculateRsi(spot.ClosePrices, settings.RsiPeriod);

        var spotVolumeRatio = LatestVolumeRatio(spot.Volumes);
        var futuresVolumeRatio = LatestVolumeRatio(futures.Volumes);
        if (Math.Min(spotVolumeRatio, futuresVolumeRatio) < settings.MinEntryVolumeRatio)
            return Reject(isLong, trace, "Volume", $"volume not participating spot={spotVolumeRatio:F2}x futures={futuresVolumeRatio:F2}x");

        trace.Pass("Volume", $"spot={spotVolumeRatio:F2}x, futures={futuresVolumeRatio:F2}x, min={settings.MinEntryVolumeRatio:F2}x");

        var takerBuyRatio = futures.Volumes[^1] > 0m && futures.TakerBuyBaseVolumes.Count == futures.Volumes.Count
            ? futures.TakerBuyBaseVolumes[^1] / futures.Volumes[^1]
            : 0.5m;
        if (isLong && takerBuyRatio < settings.MinLongTakerBuyRatio)
            return Reject(isLong, trace, "TakerFlow", $"futures taker-buy ratio {takerBuyRatio:F3} < {settings.MinLongTakerBuyRatio:F3}");
        if (!isLong && takerBuyRatio > settings.MaxShortTakerBuyRatio)
            return Reject(isLong, trace, "TakerFlow", $"futures taker-buy ratio {takerBuyRatio:F3} > {settings.MaxShortTakerBuyRatio:F3}");

        trace.Pass("TakerFlow", $"taker-buy={takerBuyRatio:F3}");

        var latest = spot.ClosePrices.Count - 1;
        var previousHigh = spot.HighPrices[^2];
        var previousLow = spot.LowPrices[^2];
        var candleConfirms = spot.OpenPrices.Count == spot.ClosePrices.Count && futures.OpenPrices.Count == futures.ClosePrices.Count &&
                             (spot.ClosePrices[^1] - spot.OpenPrices[^1]) * direction > 0m &&
                             (futures.ClosePrices[^1] - futures.OpenPrices[^1]) * direction > 0m;
        if (settings.RequireEntryClosedCandleDirectionConfirmation && !candleConfirms)
            return Reject(isLong, trace, "CandleDirection", "latest closed spot/futures candles do not confirm direction");

        if (settings.RequireEntryClosedCandleDirectionConfirmation)
            trace.Pass("CandleDirection", "latest closed spot/futures candles confirm direction");
        else
            trace.Skip("CandleDirection", "confirmation disabled by configuration");

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
            return Reject(isLong, trace, "Trigger", "waiting for pullback-reclaim or confirmed range breakout");

        trace.Pass("Trigger", trigger);

        var atrPrice = snapshot.FuturesClose * futuresAtrPercent / 100m;
        var extension = atrPrice > 0m
            ? Math.Abs(futures.ClosePrices[^1] - executionFuturesFast[^1]) / atrPrice
            : decimal.MaxValue;
        var extensionExceeded = extension > settings.MaxEntryExtensionAtr;
        var exhaustion = EvaluateDirectionalExhaustion(settings, futures, isLong);
        var weakDirectionalRsi = isLong ? rsi < settings.LongRsiMin : rsi > settings.ShortRsiMax;
        var extremeDirectionalRsi = isLong ? rsi > settings.LongRsiMax : rsi < settings.ShortRsiMin;
        string? rsiRejection = null;

        if (weakDirectionalRsi)
        {
            rsiRejection = $"RSI {rsi:F1} lacks {(isLong ? "long" : "short")} momentum; required range " +
                           (isLong
                               ? $"{settings.LongRsiMin:F0}+"
                               : $"up to {settings.ShortRsiMax:F0}");
        }
        else if (extremeDirectionalRsi && settings.EnableEntryQualityFilters)
        {
            rsiRejection =
                $"RSI {rsi:F1} outside configured {(isLong ? $"{settings.LongRsiMin:F0}-{settings.LongRsiMax:F0}" : $"{settings.ShortRsiMin:F0}-{settings.ShortRsiMax:F0}")} quality range";
        }
        else if (extremeDirectionalRsi && !breakout)
        {
            rsiRejection = $"RSI {rsi:F1} outside {(isLong ? $"{settings.LongRsiMin:F0}-{settings.LongRsiMax:F0}" : $"{settings.ShortRsiMin:F0}-{settings.ShortRsiMax:F0}")} for {trigger}";
        }
        else if (extremeDirectionalRsi && (extensionExceeded || exhaustion.Confirmed))
        {
            rsiRejection =
                $"entry exhaustion confirmed: RSI={rsi:F1}, trigger={trigger}, extension={extension:F2}ATR, " +
                $"lookbackMove={exhaustion.LookbackDirectionalMovePercent:F3}%, rangePosition={exhaustion.RangePositionPercent:F1}%";
        }

        if (rsiRejection is not null)
            trace.Fail("Rsi", rsiRejection);
        else if (extremeDirectionalRsi)
            trace.Pass("Rsi", $"Wilder RSI={rsi:F1} accepted for confirmed breakout; no directional exhaustion detected");
        else
            trace.Pass("Rsi", $"Wilder RSI={rsi:F1} inside directional range");

        if (extensionExceeded)
            trace.Fail("Extension", $"{extension:F2} ATR from fast EMA > {settings.MaxEntryExtensionAtr:F2}");
        else
            trace.Pass("Extension", $"{extension:F2} ATR from fast EMA <= {settings.MaxEntryExtensionAtr:F2}");

        if (rsiRejection is not null)
            return new EntrySetup(false, isLong, rsiRejection, trace);
        if (extensionExceeded)
            return new EntrySetup(false, isLong, $"entry extended {extension:F2} ATR from fast EMA > {settings.MaxEntryExtensionAtr:F2}", trace);

        var micro = snapshot.Microstructure;
        var microScore = CalculateMicrostructureScore(micro, isLong);
        if (settings.RequireMicrostructureConfirmation)
        {
            if (micro is null || !micro.IsFresh)
            {
                var degraded = micro is null
                    ? "not subscribed"
                    : $"{micro.DegradedReason ?? "not fresh"}, age={micro.MarketDataAgeMs}ms, latency={micro.StreamLatencyMs}ms";
                return Reject(isLong, trace, "Microstructure", $"live microstructure unavailable ({degraded})");
            }
            if (micro.SpreadBps > settings.MaxEntrySpreadBps)
                return Reject(isLong, trace, "Microstructure", $"spread {micro.SpreadBps:F2}bps > {settings.MaxEntrySpreadBps:F2}bps");
            if (microScore < settings.MinEntryMicrostructureScore)
                return Reject(isLong, trace, "Microstructure", $"microstructure score {microScore:F1} < {settings.MinEntryMicrostructureScore:F1}");

            trace.Pass("Microstructure", $"fresh age={micro.MarketDataAgeMs}ms latency={micro.StreamLatencyMs}ms, spread={micro.SpreadBps:F2}bps, score={microScore:F1}");
        }
        else
        {
            trace.Skip("Microstructure", "confirmation disabled by configuration");
        }

        trace.Qualify();

        return new EntrySetup(
            true,
            isLong,
            $"{settings.RegimeInterval} regime confirmed (ADX spot={regimeSpotAdx:F1}, futures={regimeFuturesAdx:F1}); " +
            $"{trigger}; RSI={rsi:F1}; volume spot={spotVolumeRatio:F2}x/futures={futuresVolumeRatio:F2}x; " +
            $"takerBuy={takerBuyRatio:F3}; extension={extension:F2}ATR; micro={microScore:F1}",
            trace);
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
        EntrySetup normalLong,
        EntrySetup normalShort)
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
            $"TESTNET_EVIDENCE_FALLBACK: directionalScore={score:F1}; normalLong=[{normalLong.Reason}]; normalShort=[{normalShort.Reason}]",
            isLong ? normalLong.Trace : normalShort.Trace,
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

    private static (bool Allowed, string Reason) EvaluateCandleFreshness(
        SpotFuturesCrossMarketSettings settings,
        CrossMarketSnapshot snapshot)
    {
        var now = DateTime.UtcNow;
        var clockToleranceSeconds = Math.Max(1, settings.MaxCandleMisalignmentSeconds);
        var executionMaxAgeSeconds = settings.IntervalTimeSpan.TotalSeconds + clockToleranceSeconds;
        var regimeMaxAgeSeconds = SpotFuturesCrossMarketSettings.ParseInterval(settings.RegimeInterval).TotalSeconds + clockToleranceSeconds;

        var checks = new[]
        {
            new FreshnessCheck("spot", LatestCloseTime(snapshot.Spot, snapshot.CandleCloseTimeUtc), executionMaxAgeSeconds),
            new FreshnessCheck("futures", LatestCloseTime(snapshot.Futures, snapshot.CandleCloseTimeUtc), executionMaxAgeSeconds),
            new FreshnessCheck("regimeSpot", LatestCloseTime(snapshot.RegimeSpot, null), regimeMaxAgeSeconds),
            new FreshnessCheck("regimeFutures", LatestCloseTime(snapshot.RegimeFutures, null), regimeMaxAgeSeconds)
        };

        var diagnostics = new List<string>(checks.Length);
        foreach (var check in checks)
        {
            if (check.CloseTimeUtc is null)
                return (false, $"{check.Name} latest closed-candle timestamp unavailable");

            var ageSeconds = (now - check.CloseTimeUtc.Value.ToUniversalTime()).TotalSeconds;
            diagnostics.Add($"{check.Name}Age={ageSeconds:F1}s/{check.MaxAgeSeconds:F1}s");

            if (ageSeconds < -clockToleranceSeconds)
                return (false, $"{check.Name} close time is {-ageSeconds:F1}s in the future (tolerance={clockToleranceSeconds}s)");
            if (ageSeconds > check.MaxAgeSeconds)
                return (false, $"{check.Name} closed candle age {ageSeconds:F1}s > {check.MaxAgeSeconds:F1}s");
        }

        return (true, string.Join(", ", diagnostics));

        static DateTime? LatestCloseTime(MarketSnapshot? market, DateTime? fallback)
            => market?.LatestClosedCandleCloseTimeUtc ?? market?.CurrentPriceAsOfUtc ?? fallback;
    }

    private static DirectionalExhaustion EvaluateDirectionalExhaustion(
        SpotFuturesCrossMarketSettings settings,
        MarketSnapshot futures,
        bool isLong)
    {
        var count = Math.Min(futures.ClosePrices.Count, Math.Min(futures.HighPrices.Count, futures.LowPrices.Count));
        var lookback = Math.Min(settings.EntryExhaustionLookbackCandles, count);
        if (lookback < 2 || settings.EntryExhaustionExtremeZonePercent <= 0m ||
            settings.EntryExhaustionMinMovePercent <= 0m)
        {
            return new DirectionalExhaustion(false, 0m, 50m);
        }

        var recentHigh = futures.HighPrices.TakeLast(lookback).Max();
        var recentLow = futures.LowPrices.TakeLast(lookback).Min();
        var range = recentHigh - recentLow;
        var latestClose = futures.ClosePrices[^1];
        var lookbackStartClose = futures.ClosePrices[^lookback];
        var lookbackDirectionalMovePercent = lookbackStartClose > 0m
            ? (latestClose - lookbackStartClose) / lookbackStartClose * 100m * (isLong ? 1m : -1m)
            : 0m;
        var rangePositionPercent = range > 0m
            ? Math.Clamp((latestClose - recentLow) / range * 100m, 0m, 100m)
            : 50m;
        var nearDirectionalExtreme = isLong
            ? rangePositionPercent >= 100m - settings.EntryExhaustionExtremeZonePercent
            : rangePositionPercent <= settings.EntryExhaustionExtremeZonePercent;
        var oversizedMove = lookbackDirectionalMovePercent >= settings.EntryExhaustionMinMovePercent;

        return new DirectionalExhaustion(
            Confirmed: oversizedMove && nearDirectionalExtreme,
            LookbackDirectionalMovePercent: lookbackDirectionalMovePercent,
            RangePositionPercent: rangePositionPercent);
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

        decimal averageGain = 0m, averageLoss = 0m;
        for (var i = 1; i <= period; i++)
        {
            var change = closes[i] - closes[i - 1];
            if (change >= 0m) averageGain += change;
            else averageLoss -= change;
        }

        averageGain /= period;
        averageLoss /= period;

        // Wilder smoothing uses all available closed candles, preventing the one-minute RSI
        // from jumping solely because the oldest value left a short rolling sum.
        for (var i = period + 1; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            var gain = Math.Max(change, 0m);
            var loss = Math.Max(-change, 0m);
            averageGain = (averageGain * (period - 1m) + gain) / period;
            averageLoss = (averageLoss * (period - 1m) + loss) / period;
        }

        if (averageLoss == 0m)
            return averageGain > 0m ? 100m : 50m;
        var rs = averageGain / averageLoss;
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

    private static bool DirectionalWithinTolerance(
        decimal value,
        decimal reference,
        bool isLong,
        decimal toleranceBps)
    {
        var tolerance = Math.Abs(reference) * Math.Max(0m, toleranceBps) / 10_000m;
        return isLong ? value >= reference - tolerance : value <= reference + tolerance;
    }

    private static decimal DistanceBps(decimal value, decimal reference)
        => reference == 0m ? 0m : (value - reference) / Math.Abs(reference) * 10_000m;

    private static decimal Vote(decimal value, decimal weight)
        => value > 0m ? weight : value < 0m ? -weight : 0m;

    private static EntrySetup Reject(
        bool isLong,
        EntryGateTraceBuilder trace,
        string gate,
        string reason)
    {
        trace.Fail(gate, reason);
        return new EntrySetup(false, isLong, reason, trace);
    }

    private sealed class EntryGateTraceBuilder(bool isLong)
    {
        private readonly EntryGateResult[] _gates = EntryGateOrder
            .Select(name => new EntryGateResult(name, EntryGateState.NotEvaluated, "not reached"))
            .ToArray();
        private string? _primaryRejection;
        private bool _setupQualified;

        public void Pass(string gate, string detail) => Set(gate, EntryGateState.Pass, detail);

        public void Fail(string gate, string detail)
        {
            Set(gate, EntryGateState.Fail, detail);
            _primaryRejection ??= detail;
        }

        public void Skip(string gate, string detail) => Set(gate, EntryGateState.NotEvaluated, detail);

        public void Qualify() => _setupQualified = true;

        public EntrySideGateTrace Snapshot()
            => new(
                isLong ? "Long" : "Short",
                _setupQualified,
                _primaryRejection,
                _gates.ToArray());

        private void Set(string gate, EntryGateState state, string detail)
        {
            var index = Array.FindIndex(_gates, x => string.Equals(x.Gate, gate, StringComparison.Ordinal));
            if (index < 0)
                throw new InvalidOperationException($"Unknown entry gate '{gate}'.");
            _gates[index] = new EntryGateResult(gate, state, detail);
        }
    }

    private sealed record FreshnessCheck(string Name, DateTime? CloseTimeUtc, double MaxAgeSeconds);
    private sealed record DirectionalExhaustion(bool Confirmed, decimal LookbackDirectionalMovePercent, decimal RangePositionPercent);
    private sealed record EntrySetup(
        bool Allowed,
        bool IsLong,
        string Reason,
        EntryGateTraceBuilder Trace,
        bool IsTestnetEvidence = false);
}
