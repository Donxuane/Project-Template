namespace TradingBot.Backtest;

public sealed record CurrentOpportunityScannerV1CandidateRow
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
    public int TradeCount { get; init; }
    public decimal NetModerate { get; init; }
    public decimal NetStressPlus { get; init; }
    public decimal WinRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public bool SparseWarning { get; init; }
    public bool OverfitWarning { get; init; }
    public bool SingleClusterWarning { get; init; }
    public DateTime LatestCandleUtc { get; init; }
    public bool ActivationCurrentlyPassed { get; init; }
    public string ActivationFailureReason { get; init; } = string.Empty;
    public bool BaseEntrySignalPresentNow { get; init; }
    public string EntrySignalFailureReason { get; init; } = string.Empty;
    public bool WouldBeShadowActionable { get; init; }
    public bool WouldPlaceOrder { get; init; }
    public string ReasonIfBlocked { get; init; } = string.Empty;
    public string BlockedReasonCategory { get; init; } = string.Empty;
    public decimal NormalizedNetPer100Usdt { get; init; }
    public NormalizedRiskPnlMetrics NormalizedRisk { get; init; } = new();
    public decimal AssignedShadowNotionalUsdt { get; init; }
    public string RiskStatus { get; init; } = string.Empty;
    public string PrecisionStatus { get; init; } = string.Empty;
    public bool AlmostActionable { get; init; }
}

public sealed record CurrentOpportunityScannerV1SummaryRow
{
    public const string DiagnosticWarning =
        "Current opportunity scanner V1 is diagnostic/shadow only. Results are point-in-time and are not forward proof. Never places orders.";

    public DateTime RunAtUtc { get; init; }
    public DateTime EvaluatedAtUtc { get; init; }
    public string CompactSummaryLine { get; init; } = string.Empty;
    public NormalizedRiskPnlMetrics NormalizedRisk { get; init; } = new();
    public int EvaluatedCandidateCount { get; init; }
    public int ActivationPassedCount { get; init; }
    public int BaseEntrySignalPresentCount { get; init; }
    public int ActionableShadowCount { get; init; }
    public int AlmostActionableCount { get; init; }
    public int BlockedByActivationCount { get; init; }
    public int BlockedByEntryMissingCount { get; init; }
    public int BlockedByResearchStressCount { get; init; }
    public int BlockedByBottleneckCount { get; init; }
    public int BlockedByExecutionReadinessCount { get; init; }
    public int BlockedByResearchQualityCount { get; init; }
    public IReadOnlyList<string> TopBlockers { get; init; } = [];
    public IReadOnlyList<CurrentOpportunityScannerV1CandidateRow> TopActionableCandidates { get; init; } = [];
    public IReadOnlyList<CurrentOpportunityScannerV1CandidateRow> TopAlmostActionableCandidates { get; init; } = [];
    public bool BacktestOnly { get; init; } = true;
    public bool RealOrdersPlaced { get; init; }
    public bool LiveFuturesRecommended { get; init; }
    public string V1InputDirectory { get; init; } = string.Empty;
    public string V2InputDirectory { get; init; } = string.Empty;
}

public sealed record CurrentOpportunityScannerV1RunResult(
    CurrentOpportunityScannerV1SummaryRow Summary,
    IReadOnlyList<CurrentOpportunityScannerV1CandidateRow> Candidates,
    IReadOnlyList<CurrentOpportunityScannerV1CandidateRow> ActionableShadow,
    IReadOnlyList<CurrentOpportunityScannerV1CandidateRow> Blocked);

public sealed record CurrentOpportunityScannerV1InputBundle
{
    public string V1InputDirectory { get; init; } = string.Empty;
    public string V2InputDirectory { get; init; } = string.Empty;
    public DateTime? StudyStartUtc { get; init; }
    public IReadOnlyList<CrossSymbolLeaderboardRow> Leaderboard { get; init; } = [];
    public IReadOnlyDictionary<string, CrossSymbolCandidateEngineV2CandidateRow> V2ByKey { get; init; }
        = new Dictionary<string, CrossSymbolCandidateEngineV2CandidateRow>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<FrozenProfileBottleneckAuditRow> BottleneckAudit { get; init; } = [];
    public IReadOnlyDictionary<string, CrossSymbolCandidateEngineV2ShadowDecisionImportRow> ShadowByScope { get; init; }
        = new Dictionary<string, CrossSymbolCandidateEngineV2ShadowDecisionImportRow>(StringComparer.OrdinalIgnoreCase);
}
