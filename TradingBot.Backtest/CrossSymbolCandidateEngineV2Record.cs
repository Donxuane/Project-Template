namespace TradingBot.Backtest;

public sealed record CrossSymbolCandidateEngineV2Settings
{
    public string V1InputDirectory { get; init; } = string.Empty;
    public string? BottleneckAuditDirectory { get; init; }
    public string? ShadowRunnerDirectory { get; init; }
    public bool OneCandidatePerSymbol { get; init; } = CrossSymbolCandidateEngineV2Catalog.DefaultOneCandidatePerSymbol;
    public int MaxShadowCandidates { get; init; } = CrossSymbolCandidateEngineV2Catalog.DefaultMaxShadowCandidates;
    public decimal MaxTotalShadowNotionalUsdt { get; init; } = CrossSymbolCandidateEngineV2Catalog.DefaultMaxTotalShadowNotionalUsdt;
    public decimal MaxPerCandidateNotionalUsdt { get; init; } = CrossSymbolCandidateEngineV2Catalog.DefaultMaxPerCandidateNotionalUsdt;
}

public sealed record CrossSymbolCandidateEngineV2CandidateRow
{
    public string CandidateKey { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public string ActivationRule { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public decimal NetModerate { get; init; }
    public decimal NetModerateLatency002 { get; init; }
    public decimal NetStressPlus { get; init; }
    public decimal WinRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdown { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public decimal PositiveActivatedPeriodsPercent { get; init; }
    public bool SparseWarning { get; init; }
    public bool OverfitWarning { get; init; }
    public bool SingleClusterWarning { get; init; }
    public bool CostStabilityPassed { get; init; }
    public bool StressPassed { get; init; }
    public bool MinimumTradeCountPassed { get; init; }
    public bool DataCoveragePassed { get; init; }
    public decimal NormalizedNetPer100Usdt { get; init; }
    public decimal NormalizedNetPer1000Usdt { get; init; }
    public decimal EstimatedRequiredMarginAt1x { get; init; }
    public decimal EstimatedRequiredMarginAt3x { get; init; }
    public decimal CandidateScore { get; init; }
    public string PromotionStatus { get; init; } = string.Empty;
    public string ResearchPromotionStatus { get; init; } = string.Empty;
    public string CurrentExecutionReadiness { get; init; } = string.Empty;
    public int CurrentForwardTrades { get; init; }
    public decimal CurrentForwardNetModerate { get; init; }
    public decimal? CurrentForwardNetStressPlus { get; init; }
    public decimal CurrentForwardHealthScore { get; init; }
    public string CurrentBottleneckClassification { get; init; } = string.Empty;
    public string CurrentBottleneckRecommendation { get; init; } = string.Empty;
    public bool? LatestShadowActivationPassed { get; init; }
    public bool? LatestShadowEntrySignalPresent { get; init; }
    public bool LatestShadowWouldPlaceOrder { get; init; }
    public string LatestShadowRiskStatus { get; init; } = string.Empty;
    public string LatestShadowReasonIfBlocked { get; init; } = string.Empty;
    public string ExecutionReadinessExplanation { get; init; } = string.Empty;
    public bool CanEnterTestnetOrderMode { get; init; }
    public string? MatchedFrozenProfileName { get; init; }
    public string RejectionReason { get; init; } = string.Empty;
    public bool OverlapWarning { get; init; }
    public string LegacyBottleneckRisk { get; init; } = string.Empty;
    public string BottleneckRisk { get; init; } = string.Empty;
    public string V1Recommendation { get; init; } = string.Empty;
    public string SuggestedFrozenProfileName { get; init; } = string.Empty;
    public bool SelectedForShadowPortfolio { get; init; }
    public bool SelectedForExecutionReadyPortfolio { get; init; }
    public decimal AssignedShadowNotionalUsdt { get; init; }
}

public sealed record CrossSymbolCandidateEngineV2RejectionRow
{
    public string CandidateKey { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string ActivationRule { get; init; } = string.Empty;
    public string PromotionStatus { get; init; } = string.Empty;
    public string RejectionReason { get; init; } = string.Empty;
    public decimal CandidateScore { get; init; }
}

public sealed record CrossSymbolCandidateEngineV2ShadowDecisionRow
{
    public DateTime TimestampUtc { get; init; }
    public string CandidateKey { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string ActivationRule { get; init; } = string.Empty;
    public bool ActivationPassed { get; init; }
    public string ActivationReason { get; init; } = string.Empty;
    public bool EntrySignalPresent { get; init; }
    public string EntryReason { get; init; } = string.Empty;
    public bool WouldPlaceOrder { get; init; }
    public string OrderSide { get; init; } = string.Empty;
    public decimal AssumedNotionalUsdt { get; init; }
    public decimal? NetPnlPer100Usdt { get; init; }
    public decimal? RequiredMarginAtLeverage { get; init; }
    public int Leverage { get; init; }
    public string RiskStatus { get; init; } = string.Empty;
    public string ReasonIfBlocked { get; init; } = string.Empty;
    public string PromotionStatus { get; init; } = string.Empty;
    public bool ShadowResearchOnly { get; init; } = true;
    public bool ForwardEvidenceRequired { get; init; } = true;
    public string BottleneckRisk { get; init; } = string.Empty;
    public string CurrentExecutionReadiness { get; init; } = string.Empty;
    public bool CanEnterTestnetOrderMode { get; init; }
}

public sealed record CrossSymbolCandidateEngineV2ContributionRow
{
    public string CandidateKey { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal AssignedNotionalUsdt { get; init; }
    public int TradeCount { get; init; }
    public decimal NetPer100Usdt { get; init; }
    public decimal NetPer1000Usdt { get; init; }
    public decimal ShareOfPortfolioNetPer1000Usdt { get; init; }
}

public sealed record CrossSymbolCandidateEngineV2ShadowPortfolioRow
{
    public int PromotedCandidateCount { get; init; }
    public decimal TotalAssignedNotionalUsdt { get; init; }
    public int TotalTrades { get; init; }
    public decimal NetPer100Usdt { get; init; }
    public decimal NetPer1000Usdt { get; init; }
    public decimal MaxDrawdownPer1000Usdt { get; init; }
    public decimal WorstDayPer1000Usdt { get; init; }
    public decimal BestDayPer1000Usdt { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public int OverlappingSignalCount { get; init; }
    public IReadOnlyList<CrossSymbolCandidateEngineV2ContributionRow> CandidateContributionBreakdown { get; init; } = [];
    public IReadOnlyList<CrossSymbolCandidateEngineV2ShadowDecisionRow> ShadowDecisions { get; init; } = [];
}

public sealed record CrossSymbolCandidateEngineV2SummaryRow
{
    public DateTime RunAtUtc { get; init; }
    public string V1InputDirectory { get; init; } = string.Empty;
    public string? BottleneckAuditDirectory { get; init; }
    public int CandidatesEvaluated { get; init; }
    public int PromoteToShadowCount { get; init; }
    public int KeepIncubatingCount { get; init; }
    public int NeedsMoreDataCount { get; init; }
    public int ParkCount { get; init; }
    public int RejectCount { get; init; }
    public int ShadowPortfolioCandidateCount { get; init; }
    public int ResearchPromotedCount { get; init; }
    public int ExecutableShadowCandidateCount { get; init; }
    public int CanEnterTestnetOrderModeCount { get; init; }
    public int BlockedByLookbackStarvationCount { get; init; }
    public int BlockedByMissingEntrySignalCount { get; init; }
    public int BlockedByStressNegativeForwardCount { get; init; }
    public int ExecutionReadyPortfolioCandidateCount { get; init; }
    public bool OneCandidatePerSymbol { get; init; }
    public int MaxShadowCandidates { get; init; }
    public decimal MaxTotalShadowNotionalUsdt { get; init; }
    public decimal MaxPerCandidateNotionalUsdt { get; init; }
    public bool BacktestOnly { get; init; } = true;
    public bool ShadowDryRunOnly { get; init; } = true;
    public bool RealOrdersPlaced { get; init; }
    public bool LiveFuturesRecommended { get; init; }
    public string CompactSummaryLine { get; init; } = string.Empty;
}

public sealed record CrossSymbolCandidateEngineV2RunResult(
    CrossSymbolCandidateEngineV2SummaryRow Summary,
    IReadOnlyList<CrossSymbolCandidateEngineV2CandidateRow> Candidates,
    IReadOnlyList<CrossSymbolCandidateEngineV2RejectionRow> Rejections,
    CrossSymbolCandidateEngineV2ShadowPortfolioRow ShadowPortfolio,
    CrossSymbolCandidateEngineV2ShadowPortfolioRow ExecutionReadyPortfolio);
