using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public enum MultiSymbolRuleFamily
{
    NearHighElevatedVol,
    NearLowElevatedVol,
    FundingNormalTrendContinuation,
    LongShortRatioStretchReversal,
    BtcContextDirectional,
    FlowCompressionBreakout
}

/// <summary>Direction-aware activation gates for the V2 multi-symbol engine (separate from the frozen V1 track).</summary>
public enum MultiSymbolActivationGate
{
    None,
    FundingNormal,
    BtcContext60mAgrees,
    LongShortStretchedAgainstDirection,
    OiChangeConfirms,
    TakerImbalanceConfirms,
    VolatilityNormal,
    VolatilityElevated,
    RecentNetPositive
}

public sealed record MultiSymbolComboKey(
    TradingSymbol Symbol,
    string Interval,
    LongShortDirection Direction,
    MultiSymbolRuleFamily Family)
{
    public override string ToString() => $"{Symbol}-{Interval}-{Direction}-{Family}";
}

public sealed record MultiSymbolActivationConfig(
    string ActivationRuleName,
    MultiSymbolActivationGate Gate,
    bool IsAlwaysOn,
    int CheckpointFrequencyHours,
    int ActivationPeriodHours,
    int LookbackDays,
    int MinLookbackTrades);

public sealed record MultiSymbolDataCoverageRow
{
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public bool CandleDataPresent { get; init; }
    public DateTime? CandleStartUtc { get; init; }
    public DateTime? CandleEndUtc { get; init; }
    public decimal CandleSpanDays { get; init; }
    public decimal OiCoverageDays { get; init; }
    public decimal TakerCoverageDays { get; init; }
    public decimal GlobalLongShortCoverageDays { get; init; }
    public decimal TopLongShortCoverageDays { get; init; }
    public decimal FundingSpanDays { get; init; }
    public decimal MarkIndexSpanDays { get; init; }
    public DateTime? FlowStartUtc { get; init; }
    public DateTime? FlowEndUtc { get; init; }
    public decimal UsableWindowDays { get; init; }
    public bool EligibleForShortWindowResearch { get; init; }
    public string Notes { get; init; } = string.Empty;
}

