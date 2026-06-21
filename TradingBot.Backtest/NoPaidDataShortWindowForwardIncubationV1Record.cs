namespace TradingBot.Backtest;

/// <summary>
/// Persistent frozen-candidate state. Created once and never modified afterwards:
/// FrozenStartUtc is the discovery-data end; forward evaluation only ever looks after it.
/// </summary>
public sealed record FrozenCandidateState(
    string ProfileName,
    DateTime CreatedAtUtc,
    DateTime FrozenStartUtc,
    string BaseRule,
    string Symbol,
    string Interval,
    string EntryMode,
    decimal TargetPercent,
    decimal StopPercent,
    int MaxHoldMinutes,
    int CooldownCandles,
    string OverlapPolicy,
    string ActivationFlowCondition,
    int CheckpointFrequencyHours,
    int ActivationPeriodHours,
    int LookbackDaysInformational,
    string DiscoveryWindow,
    int DiscoveryBaselineTrades,
    decimal DiscoveryBaselineNet,
    int DiscoveryCandidateTrades,
    decimal DiscoveryCandidateNet,
    decimal DiscoveryCandidateProfitFactor,
    decimal DiscoveryCandidateStressPlusNet,
    string Caveats);

public sealed record FrozenCandidateSummaryRow
{
    public string ProfileName { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime FrozenStartUtc { get; init; }
    public string BaseRule { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string EntryMode { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public int MaxHoldMinutes { get; init; }
    public int CooldownCandles { get; init; }
    public string OverlapPolicy { get; init; } = string.Empty;
    public string ActivationFlowCondition { get; init; } = string.Empty;
    public int CheckpointFrequencyHours { get; init; }
    public int ActivationPeriodHours { get; init; }
    public int LookbackDaysInformational { get; init; }
    public string DiscoveryWindow { get; init; } = string.Empty;
    public int DiscoveryBaselineTrades { get; init; }
    public decimal DiscoveryBaselineNet { get; init; }
    public int DiscoveryCandidateTrades { get; init; }
    public decimal DiscoveryCandidateNet { get; init; }
    public decimal DiscoveryCandidateProfitFactor { get; init; }
    public decimal DiscoveryCandidateStressPlusNet { get; init; }
    public string Caveats { get; init; } = string.Empty;
    public DateTime RunAtUtc { get; init; }
    public DateTime ForwardWindowEndUtc { get; init; }
    public decimal ForwardSpanDays { get; init; }
    public int ForwardTrades { get; init; }
    public decimal ForwardNetModerate { get; init; }
    public string Verdict { get; init; } = string.Empty;
    public NormalizedRiskPnlMetrics NormalizedRisk { get; init; } = new();
}

public sealed record ForwardDataCoverageRow
{
    public string Symbol { get; init; } = string.Empty;
    public string SourceKey { get; init; } = string.Empty;
    public bool LocalFilePresent { get; init; }
    public int LocalRecordCount { get; init; }
    public DateTime? LocalStartUtc { get; init; }
    public DateTime? LocalEndUtc { get; init; }
    public decimal LocalSpanDays { get; init; }
    public decimal DaysBeyondFrozenStart { get; init; }
    public string Notes { get; init; } = string.Empty;
}

public sealed record ForwardHealthGateRow
{
    public string GateName { get; init; } = string.Empty;
    public string Requirement { get; init; } = string.Empty;
    public string ObservedValue { get; init; } = string.Empty;
    public bool Applicable { get; init; }
    public bool Pass { get; init; }
    public string Notes { get; init; } = string.Empty;
}

public sealed record ForwardIncubationHistoryEntry
{
    public DateTime RunAtUtc { get; init; }
    public DateTime FrozenStartUtc { get; init; }
    public DateTime ForwardWindowEndUtc { get; init; }
    public decimal ForwardSpanDays { get; init; }
    public int ForwardTrades { get; init; }
    public decimal ForwardNetModerate { get; init; }
    public decimal ForwardNetLatency002 { get; init; }
    public decimal ForwardNetStressPlus { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public decimal PositivePeriodRate { get; init; }
    public int HealthGatesPassed { get; init; }
    public int HealthGatesTotal { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record NoPaidDataShortWindowForwardIncubationV1RunResult(
    FrozenCandidateSummaryRow FrozenSummary,
    IReadOnlyList<ForwardDataCoverageRow> DataCoverage,
    IReadOnlyList<ShortWindowTradeRow> ForwardTrades,
    IReadOnlyList<ShortWindowPeriodRow> ForwardPeriods,
    IReadOnlyList<ShortWindowCostSensitivityRow> CostSensitivity,
    IReadOnlyList<ForwardHealthGateRow> HealthGates,
    IReadOnlyList<ForwardIncubationHistoryEntry> History,
    IReadOnlyList<ReachabilityResearchAnswer> Answers,
    ForwardIncubationNoTradeReasonSummary NoTradeReasonSummary,
    string Verdict,
    DateTime FrozenStartUtc,
    DateTime ForwardWindowEndUtc,
    decimal ForwardSpanDays);
