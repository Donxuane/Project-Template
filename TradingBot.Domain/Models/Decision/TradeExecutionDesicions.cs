using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Models.Decision;

public class TradeExecutionDecisions
{
    public long? Id { get; set; }
    public string CorrelationId { get; set; } = null!;
    public string? DecisionId { get; set; }
    public TradingSymbol? Symbol { get; set; }
    public TradeSignal? Action { get; set; }
    public OrderSide? Side { get; set; }
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
    public bool? ExecutionSuccess { get; set; } = false;
    public long? LocalOrderId { get; set; }
    public long? ExchangeOrderId { get; set; }
    public string? ExecutionError { get; set; }
    public DateTimeOffset? Created_At { get; set; }
    public DateTimeOffset? Updated_At { get; set; }
}
