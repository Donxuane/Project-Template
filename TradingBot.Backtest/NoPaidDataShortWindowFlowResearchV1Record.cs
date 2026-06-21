namespace TradingBot.Backtest;

public enum ShortWindowPerfCondition
{
    AlwaysOn,
    None,
    RecentNetPositive,
    RecentProfitFactor,
    RecentWinRateAndNet
}

public enum ShortWindowFlowCondition
{
    None,
    OiRisingPriceNearHigh,
    TakerImbalanceStretched,
    LongShortStretched,
    BtcReturn30mPositive,
    BtcReturn60mNegative,
    FundingStretched,
    FundingNormal
}

public sealed record ShortWindowActivationConfig(
    string ActivationRuleName,
    ShortWindowPerfCondition PerfCondition,
    ShortWindowFlowCondition FlowCondition,
    int CheckpointFrequencyHours,
    int LookbackDays,
    int ActivationPeriodHours,
    int MinLookbackTrades,
    decimal? ProfitFactorThreshold,
    string Description);

public sealed record ShortWindowFlowSnapshot
{
    public DateTime TimestampUtc { get; init; }
    public decimal? OiChange5mPercent { get; init; }
    public decimal? OiChange15mPercent { get; init; }
    public decimal? OiChange30mPercent { get; init; }
    public decimal? OiChange60mPercent { get; init; }
    public decimal? OiZScoreRecent { get; init; }
    public decimal? TakerBuySellImbalance { get; init; }
    public decimal? TakerImbalance1h { get; init; }
    public decimal? GlobalLongShortRatio { get; init; }
    public decimal? GlobalLongShortRatioChange1hPercent { get; init; }
    public decimal? GlobalLongShortZScore { get; init; }
    public decimal? TopLongShortRatio { get; init; }
    public decimal? TopLongShortZScore { get; init; }
    public decimal? FundingRate { get; init; }
    public decimal? FundingZScore { get; init; }
    public decimal? MarkIndexDivergencePercent { get; init; }
    public decimal? BtcReturn30mPercent { get; init; }
    public decimal? BtcReturn60mPercent { get; init; }
    public decimal? BtcTrendSlopePercentPerHour { get; init; }
    public string? VolatilityRegime { get; init; }
    public decimal? AtrPercent { get; init; }
    public decimal? DistanceFromRecentHighPercent { get; init; }
    public decimal? DistanceFromRecentLowPercent { get; init; }
}

