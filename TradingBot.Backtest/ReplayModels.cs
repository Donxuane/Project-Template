using Microsoft.Extensions.Configuration;
using TradingBot.Application.DecisionEngine;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public sealed record KlineCandle(
    TradingSymbol Symbol,
    DateTime OpenTimeUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume)
{
    public DateTime CloseTimeUtc => OpenTimeUtc.AddMinutes(1);
}

public sealed record ReplayProfileDefinition(
    string ProfileName,
    IReadOnlyList<TradingSymbol> Symbols,
    IReadOnlyDictionary<string, string> ConfigOverrides);

public sealed record DataQualityIssue(
    string Interval,
    TradingSymbol Symbol,
    string Severity,
    string Message);

public sealed record SymbolValidationResult(
    TradingSymbol Symbol,
    IReadOnlyList<KlineCandle> Candles,
    IReadOnlyList<DataQualityIssue> Issues);

public sealed record ExecutionCostSettings(
    decimal FeeRatePercent,
    decimal SpreadPercent,
    decimal SlippagePercent);

public sealed record BacktestExitPolicySettings(
    string ExitPolicyName,
    decimal? ProfitLockThresholdPercent,
    bool EnableBreakevenAfterNetProfit,
    decimal BreakevenActivationNetProfitPercent,
    bool EnableTrailingAfterNetProfit,
    decimal TrailingActivationNetProfitPercent,
    decimal TrailingStopPercent,
    bool EnableStructuralStop = false,
    string StructuralStopMode = "RangeLow",
    int? MaxHoldMinutes = null,
    bool EnableHalfLockBreakevenExit = false,
    bool EnableFeeAwareTimeStopExit = false,
    bool EnableCostCoveredBreakevenExit = false,
    decimal CostCoverMinNetPercent = 0m,
    int? NoProgressExitMinutes = null);

