namespace TradingBot.Backtest;

public sealed record HistoricalStressReplayRow
{
    public string Label { get; init; } = "PreFreezeReplay";
    public int ReplayDaysBeforeFreeze { get; init; }
    public DateTime ReplayStartUtc { get; init; }
    public DateTime ReplayEndUtc { get; init; }
    public int Trades { get; init; }
    public decimal NetModerate { get; init; }
    public decimal NetModerateLatency002 { get; init; }
    public decimal NetStressPlus { get; init; }
    public decimal WinRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdown { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public int ActivationCheckpointCount { get; init; }
    public int ActivatedCheckpointCount { get; init; }
    public int ActivationFailedCheckpointCount { get; init; }
    public int ActivatedButNoEntryCount { get; init; }
    public IReadOnlyList<SkipReasonCountRow> TopActivationSkipReasons { get; init; } = [];
    public IReadOnlyList<SkipReasonCountRow> TopEntrySkipReasons { get; init; } = [];
}

public sealed record MissedOpportunityAuditRow
{
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string PeriodLabel { get; init; } = string.Empty;
    public DateTime WindowStartUtc { get; init; }
    public DateTime WindowEndUtc { get; init; }
    public decimal MaxFavorableShortMovePercent { get; init; }
    public bool WouldHitTarget { get; init; }
    public bool WouldHitStop { get; init; }
    public decimal EstimatedNetModerate { get; init; }
    public decimal? HypotheticalEntryPrice { get; init; }
    public decimal? HypotheticalExitPrice { get; init; }
    public string HypotheticalExitReason { get; init; } = "NoEntry";
    public decimal HypotheticalNetModerate { get; init; }
    public decimal HypotheticalNetLatency002 { get; init; }
    public decimal HypotheticalNetStressPlus { get; init; }
    public bool WasBaseSignalPresent { get; init; }
    public int BaseSignalCount { get; init; }
    public bool WasActivationPassed { get; init; }
    public bool IsHindsightOnly { get; init; }
    public string ActivationState { get; init; } = string.Empty;
    public string EntrySignalState { get; init; } = string.Empty;
    public string Classification { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed record ForwardIncubationAcceleratedValidationSummary
{
    public const string DiagnosticWarning =
        "Pre-freeze replay is diagnostic only and is not counted as forward proof.";

    public decimal TrueForwardNet { get; init; }
    public decimal PreFreezeReplayNet3d { get; init; }
    public decimal PreFreezeReplayNet7d { get; init; }
    public decimal PreFreezeReplayNet14d { get; init; }
    public int MissedWinnersCount { get; init; }
    public int BlockedLosersCount { get; init; }
    public string MainFinding { get; init; } = string.Empty;
    public string CompactSummaryLine { get; init; } = string.Empty;
    public IReadOnlyList<HistoricalStressReplayRow> HistoricalStressReplay { get; init; } = [];
    public IReadOnlyList<MissedOpportunityAuditRow> MissedOpportunityAudit { get; init; } = [];
}
