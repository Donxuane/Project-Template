using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Models.Decision;

public sealed class StrategySignalResult
{
    public string StrategyName { get; init; } = string.Empty;
    public TradeSignal Signal { get; init; }
    public string Reason { get; init; } = string.Empty;
    public decimal Confidence { get; init; } = 1.0m;
    public int? TrendConfidenceScore { get; init; }
    public int? MarketConditionScore { get; init; }
    public string? VolatilityRegime { get; init; }
    public decimal? ExpectedTargetPrice { get; init; }
    public decimal? ExpectedMovePercent { get; init; }
    public string? ExpectedTargetSource { get; init; }
    public decimal? BreakoutRangeHigh { get; init; }
    public decimal? BreakoutRangeLow { get; init; }
    public decimal? BreakoutThresholdPrice { get; init; }
    public decimal? ExpectedTargetStructureExtensionUsed { get; init; }
    public decimal? ExpectedTargetAtrUsed { get; init; }
    public int? ConsecutiveBullishTrendCandles { get; init; }
    public bool? EntryNearRecentHigh { get; init; }
    public decimal? DistanceToRecentHighPercent { get; init; }
    public decimal? DistanceToInvalidationPercent { get; init; }
    public bool? CurrentCloseAboveRecentHigh { get; init; }
    public bool? PreviousCandleBearish { get; init; }
    public decimal? ShortMaSlopePercent { get; init; }
    public decimal? TrendStrengthPercent { get; init; }
    public string? ProjectionMode { get; init; }
    public decimal? ProjectedExtension { get; init; }
    public string? NormalTrendEntryRejectedReason { get; init; }
}
