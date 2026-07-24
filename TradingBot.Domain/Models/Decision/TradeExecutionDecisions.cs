using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Models.Decision;

public sealed class TradeExecutionDecisions
{
    public long? Id { get; set; }
    public string CorrelationId { get; set; } = null!;
    public string? DecisionId { get; set; }
    public string? StrategyName { get; set; }
    public TradingSymbol? Symbol { get; set; }
    public TradeSignal? Action { get; set; }
    public TradeSignal? RawSignal { get; set; }
    public TradingMode? TradingMode { get; set; }
    public TradeExecutionIntent? ExecutionIntent { get; set; }
    public OrderSide? Side { get; set; }
    public DecisionStatus? DecisionStatus { get; set; }
    public GuardStage? GuardStage { get; set; }
    public string? Reason { get; set; }
    public bool? RiskIsAllowed { get; set; }
    public decimal? StopLossPrice { get; set; }
    public decimal? TakeProfitPrice { get; set; }
    public decimal? ExpectedMovePercent { get; set; }
    public int? TrendConfidenceScore { get; set; }
    public decimal? ShortMaSlopePercent { get; set; }
    public decimal? TrendStrengthPercent { get; set; }
    public bool? ExecutionSuccess { get; set; }
    public long? LocalOrderId { get; set; }
    public long? ExchangeOrderId { get; set; }
    public string? ExecutionError { get; set; }
}
