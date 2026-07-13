using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.SpotFuturesCrossMarket;

/// <summary>
/// Pure decision logic of the SpotFuturesCrossMarketTestnetV1 strategy, evaluated once per
/// fully closed candle:
///
///   - Spot data is the leading context: its trend direction and short-horizon momentum
///     define the candidate direction.
///   - Futures data (the traded instrument) must confirm the same direction before any
///     entry; futures ATR anchors the protective stop/target.
///   - Cross-market context (basis between the two feeds, funding rate) filters entries in
///     dislocated or expensive-carry conditions.
///
/// Reuses the existing ITrendStateService (SMA trend/slope/confidence) and IAtrService.
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
        if (!snapshot.MarketsInSync || snapshot.Spot is null || snapshot.Futures is null)
        {
            return NoTrade($"MarketsOutOfSync: {snapshot.SyncIssue ?? "unknown"}");
        }

        var spotTrend = trendStateService.Analyze(snapshot.Spot, settings.ShortMaPeriod, settings.LongMaPeriod);
        var futuresTrend = trendStateService.Analyze(snapshot.Futures, settings.ShortMaPeriod, settings.LongMaPeriod);

        if (!spotTrend.IsValid || !futuresTrend.IsValid)
        {
            return NoTrade($"TrendUnavailable(spotValid={spotTrend.IsValid}, futuresValid={futuresTrend.IsValid})");
        }

        var spotMomentumPercent = ComputeMomentumPercent(snapshot.Spot.ClosePrices, settings.MomentumLookbackCandles);
        var futuresAtrPercent = atrService.Calculate(
            snapshot.Futures.HighPrices,
            snapshot.Futures.LowPrices,
            snapshot.Futures.ClosePrices,
            normalize: true,
            currentPrice: snapshot.FuturesClose) * 100m;

        var stopPercent = Math.Clamp(futuresAtrPercent * settings.AtrStopMultiplier, settings.MinStopPercent, settings.MaxStopPercent);
        var targetPercent = settings.AtrStopMultiplier > 0m
            ? stopPercent * (settings.AtrTargetMultiplier / settings.AtrStopMultiplier)
            : stopPercent;
        var netExpectedMovePercent = targetPercent - settings.FeeAndSpreadPercent;

        CrossMarketDecision Build(CrossMarketAction action, string reason, decimal? stopLoss = null, decimal? takeProfit = null) => new()
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
            ExpectedMovePercent = targetPercent,
            StopLossPrice = stopLoss,
            TakeProfitPrice = takeProfit
        };

        // ------------------------------------------------------------------ exits first
        if (openPositionSide == OrderSide.BUY)
        {
            var exit = EvaluateLongExit(settings, spotTrend, futuresTrend);
            return exit is not null
                ? Build(CrossMarketAction.CloseLong, exit)
                : Build(CrossMarketAction.NoTrade, "HoldLong: no exit condition met on this closed candle.");
        }

        if (openPositionSide == OrderSide.SELL)
        {
            var exit = EvaluateShortExit(settings, spotTrend, futuresTrend);
            return exit is not null
                ? Build(CrossMarketAction.CloseShort, exit)
                : Build(CrossMarketAction.NoTrade, "HoldShort: no exit condition met on this closed candle.");
        }

        // ------------------------------------------------------------------ flat: entries
        if (netExpectedMovePercent < settings.MinNetExpectedMovePercent)
        {
            return Build(CrossMarketAction.NoTrade,
                $"ExpectedMoveTooSmall: target {targetPercent:F3}% - costs {settings.FeeAndSpreadPercent:F3}% = {netExpectedMovePercent:F3}% < min {settings.MinNetExpectedMovePercent:F3}%");
        }

        if (Math.Abs(snapshot.BasisPercent) > settings.MaxAbsBasisPercentForEntry)
        {
            return Build(CrossMarketAction.NoTrade,
                $"BasisDislocation: |{snapshot.BasisPercent:F3}%| > {settings.MaxAbsBasisPercentForEntry:F3}% (spot/futures feeds dislocated)");
        }

        var longSignal = EvaluateLongEntry(settings, spotTrend, futuresTrend, spotMomentumPercent, snapshot.FundingRate);
        if (longSignal.Allowed)
        {
            var entryAnchor = snapshot.MarkPrice is > 0m ? snapshot.MarkPrice.Value : snapshot.FuturesClose;
            var stopLoss = entryAnchor * (1m - stopPercent / 100m);
            var takeProfit = entryAnchor * (1m + targetPercent / 100m);
            return Build(CrossMarketAction.OpenLong, longSignal.Reason, stopLoss, takeProfit);
        }

        var shortSignal = EvaluateShortEntry(settings, spotTrend, futuresTrend, spotMomentumPercent, snapshot.FundingRate);
        if (shortSignal.Allowed)
        {
            var entryAnchor = snapshot.MarkPrice is > 0m ? snapshot.MarkPrice.Value : snapshot.FuturesClose;
            var stopLoss = entryAnchor * (1m + stopPercent / 100m);
            var takeProfit = entryAnchor * (1m - targetPercent / 100m);
            return Build(CrossMarketAction.OpenShort, shortSignal.Reason, stopLoss, takeProfit);
        }

        var decision = Build(CrossMarketAction.NoTrade, $"NoAlignedSignal: long[{longSignal.Reason}] short[{shortSignal.Reason}]");
        logger.LogDebug(
            "SpotFuturesCrossMarket NoTrade {Symbol}: spot={SpotTrend}({SpotScore}) futures={FutTrend}({FutScore}) momentum={Momentum}% basis={Basis}% funding={Funding}",
            snapshot.Symbol, spotTrend.CurrentTrendState, spotTrend.ConfidenceScore,
            futuresTrend.CurrentTrendState, futuresTrend.ConfidenceScore,
            spotMomentumPercent, snapshot.BasisPercent, snapshot.FundingRate);
        return decision;

        CrossMarketDecision NoTrade(string reason) => new()
        {
            Action = CrossMarketAction.NoTrade,
            Reason = reason
        };
    }

    private static (bool Allowed, string Reason) EvaluateLongEntry(
        SpotFuturesCrossMarketSettings settings,
        TrendAnalysisResult spotTrend,
        TrendAnalysisResult futuresTrend,
        decimal spotMomentumPercent,
        decimal? fundingRate)
    {
        if (!spotTrend.IsBullishTrendConfirmed)
            return (false, "spot trend not confirmed bullish");
        if (spotTrend.ConfidenceScore < settings.MinEntryTrendConfidenceScore)
            return (false, $"spot confidence {spotTrend.ConfidenceScore} < {settings.MinEntryTrendConfidenceScore}");
        if (spotMomentumPercent <= 0m)
            return (false, $"spot momentum {spotMomentumPercent:F3}% not positive");
        if (!futuresTrend.IsBullishTrendConfirmed)
            return (false, "futures did not confirm bullish direction");
        if (futuresTrend.ConfidenceScore < settings.MinEntryTrendConfidenceScore)
            return (false, $"futures confidence {futuresTrend.ConfidenceScore} < {settings.MinEntryTrendConfidenceScore}");
        if (fundingRate is not null && fundingRate.Value > settings.MaxAbsFundingRateForEntry)
            return (false, $"funding {fundingRate.Value:F6} too expensive for longs");

        return (true,
            $"OpenLong: spot leads bullish (score {spotTrend.ConfidenceScore}, momentum {spotMomentumPercent:F3}%), futures confirms (score {futuresTrend.ConfidenceScore}).");
    }

    private static (bool Allowed, string Reason) EvaluateShortEntry(
        SpotFuturesCrossMarketSettings settings,
        TrendAnalysisResult spotTrend,
        TrendAnalysisResult futuresTrend,
        decimal spotMomentumPercent,
        decimal? fundingRate)
    {
        if (!spotTrend.IsBearishTrendConfirmed)
            return (false, "spot trend not confirmed bearish");
        if (spotTrend.ConfidenceScore < settings.MinEntryTrendConfidenceScore)
            return (false, $"spot confidence {spotTrend.ConfidenceScore} < {settings.MinEntryTrendConfidenceScore}");
        if (spotMomentumPercent >= 0m)
            return (false, $"spot momentum {spotMomentumPercent:F3}% not negative");
        if (!futuresTrend.IsBearishTrendConfirmed)
            return (false, "futures did not confirm bearish direction");
        if (futuresTrend.ConfidenceScore < settings.MinEntryTrendConfidenceScore)
            return (false, $"futures confidence {futuresTrend.ConfidenceScore} < {settings.MinEntryTrendConfidenceScore}");
        if (fundingRate is not null && fundingRate.Value < -settings.MaxAbsFundingRateForEntry)
            return (false, $"funding {fundingRate.Value:F6} too expensive for shorts");

        return (true,
            $"OpenShort: spot leads bearish (score {spotTrend.ConfidenceScore}, momentum {spotMomentumPercent:F3}%), futures confirms (score {futuresTrend.ConfidenceScore}).");
    }

    private static string? EvaluateLongExit(
        SpotFuturesCrossMarketSettings settings,
        TrendAnalysisResult spotTrend,
        TrendAnalysisResult futuresTrend)
    {
        if (futuresTrend.IsBearishTrendConfirmed)
            return $"CloseLong: futures trend flipped bearish (score {futuresTrend.ConfidenceScore}).";
        if (futuresTrend.IsBearishCrossover)
            return "CloseLong: futures bearish MA crossover on closed candle.";
        if (spotTrend.IsBearishTrendConfirmed && spotTrend.ConfidenceScore >= settings.MinExitTrendConfidenceScore)
            return $"CloseLong: leading spot trend flipped bearish (score {spotTrend.ConfidenceScore}).";
        return null;
    }

    private static string? EvaluateShortExit(
        SpotFuturesCrossMarketSettings settings,
        TrendAnalysisResult spotTrend,
        TrendAnalysisResult futuresTrend)
    {
        if (futuresTrend.IsBullishTrendConfirmed)
            return $"CloseShort: futures trend flipped bullish (score {futuresTrend.ConfidenceScore}).";
        if (futuresTrend.IsBullishCrossover)
            return "CloseShort: futures bullish MA crossover on closed candle.";
        if (spotTrend.IsBullishTrendConfirmed && spotTrend.ConfidenceScore >= settings.MinExitTrendConfidenceScore)
            return $"CloseShort: leading spot trend flipped bullish (score {spotTrend.ConfidenceScore}).";
        return null;
    }

    /// <summary>Percent change of the last close vs the close N candles back.</summary>
    private static decimal ComputeMomentumPercent(IReadOnlyList<decimal> closes, int lookback)
    {
        if (closes.Count < lookback + 1)
            return 0m;

        var reference = closes[^(lookback + 1)];
        if (reference <= 0m)
            return 0m;

        return (closes[^1] - reference) / reference * 100m;
    }
}