public sealed record SimulatedTrade
{
    public string Interval { get; init; } = "1m";
    public string ProfileName { get; init; } = string.Empty;
    public string Symbols { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public DateTime EntryTimeUtc { get; init; }
    public decimal EntryPrice { get; init; }
    public DateTime ExitTimeUtc { get; init; }
    public decimal ExitPrice { get; init; }
    public decimal Quantity { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal FeeAndSpreadEstimateQuote { get; init; }
    public string EntryReason { get; init; } = string.Empty;
    public string ExitReason { get; init; } = string.Empty;
    public string ExitPolicyName { get; init; } = "OppositeSignalOnly";
    public decimal? ProfitLockThresholdPercent { get; init; }
    public decimal? ExpectedMovePercent { get; init; }
    public decimal? ExpectedTargetPrice { get; init; }
    public string? ExpectedTargetSource { get; init; }
    public decimal? RewardRisk { get; init; }
    public int? ConsecutiveBullishTrendCandles { get; init; }
    public bool? CurrentCloseAboveRecentHigh { get; init; }
    public decimal? DistanceToInvalidationPercent { get; init; }
    public bool? PreviousCandleBearish { get; init; }
    public bool? EntryNearRecentHigh { get; init; }
    public decimal? ShortMaSlopePercent { get; init; }
    public decimal? TrendStrengthPercent { get; init; }
    public string? ProjectionMode { get; init; }
    public decimal? ProjectedExtension { get; init; }
    public bool WasGuarded { get; init; }
    public decimal? EstimatedRoundTripCostPercent { get; init; }
    public decimal? EstimatedNetMovePercent { get; init; }
    public decimal? MaxFavorablePrice { get; init; }
    public decimal? MaxAdversePrice { get; init; }
    public decimal? MfePercent { get; init; }
    public decimal? MaePercent { get; init; }
    public bool TouchedExpectedTarget { get; init; }
    public DateTime? FirstExpectedTargetTouchTimeUtc { get; init; }
    public decimal? CounterfactualExitAtExpectedTargetNetPnlQuote { get; init; }
    public decimal? CounterfactualDeltaVsActualNetPnlQuote { get; init; }
    public string? VolatilityRegime { get; init; }
    public bool? PullbackSetupDetected { get; init; }
    public bool? PullbackReclaimConfirmed { get; init; }
    public bool? PullbackFollowThroughConfirmed { get; init; }
    public string? PullbackRejectedReason { get; init; }
    public decimal? ReclaimReferencePrice { get; init; }
    public decimal? FollowThroughReferencePrice { get; init; }
    public int? CandlesWaitedAfterReclaim { get; init; }
    public decimal? ResidualExpectedMovePercent { get; init; }
    public decimal? ResidualEstimatedNetMovePercent { get; init; }
    public decimal? ResidualRewardRisk { get; init; }
    public decimal? DistanceFromEntryToExpectedTargetPercent { get; init; }
    public bool ProfitCapture90Touched { get; init; }
    public bool ProfitCapture95Touched { get; init; }
    public bool ProfitCapture98Touched { get; init; }
    public decimal? ProfitCapture90CounterfactualNetPnlQuote { get; init; }
    public decimal? ProfitCapture95CounterfactualNetPnlQuote { get; init; }
    public decimal? ProfitCapture98CounterfactualNetPnlQuote { get; init; }
    public decimal? ProfitCaptureDeltaVsOppositeSignalExitQuote { get; init; }
    public decimal? GivebackFromMfePercent { get; init; }
    public decimal? CapturedMfePercent { get; init; }
    public decimal DurationMinutes { get; init; }
    public bool BnbPullbackGuardEnabled { get; init; }
    public bool BnbPullbackGuardRejected { get; init; }
    public string? BnbPullbackGuardRejectedReason { get; init; }
    public decimal? LockDistancePercent { get; init; }
    public decimal? MaxAllowedLockDistancePercent { get; init; }
    public bool LockReachabilityRejected { get; init; }
    public bool ExpectedMoveCapRejected { get; init; }
    public bool DistanceToInvalidationCapRejected { get; init; }
    public bool TrendStrengthCapRejected { get; init; }
    public bool ResidualExpectedMoveCapRejected { get; init; }
    public bool ResidualRewardRiskCapRejected { get; init; }
    public int? ConsecutiveBullishCandlesAtEntry { get; init; }
    public bool RetestContinuationEnabled { get; init; }
    public bool RetestContinuationRejected { get; init; }
    public string? RetestContinuationRejectedReason { get; init; }
    public decimal? RawExpectedMovePercent { get; init; }
    public decimal? CappedExpectedMovePercent { get; init; }
    public decimal? RawExpectedTargetPrice { get; init; }
    public decimal? CappedExpectedTargetPrice { get; init; }
    public string? TargetModelName { get; init; }
    public bool TargetWasCapped { get; init; }
    public string? CapReason { get; init; }
    public decimal? ExpectedMoveToRecentMfeRatio { get; init; }
    public bool HalfLockReachedBeforeExit { get; init; }
}

public sealed record ReplaySummaryRow
{
    public string Interval { get; init; } = "1m";
    public string ProfileName { get; init; } = string.Empty;
    public string Symbols { get; init; } = string.Empty;
    public int TradesCount { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public decimal WinRatePercent { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal EstimatedNetPnlQuote { get; init; }
    public decimal TotalFeeAndSpreadEstimateQuote { get; init; }
    public decimal AverageWinQuote { get; init; }
    public decimal AverageLossQuote { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public decimal AverageTradeDurationMinutes { get; init; }
    public int RawBuySignals { get; init; }
    public int ExecutedBuySignals { get; init; }
    public int BlockedBuySignals { get; init; }
    public int StrategyRejectedBuySignals { get; init; }
    public int GrossWinningTrades { get; init; }
    public decimal GrossWinRatePercent { get; init; }
    public int NetWinningTrades { get; init; }
    public decimal NetWinRatePercent { get; init; }
    public int ExpectedTargetTouchTrades { get; init; }
    public decimal ExpectedTargetTouchRatePercent { get; init; }
    public decimal AverageMfePercent { get; init; }
    public decimal AverageMaePercent { get; init; }
    public decimal ExpectedTargetCounterfactualNetPnlQuote { get; init; }
    public decimal ExpectedTargetCounterfactualDeltaQuote { get; init; }
    public int ProfitCapture90TouchTrades { get; init; }
    public int ProfitCapture95TouchTrades { get; init; }
    public int ProfitCapture98TouchTrades { get; init; }
    public decimal ProfitCapture90CounterfactualNetPnlQuote { get; init; }
    public decimal ProfitCapture95CounterfactualNetPnlQuote { get; init; }
    public decimal ProfitCapture98CounterfactualNetPnlQuote { get; init; }
    public decimal ProfitCaptureDeltaVsOppositeSignalExitQuote { get; init; }
    public string ExitPolicyName { get; init; } = "OppositeSignalOnly";
    public decimal? ProfitLockThresholdPercent { get; init; }
    public int ProfitLockExitTrades { get; init; }
    public int OppositeSignalExitTrades { get; init; }
    public int BreakevenExitTrades { get; init; }
    public int TrailingExitTrades { get; init; }
    public decimal AvgGivebackFromMfePercent { get; init; }
    public decimal AvgCapturedMfePercent { get; init; }
    public string CapturedMfeCalculationMode { get; init; } = string.Empty;
    public decimal? AvgCapturedMfeIncludingNegativeRatio { get; init; }
    public int NegativeCaptureTradeCount { get; init; }
    public bool BnbPullbackGuardEnabled { get; init; }
    public int BnbPullbackGuardBlockedSignals { get; init; }
    public IReadOnlyDictionary<string, int> BnbPullbackGuardBlockedByReason { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, decimal> NetPnlByExitPolicy { get; init; } = new Dictionary<string, decimal>();
    public IReadOnlyDictionary<string, int> BlockedByReason { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> StrategyRejectedByReason { get; init; } = new Dictionary<string, int>();
    public bool EnableLowVolatilityBreakoutEntry { get; init; }
    public bool EnableNormalTrendPullbackContinuationOverride { get; init; }
    public decimal NormalTrendPullbackMinExpectedRewardRisk { get; init; }
    public bool EnableNormalTrendMinDistanceToInvalidationFilter { get; init; }
    public decimal NormalTrendMinDistanceToInvalidationPercent { get; init; }
    public bool EnableNormalTrendNearRecentHighRejection { get; init; }
    public decimal NormalTrendNearRecentHighRequiresRewardRisk { get; init; }
    public decimal? NormalTrendNearRecentHighRequiresTrendStrengthPercent { get; init; }
    public bool EnablePullbackOverrideHighVolatilityBlock { get; init; }
    public bool EnableNormalTrendPullbackReclaimConfirmationFilter { get; init; }
    public string NormalTrendPullbackReclaimMode { get; init; } = "PreviousCandleHigh";
    public bool EnablePullbackFollowThroughV2 { get; init; }
    public bool EnablePullbackFollowThroughV3 { get; init; }
    public decimal PullbackV3MinResidualExpectedMovePercent { get; init; }
    public decimal PullbackV3MinResidualNetMovePercent { get; init; }
    public decimal PullbackV3MinResidualRewardRisk { get; init; }
    public bool PullbackV3RejectIfTargetAlreadyMostlyConsumed { get; init; }
    public decimal PullbackV3MaxTargetConsumedPercent { get; init; }
    public int BreakoutLookbackCandles { get; init; }
    public decimal BreakoutBufferPercent { get; init; }
    public int BreakoutConfirmationCandles { get; init; }
    public decimal MinBreakoutSlopePercent { get; init; }
    public bool UseConfirmedClosedCandlesForLowVolBreakout { get; init; }
    public IReadOnlyDictionary<TradingSymbol, int> SymbolBreakdown { get; init; } = new Dictionary<TradingSymbol, int>();
    public IReadOnlyDictionary<string, int> ExitReasonBreakdown { get; init; } = new Dictionary<string, int>();
}

public sealed record ProfileReplayContext(
    MovingAverageTrendStrategy Strategy,
    BacktestEntryGuard Guard,
    BnbPullbackEntryGuard? BnbPullbackGuard,
    BnbRetestContinuationV1Model? RetestContinuationModel,
    PullbackFollowThroughV2Filter PullbackV2Filter,
    ExecutionSimulator Simulator,
    ProfileSignalStats SignalStats,
    ProfileRuntimeSnapshot RuntimeSnapshot,
    IConfiguration Configuration);

public sealed record BacktestRunResult(
    IReadOnlyList<ReplaySummaryRow> Summaries,
    IReadOnlyList<SimulatedTrade> Trades,
    IReadOnlyList<BlockedEntryRecord> BlockedEntries,
    IReadOnlyList<AggregationDiagnosticsRecord> AggregationDiagnostics,
    IReadOnlyList<DataQualityIssue> DataIssues);

public sealed record ReachabilityResearchRunResult(
    IReadOnlyList<CandidateReachabilityRecord> Candidates,
    IReadOnlyList<SimulatedTrade> Trades,
    IReadOnlyList<BlockedEntryRecord> BlockedEntries,
    IReadOnlyList<CandidateReachabilitySummaryRow> Summaries,
    IReadOnlyList<ReachabilityResearchAnswer> ResearchAnswers,
    int ProfileCount);

public sealed record CandidateReachabilityRecord
{
    public string Interval { get; init; } = "1m";
    public string ProfileName { get; init; } = string.Empty;
    public string Symbols { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public DateTime TimeUtc { get; init; }
    public string SignalReason { get; init; } = string.Empty;
    public string RejectionLayer { get; init; } = string.Empty;
    public string RejectionReason { get; init; } = string.Empty;
    public bool Executed { get; init; }
    public decimal Confidence { get; init; }
    public decimal ConfidenceThreshold { get; init; }
    public decimal? ExpectedMovePercent { get; init; }
    public decimal? EstimatedNetMovePercent { get; init; }
    public decimal? ExpectedTargetPrice { get; init; }
    public string? ExpectedTargetSource { get; init; }
    public decimal? RewardRisk { get; init; }
    public decimal? DistanceToInvalidationPercent { get; init; }
    public decimal? TrendStrengthPercent { get; init; }
    public decimal? ShortMaSlopePercent { get; init; }
    public int? ConsecutiveBullishTrendCandles { get; init; }
    public bool? EntryNearRecentHigh { get; init; }
    public bool? PreviousCandleBearish { get; init; }
    public string? VolatilityRegime { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal? ForwardMfe15Percent { get; init; }
    public decimal? ForwardMfe30Percent { get; init; }
    public decimal? ForwardMfe60Percent { get; init; }
    public decimal? ForwardMae15Percent { get; init; }
    public decimal? ForwardMae30Percent { get; init; }
    public decimal? ForwardMae60Percent { get; init; }
    public decimal? Lock90DistancePercent { get; init; }
    public decimal? Lock95DistancePercent { get; init; }
    public decimal? Lock98DistancePercent { get; init; }
    public bool Lock90ReachableWithin60m { get; init; }
    public bool Lock95ReachableWithin60m { get; init; }
    public bool Lock98ReachableWithin60m { get; init; }
    public int? TimeToLock90Minutes { get; init; }
    public int? TimeToLock95Minutes { get; init; }
    public int? TimeToLock98Minutes { get; init; }
    public bool ExpectedMoveInflated { get; init; }
    public bool Lock90Reachable { get; init; }
    public bool Lock95Reachable { get; init; }
    public bool Lock98Reachable { get; init; }
    public bool FavorableButNetUntradable { get; init; }
    public bool ConfidenceFalseNegativeCandidate { get; init; }
}

public sealed record CandidateReachabilitySummaryRow
{
    public string Interval { get; init; } = "1m";
    public string ProfileName { get; init; } = string.Empty;
    public int CandidateCount { get; init; }
    public int ExecutedCount { get; init; }
    public int ConfidenceBlockedCount { get; init; }
    public int ConfidenceFalseNegativeCount { get; init; }
    public int ExpectedMoveInflatedCount { get; init; }
    public int Lock90ReachableCount { get; init; }
    public int Lock90ReachableButBlockedCount { get; init; }
    public int FavorableButNetUntradableCount { get; init; }
    public decimal? MedianExpectedMovePercent { get; init; }
    public decimal? MedianForwardMfe60Percent { get; init; }
    public decimal? MedianLock90DistancePercent { get; init; }
    public decimal? MedianReachableLock90DistancePercent { get; init; }
    public int WinnerCount { get; init; }
    public int LoserCount { get; init; }
    public decimal EstimatedNetPnlQuote { get; init; }
}

public sealed record ReachabilityResearchAnswer
{
    public string Question { get; init; } = string.Empty;
    public string Answer { get; init; } = string.Empty;
    public string Verdict { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, object?> Details { get; init; } = new Dictionary<string, object?>();
}

public sealed record SymbolIntervalReachabilityRankingRow
{
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "1m";
    public int CandidateCount { get; init; }
    public int ReachableLock90Count { get; init; }
    public decimal ReachableLock90Rate { get; init; }
    public decimal? MedianForwardMfe60Percent { get; init; }
    public decimal? MedianForwardMae60Percent { get; init; }
    public decimal? MedianLock90DistancePercent { get; init; }
    public decimal InflationRate { get; init; }
    public int ConfidenceFalseNegativeCount { get; init; }
    public decimal NetReachabilityScore { get; init; }
    public string RepeatabilityVerdict { get; init; } = string.Empty;
}

public sealed record BroadReachabilityScanRunResult(
    IReadOnlyList<CandidateReachabilityRecord> Candidates,
    IReadOnlyList<SymbolIntervalReachabilityRankingRow> Rankings,
    IReadOnlyList<ReachabilityResearchAnswer> DiscoveryAnswers,
    IReadOnlyList<TradingSymbol> SymbolsScanned,
    IReadOnlyList<string> IntervalsScanned);

public sealed record StrategyEvaluation(
    StrategySignalResult Signal,
    MarketSnapshot Snapshot);

public sealed record BlockedEntryRecord
{
    public string Interval { get; init; } = "1m";
    public string ProfileName { get; init; } = string.Empty;
    public string Symbols { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public DateTime TimeUtc { get; init; }
    public string Reason { get; init; } = string.Empty;
    public decimal Confidence { get; init; }
    public decimal ConfidenceThreshold { get; init; }
    public decimal? ExpectedMovePercent { get; init; }
    public decimal? EstimatedNetMovePercent { get; init; }
    public string? ExpectedTargetSource { get; init; }
    public string SignalReason { get; init; } = string.Empty;
    public string RejectionLayer { get; init; } = "Guard";
    public bool? PullbackSetupDetected { get; init; }
    public bool? PullbackReclaimConfirmed { get; init; }
    public bool? PullbackFollowThroughConfirmed { get; init; }
    public string? PullbackRejectedReason { get; init; }
    public decimal? ReclaimReferencePrice { get; init; }
    public decimal? FollowThroughReferencePrice { get; init; }
    public int? CandlesWaitedAfterReclaim { get; init; }
    public decimal? ResidualExpectedMovePercent { get; init; }
    public decimal? ResidualEstimatedNetMovePercent { get; init; }
    public decimal? ResidualRewardRisk { get; init; }
    public decimal? DistanceFromEntryToExpectedTargetPercent { get; init; }
    public bool BnbPullbackGuardEnabled { get; init; }
    public bool BnbPullbackGuardRejected { get; init; }
    public string? BnbPullbackGuardRejectedReason { get; init; }
    public decimal? LockDistancePercent { get; init; }
    public decimal? MaxAllowedLockDistancePercent { get; init; }
    public bool LockReachabilityRejected { get; init; }
    public bool ExpectedMoveCapRejected { get; init; }
    public bool DistanceToInvalidationCapRejected { get; init; }
    public bool TrendStrengthCapRejected { get; init; }
    public bool ResidualExpectedMoveCapRejected { get; init; }
    public bool ResidualRewardRiskCapRejected { get; init; }
    public int? ConsecutiveBullishCandlesAtEntry { get; init; }
    public bool? EntryNearRecentHigh { get; init; }
    public bool RetestContinuationEnabled { get; init; }
    public bool RetestContinuationRejected { get; init; }
    public string? RetestContinuationRejectedReason { get; init; }
    public decimal? RawExpectedMovePercent { get; init; }
    public decimal? CappedExpectedMovePercent { get; init; }
    public string? TargetModelName { get; init; }
    public bool TargetWasCapped { get; init; }
    public string? CapReason { get; init; }
    public decimal? ExpectedMoveToRecentMfeRatio { get; init; }
}

public sealed record ProfileSignalStats
{
    public int RawBuySignals { get; set; }
    public int ExecutedBuySignals { get; set; }
    public int BlockedBuySignals { get; set; }
    public int StrategyRejectedBuySignals { get; set; }
    public Dictionary<string, int> BlockedByReason { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> StrategyRejectedByReason { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int BnbPullbackGuardBlockedSignals { get; set; }
    public Dictionary<string, int> BnbPullbackGuardBlockedByReason { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int RetestContinuationBlockedSignals { get; set; }
    public Dictionary<string, int> RetestContinuationBlockedByReason { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void IncrementBnbGuardBlocked(string reason)
    {
        BnbPullbackGuardBlockedSignals++;
        BnbPullbackGuardBlockedByReason.TryGetValue(reason, out var count);
        BnbPullbackGuardBlockedByReason[reason] = count + 1;
    }

    public void IncrementRetestContinuationBlocked(string reason)
    {
        RetestContinuationBlockedSignals++;
        RetestContinuationBlockedByReason.TryGetValue(reason, out var count);
        RetestContinuationBlockedByReason[reason] = count + 1;
    }

    public void IncrementBlocked(string reason)
    {
        BlockedBuySignals++;
        BlockedByReason.TryGetValue(reason, out var count);
        BlockedByReason[reason] = count + 1;
    }

    public void IncrementStrategyRejected(string reason)
    {
        StrategyRejectedBuySignals++;
        StrategyRejectedByReason.TryGetValue(reason, out var count);
        StrategyRejectedByReason[reason] = count + 1;
    }
}

public sealed record AggregationDiagnosticsRecord
{
    public string Interval { get; init; } = "1m";
    public string SourceInterval { get; init; } = "1m";
    public string TargetInterval { get; init; } = "1m";
    public TradingSymbol Symbol { get; init; }
    public int InputCandleCount { get; init; }
    public int OutputCandleCount { get; init; }
    public int DroppedIncompleteFinalBucketCount { get; init; }
    public int InheritedGapCount { get; init; }
}
