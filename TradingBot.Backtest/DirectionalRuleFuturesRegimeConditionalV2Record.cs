namespace TradingBot.Backtest;

public sealed record RegimeConditionalFilter(
    string Name,
    string FilterGroup,
    string Description,
    Func<RegimeDriftDiagnosticTrade, bool> Predicate);

public sealed record RegimeConditionalMonthlyEntry(
    string MonthKey,
    int TradeCount,
    decimal NetPnlQuote,
    bool Positive);

public sealed record RegimeConditionalSummaryRow
{
    public string FilterName { get; init; } = string.Empty;
    public string FilterGroup { get; init; } = string.Empty;
    public string FilterDescription { get; init; } = string.Empty;
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public int TradeCountOlder { get; init; }
    public int TradeCountRecent { get; init; }
    public decimal OlderNetPnl { get; init; }
    public decimal Recent30dNetPnl { get; init; }
    public decimal Recent60dNetPnl { get; init; }
    public decimal Recent90dNetPnl { get; init; }
    public decimal Full365NetPnl { get; init; }
    public decimal TrainReferenceNetPnl { get; init; }
    public decimal Holdout30dNetPnl { get; init; }
    public decimal? OlderAvgNetPerTrade { get; init; }
    public decimal? RecentAvgNetPerTrade { get; init; }
    public int PositiveMonthsCount { get; init; }
    public int TotalMonthsCount { get; init; }
    public IReadOnlyList<RegimeConditionalMonthlyEntry> MonthlyNetPnl { get; init; } = [];
    public bool SparseWarning { get; init; }
    public bool OlderViable { get; init; }
    public bool RecentViable { get; init; }
    public bool BothPeriodsViable { get; init; }
    public bool Full365Positive { get; init; }
    public bool MonthlyConsistencyImproved { get; init; }
    public bool PassesAllCriteria { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record RegimeConditionalCostSensitivityRow
{
    public string FilterName { get; init; } = string.Empty;
    public string FilterGroup { get; init; } = string.Empty;
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public int TradeCountOlder { get; init; }
    public int TradeCountRecent { get; init; }
    public decimal OlderNetPnl { get; init; }
    public decimal Recent90dNetPnl { get; init; }
    public decimal Full365NetPnl { get; init; }
    public bool OlderViable { get; init; }
    public bool RecentViable { get; init; }
    public bool Full365Positive { get; init; }
    public bool SurvivesScenario { get; init; }
}

public sealed record RegimeConditionalFilterImpactRow
{
    public string FilterName { get; init; } = string.Empty;
    public string FilterGroup { get; init; } = string.Empty;
    public string FilterDescription { get; init; } = string.Empty;
    public int BaselineTrades { get; init; }
    public int FilteredTrades { get; init; }
    public decimal TradeRetentionRate { get; init; }
    public decimal BaselineFull365NetPnl { get; init; }
    public decimal FilteredFull365NetPnl { get; init; }
    public decimal Full365Delta { get; init; }
    public decimal BaselineOlderNetPnl { get; init; }
    public decimal FilteredOlderNetPnl { get; init; }
    public decimal OlderDelta { get; init; }
    public decimal BaselineRecent90dNetPnl { get; init; }
    public decimal FilteredRecent90dNetPnl { get; init; }
    public decimal Recent90dDelta { get; init; }
    public int BaselinePositiveMonths { get; init; }
    public int FilteredPositiveMonths { get; init; }
    public int TotalMonths { get; init; }
}

public sealed record RegimeConditionalTradeRow
{
    public DateTime EntryTimeUtc { get; init; }
    public DateTime ExitTimeUtc { get; init; }
    public decimal NetPnlQuote { get; init; }
    public bool IsWinner { get; init; }
    public string ExitReason { get; init; } = string.Empty;
    public decimal? BtcReturn30mPercent { get; init; }
    public decimal? BtcReturn60mPercent { get; init; }
    public decimal AtrPercent { get; init; }
    public decimal TrendSlopePercent { get; init; }
    public decimal DistanceFromRecentHighPercent { get; init; }
    public decimal DistanceFromRecentLowPercent { get; init; }
    public decimal RangeWidthPercent { get; init; }
    public string VolatilityRegime { get; init; } = string.Empty;
    public string BtcTrendRegime { get; init; } = string.Empty;
    public string SessionBucket { get; init; } = string.Empty;
    public int HourOfDayUtc { get; init; }
    public string DayOfWeek { get; init; } = string.Empty;
    public string MonthKey { get; init; } = string.Empty;
    public bool InRecent30d { get; init; }
    public bool InRecent60d { get; init; }
    public bool InRecent90d { get; init; }
    public bool InOlder { get; init; }
    public bool InTrainReference { get; init; }
    public bool InHoldout30d { get; init; }
}

public sealed record DirectionalRuleFuturesRegimeConditionalV2RunResult(
    IReadOnlyList<RegimeConditionalSummaryRow> Summary,
    IReadOnlyList<RegimeConditionalMonthlyEntry> BaselineMonthly,
    IReadOnlyList<RegimeConditionalCostSensitivityRow> CostSensitivity,
    IReadOnlyList<RegimeConditionalFilterImpactRow> FilterImpact,
    IReadOnlyList<RegimeConditionalTradeRow> Trades,
    IReadOnlyList<ReachabilityResearchAnswer> Answers,
    int TotalTrades,
    int QualifyingFilterCount,
    DateTime DataStartUtc,
    DateTime DataEndUtc);
