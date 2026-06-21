using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public enum DirectionalRuleV31ValidationTrack
{
    BestBnbLongHistory,
    CrossSymbol
}

public sealed record DirectionalRuleV31SimulationProfile(
    string ProfileKey,
    string VariantLabel,
    DirectionalRuleV31ValidationTrack ValidationTrack,
    bool IsBestBnbCandidate,
    DirectionalRuleDefinition Rule,
    TradingSymbol Symbol,
    string Interval,
    decimal TargetPercent,
    decimal StopPercent,
    int MaxHoldMinutes,
    DirectionalRuleEntryMode EntryMode,
    DirectionalRuleV2OverlapPolicy OverlapPolicy,
    int CooldownCandlesAfterExit);

public sealed record DirectionalRuleV31TradeRecord
{
    public string ProfileKey { get; init; } = string.Empty;
    public string VariantLabel { get; init; } = string.Empty;
    public DirectionalRuleV31ValidationTrack ValidationTrack { get; init; }
    public bool IsBestBnbCandidate { get; init; }
    public string RuleName { get; init; } = string.Empty;
    public LongShortDirection Direction { get; init; }
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = string.Empty;
    public string WindowLabel { get; init; } = string.Empty;
    public string EntryMode { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public int MaxHoldMinutes { get; init; }
    public int CooldownCandlesAfterExit { get; init; }
    public string OverlapPolicy { get; init; } = string.Empty;
    public string CostScenarioLabel { get; init; } = string.Empty;
    public DateTime EntryTimeUtc { get; init; }
    public DateTime ExitTimeUtc { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal ExitPrice { get; init; }
    public string ExitReason { get; init; } = string.Empty;
    public decimal GrossPnlQuote { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal FeesQuote { get; init; }
    public decimal SlippageQuote { get; init; }
    public decimal FundingQuote { get; init; }
    public decimal? BtcReturn30mPercent { get; init; }
    public string VolatilityRegime { get; init; } = string.Empty;
    public decimal DistanceFromRecentHighPercent { get; init; }
    public decimal AtrPercent { get; init; }
    public decimal TrendSlopePercent { get; init; }
    public decimal? MfePercent { get; init; }
    public decimal? MaePercent { get; init; }
    public decimal DurationMinutes { get; init; }
}

public sealed record DirectionalRuleV31SummaryRow
{
    public string ProfileKey { get; init; } = string.Empty;
    public string VariantLabel { get; init; } = string.Empty;
    public DirectionalRuleV31ValidationTrack ValidationTrack { get; init; }
    public bool IsBestBnbCandidate { get; init; }
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
    public decimal GrossPnlQuote { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal? AvgNetPnlPerTrade { get; init; }
    public decimal? MedianNetPerTrade { get; init; }
    public decimal WinRate { get; init; }
    public decimal? AverageWin { get; init; }
    public decimal? AverageLoss { get; init; }
    public decimal? ProfitFactor { get; init; }
    public decimal AverageHoldMinutes { get; init; }
    public decimal TimeStopRate { get; init; }
    public decimal StopLossRate { get; init; }
    public decimal ProfitTargetRate { get; init; }
    public bool SymbolPositive { get; init; }
    public bool AllWindowsPositive { get; init; }
    public bool HoldoutPositive { get; init; }
    public bool StressPositive { get; init; }
    public bool StressAllWindowsPositive { get; init; }
    public bool TradeCountSufficient { get; init; }
    public bool LongHistoryPositive { get; init; }
    public bool OverfitWarning { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record DirectionalRuleV31WindowRobustnessRow
{
    public string ProfileKey { get; init; } = string.Empty;
    public string VariantLabel { get; init; } = string.Empty;
    public DirectionalRuleV31ValidationTrack ValidationTrack { get; init; }
    public bool IsBestBnbCandidate { get; init; }
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = string.Empty;
    public string EntryMode { get; init; } = string.Empty;
    public int CooldownCandlesAfterExit { get; init; }
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public int MaxHoldMinutes { get; init; }
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int Window30dTrades { get; init; }
    public int Window60dTrades { get; init; }
    public int Window90dTrades { get; init; }
    public int Window120dTrades { get; init; }
    public int Window180dTrades { get; init; }
    public int Window270dTrades { get; init; }
    public int Window365dTrades { get; init; }
    public int Holdout30dTrades { get; init; }
    public int TrainReferenceTrades { get; init; }
    public decimal Window30dNetPnl { get; init; }
    public decimal Window60dNetPnl { get; init; }
    public decimal Window90dNetPnl { get; init; }
    public decimal Window120dNetPnl { get; init; }
    public decimal Window180dNetPnl { get; init; }
    public decimal Window270dNetPnl { get; init; }
    public decimal Window365dNetPnl { get; init; }
    public decimal Holdout30dNetPnl { get; init; }
    public decimal TrainReferenceNetPnl { get; init; }
    public decimal AggregateNetPnl { get; init; }
    public bool SymbolPositive { get; init; }
    public bool AllWindowsPositive { get; init; }
    public bool HoldoutPositive { get; init; }
    public bool StressPositive { get; init; }
    public bool StressAllWindowsPositive { get; init; }
    public bool TradeCountSufficient { get; init; }
    public bool LongHistoryPositive { get; init; }
    public bool OverfitWarning { get; init; }
    public string RobustnessVerdict { get; init; } = string.Empty;
}

public sealed record DirectionalRuleV31CostSensitivityRow
{
    public string ProfileKey { get; init; } = string.Empty;
    public string VariantLabel { get; init; } = string.Empty;
    public DirectionalRuleV31ValidationTrack ValidationTrack { get; init; }
    public bool IsBestBnbCandidate { get; init; }
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = string.Empty;
    public string CostScenarioLabel { get; init; } = string.Empty;
    public decimal RoundTripCostPercent { get; init; }
    public decimal ExtraAdverseSlippagePercentPerSide { get; init; }
    public int TradeCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal? AvgNetPnlPerTrade { get; init; }
    public bool SymbolPositive { get; init; }
    public bool StressPositive { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record DirectionalRuleV31PeriodBucketRow(
    string PeriodKey,
    int TradeCount,
    decimal NetPnlQuote,
    decimal MaxDrawdownQuote,
    int MaxConsecutiveLosses);

public sealed record DirectionalRuleV31DrawdownRow
{
    public string ProfileKey { get; init; } = string.Empty;
    public string VariantLabel { get; init; } = string.Empty;
    public DirectionalRuleV31ValidationTrack ValidationTrack { get; init; }
    public bool IsBestBnbCandidate { get; init; }
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = string.Empty;
    public string WindowLabel { get; init; } = string.Empty;
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
    public decimal AverageHoldMinutes { get; init; }
    public decimal TimeStopRate { get; init; }
    public decimal StopLossRate { get; init; }
    public decimal ProfitTargetRate { get; init; }
    public int LongestFlatPeriodDays { get; init; }
    public decimal LargestGivebackFromPeak { get; init; }
    public IReadOnlyList<DirectionalRuleV31PeriodBucketRow> NetPnlByWeek { get; init; } = [];
    public IReadOnlyList<DirectionalRuleV31PeriodBucketRow> NetPnlByMonth { get; init; } = [];
}

public sealed record DirectionalRuleV31MonthlyWeeklyPnlRow
{
    public string ProfileKey { get; init; } = string.Empty;
    public string VariantLabel { get; init; } = string.Empty;
    public DirectionalRuleV31ValidationTrack ValidationTrack { get; init; }
    public bool IsBestBnbCandidate { get; init; }
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = string.Empty;
    public string WindowLabel { get; init; } = string.Empty;
    public string CostScenarioLabel { get; init; } = string.Empty;
    public string PeriodType { get; init; } = string.Empty;
    public string PeriodKey { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal MaxDrawdownQuote { get; init; }
    public int MaxConsecutiveLosses { get; init; }
}

public sealed record DirectionalRuleFuturesValidationV31RunResult(
    IReadOnlyList<DirectionalRuleV31SummaryRow> BestBnbLongHistorySummary,
    IReadOnlyList<DirectionalRuleV31SummaryRow> CrossSymbolSummary,
    IReadOnlyList<DirectionalRuleV31WindowRobustnessRow> WindowRobustness,
    IReadOnlyList<DirectionalRuleV31CostSensitivityRow> CostSensitivity,
    IReadOnlyList<DirectionalRuleV31DrawdownRow> Drawdown,
    IReadOnlyList<DirectionalRuleV31MonthlyWeeklyPnlRow> MonthlyWeeklyPnl,
    IReadOnlyList<ReachabilityResearchAnswer> GeneralizationAnswers,
    long ExecutedTradeCount,
    long SkippedSignalCount);
