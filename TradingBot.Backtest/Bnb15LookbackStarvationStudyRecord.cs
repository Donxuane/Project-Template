namespace TradingBot.Backtest;

public sealed record Bnb15LookbackCheckpointDiagnosticRow
{
    public DateTime CheckpointUtc { get; init; }
    public DateTime LookbackStartUtc { get; init; }
    public DateTime LookbackEndUtc { get; init; }
    public int RequiredMinLookbackTrades { get; init; }
    public int ActualLookbackTrades { get; init; }
    public decimal LookbackNetModerate { get; init; }
    public decimal LookbackNetStressPlus { get; init; }
    public decimal LookbackProfitFactor { get; init; }
    public bool ActivationPassed { get; init; }
    public string SkipReason { get; init; } = string.Empty;
    public int ForwardBaseSignalsInActivationWindow { get; init; }
    public int ForwardTradesInActivationWindow { get; init; }
    public decimal NetIfActivated { get; init; }
    public decimal StressNetIfActivated { get; init; }
    public string RootCauseClassification { get; init; } = string.Empty;
}

public sealed record Bnb15LookbackDiagnosticVariantRow
{
    public string VariantName { get; init; } = string.Empty;
    public int MinLookbackTrades { get; init; }
    public int LookbackDays { get; init; }
    public int CheckpointFrequencyHours { get; init; }
    public int ActivationDurationHours { get; init; }
    public bool RequireNetPositive { get; init; }
    public bool RequireStressPlusPositive { get; init; }
    public int ActivatedCheckpointCount { get; init; }
    public int ForwardTrades { get; init; }
    public decimal NetModerate { get; init; }
    public decimal NetStressPlus { get; init; }
    public decimal WinRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdown { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public bool StressPassed { get; init; }
    public bool ForwardTradeCountPassed { get; init; }
    public string Recommendation { get; init; } = string.Empty;
}

public sealed record Bnb15LookbackStarvationPlainEnglishSummary
{
    public string SparseVsStrictGate { get; init; } = string.Empty;
    public string PrimaryStarvationGate { get; init; } = string.Empty;
    public string SafeNewIncubationCandidate { get; init; } = string.Empty;
    public string ShouldCurrentBnb15StayParked { get; init; } = string.Empty;
    public string OverallStudyRecommendation { get; init; } = string.Empty;
}

public sealed record Bnb15LookbackStarvationStudySummary
{
    public const string DiagnosticWarning =
        "BNB15 lookback starvation study is diagnostic/research only. Variants do not modify the frozen profile and are not forward proof.";

    public DateTime RunAtUtc { get; init; }
    public string FrozenProfileName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public string FrozenActivationRule { get; init; } = string.Empty;
    public DateTime ForwardWindowStartUtc { get; init; }
    public DateTime ForwardWindowEndUtc { get; init; }
    public decimal ForwardSpanDays { get; init; }
    public int FrozenForwardTrades { get; init; }
    public int FrozenActivatedCheckpointCount { get; init; }
    public int FrozenActivationCheckpointCount { get; init; }
    public string PrimaryRootCause { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, int> RootCauseCounts { get; init; }
        = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public Bnb15LookbackStarvationPlainEnglishSummary PlainEnglish { get; init; } = new();
    public string CompactSummaryLine { get; init; } = string.Empty;
    public bool BacktestOnly { get; init; } = true;
    public bool RealOrdersPlaced { get; init; }
    public bool LiveFuturesRecommended { get; init; }
    public int DiagnosticVariantCount { get; init; }
    public int CandidateForNewIncubationVariantCount { get; init; }
}

public sealed record Bnb15LookbackStarvationStudyResult(
    Bnb15LookbackStarvationStudySummary Summary,
    IReadOnlyList<Bnb15LookbackCheckpointDiagnosticRow> Checkpoints,
    IReadOnlyList<Bnb15LookbackDiagnosticVariantRow> Variants);
