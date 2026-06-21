using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public enum DirectionalRuleV2OverlapPolicy
{
    AllowOverlap,
    OneOpenTradePerRuleSymbol,
    OneOpenTradePerSymbol
}

public enum DirectionalRuleV2SkipReason
{
    SkippedOverlapOpenTrade,
    SkippedCooldown,
    SkippedPriorityOtherRule
}

public enum DirectionalRuleV2RulePriority
{
    None,
    Rule01First,
    Rule05First,
    StrongerEdgeFirst
}

public sealed record DirectionalRuleV2Candidate(
    string RuleKey,
    TradingSymbol Symbol,
    string Interval);

public sealed record DirectionalRuleV2SimulationProfile(
    string ProfileKey,
    DirectionalRuleDefinition Rule,
    TradingSymbol Symbol,
    string Interval,
    decimal TargetPercent,
    decimal StopPercent,
    int MaxHoldMinutes,
    DirectionalRuleEntryMode EntryMode,
    DirectionalRuleV2OverlapPolicy OverlapPolicy,
    int CooldownCandlesAfterExit,
    DirectionalRuleV2RulePriority RulePriority = DirectionalRuleV2RulePriority.None);

public sealed record DirectionalRuleV2SkippedSignalRecord
{
    public string ProfileKey { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = string.Empty;
    public string WindowLabel { get; init; } = string.Empty;
    public DateTime TimeUtc { get; init; }
    public string SkipReason { get; init; } = string.Empty;
    public string OverlapPolicy { get; init; } = string.Empty;
    public int CooldownCandlesAfterExit { get; init; }
    public string EntryMode { get; init; } = string.Empty;
}

public sealed record DirectionalRuleV2TradeRecord
{
    public string ProfileKey { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
    public LongShortDirection Direction { get; init; }
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = string.Empty;
    public string WindowLabel { get; init; } = string.Empty;
    public DateTime TimeUtc { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal ExitPrice { get; init; }
    public string ExitReason { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public int MaxHoldMinutes { get; init; }
    public string CostScenarioLabel { get; init; } = string.Empty;
    public decimal GrossPnlQuote { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal FundingEstimateQuote { get; init; }
    public decimal SlippageEstimateQuote { get; init; }
    public decimal? BtcReturn30mPercent { get; init; }
    public string VolatilityRegime { get; init; } = string.Empty;
    public decimal RangeWidthPercent { get; init; }
    public decimal DistanceFromRecentHighPercent { get; init; }
    public decimal DistanceFromRecentLowPercent { get; init; }
    public decimal AtrPercent { get; init; }
    public decimal TrendSlopePercent { get; init; }
    public decimal? MfePercent { get; init; }
    public decimal? MaePercent { get; init; }
    public decimal DurationMinutes { get; init; }
    public string EntryMode { get; init; } = string.Empty;
    public string OverlapPolicy { get; init; } = string.Empty;
    public int CooldownCandlesAfterExit { get; init; }
}

public sealed record DirectionalRuleV2SummaryRow
{
    public string ProfileKey { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = string.Empty;
    public string WindowLabel { get; init; } = string.Empty;
    public string EntryMode { get; init; } = string.Empty;
    public string OverlapPolicy { get; init; } = string.Empty;
    public int CooldownCandlesAfterExit { get; init; }
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public int MaxHoldMinutes { get; init; }
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int SignalCount { get; init; }
    public int ExecutedTrades { get; init; }
    public int SkippedOverlapSignals { get; init; }
    public int SkippedCooldownSignals { get; init; }
    public int SkippedPrioritySignals { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal? AvgNetPnlPerTrade { get; init; }
    public decimal? MedianNetPerTrade { get; init; }
    public decimal WinRate { get; init; }
    public decimal? AverageWin { get; init; }
    public decimal? AverageLoss { get; init; }
    public decimal? ProfitFactor { get; init; }
    public bool AggregateNetPositive { get; init; }
    public bool Window30dNetPositive { get; init; }
    public bool Window60dNetPositive { get; init; }
    public bool Window90dNetPositive { get; init; }
    public bool AllWindowsPositive { get; init; }
    public bool Holdout90dPositive { get; init; }
    public bool StressAggregatePositive { get; init; }
    public bool StressAllWindowsPositive { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record DirectionalRuleV2WindowRobustnessRow
{
    public string ProfileKey { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = string.Empty;
    public string EntryMode { get; init; } = string.Empty;
    public string OverlapPolicy { get; init; } = string.Empty;
    public int CooldownCandlesAfterExit { get; init; }
    public int MaxHoldMinutes { get; init; }
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int Window30dTrades { get; init; }
    public int Window60dTrades { get; init; }
    public int Window90dTrades { get; init; }
    public decimal Window30dNetPnl { get; init; }
    public decimal Window60dNetPnl { get; init; }
    public decimal Window90dNetPnl { get; init; }
    public decimal AggregateNetPnl { get; init; }
    public bool AggregateNetPositive { get; init; }
    public bool Window30dNetPositive { get; init; }
    public bool Window60dNetPositive { get; init; }
    public bool Window90dNetPositive { get; init; }
    public bool AllWindowsPositive { get; init; }
    public bool Holdout90dPositive { get; init; }
    public bool StressAggregatePositive { get; init; }
    public bool StressAllWindowsPositive { get; init; }
    public string RobustnessVerdict { get; init; } = string.Empty;
}

public sealed record DirectionalRuleV2CostSensitivityRow
{
    public string ProfileKey { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = string.Empty;
    public string EntryMode { get; init; } = string.Empty;
    public string OverlapPolicy { get; init; } = string.Empty;
    public int CooldownCandlesAfterExit { get; init; }
    public int MaxHoldMinutes { get; init; }
    public string CostScenarioLabel { get; init; } = string.Empty;
    public decimal RoundTripCostPercent { get; init; }
    public decimal ExtraAdverseSlippagePercentPerSide { get; init; }
    public int TradeCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal? AvgNetPnlPerTrade { get; init; }
    public bool AggregateNetPositive { get; init; }
    public bool StressAggregatePositive { get; init; }
    public bool StressAllWindowsPositive { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record DirectionalRuleV2DrawdownRow
{
    public string ProfileKey { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = string.Empty;
    public string WindowLabel { get; init; } = string.Empty;
    public string EntryMode { get; init; } = string.Empty;
    public string OverlapPolicy { get; init; } = string.Empty;
    public int CooldownCandlesAfterExit { get; init; }
    public int MaxHoldMinutes { get; init; }
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public decimal MaxDrawdownQuote { get; init; }
    public decimal WorstWindowNet { get; init; }
    public decimal WorstTradeNet { get; init; }
    public decimal? ProfitFactor { get; init; }
    public decimal WinRate { get; init; }
    public decimal? AverageWin { get; init; }
    public decimal? AverageLoss { get; init; }
    public decimal? MedianNetPerTrade { get; init; }
}

public sealed record DirectionalRuleV2OverlapAnalysisRow
{
    public TradingSymbol Symbol { get; init; }
    public string Rule01Interval { get; init; } = string.Empty;
    public string Rule05Interval { get; init; } = string.Empty;
    public string WindowLabel { get; init; } = string.Empty;
    public int Rule01SignalCount { get; init; }
    public int Rule05SignalCount { get; init; }
    public int CoFireWithin30mCount { get; init; }
    public decimal CoFireRateVsRule01 { get; init; }
    public decimal CoFireRateVsRule05 { get; init; }
    public string RulePriorityMode { get; init; } = string.Empty;
    public int PriorityRule01Wins { get; init; }
    public int PriorityRule05Wins { get; init; }
    public string Notes { get; init; } = string.Empty;
}

public sealed record DirectionalRuleFuturesValidationV2RunResult(
    IReadOnlyList<DirectionalRuleV2SummaryRow> Summaries,
    IReadOnlyList<DirectionalRuleV2WindowRobustnessRow> WindowRobustness,
    IReadOnlyList<DirectionalRuleV2CostSensitivityRow> CostSensitivity,
    IReadOnlyList<DirectionalRuleV2DrawdownRow> Drawdown,
    IReadOnlyList<DirectionalRuleV2OverlapAnalysisRow> OverlapAnalysis,
    IReadOnlyList<ReachabilityResearchAnswer> ResearchAnswers,
    long ExecutedTradeCount,
    long SkippedSignalCount);
