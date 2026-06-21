namespace TradingBot.Backtest;

public sealed record CrossCandidateExactEntryFrequencyStudyV1CandidateRow
{
    public string CandidateKey { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public string ActivationRule { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public int V1TradeCount { get; init; }
    public int V1TradesInsideActivatedPeriods { get; init; }
    public int ActivatedCheckpointCount { get; init; }
    public int ExactEntryCountInsideActivatedWindows { get; init; }
    public decimal ExactEntriesPerDay { get; init; }
    public decimal? DaysSinceLastExactEntry { get; init; }
    public DateTime? LastExactEntryUtc { get; init; }
    public decimal? MedianHoursBetweenExactEntries { get; init; }
    public decimal? MaxHoursBetweenExactEntries { get; init; }
    public int EvaluatorReplayPresentCount { get; init; }
    public int EvaluatorOpenTradeOverlapCount { get; init; }
    public string FrequencyCountingMethod { get; init; } = "V1TradesInsideActivatedPeriods";
    public decimal NetModerate { get; init; }
    public decimal NetStressPlus { get; init; }
    public decimal WinRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdown { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public bool SparseWarning { get; init; }
    public bool OverfitWarning { get; init; }
    public bool SingleClusterWarning { get; init; }
    public decimal PositiveActivatedPeriodsPercent { get; init; }
    public decimal EntryFrequencyScore { get; init; }
    public decimal StressQualityScore { get; init; }
    public decimal CombinedCandidateScore { get; init; }
    public string EntryFrequencyClassification { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public bool? ActivationCurrentlyPassed { get; init; }
    public bool? BaseEntrySignalPresentNow { get; init; }
}

public sealed record CrossCandidateExactEntryFrequencyStudyV1PlainEnglish
{
    public string CountingBugFixNote { get; init; } = string.Empty;
    public string FixedFrequencyMethodNote { get; init; } = string.Empty;
    public string WhichCandidatesProduceExactEntriesMostOften { get; init; } = string.Empty;
    public string WhichAreStressPositiveAndNotTooRare { get; init; } = string.Empty;
    public string WhichAreProfitableButTooRare { get; init; } = string.Empty;
    public string WhichShouldWatcherFocusOn { get; init; } = string.Empty;
    public string WorthMovingTowardTestnetOrderPreparation { get; init; } = string.Empty;
    public string HistoricalNotForwardProofNote { get; init; } = string.Empty;
    public string LiveTestnetGuardNote { get; init; } = string.Empty;
}

public sealed record CrossCandidateExactEntryFrequencyStudyV1SummaryRow
{
    public const string DiagnosticWarning =
        "Cross-candidate exact entry frequency V1 is diagnostic/research only. Never places orders or trades near-miss before exact entry. Historical frequency is not forward proof.";

    public DateTime RunAtUtc { get; init; }
    public DateTime StudyStartUtc { get; init; }
    public DateTime StudyEndUtc { get; init; }
    public decimal StudySpanDays { get; init; }
    public int EvaluatedCandidateCount { get; init; }
    public int CandidatesWithExactEntries { get; init; }
    public int PromoteToExactEntryWatcherCount { get; init; }
    public int TooRareCount { get; init; }
    public int StressNegativeCount { get; init; }
    public int TotalV1Trades { get; init; }
    public int TotalV1TradesInsideActivatedPeriods { get; init; }
    public int TotalExactEntriesAfterFix { get; init; }
    public int CandidatesWithExactEntriesAfterFix { get; init; }
    public int CandidatesStillTooRare { get; init; }
    public int CandidatesStressPositiveAndFrequentEnough { get; init; }
    public bool CountingBugFixed { get; init; } = true;
    public string CompactSummaryLine { get; init; } = string.Empty;
    public CrossCandidateExactEntryFrequencyStudyV1PlainEnglish PlainEnglish { get; init; } = new();
    public bool BacktestOnly { get; init; } = true;
    public bool RealOrdersPlaced { get; init; }
    public bool LiveFuturesRecommended { get; init; }
    public bool NearMissNotUsed { get; init; } = true;
}

public sealed record CrossCandidateExactEntryFrequencyStudyV1RunResult(
    CrossCandidateExactEntryFrequencyStudyV1SummaryRow Summary,
    IReadOnlyList<CrossCandidateExactEntryFrequencyStudyV1CandidateRow> Candidates,
    IReadOnlyList<CrossCandidateExactEntryFrequencyStudyV1CandidateRow> TopFrequency,
    IReadOnlyList<CrossCandidateExactEntryFrequencyStudyV1CandidateRow> TopStressPositive,
    IReadOnlyList<CrossCandidateExactEntryFrequencyStudyV1CandidateRow> TooRare);

public sealed record CrossCandidateExactEntryFrequencyStudyV1InputBundle
{
    public string V1InputDirectory { get; init; } = string.Empty;
    public string? V2InputDirectory { get; init; }
    public string? ScannerInputDirectory { get; init; }
    public DateTime? StudyStartUtc { get; init; }
    public IReadOnlyList<CrossSymbolLeaderboardRow> Leaderboard { get; init; } = [];
    public IReadOnlyList<CrossSymbolPeriodRow> Periods { get; init; } = [];
    public IReadOnlyList<CrossSymbolTradeRow> Trades { get; init; } = [];
    public IReadOnlyList<CrossSymbolCostSensitivityRow> CostSensitivity { get; init; } = [];
    public IReadOnlyDictionary<string, CrossSymbolCandidateEngineV2CandidateRow> V2ByKey { get; init; }
        = new Dictionary<string, CrossSymbolCandidateEngineV2CandidateRow>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, CurrentOpportunityScannerV1CandidateRow> ScannerByKey { get; init; }
        = new Dictionary<string, CurrentOpportunityScannerV1CandidateRow>(StringComparer.OrdinalIgnoreCase);
}
