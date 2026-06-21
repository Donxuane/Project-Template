namespace TradingBot.Backtest;

public sealed record CurrentOpportunityWatchV1StatusRow
{
    public const string DiagnosticWarning =
        "Current opportunity watch V1 is diagnostic/shadow only. Near-miss is a watch signal only, not forward proof. Never places orders.";

    public DateTime RunAtUtc { get; init; }
    public DateTime EvaluatedAtUtc { get; init; }
    public string WatchStatus { get; init; } = string.Empty;
    public int EvaluatedCandidateCount { get; init; }
    public int ActivationPassedCount { get; init; }
    public int EntrySignalPresentCount { get; init; }
    public int ActionableShadowCount { get; init; }
    public int TopNearMissCount { get; init; }
    public string TopWatchCandidate { get; init; } = string.Empty;
    public string TopWatchSymbol { get; init; } = string.Empty;
    public string TopWatchInterval { get; init; } = string.Empty;
    public string TopWatchDirection { get; init; } = string.Empty;
    public string TopWatchActivationRule { get; init; } = string.Empty;
    public decimal TopWatchDistanceToEntryPercent { get; init; }
    public string TopWatchFailedCondition { get; init; } = string.Empty;
    public string ExactEntrySignalCandidate { get; init; } = string.Empty;
    public string ExactEntryAppearedNote { get; init; } = string.Empty;
    public DateTime DataLastCandleUtc { get; init; }
    public bool EvalAdvancedSincePreviousCycle { get; init; }
    public int CycleNumber { get; init; }
    public bool UsesConfirmedClosedCandlesOnly { get; init; } = true;
    public bool WouldPlaceOrder { get; init; }
    public bool RealOrdersPlaced { get; init; }
    public bool LiveFuturesRecommended { get; init; }

    // Fixed-frequency study integration (diagnostic only).
    public int FixedFrequencyPromotedCount { get; init; }
    public int FixedFrequencyWatchedCount { get; init; }
    public int FixedFrequencyBlockedByReadinessCount { get; init; }
    public int FixedFrequencyNeedsIncubationCount { get; init; }
    public bool FixedFrequencyExactEntryPresent { get; init; }
    public string FixedFrequencyExactEntryCandidate { get; init; } = string.Empty;
    public string ClosestFixedFrequencyCandidate { get; init; } = string.Empty;
    public string ClosestFixedFrequencyWatchReason { get; init; } = string.Empty;
    public bool CountingBugFixed { get; init; }
    public bool CanEnterTestnetOrderMode { get; init; }

    public string CompactSummaryLine { get; init; } = string.Empty;
    public NormalizedRiskPnlMetrics NormalizedRisk { get; init; } = new();
    public CurrentOpportunityWatchV1PlainEnglish PlainEnglish { get; init; } = new();
}

public sealed record CurrentOpportunityWatchV1PlainEnglish
{
    public string IsExactEntrySignalNow { get; init; } = string.Empty;
    public string IsNearMissWorthWatching { get; init; } = string.Empty;
    public string ClosestCandidate { get; init; } = string.Empty;
    public string MissingCondition { get; init; } = string.Empty;
    public string ShouldWeTrade { get; init; } = "No. Diagnostic shadow mode never places orders and near-miss is not forward proof.";

    // Fixed-frequency study questions.
    public string WhichFixedFrequencyCandidatesWatched { get; init; } = string.Empty;
    public string WhichFixedFrequencyClosestOrActivated { get; init; } = string.Empty;
    public string AnyCurrentExactEntries { get; init; } = string.Empty;
    public string AnyBlockedByCurrentReadiness { get; init; } = string.Empty;
    public string ShouldWeTradeNow { get; init; } =
        "No. This is diagnostic shadow mode. Historical exact-entry frequency is research, not forward proof. No orders are placed and testnet/live remain disabled until explicit forward and current-execution-readiness gates are built and pass.";
}

