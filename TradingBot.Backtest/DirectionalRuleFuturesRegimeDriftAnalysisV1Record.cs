using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public enum RegimeDriftPeriodKind
{
    Recent30d,
    Recent60d,
    Recent90d,
    Older,
    TrainReference,
    Holdout30d,
    Monthly
}

public sealed record RegimeDriftDiagnosticTrade
{
    public DateTime EntryTimeUtc { get; init; }
    public DateTime ExitTimeUtc { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public bool IsWinner { get; init; }
    public string CostScenarioLabel { get; init; } = string.Empty;
    public string ExitReason { get; init; } = string.Empty;
    public decimal? MfePercent { get; init; }
    public decimal? MaePercent { get; init; }
    public decimal DistanceFromRecentHighPercent { get; init; }
    public decimal DistanceFromRecentLowPercent { get; init; }
    public decimal RangeWidthPercent { get; init; }
    public decimal AtrPercent { get; init; }
    public decimal TrendSlopePercent { get; init; }
    public decimal? BtcReturn30mPercent { get; init; }
    public decimal? BtcReturn60mPercent { get; init; }
    public string VolatilityRegime { get; init; } = string.Empty;
    public string? BtcTrendRegime { get; init; }
    public string? BtcVolatilityRegime { get; init; }
    public string? BtcMarketDirectionBucket { get; init; }
    public int HourOfDayUtc { get; init; }
    public string DayOfWeek { get; init; } = string.Empty;
    public string SessionBucket { get; init; } = string.Empty;
    public string MonthKey { get; init; } = string.Empty;
    public bool InRecent30d { get; init; }
    public bool InRecent60d { get; init; }
    public bool InRecent90d { get; init; }
    public bool InOlder { get; init; }
    public bool InTrainReference { get; init; }
    public bool InHoldout30d { get; init; }
}

public sealed record RegimeDriftSummaryRow
{
    public string PeriodLabel { get; init; } = string.Empty;
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public int WinCount { get; init; }
    public decimal WinRate { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal? AvgNetPerTrade { get; init; }
    public decimal? MedianNetPerTrade { get; init; }
    public bool TradeCountSufficient { get; init; }
    public bool PeriodPositive { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record RegimeDriftFeatureComparisonRow
{
    public string ComparisonGroup { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public decimal? AvgDistanceFromRecentHighPercent { get; init; }
    public decimal? AvgDistanceFromRecentLowPercent { get; init; }
    public decimal? AvgRangeWidthPercent { get; init; }
    public decimal? AvgAtrPercent { get; init; }
    public decimal? AvgTrendSlopePercent { get; init; }
    public decimal? AvgBtcReturn30mPercent { get; init; }
    public decimal? AvgBtcReturn60mPercent { get; init; }
    public decimal? AvgNetPnlQuote { get; init; }
    public decimal? AvgMfePercent { get; init; }
    public decimal? AvgMaePercent { get; init; }
    public string TopVolatilityRegime { get; init; } = string.Empty;
    public string TopSessionBucket { get; init; } = string.Empty;
    public string TopBtcTrendRegime { get; init; } = string.Empty;
    public int TopHourOfDayUtc { get; init; }
}

public sealed record RegimeDriftMonthlyPerformanceRow
{
    public string MonthKey { get; init; } = string.Empty;
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public int WinCount { get; init; }
    public decimal WinRate { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal? AvgNetPerTrade { get; init; }
    public bool MonthPositive { get; init; }
}

public sealed record RegimeDriftEntryTimeRuleRow
{
    public string RuleName { get; init; } = string.Empty;
    public string TrainPeriod { get; init; } = string.Empty;
    public string TestPeriod { get; init; } = string.Empty;
    public string RuleDescription { get; init; } = string.Empty;
    public string FeaturesUsed { get; init; } = string.Empty;
    public int TrainSamples { get; init; }
    public int TestSamples { get; init; }
    public decimal TrainNetPnlQuote { get; init; }
    public decimal TestNetPnlQuote { get; init; }
    public decimal? TrainMedianNetPerTrade { get; init; }
    public decimal? TestMedianNetPerTrade { get; init; }
    public decimal TrainWinRate { get; init; }
    public decimal TestWinRate { get; init; }
    public bool TrainPositive { get; init; }
    public bool TestPositive { get; init; }
    public bool BothPeriodsPositive { get; init; }
    public bool SparseWarning { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record RegimeDriftOutcomeRuleRow
{
    public string RuleName { get; init; } = string.Empty;
    public string RuleDescription { get; init; } = string.Empty;
    public string TrainPeriod { get; init; } = string.Empty;
    public string TestPeriod { get; init; } = string.Empty;
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int BaselineTrades { get; init; }
    public int FilteredTrades { get; init; }
    public decimal BaselineNetPnlQuote { get; init; }
    public decimal FilteredNetPnlQuote { get; init; }
    public decimal BaselineTestNetPnlQuote { get; init; }
    public decimal FilteredTestNetPnlQuote { get; init; }
    public bool RemovesOlderLosers { get; init; }
    public bool KeepsRecentWinners { get; init; }
    public bool SurvivesBothPeriods { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record DirectionalRuleFuturesRegimeDriftAnalysisV1RunResult(
    IReadOnlyList<RegimeDriftSummaryRow> Summary,
    IReadOnlyList<RegimeDriftFeatureComparisonRow> FeatureComparison,
    IReadOnlyList<RegimeDriftMonthlyPerformanceRow> MonthlyPerformance,
    IReadOnlyList<RegimeDriftEntryTimeRuleRow> EntryTimeRules,
    IReadOnlyList<RegimeDriftOutcomeRuleRow> OutcomeRules,
    IReadOnlyList<ReachabilityResearchAnswer> Answers,
    int TotalTrades,
    DateTime DataStartUtc,
    DateTime DataEndUtc);
