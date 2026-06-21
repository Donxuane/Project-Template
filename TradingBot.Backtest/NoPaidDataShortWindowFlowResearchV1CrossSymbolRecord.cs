using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public enum CrossSymbolPerfKind
{
    None,
    RecentNetPositive,
    RecentProfitFactor
}

public sealed record CrossSymbolComboKey(
    TradingSymbol Symbol,
    string Interval,
    LongShortDirection Direction,
    decimal TargetPercent,
    decimal StopPercent,
    int MaxHoldMinutes)
{
    public override string ToString()
        => $"{Symbol}-{Interval}-{Direction}-T{TargetPercent:0.00}-S{StopPercent:0.00}-H{MaxHoldMinutes}m";
}

public sealed record CrossSymbolActivationConfig(
    string ActivationRuleName,
    bool IsAlwaysOn,
    CrossSymbolPerfKind PerfKind,
    MultiSymbolActivationGate FlowGate,
    int CheckpointFrequencyHours,
    int ActivationPeriodHours,
    int LookbackDays,
    int MinLookbackTrades,
    decimal? ProfitFactorThreshold);

public sealed record CrossSymbolPeriodRow
{
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public string ActivationRule { get; init; } = string.Empty;
    public DateTime CheckpointUtc { get; init; }
    public DateTime ActivationStartUtc { get; init; }
    public DateTime ActivationEndUtc { get; init; }
    public int LookbackTradeCount { get; init; }
    public decimal LookbackNetPnl { get; init; }
    public decimal LookbackProfitFactor { get; init; }
    public bool PerfPass { get; init; }
    public bool FlowDataAvailable { get; init; }
    public bool FlowPass { get; init; }
    public bool Activated { get; init; }
    public string SkipReason { get; init; } = string.Empty;
    public int TradesInActivationWindow { get; init; }
    public decimal NetInActivationWindow { get; init; }
    public string CostScenario { get; init; } = string.Empty;
}

public sealed record CrossSymbolTradeRow
{
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public string ActivationRule { get; init; } = string.Empty;
    public DateTime EntryTimeUtc { get; init; }
    public DateTime ExitTimeUtc { get; init; }
    public decimal NetPnlQuote { get; init; }
    public bool IsWinner { get; init; }
    public string ExitReason { get; init; } = string.Empty;
    public string CostScenario { get; init; } = string.Empty;
}

public sealed record CrossSymbolSummaryRow
{
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public int HoldHours { get; init; }
    public int SignalCount { get; init; }
    public int BaselineTrades { get; init; }
    public decimal BaselineNet { get; init; }
    public decimal BaselineWinRate { get; init; }
    public decimal BaselineProfitFactor { get; init; }
    public string BestActivationRule { get; init; } = string.Empty;
    public decimal BestActivationNet { get; init; }
    public int BestActivationTrades { get; init; }
    public int ConfigsEvaluated { get; init; }
    public int ConfigsNetPositive { get; init; }
    public string CostScenario { get; init; } = string.Empty;
}

public sealed record CrossSymbolLeaderboardRow
{
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public int HoldHours { get; init; }
    public string ActivationRule { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public decimal NetPnl { get; init; }
    public decimal WinRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdown { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public decimal PositiveActivatedPeriodsPercent { get; init; }
    public decimal ModerateLatencyNet { get; init; }
    public decimal StressPlusNet { get; init; }
    public bool SparseWarning { get; init; }
    public bool OverfitWarning { get; init; }
    public bool SingleClusterWarning { get; init; }
    public string Recommendation { get; init; } = string.Empty;
    public string SuggestedFrozenProfileName { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}

public sealed record CrossSymbolCostSensitivityRow
{
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public string ActivationRule { get; init; } = string.Empty;
    public string CostScenario { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal WinRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public bool NetPositive { get; init; }
}

public sealed record CrossSymbolSimOutcome(
    CrossSymbolActivationConfig Config,
    IReadOnlyList<CrossSymbolPeriodRow> Periods,
    IReadOnlyList<RegimeDriftDiagnosticTrade> TakenTrades,
    int ActivatedPeriodCount,
    int PositivePeriodCount,
    int ClusterCount,
    IReadOnlyList<(DateTime Start, DateTime End)> ActiveRanges);

public sealed record NoPaidDataShortWindowFlowResearchV1CrossSymbolRunResult(
    IReadOnlyList<MultiSymbolDataCoverageRow> Coverage,
    IReadOnlyList<CrossSymbolSummaryRow> Summary,
    IReadOnlyList<CrossSymbolLeaderboardRow> Leaderboard,
    IReadOnlyList<CrossSymbolTradeRow> Trades,
    IReadOnlyList<CrossSymbolPeriodRow> Periods,
    IReadOnlyList<CrossSymbolCostSensitivityRow> CostSensitivity,
    IReadOnlyList<ReachabilityResearchAnswer> Answers,
    DateTime StudyStartUtc,
    DateTime StudyEndUtc);
