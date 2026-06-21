using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

internal static class BnbRetestContinuationReplayHelper
{
    public static bool TryBlock(
        BnbRetestContinuationV1Model? model,
        TradingSymbol symbol,
        StrategySignalResult signal,
        MarketSnapshot snapshot,
        string interval,
        decimal? profitLockThresholdPercent,
        string profileName,
        string symbolsText,
        DateTime timeUtc,
        decimal confidence,
        decimal confidenceThreshold,
        decimal? estimatedNetMovePercent,
        PullbackV2Diagnostics? v2Diagnostics,
        ProfileSignalStats signalStats,
        List<BlockedEntryRecord> blockedEntriesDestination,
        out StrategySignalResult rewrittenSignal,
        out BnbRetestContinuationDiagnostics? allowedDiagnostics)
    {
        rewrittenSignal = signal;
        allowedDiagnostics = null;
        if (model is null || !model.IsEnabled || symbol != TradingSymbol.BNBUSDT)
            return false;

        var decision = model.Evaluate(symbol, signal, snapshot, interval, profitLockThresholdPercent);
        if (decision.IsAllowed)
        {
            rewrittenSignal = decision.Signal;
            allowedDiagnostics = decision.Diagnostics;
            return false;
        }

        signalStats.IncrementRetestContinuationBlocked(decision.Reason);
        blockedEntriesDestination.Add(BuildBlockedEntry(
            interval,
            profileName,
            symbolsText,
            symbol,
            timeUtc,
            signal,
            v2Diagnostics,
            confidence,
            confidenceThreshold,
            estimatedNetMovePercent,
            decision));
        return true;
    }

    public static BlockedEntryRecord BuildBlockedEntry(
        string interval,
        string profileName,
        string symbolsText,
        TradingSymbol symbol,
        DateTime timeUtc,
        StrategySignalResult signal,
        PullbackV2Diagnostics? v2Diagnostics,
        decimal confidence,
        decimal confidenceThreshold,
        decimal? estimatedNetMovePercent,
        BnbRetestContinuationDecision decision)
    {
        var d = decision.Diagnostics;
        return new BlockedEntryRecord
        {
            Interval = interval,
            ProfileName = profileName,
            Symbols = symbolsText,
            Symbol = symbol,
            TimeUtc = timeUtc,
            Reason = decision.Reason,
            Confidence = confidence,
            ConfidenceThreshold = confidenceThreshold,
            ExpectedMovePercent = signal.ExpectedMovePercent,
            EstimatedNetMovePercent = estimatedNetMovePercent,
            ExpectedTargetSource = signal.ExpectedTargetSource,
            SignalReason = signal.Reason,
            RejectionLayer = "RetestContinuation",
            PullbackSetupDetected = v2Diagnostics?.PullbackSetupDetected,
            PullbackReclaimConfirmed = v2Diagnostics?.PullbackReclaimConfirmed,
            PullbackFollowThroughConfirmed = v2Diagnostics?.PullbackFollowThroughConfirmed,
            PullbackRejectedReason = v2Diagnostics?.PullbackRejectedReason,
            ReclaimReferencePrice = v2Diagnostics?.ReclaimReferencePrice,
            FollowThroughReferencePrice = v2Diagnostics?.FollowThroughReferencePrice,
            CandlesWaitedAfterReclaim = v2Diagnostics?.CandlesWaitedAfterReclaim,
            ResidualExpectedMovePercent = v2Diagnostics?.ResidualExpectedMovePercent,
            ResidualEstimatedNetMovePercent = v2Diagnostics?.ResidualEstimatedNetMovePercent,
            ResidualRewardRisk = v2Diagnostics?.ResidualRewardRisk,
            DistanceFromEntryToExpectedTargetPercent = v2Diagnostics?.DistanceFromEntryToExpectedTargetPercent,
            EntryNearRecentHigh = d.EntryNearRecentHigh ?? signal.EntryNearRecentHigh,
            ConsecutiveBullishCandlesAtEntry = d.ConsecutiveBullishCandlesAtEntry ?? signal.ConsecutiveBullishTrendCandles,
            RetestContinuationEnabled = d.RetestContinuationEnabled,
            RetestContinuationRejected = d.RetestContinuationRejected,
            RetestContinuationRejectedReason = d.RetestContinuationRejectedReason,
            RawExpectedMovePercent = d.RawExpectedMovePercent,
            CappedExpectedMovePercent = d.CappedExpectedMovePercent,
            LockDistancePercent = d.LockDistancePercent,
            TargetModelName = d.TargetModelName,
            TargetWasCapped = d.TargetWasCapped,
            CapReason = d.CapReason,
            ExpectedMoveToRecentMfeRatio = d.ExpectedMoveToRecentMfeRatio
        };
    }
}