public sealed record CurrentOpportunityWatchV1HistoryRow
{
    public DateTime RunAtUtc { get; init; }
    public DateTime EvaluatedAtUtc { get; init; }
    public string WatchStatus { get; init; } = string.Empty;
    public int EvaluatedCandidateCount { get; init; }
    public int ActivationPassedCount { get; init; }
    public int EntrySignalPresentCount { get; init; }
    public int ActionableShadowCount { get; init; }
    public int TopNearMissCount { get; init; }
    public string TopWatchCandidate { get; init; } = string.Empty;
    public string ExactEntrySignalCandidate { get; init; } = string.Empty;
}

public sealed record CurrentOpportunityWatchV1WatchlistRow
{
    public int WatchRank { get; init; }
    public string WatchKind { get; init; } = string.Empty;
    public string CandidateKey { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public string ActivationRule { get; init; } = string.Empty;
    public decimal ResearchScore { get; init; }
    public decimal NetModerate { get; init; }
    public decimal NetStressPlus { get; init; }
    public bool ActivationCurrentlyPassed { get; init; }
    public bool BaseEntrySignalPresentNow { get; init; }
    public bool WouldBeShadowActionable { get; init; }
    public string NearMissClassification { get; init; } = string.Empty;
    public decimal DistanceToEntryPercent { get; init; }
    public string FailedCondition { get; init; } = string.Empty;
    public bool SparseWarning { get; init; }
    public bool OverfitWarning { get; init; }
    public bool SingleClusterWarning { get; init; }
    public bool IsSolUsdt30mShort { get; init; }

    // Fixed-frequency study integration (diagnostic only).
    public bool IsFixedFrequencyPromoted { get; init; }
    public string FixedFrequencyRecommendation { get; init; } = string.Empty;
    public int ExactEntryCountInsideActivatedWindows { get; init; }
    public decimal ExactEntriesPerDay { get; init; }
    public DateTime? LastExactEntryUtc { get; init; }
    public decimal? DaysSinceLastExactEntry { get; init; }
    public string WatchReason { get; init; } = string.Empty;
}

// Diagnostic-only watch row for fixed-frequency study promoted candidates.
// Historical exact-entry frequency is research, not forward proof. Never places orders.
public sealed record CurrentOpportunityWatchV1FixedFrequencyRow
{
    public int WatchPriority { get; init; }
    public string CandidateKey { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public string ActivationRule { get; init; } = string.Empty;
    public string FixedFrequencyRecommendation { get; init; } = string.Empty;
    public int ExactEntryCountInsideActivatedWindows { get; init; }
    public decimal ExactEntriesPerDay { get; init; }
    public DateTime? LastExactEntryUtc { get; init; }
    public decimal? DaysSinceLastExactEntry { get; init; }
    public decimal NetModerate { get; init; }
    public decimal NetStressPlus { get; init; }
    public decimal WinRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public string ResearchPromotionStatus { get; init; } = string.Empty;
    public string CurrentExecutionReadiness { get; init; } = string.Empty;
    public string CurrentBottleneckClassification { get; init; } = string.Empty;
    public string CurrentBottleneckRecommendation { get; init; } = string.Empty;
    public bool ActivationCurrentlyPassed { get; init; }
    public bool BaseEntrySignalPresentNow { get; init; }
    public bool ActionableShadow { get; init; }
    public string WatchPriorityTier { get; init; } = string.Empty;
    public string WatchReason { get; init; } = string.Empty;
    public string WatchStatus { get; init; } = string.Empty;
    public bool WouldPlaceOrder { get; init; }
    public bool CanEnterTestnetOrderMode { get; init; }
    public NormalizedRiskPnlMetrics NormalizedRisk { get; init; } = new();
}

public sealed record CurrentOpportunityWatchV1RunResult(
    CurrentOpportunityWatchV1StatusRow Status,
    IReadOnlyList<CurrentOpportunityWatchV1HistoryRow> History,
    IReadOnlyList<CurrentOpportunityWatchV1WatchlistRow> TopWatchlist,
    IReadOnlyList<CurrentOpportunityWatchV1WatchlistRow> ExactEntrySignals,
    IReadOnlyList<CurrentOpportunityWatchV1WatchlistRow> NearMisses,
    IReadOnlyList<CurrentOpportunityWatchV1FixedFrequencyRow> FixedFrequencyWatchlist);
