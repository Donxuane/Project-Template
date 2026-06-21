namespace TradingBot.Backtest;

public sealed record LookbackTradeCountRow
{
    public DateTime CheckpointUtc { get; init; }
    public int LookbackTradeCount { get; init; }
}

/// <summary>
/// Diagnostic-only bottleneck audit for frozen forward-incubation profiles.
/// Does not affect health gates, verdicts, activation thresholds, or frozen state.
/// </summary>
public sealed record FrozenProfileBottleneckAuditRow
{
    public string ProfileName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public DateTime ForwardWindowStartUtc { get; init; }
    public DateTime ForwardWindowEndUtc { get; init; }
    public decimal ForwardSpanDays { get; init; }
    public int ForwardTrades { get; init; }
    public decimal NetModerate { get; init; }
    public decimal NetStressPlus { get; init; }
    public int ActivationCheckpointCount { get; init; }
    public int ActivatedCheckpointCount { get; init; }
    public int ActivationFailedCheckpointCount { get; init; }
    public int ActivatedButNoEntryCount { get; init; }
    public int BaseSignalsInsideForwardWindow { get; init; }
    public int BaseSignalsInsideActivatedWindows { get; init; }
    public int BaseSignalsInsideActivationFailedWindows { get; init; }
    public decimal NetIfAllBaseSignalsAllowed { get; init; }
    public decimal NetIfOnlyActivatedBaseSignalsAllowed { get; init; }
    public decimal NetIfActivationFailedBaseSignalsAllowed { get; init; }
    public IReadOnlyList<SkipReasonCountRow> TopActivationFailureReasons { get; init; } = [];
    public IReadOnlyList<SkipReasonCountRow> TopEntryFailureReasons { get; init; } = [];
    public IReadOnlyList<LookbackTradeCountRow> LookbackTradeCountsByCheckpoint { get; init; } = [];
    public int CooldownBlockedCount { get; init; }
    public int HindsightOnlyMoveCount { get; init; }
    public int RealMissedWinnerCount { get; init; }
    public int BlockedLoserCount { get; init; }
    public string BottleneckClassification { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public string BottleneckExplanation { get; init; } = string.Empty;
    public bool ShadowEntrySignalPresent { get; init; }
    public bool ShadowActivationPassed { get; init; }
}

public sealed record FrozenProfileBottleneckAuditSummary
{
    public const string DiagnosticWarning =
        "Frozen-profile bottleneck audit is diagnostic only. It does not change strategy logic, activation thresholds, frozen profiles, health gates, or verdicts.";

    public DateTime RunAtUtc { get; init; }
    public string CompactSummaryLine { get; init; } = string.Empty;
    public IReadOnlyList<FrozenProfileBottleneckAuditRow> Profiles { get; init; } = [];
}