public sealed record MultiSymbolBaseRuleSummaryRow
{
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string RuleFamily { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public int MaxHoldMinutes { get; init; }
    public int CooldownCandles { get; init; }
    public int SignalCount { get; init; }
    public int TradeCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal WinRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdownQuote { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public decimal DiscoveryNet { get; init; }
    public decimal ValidationNet { get; init; }
    public decimal HoldoutNet { get; init; }
    public int DiscoveryTrades { get; init; }
    public int ValidationTrades { get; init; }
    public int HoldoutTrades { get; init; }
    public string CostScenario { get; init; } = string.Empty;
}

public sealed record MultiSymbolSplitValidationRow
{
    public string SplitScheme { get; init; } = string.Empty;
    public string Segment { get; init; } = string.Empty;
    public DateTime SegmentStartUtc { get; init; }
    public DateTime SegmentEndUtc { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string RuleFamily { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal WinRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public bool SelectedInDiscovery { get; init; }
    public string Notes { get; init; } = string.Empty;
}

public sealed record MultiSymbolCandidateTradeRow
{
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string RuleFamily { get; init; } = string.Empty;
    public string ActivationRule { get; init; } = string.Empty;
    public DateTime EntryTimeUtc { get; init; }
    public DateTime ExitTimeUtc { get; init; }
    public decimal NetPnlQuote { get; init; }
    public bool IsWinner { get; init; }
    public string ExitReason { get; init; } = string.Empty;
    public string Segment { get; init; } = string.Empty;
    public string CostScenario { get; init; } = string.Empty;
}

public sealed record MultiSymbolActivationPeriodRow
{
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string RuleFamily { get; init; } = string.Empty;
    public string ActivationRule { get; init; } = string.Empty;
    public DateTime CheckpointUtc { get; init; }
    public DateTime ActivationStartUtc { get; init; }
    public DateTime ActivationEndUtc { get; init; }
    public int LookbackTradeCount { get; init; }
    public decimal LookbackNetPnl { get; init; }
    public bool GateDataAvailable { get; init; }
    public bool GatePass { get; init; }
    public bool Activated { get; init; }
    public string SkipReason { get; init; } = string.Empty;
    public int TradesInActivationWindow { get; init; }
    public decimal NetInActivationWindow { get; init; }
    public string CostScenario { get; init; } = string.Empty;
}

public sealed record MultiSymbolCostSensitivityRow
{
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string RuleFamily { get; init; } = string.Empty;
    public string ActivationRule { get; init; } = string.Empty;
    public string CostScenario { get; init; } = string.Empty;
    public int FullWindowTrades { get; init; }
    public decimal FullWindowNet { get; init; }
    public int ValidationHoldoutTrades { get; init; }
    public decimal ValidationHoldoutNet { get; init; }
    public bool ValidationHoldoutNetPositive { get; init; }
}

public sealed record MultiSymbolLeaderboardRow
{
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string RuleFamily { get; init; } = string.Empty;
    public string ActivationRule { get; init; } = string.Empty;
    public decimal DiscoveryNet { get; init; }
    public decimal ValidationNet { get; init; }
    public decimal HoldoutNet { get; init; }
    public decimal FullWindowNet { get; init; }
    public int TradeCount { get; init; }
    public int ValidationTradeCount { get; init; }
    public int HoldoutTradeCount { get; init; }
    public decimal WinRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdown { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public decimal PositiveActivatedPeriodsPercent { get; init; }
    public decimal BestCostScenarioNet { get; init; }
    public decimal ModerateLatencyNet { get; init; }
    public decimal StressPlusNet { get; init; }
    public bool OverfitWarning { get; init; }
    public bool SparseWarning { get; init; }
    public bool SingleClusterWarning { get; init; }
    public string Recommendation { get; init; } = string.Empty;
    public string SuggestedFrozenProfileName { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}

public sealed record MultiSymbolWatchlistCandidateRow
{
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string RuleFamily { get; init; } = string.Empty;
    public string ActivationRule { get; init; } = string.Empty;
    public decimal DiscoveryNet { get; init; }
    public decimal ValidationNet { get; init; }
    public decimal HoldoutNet { get; init; }
    public int ValidationHoldoutTradeCount { get; init; }
    public int RequiredTradeCount { get; init; }
    public int MissingTradeCount { get; init; }
    public IReadOnlyDictionary<string, decimal> CostScenarioResults { get; init; } = new Dictionary<string, decimal>();
    public bool OverfitWarning { get; init; }
    public bool SparseWarning { get; init; }
    public bool SingleClusterWarning { get; init; }
    public string Recommendation { get; init; } = string.Empty;
    public string NextRerunCondition { get; init; } = string.Empty;
    public bool ExplicitlyTracked { get; init; }
    public string Notes { get; init; } = string.Empty;
}

public sealed record MultiSymbolComboScanResult(
    MultiSymbolComboKey Key,
    int SignalCount,
    IReadOnlyList<DirectionalRuleV2TradeRecord> BaseTrades);

public sealed record MultiSymbolActivationSimResult(
    MultiSymbolActivationConfig Config,
    IReadOnlyList<MultiSymbolActivationPeriodRow> Periods,
    IReadOnlyList<RegimeDriftDiagnosticTrade> TakenTrades,
    int ActivatedPeriodCount,
    int PositivePeriodCount,
    int ClusterCount,
    IReadOnlyList<(DateTime Start, DateTime End)> ActiveRanges);

public sealed record NoPaidDataShortWindowMultiSymbolResearchV2RunResult(
    IReadOnlyList<MultiSymbolDataCoverageRow> Coverage,
    IReadOnlyList<MultiSymbolBaseRuleSummaryRow> BaseRuleSummary,
    IReadOnlyList<MultiSymbolSplitValidationRow> SplitValidation,
    IReadOnlyList<MultiSymbolCandidateTradeRow> CandidateTrades,
    IReadOnlyList<MultiSymbolActivationPeriodRow> ActivationPeriods,
    IReadOnlyList<MultiSymbolCostSensitivityRow> CostSensitivity,
    IReadOnlyList<MultiSymbolLeaderboardRow> Leaderboard,
    IReadOnlyList<MultiSymbolWatchlistCandidateRow> Watchlist,
    IReadOnlyList<ReachabilityResearchAnswer> Answers,
    DateTime StudyStartUtc,
    DateTime StudyEndUtc);
