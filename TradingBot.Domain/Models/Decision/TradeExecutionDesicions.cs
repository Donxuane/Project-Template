using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Models.Decision;

public class TradeExecutionDecisions
{
    public long? Id { get; set; }
    public string CorrelationId { get; set; } = null!;
    public string? DecisionId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? StrategyName { get; set; }
    public TradingSymbol? Symbol { get; set; }
    public TradeSignal? Action { get; set; }
    public TradeSignal? RawSignal { get; set; }
    public TradingMode? TradingMode { get; set; }
    public TradeExecutionIntent? ExecutionIntent { get; set; }
    public OrderSide? Side { get; set; }
    public DecisionStatus? DecisionStatus { get; set; }
    public GuardStage? GuardStage { get; set; }
    public decimal? Confidence { get; set; }
    public decimal? MinConfidence { get; set; }
    public string? Reason { get; set; }
    public bool? IsInCooldown { get; set; } = false;
    public long? CooldownRemainingSeconds { get; set; }
    public DateTimeOffset? CooldownLastTrade { get; set; }
    public bool? IdempotencyDuplicate { get; set; } = false;
    public bool? RiskIsAllowed { get; set; }
    public string? RiskReason { get; set; }
    public decimal? StopLossPrice { get; set; }
    public decimal? TakeProfitPrice { get; set; }
    public decimal? ExpectedMovePercent { get; set; }
    public decimal? ExpectedTargetPrice { get; set; }
    public string? ExpectedTargetSource { get; set; }
    public int? TrendConfidenceScore { get; set; }
    public int? MarketConditionScore { get; set; }
    public string? VolatilityRegime { get; set; }
    public bool? RequiresReducedPositionSize { get; set; }
    public int? ConsecutiveBullishTrendCandles { get; set; }
    public bool? CurrentCloseAboveRecentHigh { get; set; }
    public decimal? DistanceToInvalidationPercent { get; set; }
    public bool? PreviousCandleBearish { get; set; }
    public bool? EntryNearRecentHigh { get; set; }
    public decimal? ShortMaSlopePercent { get; set; }
    public decimal? TrendStrengthPercent { get; set; }
    public string? ProjectionMode { get; set; }
    public decimal? ProjectedExtension { get; set; }
    public bool? ExecutionSuccess { get; set; } = false;
    public long? LocalOrderId { get; set; }
    public long? ExchangeOrderId { get; set; }
    public string? ExecutionError { get; set; }
    public DateTimeOffset? Created_At { get; set; }
    public DateTimeOffset? Updated_At { get; set; }
}
