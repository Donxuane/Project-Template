namespace TradingBot.Application.CrossSymbolShadowBridge;

public sealed record CrossSymbolShadowBridgeCandidateImport
{
    public string CandidateKey { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public string ActivationRule { get; init; } = string.Empty;
    public string ResearchPromotionStatus { get; init; } = string.Empty;
    public string PromotionStatus { get; init; } = string.Empty;
    public string CurrentExecutionReadiness { get; init; } = string.Empty;
    public bool CanEnterTestnetOrderMode { get; init; }
    public int CurrentForwardTrades { get; init; }
    public decimal CurrentForwardNetModerate { get; init; }
    public decimal? CurrentForwardNetStressPlus { get; init; }
    public bool? LatestShadowActivationPassed { get; init; }
    public bool? LatestShadowEntrySignalPresent { get; init; }
    public string LatestShadowRiskStatus { get; init; } = string.Empty;
    public string ExecutionReadinessExplanation { get; init; } = string.Empty;
    public decimal AssignedShadowNotionalUsdt { get; init; }
    public bool SelectedForShadowPortfolio { get; init; }
    public bool SelectedForExecutionReadyPortfolio { get; init; }
    public string BottleneckRisk { get; init; } = string.Empty;
    public string? MatchedFrozenProfileName { get; init; }
}

public sealed record CrossSymbolShadowBridgeSummaryImport
{
    public DateTime RunAtUtc { get; init; }
    public string CompactSummaryLine { get; init; } = string.Empty;
    public int ResearchPromotedCount { get; init; }
    public int CanEnterTestnetOrderModeCount { get; init; }
    public int ExecutionReadyPortfolioCandidateCount { get; init; }
    public ExecutionReadyPortfolioImport? ExecutionReadyPortfolio { get; init; }
}

public sealed record ExecutionReadyPortfolioImport
{
    public int PromotedCandidateCount { get; init; }
    public decimal TotalAssignedNotionalUsdt { get; init; }
    public IReadOnlyList<PortfolioContributionImport> CandidateContributionBreakdown { get; init; } = [];
}

public sealed record PortfolioContributionImport
{
    public string CandidateKey { get; init; } = string.Empty;
    public decimal AssignedNotionalUsdt { get; init; }
}

public sealed record CrossSymbolShadowBridgeExecutionPortfolioImport
{
    public PortfolioWrapper? Portfolio { get; init; }

    public sealed record PortfolioWrapper
    {
        public int PromotedCandidateCount { get; init; }
        public IReadOnlyList<PortfolioContributionImport> CandidateContributionBreakdown { get; init; } = [];
    }
}

public sealed record CrossSymbolShadowBridgeInputProbe
{
    public string CandidateInputDirectory { get; init; } = string.Empty;
    public string CandidatesFilePath { get; init; } = string.Empty;
    public string SummaryFilePath { get; init; } = string.Empty;
    public string ExecutionPortfolioFilePath { get; init; } = string.Empty;
    public bool CandidateInputDirectoryExists { get; init; }
    public bool CandidatesFileExists { get; init; }
    public bool SummaryFileExists { get; init; }
    public bool ExecutionPortfolioFileExists { get; init; }
    public bool RequiredInputAvailable => CandidateInputDirectoryExists && CandidatesFileExists;
    public IReadOnlyList<string> MissingFiles { get; init; } = [];
}

public sealed record CrossSymbolShadowBridgeInputBundle
{
    public string CandidateInputDirectory { get; init; } = string.Empty;
    public string? ShadowDecisionsPath { get; init; }
    public string? BottleneckAuditPath { get; init; }
    public bool ShadowDecisionsAvailable { get; init; }
    public bool BottleneckAuditAvailable { get; init; }
    public CrossSymbolShadowBridgeSummaryImport? Summary { get; init; }
    public CrossSymbolShadowBridgeExecutionPortfolioImport? ExecutionReadyPortfolio { get; init; }
    public IReadOnlyList<CrossSymbolShadowBridgeCandidateImport> Candidates { get; init; } = [];
}

public enum CrossSymbolShadowBridgeCandidateCategory
{
    ExecutionReadyCandidates,
    ResearchPromotedShadowOnly,
    RejectedOrParked
}

public sealed record CrossSymbolShadowBridgeDecisionRow
{
    public DateTime EvaluatedAtUtc { get; init; }
    public string CandidateKey { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public string ActivationRule { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string ResearchPromotionStatus { get; init; } = string.Empty;
    public string CurrentExecutionReadiness { get; init; } = string.Empty;
    public bool CanEnterTestnetOrderMode { get; init; }
    public int CurrentForwardTrades { get; init; }
    public decimal CurrentForwardNetModerate { get; init; }
    public decimal? CurrentForwardNetStressPlus { get; init; }
    public bool? LatestShadowActivationPassed { get; init; }
    public bool? LatestShadowEntrySignalPresent { get; init; }
    public string LatestShadowRiskStatus { get; init; } = string.Empty;
    public string ExecutionReadinessExplanation { get; init; } = string.Empty;
    public decimal AssignedShadowNotionalUsdt { get; init; }
    public bool WouldPlaceOrder { get; init; }
    public string ReasonIfBlocked { get; init; } = string.Empty;
}

public sealed record CrossSymbolShadowBridgeRiskRow
{
    public string CandidateKey { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string RiskStatus { get; init; } = string.Empty;
    public string ReasonIfBlocked { get; init; } = string.Empty;
    public bool OrdersPermitted { get; init; }
    public bool TestnetOrdersPermitted { get; init; }
    public bool RealOrdersPermitted { get; init; }
    public bool ShadowOnly { get; init; }
}

public sealed record CrossSymbolShadowBridgeStatus
{
    public DateTime EvaluatedAtUtc { get; init; }
    public string Status { get; init; } = string.Empty;
    public string CompactSummaryLine { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public bool ShadowOnly { get; init; } = true;
    public bool BacktestOnly { get; init; }
    public bool DryRunOnly { get; init; } = true;
    public bool AllowOrders { get; init; }
    public bool AllowTestnetOrders { get; init; }
    public bool AllowRealOrders { get; init; }
    public bool RealOrdersPlaced { get; init; }
    public bool LiveFuturesRecommended { get; init; }
    public string CandidateInputDirectory { get; init; } = string.Empty;
    public string OutputDirectory { get; init; } = string.Empty;
    public int TotalCandidatesLoaded { get; init; }
    public int ExecutionReadyCandidateCount { get; init; }
    public int ResearchPromotedShadowOnlyCount { get; init; }
    public int RejectedOrParkedCount { get; init; }
    public int ExecutionReadyPortfolioCandidateCount { get; init; }
    public bool ShadowDecisionsAvailable { get; init; }
    public bool BottleneckAuditAvailable { get; init; }
    public bool CandidatesFileExists { get; init; }
    public bool SummaryFileExists { get; init; }
    public bool ExecutionPortfolioFileExists { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<string> MissingInputFiles { get; init; } = [];
}

public sealed record CrossSymbolShadowBridgeRunResult
{
    public CrossSymbolShadowBridgeStatus Status { get; init; } = new();
    public IReadOnlyList<CrossSymbolShadowBridgeDecisionRow> Decisions { get; init; } = [];
    public IReadOnlyList<CrossSymbolShadowBridgeRiskRow> RiskRows { get; init; } = [];
}
