using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public sealed record KlineCandle(
    TradingSymbol Symbol,
    DateTime OpenTimeUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume)
{
    public DateTime CloseTimeUtc => OpenTimeUtc.AddMinutes(1);
}

public sealed record ReplayProfileDefinition(
    string ProfileName,
    IReadOnlyList<TradingSymbol> Symbols,
    IReadOnlyDictionary<string, string> ConfigOverrides);

public sealed record DataQualityIssue(
    string Interval,
    TradingSymbol Symbol,
    string Severity,
    string Message);

public sealed record SymbolValidationResult(
    TradingSymbol Symbol,
    IReadOnlyList<KlineCandle> Candles,
    IReadOnlyList<DataQualityIssue> Issues);

public sealed record ExecutionCostSettings(
    decimal FeeRatePercent,
    decimal SpreadPercent,
    decimal SlippagePercent);

public sealed record SimulatedTrade
{
    public string Interval { get; init; } = "1m";
    public string ProfileName { get; init; } = string.Empty;
    public string Symbols { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public DateTime EntryTimeUtc { get; init; }
    public decimal EntryPrice { get; init; }
    public DateTime ExitTimeUtc { get; init; }
    public decimal ExitPrice { get; init; }
    public decimal Quantity { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal FeeAndSpreadEstimateQuote { get; init; }
    public string EntryReason { get; init; } = string.Empty;
    public string ExitReason { get; init; } = string.Empty;
    public decimal? ExpectedMovePercent { get; init; }
    public decimal? ExpectedTargetPrice { get; init; }
    public string? ExpectedTargetSource { get; init; }
    public decimal? RewardRisk { get; init; }
    public int? ConsecutiveBullishTrendCandles { get; init; }
    public bool? CurrentCloseAboveRecentHigh { get; init; }
    public decimal? DistanceToInvalidationPercent { get; init; }
    public bool? PreviousCandleBearish { get; init; }
    public bool? EntryNearRecentHigh { get; init; }
    public decimal? ShortMaSlopePercent { get; init; }
    public decimal? TrendStrengthPercent { get; init; }
    public string? ProjectionMode { get; init; }
    public decimal? ProjectedExtension { get; init; }
    public bool WasGuarded { get; init; }
    public decimal? EstimatedRoundTripCostPercent { get; init; }
    public decimal? EstimatedNetMovePercent { get; init; }
    public decimal? MaxFavorablePrice { get; init; }
    public decimal? MaxAdversePrice { get; init; }
    public decimal? MfePercent { get; init; }
    public decimal? MaePercent { get; init; }
    public bool TouchedExpectedTarget { get; init; }
    public DateTime? FirstExpectedTargetTouchTimeUtc { get; init; }
    public decimal? CounterfactualExitAtExpectedTargetNetPnlQuote { get; init; }
    public decimal? CounterfactualDeltaVsActualNetPnlQuote { get; init; }
    public string? VolatilityRegime { get; init; }
    public decimal DurationMinutes { get; init; }
}

public sealed record ReplaySummaryRow
{
    public string Interval { get; init; } = "1m";
    public string ProfileName { get; init; } = string.Empty;
    public string Symbols { get; init; } = string.Empty;
    public int TradesCount { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public decimal WinRatePercent { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal EstimatedNetPnlQuote { get; init; }
    public decimal TotalFeeAndSpreadEstimateQuote { get; init; }
    public decimal AverageWinQuote { get; init; }
    public decimal AverageLossQuote { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public decimal AverageTradeDurationMinutes { get; init; }
    public int RawBuySignals { get; init; }
    public int ExecutedBuySignals { get; init; }
    public int BlockedBuySignals { get; init; }
    public int GrossWinningTrades { get; init; }
    public decimal GrossWinRatePercent { get; init; }
    public int NetWinningTrades { get; init; }
    public decimal NetWinRatePercent { get; init; }
    public int ExpectedTargetTouchTrades { get; init; }
    public decimal ExpectedTargetTouchRatePercent { get; init; }
    public decimal AverageMfePercent { get; init; }
    public decimal AverageMaePercent { get; init; }
    public decimal ExpectedTargetCounterfactualNetPnlQuote { get; init; }
    public decimal ExpectedTargetCounterfactualDeltaQuote { get; init; }
    public IReadOnlyDictionary<string, int> BlockedByReason { get; init; } = new Dictionary<string, int>();
    public bool EnableLowVolatilityBreakoutEntry { get; init; }
    public int BreakoutLookbackCandles { get; init; }
    public decimal BreakoutBufferPercent { get; init; }
    public int BreakoutConfirmationCandles { get; init; }
    public decimal MinBreakoutSlopePercent { get; init; }
    public bool UseConfirmedClosedCandlesForLowVolBreakout { get; init; }
    public IReadOnlyDictionary<TradingSymbol, int> SymbolBreakdown { get; init; } = new Dictionary<TradingSymbol, int>();
    public IReadOnlyDictionary<string, int> ExitReasonBreakdown { get; init; } = new Dictionary<string, int>();
}

public sealed record BacktestRunResult(
    IReadOnlyList<ReplaySummaryRow> Summaries,
    IReadOnlyList<SimulatedTrade> Trades,
    IReadOnlyList<BlockedEntryRecord> BlockedEntries,
    IReadOnlyList<AggregationDiagnosticsRecord> AggregationDiagnostics,
    IReadOnlyList<DataQualityIssue> DataIssues);

public sealed record StrategyEvaluation(
    StrategySignalResult Signal,
    MarketSnapshot Snapshot);

public sealed record BlockedEntryRecord
{
    public string Interval { get; init; } = "1m";
    public string ProfileName { get; init; } = string.Empty;
    public string Symbols { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public DateTime TimeUtc { get; init; }
    public string Reason { get; init; } = string.Empty;
    public decimal Confidence { get; init; }
    public decimal ConfidenceThreshold { get; init; }
    public decimal? ExpectedMovePercent { get; init; }
    public decimal? EstimatedNetMovePercent { get; init; }
    public string? ExpectedTargetSource { get; init; }
    public string SignalReason { get; init; } = string.Empty;
}

public sealed record ProfileSignalStats
{
    public int RawBuySignals { get; set; }
    public int ExecutedBuySignals { get; set; }
    public int BlockedBuySignals { get; set; }
    public Dictionary<string, int> BlockedByReason { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void IncrementBlocked(string reason)
    {
        BlockedBuySignals++;
        BlockedByReason.TryGetValue(reason, out var count);
        BlockedByReason[reason] = count + 1;
    }
}

public sealed record AggregationDiagnosticsRecord
{
    public string Interval { get; init; } = "1m";
    public string SourceInterval { get; init; } = "1m";
    public string TargetInterval { get; init; } = "1m";
    public TradingSymbol Symbol { get; init; }
    public int InputCandleCount { get; init; }
    public int OutputCandleCount { get; init; }
    public int DroppedIncompleteFinalBucketCount { get; init; }
    public int InheritedGapCount { get; init; }
}
