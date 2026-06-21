namespace TradingBot.Backtest;

public sealed record FuturesFundingPoint(long TimestampMs, decimal Rate);
public sealed record FuturesOiPoint(long TimestampMs, decimal OpenInterest, decimal OpenInterestValue);
public sealed record FuturesTakerPoint(long TimestampMs, decimal BuySellRatio, decimal BuyVol, decimal SellVol);
public sealed record FuturesLongShortPoint(long TimestampMs, decimal LongShortRatio, decimal LongAccount, decimal ShortAccount);
public sealed record FuturesPriceKlinePoint(long TimestampMs, decimal Open, decimal High, decimal Low, decimal Close);

public sealed record FuturesDataAvailabilityRow
{
    public string Symbol { get; init; } = string.Empty;
    public string SourceKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string Granularity { get; init; } = string.Empty;
    public string AvailabilityClass { get; init; } = string.Empty;
    public bool BootstrapSupported { get; init; }
    public bool LocalFilePresent { get; init; }
    public int LocalRecordCount { get; init; }
    public DateTime? LocalStartUtc { get; init; }
    public DateTime? LocalEndUtc { get; init; }
    public decimal LocalSpanDays { get; init; }
    public bool Supports365dStudy { get; init; }
    public string Notes { get; init; } = string.Empty;
}

public sealed record FuturesDataQualityRow
{
    public string Symbol { get; init; } = string.Empty;
    public string SourceKey { get; init; } = string.Empty;
    public int RecordCount { get; init; }
    public int DuplicateTimestampCount { get; init; }
    public int GapCount { get; init; }
    public decimal ExpectedCadenceMinutes { get; init; }
    public DateTime? StartUtc { get; init; }
    public DateTime? EndUtc { get; init; }
    public decimal SpanDays { get; init; }
    public decimal CoveragePercent { get; init; }
    public bool TimestampsSorted { get; init; }
    public bool AlignedWithCandles { get; init; }
    public int AlignmentSampleCount { get; init; }
    public int AlignmentMatchedCount { get; init; }
    public string FieldAvailability { get; init; } = string.Empty;
    public string Verdict { get; init; } = string.Empty;
}

public sealed record FuturesFlowFeatureSummaryRow
{
    public string Feature { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public int SampleCount { get; init; }
    public int NonNullCount { get; init; }
    public decimal NonNullPercent { get; init; }
    public decimal? Min { get; init; }
    public decimal? Median { get; init; }
    public decimal? Max { get; init; }
    public decimal? Mean { get; init; }
    public decimal? StdDev { get; init; }
    public bool Supports365dStudy { get; init; }
    public string SourceKey { get; init; } = string.Empty;
}

public sealed record FuturesFlowRuleCandidateRow
{
    public string RuleName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string RuleDescription { get; init; } = string.Empty;
    public string FeaturesUsed { get; init; } = string.Empty;
    public bool UsesFlowFeature { get; init; }
    public int FeatureCount { get; init; }
    public int TotalTrades { get; init; }
    public int TrainTrades { get; init; }
    public int ValidationTrades { get; init; }
    public int HoldoutTrades { get; init; }
    public decimal TrainNet { get; init; }
    public decimal ValidationNet { get; init; }
    public decimal HoldoutNet { get; init; }
    public decimal FullHistoryNet { get; init; }
    public decimal WinRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public bool TrainPositive { get; init; }
    public bool ValidationPositive { get; init; }
    public bool HoldoutPositive { get; init; }
    public bool AllSplitsPositive { get; init; }
    public bool TradeCountSufficient { get; init; }
    public bool OverfitWarning { get; init; }
    public bool UsesFutureInformation { get; init; }
    public string SelectionStage { get; init; } = string.Empty;
    public string Verdict { get; init; } = string.Empty;
}

public sealed record FuturesFlowSplitPerformanceRow
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
    public bool Positive { get; init; }
}

public sealed record FuturesMarketDataExpansionV1RunResult(
    IReadOnlyList<FuturesDataAvailabilityRow> Availability,
    IReadOnlyList<FuturesDataQualityRow> Quality,
    IReadOnlyList<FuturesFlowFeatureSummaryRow> FlowFeatureSummary,
    IReadOnlyList<FuturesFlowRuleCandidateRow> RuleCandidates,
    IReadOnlyList<FuturesFlowSplitPerformanceRow> SplitPerformance,
    IReadOnlyList<ReachabilityResearchAnswer> Answers,
    bool BootstrapAttempted,
    bool StudyRan,
    string StudySkipReason);
