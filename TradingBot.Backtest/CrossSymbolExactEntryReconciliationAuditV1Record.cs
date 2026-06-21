namespace TradingBot.Backtest;

public sealed record CrossSymbolExactEntryReconciliationAuditV1CandidateRow
{
    public string CandidateKey { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public string ActivationRule { get; init; } = string.Empty;
    public int V1TradeCount { get; init; }
    public IReadOnlyList<DateTime> V1TradeEntryTimes { get; init; } = [];
    public int V1ActivatedPeriodCount { get; init; }
    public int V1TradesInsideActivatedPeriods { get; init; }
    public int FrequencyStudyExactEntryCount { get; init; }
    public int ReplayedExactEntryCount { get; init; }
    public int MatchedByEntryTimeCount { get; init; }
    public int MatchedWithinOneCandleCount { get; init; }
    public int MatchedWithinActivationWindowCount { get; init; }
    public DateTime? FirstMismatchTimeUtc { get; init; }
    public string MismatchType { get; init; } = string.Empty;
    public string ReconciliationStatus { get; init; } = string.Empty;
    public bool LeaderboardKeyPresent { get; init; }
    public bool V2KeyPresent { get; init; }
    public bool FrequencyStudyKeyPresent { get; init; }
}

public sealed record CrossSymbolExactEntryReconciliationAuditV1SampleRow
{
    public string CandidateKey { get; init; } = string.Empty;
    public DateTime V1EntryTimeUtc { get; init; }
    public DateTime? ActivationStartUtc { get; init; }
    public DateTime? ActivationEndUtc { get; init; }
    public DateTime EvaluatorCheckedTimeUtc { get; init; }
    public bool EvaluatorActivationPassed { get; init; }
    public bool EvaluatorEntryPresent { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string MismatchType { get; init; } = string.Empty;
}

public sealed record CrossSymbolExactEntryReconciliationAuditV1PlainEnglish
{
    public string AreV1TradesRealExactBaseEntries { get; init; } = string.Empty;
    public string WhyFrequencyStudyReportedZero { get; init; } = string.Empty;
    public string IsZeroExactEntryResultValid { get; init; } = string.Empty;
    public string ShouldFrequencyStudyBeFixed { get; init; } = string.Empty;
    public string ExactFieldTimeAlignmentGoingForward { get; init; } = string.Empty;
}

public sealed record CrossSymbolExactEntryReconciliationAuditV1SummaryRow
{
    public const string DiagnosticWarning =
        "Cross-symbol exact entry reconciliation V1 is diagnostic/research only. Reporting/reconciliation only — never places orders.";

    public DateTime RunAtUtc { get; init; }
    public DateTime StudyStartUtc { get; init; }
    public DateTime StudyEndUtc { get; init; }
    public int EvaluatedCandidateCount { get; init; }
    public int CandidatesWithV1Trades { get; init; }
    public int CandidatesFrequencyZero { get; init; }
    public int CandidatesReplayedNonZero { get; init; }
    public int CandidatesExactMatch { get; init; }
    public int CandidatesV1TradesButFrequencyZero { get; init; }
    public int CandidatesEntryEvaluatorMismatch { get; init; }
    public int CandidatesActivationWindowMismatch { get; init; }
    public int CandidatesTimeAlignmentMismatch { get; init; }
    public int TotalV1TradesInWindow { get; init; }
    public int TotalV1TradesInsideActivatedPeriods { get; init; }
    public int TotalReplayedExactEntries { get; init; }
    public int TotalFrequencyStudyExactEntries { get; init; }
    public string PrimaryRootCause { get; init; } = string.Empty;
    public string CompactSummaryLine { get; init; } = string.Empty;
    public CrossSymbolExactEntryReconciliationAuditV1PlainEnglish PlainEnglish { get; init; } = new();
    public bool BacktestOnly { get; init; } = true;
    public bool RealOrdersPlaced { get; init; }
}

public sealed record CrossSymbolExactEntryReconciliationAuditV1RunResult(
    CrossSymbolExactEntryReconciliationAuditV1SummaryRow Summary,
    IReadOnlyList<CrossSymbolExactEntryReconciliationAuditV1CandidateRow> Candidates,
    IReadOnlyList<CrossSymbolExactEntryReconciliationAuditV1CandidateRow> Mismatches,
    IReadOnlyList<CrossSymbolExactEntryReconciliationAuditV1SampleRow> SampleTrades);

public sealed record CrossSymbolExactEntryReconciliationAuditV1InputBundle
{
    public string V1InputDirectory { get; init; } = string.Empty;
    public string? V2InputDirectory { get; init; }
    public string? FrequencyInputDirectory { get; init; }
    public DateTime? StudyStartUtc { get; init; }
    public DateTime? StudyEndUtc { get; init; }
    public IReadOnlyList<CrossSymbolLeaderboardRow> Leaderboard { get; init; } = [];
    public IReadOnlyList<CrossSymbolPeriodRow> Periods { get; init; } = [];
    public IReadOnlyList<CrossSymbolTradeRow> Trades { get; init; } = [];
    public IReadOnlyDictionary<string, CrossSymbolCandidateEngineV2CandidateRow> V2ByKey { get; init; }
        = new Dictionary<string, CrossSymbolCandidateEngineV2CandidateRow>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, CrossCandidateExactEntryFrequencyStudyV1CandidateRow> FrequencyByKey { get; init; }
        = new Dictionary<string, CrossCandidateExactEntryFrequencyStudyV1CandidateRow>(StringComparer.OrdinalIgnoreCase);
}
