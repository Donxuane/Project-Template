using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

internal static class BnbPullbackGuardReplayHelper
{
    public static bool TryBlock(
        BnbPullbackEntryGuard? guard,
        TradingSymbol symbol,
        StrategySignalResult signal,
        PullbackV2Diagnostics? v2Diagnostics,
        string interval,
        decimal? profitLockThresholdPercent,
        bool isV2Path,
        string profileName,
        string symbolsText,
        DateTime timeUtc,
        decimal confidence,
        decimal confidenceThreshold,
        decimal? estimatedNetMovePercent,
        ProfileSignalStats signalStats,
        List<BlockedEntryRecord> blockedEntriesDestination,
        out BnbPullbackGuardDiagnostics? allowedDiagnostics)
    {
        allowedDiagnostics = null;
        if (guard is null || !guard.IsEnabled || symbol != TradingSymbol.BNBUSDT)
            return false;

        var decision = guard.Evaluate(symbol, signal, v2Diagnostics, interval, profitLockThresholdPercent, isV2Path);
        if (decision.IsAllowed)
        {
            allowedDiagnostics = decision.Diagnostics;
            return false;
        }

        signalStats.IncrementBnbGuardBlocked(decision.Reason);
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
        BnbPullbackGuardDecision decision)
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
            RejectionLayer = "BnbPullbackGuard",
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
            BnbPullbackGuardEnabled = d.BnbPullbackGuardEnabled,
            BnbPullbackGuardRejected = d.BnbPullbackGuardRejected,
            BnbPullbackGuardRejectedReason = d.BnbPullbackGuardRejectedReason,
            LockDistancePercent = d.LockDistancePercent,
            MaxAllowedLockDistancePercent = d.MaxAllowedLockDistancePercent,
            LockReachabilityRejected = d.LockReachabilityRejected,
            ExpectedMoveCapRejected = d.ExpectedMoveCapRejected,
            DistanceToInvalidationCapRejected = d.DistanceToInvalidationCapRejected,
            TrendStrengthCapRejected = d.TrendStrengthCapRejected,
            ResidualExpectedMoveCapRejected = d.ResidualExpectedMoveCapRejected,
            ResidualRewardRiskCapRejected = d.ResidualRewardRiskCapRejected,
            ConsecutiveBullishCandlesAtEntry = d.ConsecutiveBullishCandlesAtEntry,
            EntryNearRecentHigh = d.EntryNearRecentHigh
        };
    }
}
