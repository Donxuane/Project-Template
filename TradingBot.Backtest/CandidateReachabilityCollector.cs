using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public sealed class CandidateReachabilityCollector(CandidateReachabilitySettings settings)
{
    private readonly List<CandidateReachabilityRecord> _records = new();

    public IReadOnlyList<CandidateReachabilityRecord> Records => _records;

    public void Capture(
        string interval,
        string profileName,
        string symbolsText,
        TradingSymbol symbol,
        StrategySignalResult signal,
        MarketSnapshot snapshot,
        string rejectionLayer,
        string rejectionReason,
        decimal confidenceThreshold,
        decimal? estimatedNetMovePercent,
        bool executed,
        IReadOnlyList<KlineCandle> oneMinuteCandles)
    {
        var forward = CandidateForwardOutcomeAnalyzer.Analyze(
            oneMinuteCandles,
            snapshot.TimestampUtc,
            snapshot.CurrentPrice,
            signal.ExpectedMovePercent,
            settings.ForwardHorizonMinutes);

        var labels = ComputeLabels(
            signal,
            rejectionLayer,
            rejectionReason,
            confidenceThreshold,
            estimatedNetMovePercent,
            executed,
            forward);

        _records.Add(new CandidateReachabilityRecord
        {
            Interval = interval,
            ProfileName = profileName,
            Symbols = symbolsText,
            Symbol = symbol,
            TimeUtc = snapshot.TimestampUtc,
            SignalReason = signal.Reason,
            RejectionLayer = rejectionLayer,
            RejectionReason = rejectionReason,
            Executed = executed,
            Confidence = signal.Confidence,
            ConfidenceThreshold = confidenceThreshold,
            ExpectedMovePercent = signal.ExpectedMovePercent,
            EstimatedNetMovePercent = estimatedNetMovePercent,
            ExpectedTargetPrice = signal.ExpectedTargetPrice,
            ExpectedTargetSource = signal.ExpectedTargetSource,
            RewardRisk = CandidateForwardOutcomeAnalyzer.ComputeRewardRisk(signal.ExpectedMovePercent, signal.DistanceToInvalidationPercent),
            DistanceToInvalidationPercent = signal.DistanceToInvalidationPercent,
            TrendStrengthPercent = signal.TrendStrengthPercent,
            ShortMaSlopePercent = signal.ShortMaSlopePercent,
            ConsecutiveBullishTrendCandles = signal.ConsecutiveBullishTrendCandles,
            EntryNearRecentHigh = signal.EntryNearRecentHigh,
            PreviousCandleBearish = signal.PreviousCandleBearish,
            VolatilityRegime = signal.VolatilityRegime,
            EntryPrice = snapshot.CurrentPrice,
            ForwardMfe15Percent = forward.ForwardMfe15Percent,
            ForwardMfe30Percent = forward.ForwardMfe30Percent,
            ForwardMfe60Percent = forward.ForwardMfe60Percent,
            ForwardMae15Percent = forward.ForwardMae15Percent,
            ForwardMae30Percent = forward.ForwardMae30Percent,
            ForwardMae60Percent = forward.ForwardMae60Percent,
            Lock90DistancePercent = forward.Lock90DistancePercent,
            Lock95DistancePercent = forward.Lock95DistancePercent,
            Lock98DistancePercent = forward.Lock98DistancePercent,
            Lock90ReachableWithin60m = forward.Lock90ReachableWithin60m,
            Lock95ReachableWithin60m = forward.Lock95ReachableWithin60m,
            Lock98ReachableWithin60m = forward.Lock98ReachableWithin60m,
            TimeToLock90Minutes = forward.TimeToLock90Minutes,
            TimeToLock95Minutes = forward.TimeToLock95Minutes,
            TimeToLock98Minutes = forward.TimeToLock98Minutes,
            ExpectedMoveInflated = labels.ExpectedMoveInflated,
            Lock90Reachable = labels.Lock90Reachable,
            Lock95Reachable = labels.Lock95Reachable,
            Lock98Reachable = labels.Lock98Reachable,
            FavorableButNetUntradable = labels.FavorableButNetUntradable,
            ConfidenceFalseNegativeCandidate = labels.ConfidenceFalseNegativeCandidate
        });
    }

    private (bool ExpectedMoveInflated, bool Lock90Reachable, bool Lock95Reachable, bool Lock98Reachable, bool FavorableButNetUntradable, bool ConfidenceFalseNegativeCandidate) ComputeLabels(
        StrategySignalResult signal,
        string rejectionLayer,
        string rejectionReason,
        decimal confidenceThreshold,
        decimal? estimatedNetMovePercent,
        bool executed,
        ForwardOutcomeAnalytics forward)
    {
        var inflated = signal.ExpectedMovePercent.HasValue
                       && forward.ForwardMfe60Percent.HasValue
                       && signal.ExpectedMovePercent.Value > forward.ForwardMfe60Percent.Value * settings.ExpectedMoveInflatedMultiplier;

        var lock90Reachable = forward.Lock90ReachableWithin60m;
        var lock95Reachable = forward.Lock95ReachableWithin60m;
        var lock98Reachable = forward.Lock98ReachableWithin60m;

        var favorableMfe = forward.ForwardMfe60Percent.HasValue && forward.ForwardMfe60Percent.Value >= settings.MinFavorableMfePercent;
        var favorableButUntradable = !executed
                                     && favorableMfe
                                     && !lock90Reachable
                                     && (estimatedNetMovePercent is null || estimatedNetMovePercent <= 0m);

        var confidenceBlocked = string.Equals(rejectionReason, BacktestEntryGuard.ConfidenceBelowThreshold, StringComparison.OrdinalIgnoreCase)
                                || (string.Equals(rejectionLayer, "Guard", StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(rejectionReason, BacktestEntryGuard.ConfidenceBelowThreshold, StringComparison.OrdinalIgnoreCase));
        var maeAcceptable = !forward.ForwardMae60Percent.HasValue
                            || forward.ForwardMae60Percent.Value >= -settings.MaxAcceptableForwardMae60Percent;
        var confidenceFalseNegative = confidenceBlocked
                                      && signal.Confidence < confidenceThreshold
                                      && lock90Reachable
                                      && maeAcceptable;

        return (inflated, lock90Reachable, lock95Reachable, lock98Reachable, favorableButUntradable, confidenceFalseNegative);
    }
}

internal static class CandidateReachabilityReplayHelper
{
    public static void CaptureCandidate(
        CandidateReachabilityCollector? collector,
        IReadOnlyList<KlineCandle>? sourceOneMinuteCandles,
        BacktestEntryGuard guard,
        string interval,
        string profileName,
        string symbolsText,
        TradingSymbol symbol,
        StrategySignalResult signal,
        MarketSnapshot snapshot,
        string rejectionLayer,
        string rejectionReason,
        decimal confidenceThreshold,
        decimal? estimatedNetMovePercent,
        bool executed)
    {
        if (collector is null || sourceOneMinuteCandles is null || sourceOneMinuteCandles.Count == 0)
            return;

        if (confidenceThreshold <= 0m)
        {
            var peek = guard.Evaluate(symbol, signal, snapshot, hasOpenPositionForSymbol: false);
            confidenceThreshold = peek.ConfidenceThreshold;
            estimatedNetMovePercent ??= peek.EstimatedNetMovePercent;
        }

        collector.Capture(
            interval,
            profileName,
            symbolsText,
            symbol,
            signal,
            snapshot,
            rejectionLayer,
            rejectionReason,
            confidenceThreshold,
            estimatedNetMovePercent,
            executed,
            sourceOneMinuteCandles);
    }
}
