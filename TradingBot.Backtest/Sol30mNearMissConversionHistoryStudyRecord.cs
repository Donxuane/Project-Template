namespace TradingBot.Backtest;

public sealed record Sol30mNearMissConversionHistoryEventRow
{
    public DateTime EventTimeUtc { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public bool ActivationPassed { get; init; }
    public string NearMissClassification { get; init; } = string.Empty;
    public string FailedCondition { get; init; } = string.Empty;
    public decimal DistanceToEntryPercent { get; init; }
    public string DistanceBucket { get; init; } = string.Empty;
    public decimal LatestClose { get; init; }
    public decimal RecentHigh { get; init; }
    public decimal RecentLow { get; init; }
    public decimal AtrPercent { get; init; }
    public bool ElevatedVolPassed { get; init; }
    public bool CooldownClear { get; init; }
    public bool NoOpenTradeOverlap { get; init; }
    public bool ConvertedToExactEntry { get; init; }
    public DateTime? ConversionTimeUtc { get; init; }
    public int? MinutesToConversion { get; init; }
    public int? CandlesToConversion { get; init; }
    public decimal? MaxDistanceBeforeConversion { get; init; }
    public bool DidPriceMoveTowardEntry { get; init; }
    public bool DidPriceMoveAwayFromEntry { get; init; }
    public decimal? ConversionEntryPrice { get; init; }
    public DateTime? ConversionExitTimeUtc { get; init; }
    public string ConversionExitReason { get; init; } = string.Empty;
    public decimal? ConversionNetModerate { get; init; }
    public decimal? ConversionNetStressPlus { get; init; }
    public bool? IsWinnerModerate { get; init; }
    public bool? IsWinnerStressPlus { get; init; }
    public bool ConvertedWithin1Candle { get; init; }
    public bool ConvertedWithin2Candles { get; init; }
    public bool ConvertedWithin4Candles { get; init; }
    public bool ConvertedWithin8Candles { get; init; }
    public bool ConvertedWithin24h { get; init; }
}

public sealed record Sol30mNearMissConversionHistoryPlainEnglish
{
    public string DoesNearMissUsuallyConvert { get; init; } = string.Empty;
    public string HowFastDoesItConvert { get; init; } = string.Empty;
    public string AreConvertedEntriesProfitableStressPlus { get; init; } = string.Empty;
    public string IsCurrentDistanceBucketGoodOrBad { get; init; } = string.Empty;
    public string ShouldKeepWatcherRunning { get; init; } = string.Empty;
    public string ShouldTradeNearMissBeforeExactEntry { get; init; } = "No.";
}

public sealed record Sol30mNearMissConversionHistorySummaryRow
{
    public const string DiagnosticWarning =
        "SOL 30m near-miss conversion history is diagnostic/research only. Historical conversion is not forward proof for the current near-miss. Never places orders or trades near-miss before exact entry.";

    public DateTime RunAtUtc { get; init; }
    public DateTime StudyStartUtc { get; init; }
    public DateTime StudyEndUtc { get; init; }
    public string CandidateKey { get; init; } = string.Empty;
    public string ActivationRule { get; init; } = string.Empty;
    public decimal? CurrentNearMissDistancePercent { get; init; }
    public int TotalNearMissEvents { get; init; }
    public int OneConditionAwayEvents { get; init; }
    public int ConvertedWithin1Candle { get; init; }
    public int ConvertedWithin2Candles { get; init; }
    public int ConvertedWithin4Candles { get; init; }
    public int ConvertedWithin8Candles { get; init; }
    public int ConvertedWithin24h { get; init; }
    public decimal ConversionRateWithin4Candles { get; init; }
    public decimal ConversionRateWithin24h { get; init; }
    public int ConvertedTradeCount { get; init; }
    public decimal ConvertedNetModerate { get; init; }
    public decimal ConvertedNetStressPlus { get; init; }
    public decimal ConvertedWinRate { get; init; }
    public decimal ConvertedProfitFactor { get; init; }
    public int NonConvertedCount { get; init; }
    public decimal AverageDistanceToEntry { get; init; }
    public decimal MedianDistanceToEntry { get; init; }
    public string BestDistanceBucket { get; init; } = string.Empty;
    public string WorstDistanceBucket { get; init; } = string.Empty;
    public string CurrentNearMissSimilarityBucket { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public string CompactSummaryLine { get; init; } = string.Empty;
    public Sol30mNearMissConversionHistoryPlainEnglish PlainEnglish { get; init; } = new();
    public bool BacktestOnly { get; init; } = true;
    public bool RealOrdersPlaced { get; init; }
    public bool LiveFuturesRecommended { get; init; }
    public bool NearMissConversionIsNotForwardProof { get; init; } = true;
}

public sealed record Sol30mNearMissConversionHistoryStudyResult(
    Sol30mNearMissConversionHistorySummaryRow Summary,
    IReadOnlyList<Sol30mNearMissConversionHistoryEventRow> Events,
    IReadOnlyList<Sol30mNearMissConversionHistoryEventRow> Conversions,
    IReadOnlyList<Sol30mNearMissConversionHistoryEventRow> NonConversions);
