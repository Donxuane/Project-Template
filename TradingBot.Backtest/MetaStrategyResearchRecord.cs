namespace TradingBot.Backtest;

public sealed record MetaStrategyResearchRecord
{
    public string StrategyFamily { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string WindowLabel { get; init; } = string.Empty;
    public DateTime TimeUtc { get; init; }
    public decimal? EntryPrice { get; init; }
    public decimal? ExitPrice { get; init; }
    public string? ExitReason { get; init; }
    public decimal? GrossPnlQuote { get; init; }
    public decimal? NetPnlQuote { get; init; }
    public bool? IsNetWinner { get; init; }
    public bool CandidateWasExecuted { get; init; }
    public string? RejectionReason { get; init; }
    public decimal? ExpectedMovePercent { get; init; }
    public decimal? RequiredGrossMovePercent { get; init; }
    public decimal? StopDistancePercent { get; init; }
    public decimal? RewardRisk { get; init; }
    public decimal? MfePercent { get; init; }
    public decimal? MaePercent { get; init; }
    public decimal? ForwardMfe15Percent { get; init; }
    public decimal? ForwardMfe30Percent { get; init; }
    public decimal? ForwardMfe60Percent { get; init; }
    public decimal? ForwardMae15Percent { get; init; }
    public decimal? ForwardMae30Percent { get; init; }
    public decimal? ForwardMae60Percent { get; init; }
    public int? TimeToTargetMinutes { get; init; }
    public decimal? DurationMinutes { get; init; }
    public string? VolatilityRegime { get; init; }
    public decimal? TrendStrengthPercent { get; init; }
    public decimal? ShortMaSlopePercent { get; init; }
    public decimal? RangeWidthPercent { get; init; }
    public decimal? BreakoutBodyStrengthPercent { get; init; }
    public decimal? VolumeExpansionRatio { get; init; }
    public decimal? AtrExpansionRatio { get; init; }
    public decimal? DistanceToInvalidationPercent { get; init; }
    public decimal? StopToLockRatio { get; init; }
    public string? TargetModelName { get; init; }
    public string? ExitPolicyName { get; init; }
    public string SourceDirectory { get; init; } = string.Empty;
}

public sealed record MetaStrategyFamilySummaryRow
{
    public string StrategyFamily { get; init; } = string.Empty;
    public int Trades { get; init; }
    public int ExecutedCandidates { get; init; }
    public int BlockedCandidates { get; init; }
    public int NetWinners { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal NetPerTrade { get; init; }
    public decimal NetWinnerRate { get; init; }
    public decimal StopLossRate { get; init; }
    public decimal TimeStopRate { get; init; }
    public decimal ProfitExitRate { get; init; }
    public int WindowCount { get; init; }
}

public sealed record MetaSymbolIntervalSummaryRow
{
    public string StrategyFamily { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public int Trades { get; init; }
    public int NetWinners { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal NetPerTrade { get; init; }
    public decimal NetWinnerRate { get; init; }
    public decimal StopLossRate { get; init; }
    public int WindowCount { get; init; }
    public bool MeetsMinimumSample { get; init; }
}

public sealed record MetaFeatureBucketSummaryRow
{
    public string FeatureName { get; init; } = string.Empty;
    public string BucketLabel { get; init; } = string.Empty;
    public int BucketIndex { get; init; }
    public decimal? BucketMin { get; init; }
    public decimal? BucketMax { get; init; }
    public int Trades { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal NetPerTrade { get; init; }
    public decimal NetWinnerRate { get; init; }
    public decimal ProfitExitRate { get; init; }
    public decimal StopLossRate { get; init; }
    public decimal TimeStopRate { get; init; }
    public decimal? MedianMfePercent { get; init; }
    public decimal? MedianMaePercent { get; init; }
}

public sealed record MetaExitReasonSummaryRow
{
    public string StrategyFamily { get; init; } = string.Empty;
    public string ExitReason { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal ShareOfExits { get; init; }
}

public sealed record MetaBestSubsetRow
{
    public string SubsetKey { get; init; } = string.Empty;
    public string RuleDescription { get; init; } = string.Empty;
    public int Trades { get; init; }
    public int WindowCount { get; init; }
    public IReadOnlyList<string> WindowsRepresented { get; init; } = [];
    public decimal NetPnlQuote { get; init; }
    public decimal NetPerTrade { get; init; }
    public decimal NetWinnerRate { get; init; }
    public decimal StopLossRate { get; init; }
    public bool MeetsRobustnessCriteria { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record MetaOverfitWarningRow
{
    public string WarningType { get; init; } = string.Empty;
    public string SubsetKey { get; init; } = string.Empty;
    public int Trades { get; init; }
    public int WindowCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed record MetaRuleDiscoveryRow
{
    public string RuleGroup { get; init; } = string.Empty;
    public string RuleDescription { get; init; } = string.Empty;
    public IReadOnlyList<string> FeaturesUsed { get; init; } = [];
    public bool UsesFutureInformation { get; init; }
    public bool TradableRule { get; init; }
    public string TrainWindows { get; init; } = string.Empty;
    public string HoldoutWindows { get; init; } = string.Empty;
    public int TrainTrades { get; init; }
    public int HoldoutTrades { get; init; }
    public decimal TrainNetPnlQuote { get; init; }
    public decimal HoldoutNetPnlQuote { get; init; }
    public decimal TrainNetPerTrade { get; init; }
    public decimal HoldoutNetPerTrade { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record MetaStrategyResearchDiagnostics(
    IReadOnlyList<MetaStrategyResearchRecord> Records,
    IReadOnlyList<MetaStrategyFamilySummaryRow> StrategyFamilySummary,
    IReadOnlyList<MetaSymbolIntervalSummaryRow> SymbolIntervalSummary,
    IReadOnlyList<MetaFeatureBucketSummaryRow> FeatureBucketSummary,
    IReadOnlyList<MetaExitReasonSummaryRow> ExitReasonSummary,
    IReadOnlyList<MetaBestSubsetRow> BestSubsets,
    IReadOnlyList<MetaOverfitWarningRow> OverfitWarnings,
    IReadOnlyList<MetaRuleDiscoveryRow> EntryTimeRuleDiscovery,
    IReadOnlyList<MetaRuleDiscoveryRow> OutcomeDiagnosticRuleDiscovery,
    IReadOnlyList<ReachabilityResearchAnswer> ResearchAnswers,
    MetaStrategyResearchImportReport ImportReport);

public sealed record MetaStrategyResearchImportReport(
    IReadOnlyList<string> InputDirectories,
    IReadOnlyList<MetaStrategyResearchImportSourceReport> Sources,
    bool IncludedBlockedCandidates,
    int BlockedCandidateCap);

public sealed record MetaStrategyResearchImportSourceReport(
    string Directory,
    string StrategyFamily,
    string TradeFile,
    int ExecutedImported,
    int BlockedImported,
    string? SkippedBlockedReason,
    string ImportSourceType = "Directory");
