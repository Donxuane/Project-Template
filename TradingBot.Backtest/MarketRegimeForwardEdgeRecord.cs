using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed record MarketRegimeForwardEdgeObservation
{
    public string WindowLabel { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "1m";
    public DateTime TimeUtc { get; init; }
    public int HourOfDayUtc { get; init; }
    public DayOfWeek DayOfWeek { get; init; }
    public string SessionBucket { get; init; } = string.Empty;
    public decimal RecentReturn5CandlesPercent { get; init; }
    public decimal RecentReturn15CandlesPercent { get; init; }
    public decimal RecentReturn30CandlesPercent { get; init; }
    public decimal RecentReturn60CandlesPercent { get; init; }
    public decimal RangeWidthPercent { get; init; }
    public decimal AtrPercent { get; init; }
    public decimal VolumeExpansionRatio { get; init; }
    public string VolatilityRegime { get; init; } = string.Empty;
    public decimal TrendSlopePercent { get; init; }
    public decimal TrendStrengthPercent { get; init; }
    public string TrendRegime { get; init; } = string.Empty;
    public decimal DistanceFromRecentHighPercent { get; init; }
    public decimal DistanceFromRecentLowPercent { get; init; }
    public decimal CandleBodyStrengthPercent { get; init; }
    public decimal ClosePositionInRange { get; init; }
    public decimal? BtcReturn15mPercent { get; init; }
    public decimal? BtcReturn30mPercent { get; init; }
    public decimal? BtcReturn60mPercent { get; init; }
    public decimal? BtcTrendSlopePercent { get; init; }
    public string? BtcTrendRegime { get; init; }
    public string? BtcVolatilityRegime { get; init; }
    public bool? BtcAboveMediumMa { get; init; }
    public string? BtcMarketDirectionBucket { get; init; }
    public decimal? SymbolReturnRelativeToBtc60mPercent { get; init; }
    public decimal? MarketWideReturnProxyPercent { get; init; }
    public string? MarketWideDirection { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal? ForwardReturn15mPercent { get; init; }
    public decimal? ForwardReturn30mPercent { get; init; }
    public decimal? ForwardReturn60mPercent { get; init; }
    public decimal? ForwardReturn4hPercent { get; init; }
    public decimal? ForwardReturn8hPercent { get; init; }
    public decimal? ForwardMfePercent { get; init; }
    public decimal? ForwardMaePercent { get; init; }
    public decimal PrimaryForwardHorizonMinutes { get; init; }
    public bool Target030BeforeStop025 { get; init; }
    public bool Target050BeforeStop050 { get; init; }
    public bool Target075BeforeStop075 { get; init; }
    public bool Target100BeforeStop100 { get; init; }
    public decimal TargetBeforeStopProbability { get; init; }
    public decimal ExpectedNetAfterCostPercent { get; init; }
    public decimal RoundTripCostPercent { get; init; }
    public decimal LongEdgeScore { get; init; }
}

public sealed record MarketRegimeForwardEdgeSummaryRow
{
    public string WindowLabel { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "1m";
    public int SampleCount { get; init; }
    public decimal MedianForwardMfePercent { get; init; }
    public decimal MedianForwardMaePercent { get; init; }
    public decimal MedianExpectedNetAfterCostPercent { get; init; }
    public decimal Target050BeforeStop050Rate { get; init; }
    public decimal LongEdgeScore { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record SymbolIntervalEdgeRankingRow
{
    public string WindowLabel { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "1m";
    public int SampleCount { get; init; }
    public decimal MedianForwardMfePercent { get; init; }
    public decimal MedianForwardMaePercent { get; init; }
    public decimal MedianExpectedNetAfterCostPercent { get; init; }
    public decimal Target050BeforeStop050Rate { get; init; }
    public decimal LongEdgeScore { get; init; }
    public int Rank { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record RegimeBucketEdgeRankingRow
{
    public string WindowLabel { get; init; } = string.Empty;
    public string BucketType { get; init; } = string.Empty;
    public string BucketLabel { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "1m";
    public int SampleCount { get; init; }
    public decimal MedianForwardMfePercent { get; init; }
    public decimal MedianForwardMaePercent { get; init; }
    public decimal MedianExpectedNetAfterCostPercent { get; init; }
    public decimal Target050BeforeStop050Rate { get; init; }
    public decimal LongEdgeScore { get; init; }
    public int Rank { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record SessionEdgeRankingRow
{
    public string WindowLabel { get; init; } = string.Empty;
    public string SessionBucket { get; init; } = string.Empty;
    public int HourOfDayUtc { get; init; }
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "1m";
    public int SampleCount { get; init; }
    public decimal MedianExpectedNetAfterCostPercent { get; init; }
    public decimal Target050BeforeStop050Rate { get; init; }
    public decimal LongEdgeScore { get; init; }
    public int Rank { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record TargetBeforeStopMatrixRow
{
    public string WindowLabel { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "1m";
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public int SampleCount { get; init; }
    public int TargetBeforeStopCount { get; init; }
    public int StopBeforeTargetCount { get; init; }
    public int UnresolvedCount { get; init; }
    public decimal TargetBeforeStopRate { get; init; }
    public decimal ExpectedNetAfterCostPercent { get; init; }
}

public sealed record MarketRegimeEntryTimeRuleRow
{
    public string RuleDescription { get; init; } = string.Empty;
    public string[] FeaturesUsed { get; init; } = [];
    public string TrainWindows { get; init; } = string.Empty;
    public string HoldoutWindow { get; init; } = "90d";
    public int TrainSamples { get; init; }
    public int HoldoutSamples { get; init; }
    public int TrainTargetBeforeStopEvents { get; init; }
    public int HoldoutTargetBeforeStopEvents { get; init; }
    public decimal TrainMedianExpectedNetPercent { get; init; }
    public decimal HoldoutMedianExpectedNetPercent { get; init; }
    public decimal TrainTargetBeforeStopRate { get; init; }
    public decimal HoldoutTargetBeforeStopRate { get; init; }
    public bool UsesFutureInformation { get; init; }
    public bool TradableRule { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record BtcContextEdgeRankingRow
{
    public string WindowLabel { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "1m";
    public string BtcContextBucketType { get; init; } = string.Empty;
    public string BtcContextBucketLabel { get; init; } = string.Empty;
    public int SampleCount { get; init; }
    public decimal MedianForwardMfePercent { get; init; }
    public decimal MedianForwardMaePercent { get; init; }
    public decimal MedianExpectedNetAfterCostPercent { get; init; }
    public decimal Target050BeforeStop050Rate { get; init; }
    public decimal LongEdgeScore { get; init; }
    public int Rank { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record MarketRegimeForwardEdgeStudyResult(
    IReadOnlyList<MarketRegimeForwardEdgeObservation> Observations,
    IReadOnlyList<MarketRegimeForwardEdgeSummaryRow> Summary,
    IReadOnlyList<SymbolIntervalEdgeRankingRow> SymbolIntervalRanking,
    IReadOnlyList<RegimeBucketEdgeRankingRow> RegimeBucketRanking,
    IReadOnlyList<SessionEdgeRankingRow> SessionRanking,
    IReadOnlyList<TargetBeforeStopMatrixRow> TargetBeforeStopMatrix,
    IReadOnlyList<MarketRegimeEntryTimeRuleRow> EntryTimeRules,
    IReadOnlyList<BtcContextEdgeRankingRow> BtcContextRanking,
    IReadOnlyList<ReachabilityResearchAnswer> ResearchAnswers,
    IReadOnlyList<TradingSymbol> SymbolsScanned,
    IReadOnlyList<string> IntervalsScanned,
    bool BtcContextEnabled = false);
