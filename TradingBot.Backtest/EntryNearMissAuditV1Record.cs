namespace TradingBot.Backtest;

public sealed record EntryNearMissAuditV1CandidateRow
{
    public string CandidateKey { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public string ActivationRule { get; init; } = string.Empty;
    public string ResearchPromotionStatus { get; init; } = string.Empty;
    public string CurrentExecutionReadiness { get; init; } = string.Empty;
    public decimal ResearchScore { get; init; }
    public decimal NetModerate { get; init; }
    public decimal NetStressPlus { get; init; }
    public decimal WinRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public bool SparseWarning { get; init; }
    public bool OverfitWarning { get; init; }
    public bool SingleClusterWarning { get; init; }
    public DateTime LatestCandleUtc { get; init; }
    public bool ActivationCurrentlyPassed { get; init; }
    public bool BaseEntrySignalPresentNow { get; init; }
    public string EntrySignalFailureReason { get; init; } = string.Empty;
    public decimal NearMissScore { get; init; }
    public string NearMissClassification { get; init; } = string.Empty;
    public decimal DistanceToEntryPercent { get; init; }
    public decimal DistanceToNearExtremeThresholdPercent { get; init; }
    public decimal LatestClose { get; init; }
    public decimal RecentHigh { get; init; }
    public decimal RecentLow { get; init; }
    public decimal DistanceToRecentHighPercent { get; init; }
    public decimal DistanceToRecentLowPercent { get; init; }
    public string VolatilityState { get; init; } = string.Empty;
    public bool ElevatedVolPassed { get; init; }
    public bool TrendContextPassed { get; init; }
    public bool FlowContextPassed { get; init; }
    public bool CandlePatternPassed { get; init; }
    public IReadOnlyList<string> RequiredEntryConditions { get; init; } = [];
    public IReadOnlyList<string> PassedEntryConditions { get; init; } = [];
    public IReadOnlyList<string> FailedEntryConditions { get; init; } = [];
    public int FailedConditionCount { get; init; }
    public bool WouldBecomeEntryIfOneConditionRelaxed { get; init; }
    public string OneConditionRelaxationName { get; init; } = string.Empty;
    public string HypotheticalSignalDirection { get; init; } = string.Empty;
    public string HypotheticalRiskNote { get; init; } = string.Empty;
    public bool IsTopNearMiss { get; init; }
}

public sealed record EntryNearMissAuditV1PlainEnglish
{
    public string WhyActivatedCandidatesNotEntering { get; init; } = string.Empty;
    public string AreAnyCloseEnoughToWatchAggressively { get; init; } = string.Empty;
    public string SolUsdt30mShortProximity { get; init; } = string.Empty;
    public string ShouldCreateIncubationFromNearMiss { get; init; } = string.Empty;
    public string ShouldWaitForActualEntrySignal { get; init; } = string.Empty;
}

public sealed record EntryNearMissAuditV1SummaryRow
{
    public const string DiagnosticWarning =
        "Entry near-miss audit V1 is diagnostic/shadow only. Near-miss results are not forward proof and must not be traded. Never places orders.";

    public DateTime RunAtUtc { get; init; }
    public DateTime EvaluatedAtUtc { get; init; }
    public string CompactSummaryLine { get; init; } = string.Empty;
    public int EvaluatedActivationPassedCount { get; init; }
    public int ExactEntrySignalCount { get; init; }
    public int OneConditionAwayCount { get; init; }
    public int TwoConditionsAwayCount { get; init; }
    public int PriceDistanceNearCount { get; init; }
    public int FarFromEntryCount { get; init; }
    public int ResearchWeakIgnoreCount { get; init; }
    public int BottleneckBlockedIgnoreCount { get; init; }
    public int TopNearMissCount { get; init; }
    public string TopNearMissCandidate { get; init; } = string.Empty;
    public string TopNearMissReason { get; init; } = string.Empty;
    public string EntryRarityVerdict { get; init; } = string.Empty;
    public IReadOnlyList<EntryNearMissAuditV1CandidateRow> TopNearMisses { get; init; } = [];
    public IReadOnlyList<EntryNearMissAuditV1CandidateRow> FarMisses { get; init; } = [];
    public EntryNearMissAuditV1PlainEnglish PlainEnglish { get; init; } = new();
    public string ScannerInputDirectory { get; init; } = string.Empty;
    public string V1InputDirectory { get; init; } = string.Empty;
    public string V2InputDirectory { get; init; } = string.Empty;
    public bool BacktestOnly { get; init; } = true;
    public bool RealOrdersPlaced { get; init; }
    public bool LiveFuturesRecommended { get; init; }
}

public sealed record EntryNearMissAuditV1RunResult(
    EntryNearMissAuditV1SummaryRow Summary,
    IReadOnlyList<EntryNearMissAuditV1CandidateRow> Candidates,
    IReadOnlyList<EntryNearMissAuditV1CandidateRow> TopNearMisses,
    IReadOnlyList<EntryNearMissAuditV1CandidateRow> FarMisses);

public sealed record EntryNearMissAuditV1InputBundle
{
    public string ScannerInputDirectory { get; init; } = string.Empty;
    public string V1InputDirectory { get; init; } = string.Empty;
    public string V2InputDirectory { get; init; } = string.Empty;
    public DateTime? StudyStartUtc { get; init; }
    public IReadOnlyList<CurrentOpportunityScannerV1CandidateRow> ScannerCandidates { get; init; } = [];
    public IReadOnlyDictionary<string, CrossSymbolLeaderboardRow> LeaderboardByKey { get; init; }
        = new Dictionary<string, CrossSymbolLeaderboardRow>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<FrozenProfileBottleneckAuditRow> BottleneckAudit { get; init; } = [];
}
