namespace TradingBot.Backtest;

public enum AdaptiveActivationConditionType
{
    AlwaysOn,
    RecentNetPositive,
    RecentProfitFactor,
    RecentWinRateAndNet,
    DrawdownGuard,
    RegimeRecentPerformance
}

public enum AdaptiveRegimeFilterKind
{
    None,
    BtcReturn30mPositive,
    BtcReturn60mPositive,
    VolatilityNormal,
    Btc30Q3VolNormal
}

public sealed record AdaptiveActivationRuleConfig(
    string ActivationRuleName,
    AdaptiveActivationConditionType ConditionType,
    int CheckpointFrequencyDays,
    int LookbackDays,
    int ActivationPeriodDays,
    int MinLookbackTrades,
    decimal? ProfitFactorThreshold,
    decimal? MaxDrawdownQuote,
    int? ConsecutiveLossLimit,
    int CooldownDays,
    AdaptiveRegimeFilterKind RegimeFilter,
    string Description);

public sealed record AdaptiveActivationPeriodRow
{
    public string ActivationRuleName { get; init; } = string.Empty;
    public int LookbackDays { get; init; }
    public int ActivationPeriodDays { get; init; }
    public int CheckpointFrequencyDays { get; init; }
    public DateTime CheckpointUtc { get; init; }
    public DateTime ActivationStartUtc { get; init; }
    public DateTime ActivationEndUtc { get; init; }
    public int LookbackTradeCount { get; init; }
    public decimal LookbackNetPnl { get; init; }
    public decimal LookbackProfitFactor { get; init; }
    public decimal LookbackWinRate { get; init; }
    public bool Activated { get; init; }
    public string DeactivationReason { get; init; } = string.Empty;
    public int TradesDuringActivation { get; init; }
    public decimal NetPnlDuringActivation { get; init; }
    public string CostScenario { get; init; } = string.Empty;
}

public sealed record AdaptiveActivationSummaryRow
{
    public string ActivationRuleName { get; init; } = string.Empty;
    public string ConditionType { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int CheckpointFrequencyDays { get; init; }
    public int LookbackDays { get; init; }
    public int ActivationPeriodDays { get; init; }
    public int MinLookbackTrades { get; init; }
    public string CostScenario { get; init; } = string.Empty;
    public int TotalTrades { get; init; }
    public int BaselineTrades { get; init; }
    public decimal TradeRetentionRate { get; init; }
    public decimal Full365NetPnl { get; init; }
    public decimal BaselineFull365NetPnl { get; init; }
    public decimal Full365Delta { get; init; }
    public decimal OlderNetPnl { get; init; }
    public decimal BaselineOlderNetPnl { get; init; }
    public decimal OlderDelta { get; init; }
    public decimal Recent90dNetPnl { get; init; }
    public decimal BaselineRecent90dNetPnl { get; init; }
    public decimal Recent90dDelta { get; init; }
    public int PositivePeriodsCount { get; init; }
    public int TotalPeriodsCount { get; init; }
    public decimal PositivePeriodRate { get; init; }
    public decimal MaxDrawdownQuote { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public decimal WinRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public bool MeetsMinTrades { get; init; }
    public bool Full365NearBreakeven { get; init; }
    public bool OlderLossReduced { get; init; }
    public bool Recent90dPositive { get; init; }
    public bool PositivePeriodsMajority { get; init; }
    public bool PassesSuccessCriteria { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record AdaptiveActivationTradeRow
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
    public bool InOlder { get; init; }
    public bool InRecent90d { get; init; }
    public string MonthKey { get; init; } = string.Empty;
}

public sealed record AdaptiveActivationWindowPerformanceRow
{
    public string ActivationRuleName { get; init; } = string.Empty;
    public string WindowLabel { get; init; } = string.Empty;
    public string CostScenario { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal BaselineNetPnlQuote { get; init; }
    public decimal Delta { get; init; }
    public bool Positive { get; init; }
}

public sealed record AdaptiveActivationCostSensitivityRow
{
    public string ActivationRuleName { get; init; } = string.Empty;
    public string CostScenario { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public decimal Full365NetPnl { get; init; }
    public decimal OlderNetPnl { get; init; }
    public decimal Recent90dNetPnl { get; init; }
    public bool Full365Positive { get; init; }
    public bool SurvivesModerateSlippage { get; init; }
    public bool SurvivesStress { get; init; }
}

public sealed record AdaptiveActivationDrawdownRow
{
    public string ActivationRuleName { get; init; } = string.Empty;
    public string CostScenario { get; init; } = string.Empty;
    public decimal MaxDrawdownQuote { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public decimal WorstTradeNet { get; init; }
    public DateTime? MaxDrawdownPeakUtc { get; init; }
    public DateTime? MaxDrawdownTroughUtc { get; init; }
}

public sealed record AdaptiveActivationSimResult(
    AdaptiveActivationRuleConfig Rule,
    IReadOnlyList<AdaptiveActivationPeriodRow> Periods,
    IReadOnlyList<AdaptiveActivationTradeRow> Trades,
    AdaptiveActivationSummaryRow Summary);

public sealed record NoPaidDataAdaptiveActivationV1RunResult(
    IReadOnlyList<AdaptiveActivationSummaryRow> Summary,
    IReadOnlyList<AdaptiveActivationTradeRow> Trades,
    IReadOnlyList<AdaptiveActivationPeriodRow> Periods,
    IReadOnlyList<AdaptiveActivationWindowPerformanceRow> WindowPerformance,
    IReadOnlyList<AdaptiveActivationCostSensitivityRow> CostSensitivity,
    IReadOnlyList<AdaptiveActivationDrawdownRow> Drawdown,
    IReadOnlyList<ReachabilityResearchAnswer> Answers,
    int BaselineTradeCount,
    decimal BaselineFull365NetPnl,
    DateTime DataStartUtc,
    DateTime DataEndUtc);
