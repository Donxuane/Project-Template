using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public enum LongShortDirection
{
    Long,
    Short
}

public enum LongShortTradeMode
{
    SpotLongOnly,
    FuturesLongOnly,
    FuturesShortOnly,
    FuturesLongPlusShort
}

public sealed record LongShortFuturesFeasibilityObservation
{
    public string WindowLabel { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "15m";
    public DateTime TimeUtc { get; init; }
    public int HourOfDayUtc { get; init; }
    public string SessionBucket { get; init; } = string.Empty;
    public string VolatilityRegime { get; init; } = string.Empty;
    public string TrendRegime { get; init; } = string.Empty;
    public decimal TrendSlopePercent { get; init; }
    public decimal RangeWidthPercent { get; init; }
    public decimal DistanceFromRecentHighPercent { get; init; }
    public decimal DistanceFromRecentLowPercent { get; init; }
    public decimal RecentReturn60CandlesPercent { get; init; }
    public decimal AtrPercent { get; init; }
    public decimal VolumeExpansionRatio { get; init; }
    public decimal? BtcReturn30mPercent { get; init; }
    public string? BtcTrendRegime { get; init; }
    public string? BtcMarketDirectionBucket { get; init; }
    public decimal? MarketWideReturnProxyPercent { get; init; }
    public decimal EntryPrice { get; init; }
    public int PrimaryForwardHorizonMinutes { get; init; }
    public decimal? LongForwardMfePercent { get; init; }
    public decimal? LongForwardMaePercent { get; init; }
    public decimal? ShortForwardMfePercent { get; init; }
    public decimal? ShortForwardMaePercent { get; init; }
    public bool LongTarget050BeforeStop050 { get; init; }
    public bool ShortTarget050BeforeStop050 { get; init; }
    public decimal LongExpectedNetSpotConservativePercent { get; init; }
    public decimal ShortExpectedNetSpotConservativePercent { get; init; }
    public decimal LongExpectedNetFuturesModeratePercent { get; init; }
    public decimal ShortExpectedNetFuturesModeratePercent { get; init; }
    public decimal LongExpectedNetFuturesLowPercent { get; init; }
    public decimal ShortExpectedNetFuturesLowPercent { get; init; }
    public decimal LongExpectedNetFuturesStressPercent { get; init; }
    public decimal ShortExpectedNetFuturesStressPercent { get; init; }
    public decimal BestDirectionExpectedNetFuturesModeratePercent { get; init; }
    public LongShortDirection BestDirectionFuturesModerate { get; init; }
}

public sealed record LongShortFuturesFeasibilitySummaryRow
{
    public string WindowLabel { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "15m";
    public LongShortTradeMode TradeMode { get; init; }
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int SampleCount { get; init; }
    public decimal MedianExpectedNetPercent { get; init; }
    public decimal Target050BeforeStop050Rate { get; init; }
    public decimal EdgeScore { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record LongShortSymbolIntervalRankingRow
{
    public string WindowLabel { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "15m";
    public LongShortDirection Direction { get; init; }
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int SampleCount { get; init; }
    public decimal MedianExpectedNetPercent { get; init; }
    public decimal Target050BeforeStop050Rate { get; init; }
    public decimal EdgeScore { get; init; }
    public int Rank { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record LongShortRegimeRankingRow
{
    public string WindowLabel { get; init; } = string.Empty;
    public string BucketType { get; init; } = string.Empty;
    public string BucketLabel { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "15m";
    public LongShortDirection Direction { get; init; }
    public string CostScenarioLabel { get; init; } = string.Empty;
    public int SampleCount { get; init; }
    public decimal MedianExpectedNetPercent { get; init; }
    public decimal Target050BeforeStop050Rate { get; init; }
    public decimal EdgeScore { get; init; }
    public int Rank { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record LongShortTargetStopMatrixRow
{
    public string WindowLabel { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "15m";
    public LongShortDirection Direction { get; init; }
    public string CostScenarioLabel { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public int ForwardHorizonMinutes { get; init; }
    public int SampleCount { get; init; }
    public int TargetBeforeStopCount { get; init; }
    public int StopBeforeTargetCount { get; init; }
    public decimal TargetBeforeStopRate { get; init; }
    public decimal MedianExpectedNetPercent { get; init; }
}

public sealed record LongShortCostSensitivityRow
{
    public string CostScenarioLabel { get; init; } = string.Empty;
    public LongShortTradeMode TradeMode { get; init; }
    public LongShortDirection Direction { get; init; }
    public decimal RoundTripCostPercent { get; init; }
    public decimal FundingRatePercentPerHour { get; init; }
    public int SampleCount { get; init; }
    public decimal MedianExpectedNetPercent { get; init; }
    public decimal Target050BeforeStop050Rate { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record LongShortEntryTimeRuleRow
{
    public LongShortDirection Direction { get; init; }
    public string RuleDescription { get; init; } = string.Empty;
    public string[] FeaturesUsed { get; init; } = [];
    public string TrainWindows { get; init; } = string.Empty;
    public string HoldoutWindow { get; init; } = "90d";
    public int TrainSamples { get; init; }
    public int HoldoutSamples { get; init; }
    public decimal TrainMedianExpectedNetPercent { get; init; }
    public decimal HoldoutMedianExpectedNetPercent { get; init; }
    public decimal TrainTargetBeforeStopRate { get; init; }
    public decimal HoldoutTargetBeforeStopRate { get; init; }
    public string CostScenarioLabel { get; init; } = string.Empty;
    public string Verdict { get; init; } = string.Empty;
}

public sealed record LongShortScanBatchResult(
    IReadOnlyList<LongShortFuturesFeasibilityObservation> Observations,
    IReadOnlyList<LongShortTargetStopMatrixRow> TargetStopMatrix);

public sealed record LongShortFuturesFeasibilityStudyResult(
    IReadOnlyList<LongShortFuturesFeasibilityObservation> Observations,
    IReadOnlyList<LongShortFuturesFeasibilitySummaryRow> Summary,
    IReadOnlyList<LongShortSymbolIntervalRankingRow> SymbolIntervalRanking,
    IReadOnlyList<LongShortRegimeRankingRow> RegimeRanking,
    IReadOnlyList<LongShortTargetStopMatrixRow> TargetStopMatrix,
    IReadOnlyList<LongShortCostSensitivityRow> CostSensitivity,
    IReadOnlyList<LongShortEntryTimeRuleRow> EntryTimeRules,
    IReadOnlyList<ReachabilityResearchAnswer> ResearchAnswers,
    IReadOnlyList<TradingSymbol> SymbolsScanned,
    IReadOnlyList<string> IntervalsScanned);