public sealed record ShortWindowDataAvailabilityRow
{
    public string Provider { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string SourceKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string IntervalOptions { get; init; } = string.Empty;
    public string RequestedInterval { get; init; } = string.Empty;
    public string MaxLookbackDocumented { get; init; } = string.Empty;
    public decimal? MaxLookbackDaysObserved { get; init; }
    public string RateLimitNotes { get; init; } = string.Empty;
    public string SymbolsSupported { get; init; } = string.Empty;
    public bool LocalFilePresent { get; init; }
    public int LocalRecordCount { get; init; }
    public DateTime? LocalStartUtc { get; init; }
    public DateTime? LocalEndUtc { get; init; }
    public decimal LocalSpanDays { get; init; }
    public bool UsefulFor7d { get; init; }
    public bool UsefulFor14d { get; init; }
    public bool UsefulFor30d { get; init; }
    public bool UsefulFor365d { get; init; }
    public string ProbeStatus { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}

public sealed record ShortWindowFeatureSampleRow
{
    public string Symbol { get; init; } = string.Empty;
    public DateTime TimestampUtc { get; init; }
    public decimal? OiChange5mPercent { get; init; }
    public decimal? OiChange15mPercent { get; init; }
    public decimal? OiChange30mPercent { get; init; }
    public decimal? OiChange60mPercent { get; init; }
    public decimal? OiZScoreRecent { get; init; }
    public decimal? TakerBuySellImbalance { get; init; }
    public decimal? TakerImbalance1h { get; init; }
    public decimal? GlobalLongShortRatio { get; init; }
    public decimal? GlobalLongShortRatioChange1hPercent { get; init; }
    public decimal? GlobalLongShortZScore { get; init; }
    public decimal? TopLongShortRatio { get; init; }
    public decimal? TopLongShortZScore { get; init; }
    public decimal? FundingRate { get; init; }
    public decimal? FundingZScore { get; init; }
    public decimal? MarkIndexDivergencePercent { get; init; }
    public decimal? BtcReturn30mPercent { get; init; }
    public decimal? BtcReturn60mPercent { get; init; }
    public decimal? BtcTrendSlopePercentPerHour { get; init; }
    public string? VolatilityRegime { get; init; }
    public decimal? AtrPercent { get; init; }
    public decimal? DistanceFromRecentHighPercent { get; init; }
    public decimal? DistanceFromRecentLowPercent { get; init; }
}

public sealed record ShortWindowPeriodRow
{
    public string ActivationRuleName { get; init; } = string.Empty;
    public int CheckpointFrequencyHours { get; init; }
    public int LookbackDays { get; init; }
    public int ActivationPeriodHours { get; init; }
    public DateTime CheckpointUtc { get; init; }
    public DateTime ActivationStartUtc { get; init; }
    public DateTime ActivationEndUtc { get; init; }
    public int LookbackTradeCount { get; init; }
    public decimal LookbackNetPnl { get; init; }
    public decimal LookbackProfitFactor { get; init; }
    public decimal LookbackWinRate { get; init; }
    public bool PerfConditionPass { get; init; }
    public bool FlowDataAvailable { get; init; }
    public bool FlowConditionPass { get; init; }
    public bool SparseLookback { get; init; }
    public bool Activated { get; init; }
    public string SkipReason { get; init; } = string.Empty;
    public int TradesInActivationWindow { get; init; }
    public decimal NetInActivationWindow { get; init; }
    public decimal? OiChange60mPercent { get; init; }
    public decimal? TakerImbalance1h { get; init; }
    public decimal? GlobalLongShortZScore { get; init; }
    public decimal? FundingZScore { get; init; }
    public decimal? BtcReturn30mPercent { get; init; }
    public decimal? BtcReturn60mPercent { get; init; }
    public decimal? DistanceFromRecentHighPercent { get; init; }
    public string CostScenario { get; init; } = string.Empty;
}

public sealed record ShortWindowTradeRow
{
    public string ActivationRuleName { get; init; } = string.Empty;
    public DateTime EntryTimeUtc { get; init; }
    public DateTime ExitTimeUtc { get; init; }
    public decimal NetPnlQuote { get; init; }
    public bool IsWinner { get; init; }
    public string ExitReason { get; init; } = string.Empty;
    public string CostScenario { get; init; } = string.Empty;
    public DateTime ActivationStartUtc { get; init; }
    public DateTime ActivationEndUtc { get; init; }
    public bool SparseLookbackActivation { get; init; }
}

public sealed record ShortWindowSummaryRow
{
    public string ActivationRuleName { get; init; } = string.Empty;
    public string PerfCondition { get; init; } = string.Empty;
    public string FlowCondition { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int CheckpointFrequencyHours { get; init; }
    public int LookbackDays { get; init; }
    public int ActivationPeriodHours { get; init; }
    public int MinLookbackTrades { get; init; }
    public decimal? ProfitFactorThreshold { get; init; }
    public string CostScenario { get; init; } = string.Empty;
    public int TotalTrades { get; init; }
    public int BaselineTrades { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal BaselineNetPnlQuote { get; init; }
    public decimal Delta { get; init; }
    public decimal WinRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdownQuote { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public int CheckpointCount { get; init; }
    public int ActivatedPeriodCount { get; init; }
    public int PositivePeriodCount { get; init; }
    public decimal PositivePeriodRate { get; init; }
    public int ActivationClusterCount { get; init; }
    public int SparseActivationCount { get; init; }
    public int FlowUnavailableCheckpointCount { get; init; }
    public bool MeetsMinExecutedTrades { get; init; }
    public bool SparseFlagged { get; init; }
    public bool NetPositive { get; init; }
    public decimal? Latency002NetPnl { get; init; }
    public bool SurvivesModerateSlippage002 { get; init; }
    public bool MultipleClusters { get; init; }
    public bool PassesSuccessCriteria { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record ShortWindowCostSensitivityRow
{
    public string ActivationRuleName { get; init; } = string.Empty;
    public string CostScenario { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal WinRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public bool NetPositive { get; init; }
    public bool SurvivesModerateSlippage002 { get; init; }
    public bool SurvivesStress { get; init; }
}

public sealed record ShortWindowSimResult(
    ShortWindowActivationConfig Config,
    IReadOnlyList<ShortWindowPeriodRow> Periods,
    IReadOnlyList<ShortWindowTradeRow> Trades,
    ShortWindowSummaryRow Summary);

public sealed record ShortWindowDownloadOutcome(
    string Symbol,
    string SourceKey,
    bool Success,
    int AddedCount,
    int TotalCount,
    string Message);

public sealed record NoPaidDataShortWindowFlowResearchV1RunResult(
    IReadOnlyList<ShortWindowDataAvailabilityRow> Availability,
    IReadOnlyList<ShortWindowFeatureSampleRow> FeatureSamples,
    IReadOnlyList<ShortWindowSummaryRow> Summary,
    IReadOnlyList<ShortWindowTradeRow> Trades,
    IReadOnlyList<ShortWindowPeriodRow> Periods,
    IReadOnlyList<ShortWindowCostSensitivityRow> CostSensitivity,
    IReadOnlyList<ReachabilityResearchAnswer> Answers,
    int BaselineTradeCount,
    decimal BaselineNetPnl,
    DateTime StudyStartUtc,
    DateTime StudyEndUtc,
    DateTime? FlowCoverageStartUtc,
    DateTime? FlowCoverageEndUtc);
