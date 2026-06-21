using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed record DiscoveryRuleConfig(
    DirectionalRuleEntryMode EntryMode,
    decimal TargetPercent,
    decimal StopPercent,
    int MaxHoldMinutes,
    int CooldownCandles)
{
    public string Label
        => $"{EntryMode}_{TargetPercent:0.00}/{StopPercent:0.00}_{MaxHoldMinutes}m_cd{CooldownCandles}";
}

public sealed record DiscoveryRiskMetrics
{
    public decimal NetPnlQuote { get; init; }
    public int TradeCount { get; init; }
    public int WinCount { get; init; }
    public decimal WinRate { get; init; }
    public decimal? AverageWin { get; init; }
    public decimal? AverageLoss { get; init; }
    public decimal? MedianNetPerTrade { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdownQuote { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public decimal WorstTradeNet { get; init; }
    public decimal BestTradeNet { get; init; }
    public decimal AverageHoldMinutes { get; init; }
    public decimal ProfitTargetRate { get; init; }
    public decimal StopLossRate { get; init; }
    public decimal TimeStopRate { get; init; }
}

public sealed record DiscoveryRuleCandidateRow
{
    public string RuleName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string RuleDescription { get; init; } = string.Empty;
    public string FeaturesUsed { get; init; } = string.Empty;
    public int FeatureCount { get; init; }
    public string ConfigLabel { get; init; } = string.Empty;
    public string EntryMode { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public int MaxHoldMinutes { get; init; }
    public int CooldownCandles { get; init; }
    public int TotalTrades { get; init; }
    public int TrainTrades { get; init; }
    public int ValidationTrades { get; init; }
    public int HoldoutTrades { get; init; }
    public decimal TrainNet { get; init; }
    public decimal ValidationNet { get; init; }
    public decimal HoldoutNet { get; init; }
    public decimal FullHistoryNet { get; init; }
    public int PositiveMonths { get; init; }
    public int TotalMonths { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal WinRate { get; init; }
    public decimal MaxDrawdownQuote { get; init; }
    public bool TrainPositive { get; init; }
    public bool ValidationPositive { get; init; }
    public bool HoldoutPositive { get; init; }
    public bool AllSplitsPositive { get; init; }
    public bool FullHistoryPositive { get; init; }
    public bool StressPositive { get; init; }
    public bool StressPlusPositive { get; init; }
    public bool MonthlyConsistencyPass { get; init; }
    public bool TradeCountSufficient { get; init; }
    public bool OverfitWarning { get; init; }
    public bool UsesFutureInformation { get; init; }
    public int ConfigVariantsTested { get; init; }
    public int ConfigVariantsFullHistoryPositive { get; init; }
    public string BestConfigLabel { get; init; } = string.Empty;
    public decimal BestConfigTrainNet { get; init; }
    public decimal BestConfigValidationNet { get; init; }
    public decimal BestConfigHoldoutNet { get; init; }
    public decimal BestConfigFullHistoryNet { get; init; }
    public string SelectionStage { get; init; } = string.Empty;
    public string Verdict { get; init; } = string.Empty;
}

public sealed record DiscoverySplitPerformanceRow
{
    public string RuleName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string Split { get; init; } = string.Empty;
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public int WinCount { get; init; }
    public decimal WinRate { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal? AvgNetPerTrade { get; init; }
    public decimal? MedianNetPerTrade { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdownQuote { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public decimal WorstTradeNet { get; init; }
    public decimal ProfitTargetRate { get; init; }
    public decimal StopLossRate { get; init; }
    public decimal TimeStopRate { get; init; }
    public decimal AverageHoldMinutes { get; init; }
    public bool Positive { get; init; }
}

public sealed record DiscoveryWindowRobustnessRow
{
    public string RuleName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string WindowLabel { get; init; } = string.Empty;
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal WinRate { get; init; }
    public bool Positive { get; init; }
}

public sealed record DiscoveryMonthlyPerformanceRow
{
    public string RuleName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string MonthKey { get; init; } = string.Empty;
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal WinRate { get; init; }
    public bool Positive { get; init; }
}

public sealed record DiscoveryCostSensitivityRow
{
    public string RuleName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public decimal TrainNet { get; init; }
    public decimal ValidationNet { get; init; }
    public decimal HoldoutNet { get; init; }
    public decimal FullHistoryNet { get; init; }
    public bool FullHistoryPositive { get; init; }
    public bool AllSplitsPositive { get; init; }
}

public sealed record DiscoveryDrawdownRow
{
    public string RuleName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int TotalTrades { get; init; }
    public decimal MaxDrawdownQuote { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public decimal WorstTradeNet { get; init; }
    public decimal BestTradeNet { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal? AverageWin { get; init; }
    public decimal? AverageLoss { get; init; }
    public decimal EquityFinalQuote { get; init; }
    public int PositiveMonthsCount { get; init; }
    public int TotalMonthsCount { get; init; }
    public decimal WorstMonthNet { get; init; }
    public decimal BestMonthNet { get; init; }
}

public sealed record DiscoveryFeatureImportanceRow
{
    public string Feature { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public int CandidateCount { get; init; }
    public int TrainQualifiedCount { get; init; }
    public int ValidationSurvivorCount { get; init; }
    public int HoldoutPositiveCount { get; init; }
    public decimal? AvgTrainNet { get; init; }
    public decimal? AvgValidationNet { get; init; }
    public decimal? AvgHoldoutNet { get; init; }
}

public sealed record DiscoveryTradeRow
{
    public string RuleName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public DateTime EntryTimeUtc { get; init; }
    public DateTime ExitTimeUtc { get; init; }
    public string Split { get; init; } = string.Empty;
    public string MonthKey { get; init; } = string.Empty;
    public decimal EntryPrice { get; init; }
    public decimal ExitPrice { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal NetPnlQuote { get; init; }
    public string ExitReason { get; init; } = string.Empty;
    public decimal DurationMinutes { get; init; }
    public decimal AtrPercent { get; init; }
    public decimal RangeWidthPercent { get; init; }
    public decimal TrendSlopePercent { get; init; }
    public decimal DistanceFromRecentHighPercent { get; init; }
    public decimal DistanceFromRecentLowPercent { get; init; }
    public decimal? BtcReturn30mPercent { get; init; }
    public string VolatilityRegime { get; init; } = string.Empty;
    public string SessionBucket { get; init; } = string.Empty;
    public int HourOfDayUtc { get; init; }
}

public sealed record FuturesDirectionalRuleDiscoveryV2RunResult(
    IReadOnlyList<DiscoveryRuleCandidateRow> Candidates,
    IReadOnlyList<DiscoveryTradeRow> Trades,
    IReadOnlyList<DiscoverySplitPerformanceRow> SplitPerformance,
    IReadOnlyList<DiscoveryWindowRobustnessRow> WindowRobustness,
    IReadOnlyList<DiscoveryMonthlyPerformanceRow> MonthlyPerformance,
    IReadOnlyList<DiscoveryCostSensitivityRow> CostSensitivity,
    IReadOnlyList<DiscoveryDrawdownRow> Drawdown,
    IReadOnlyList<DiscoveryFeatureImportanceRow> FeatureImportance,
    IReadOnlyList<ReachabilityResearchAnswer> Answers,
    int SymbolsScanned,
    int CombosScanned,
    int CandidateCount,
    int ValidationSurvivorCount,
    int HoldoutSurvivorCount,
    DateTime DataStartUtc,
    DateTime DataEndUtc);
