using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Models.TradingEndpoints;

public class OcoOrderRequest
{
    public string Symbol { get; set; }
    public string ListClientOrderId { get; set; }
    public OrderSide Side { get; set; }                // BUY / SELL
    public decimal Quantity { get; set; }

    // Above order
    public OrderTypes AboveType { get; set; }
    public string AboveClientOrderId { get; set; }
    public decimal? AboveIcebergQty { get; set; }
    public decimal? AbovePrice { get; set; }
    public decimal? AboveStopPrice { get; set; }
    public long? AboveTrailingDelta { get; set; }
    public TimeInForce? AboveTimeInForce { get; set; }

    // Below order
    public OrderTypes BelowType { get; set; }
    public string BelowClientOrderId { get; set; }
    public decimal? BelowIcebergQty { get; set; }
    public decimal? BelowPrice { get; set; }
    public decimal? BelowStopPrice { get; set; }
    public long? BelowTrailingDelta { get; set; }
    public TimeInForce? BelowTimeInForce { get; set; }

    public OrderResponseType? NewOrderRespType { get; set; }
    public SelfTradePreventionMode? SelfTradePreventionMode { get; set; }
    public long? RecvWindow { get; set; }
    public long Timestamp { get; set; }
}
