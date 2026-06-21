using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public enum DirectionalRuleEntryMode
{
    NextOpen,
    NextClose
}

public sealed record DirectionalRuleFuturesTradeRecord
{
    public string RuleName { get; init; } = string.Empty;
    public LongShortDirection Direction { get; init; }
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "15m";
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
}

public sealed record DirectionalRuleFuturesSummaryRow
{
    public string RuleName { get; init; } = string.Empty;
    public LongShortDirection Direction { get; init; }
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "15m";
    public string WindowLabel { get; init; } = string.Empty;
    public string EntryMode { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public int MaxHoldMinutes { get; init; }
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public int NetWinnerCount { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal? AvgNetPnlPerTrade { get; init; }
    public decimal? MedianNetPnlPerTrade { get; init; }
    public decimal ProfitTargetRate { get; init; }
    public decimal StopLossRate { get; init; }
    public decimal TimeStopRate { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record DirectionalRuleFuturesRulePerformanceRow
{
    public string RuleName { get; init; } = string.Empty;
    public LongShortDirection Direction { get; init; }
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "15m";
    public string EntryMode { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public int MaxHoldMinutes { get; init; }
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal? AvgNetPnlPerTrade { get; init; }
    public decimal ProfitTargetRate { get; init; }
    public decimal StopLossRate { get; init; }
    public decimal TimeStopRate { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record DirectionalRuleFuturesWindowRobustnessRow
{
    public string RuleName { get; init; } = string.Empty;
    public LongShortDirection Direction { get; init; }
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "15m";
    public string EntryMode { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public int MaxHoldMinutes { get; init; }
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int Window30dTrades { get; init; }
    public int Window60dTrades { get; init; }
    public int Window90dTrades { get; init; }
    public decimal Window30dNetPnl { get; init; }
    public decimal Window60dNetPnl { get; init; }
    public decimal Window90dNetPnl { get; init; }
    public string RobustnessVerdict { get; init; } = string.Empty;
}

public sealed record DirectionalRuleFuturesCostSensitivityRow
{
    public string RuleName { get; init; } = string.Empty;
    public LongShortDirection Direction { get; init; }
    public string CostScenarioLabel { get; init; } = string.Empty;
    public decimal RoundTripCostPercent { get; init; }
    public decimal FundingRatePercentPerHour { get; init; }
    public int TradeCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal? AvgNetPnlPerTrade { get; init; }
    public decimal? MedianNetPnlPerTrade { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record DirectionalRuleFuturesSimulationRunResult(
    IReadOnlyList<DirectionalRuleFuturesTradeRecord> Trades,
    IReadOnlyList<DirectionalRuleFuturesSummaryRow> Summaries,
    IReadOnlyList<DirectionalRuleFuturesRulePerformanceRow> RulePerformance,
    IReadOnlyList<DirectionalRuleFuturesWindowRobustnessRow> WindowRobustness,
    IReadOnlyList<DirectionalRuleFuturesCostSensitivityRow> CostSensitivity,
    IReadOnlyList<ReachabilityResearchAnswer> ResearchAnswers,
    IReadOnlyList<DirectionalRuleDefinition> Rules,
    IReadOnlyList<TradingSymbol> SymbolsScanned,
    IReadOnlyList<string> IntervalsScanned,
    long BaseTradeCount = 0,
    long ExpandedTradeCount = 0,
    int ModerateTradeCount = 0,
    decimal ModerateNetPnlQuote = 0m,
    int PositiveModerateConfigs = 0);
