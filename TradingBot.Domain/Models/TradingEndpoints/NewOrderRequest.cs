using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Models.TradingEndpoints;

public class NewOrderRequest
{
    public string Symbol { get; set; }           // e.g. "BTCUSDT"
    public OrderSide Side { get; set; }          // BUY / SELL
    public OrderTypes Type { get; set; }          // LIMIT, MARKET, STOP_LOSS, etc.
    public TimeInForce? TimeInForce { get; set; }// Required for LIMIT orders
    public decimal? Quantity { get; set; }
    public decimal? QuoteOrderQty { get; set; }  // For MARKET orders using quote asset
    public decimal? Price { get; set; }
    public string NewClientOrderId { get; set; }
    public long? StrategyId { get; set; }
    public int? StrategyType { get; set; }
    public decimal? StopPrice { get; set; }      // For STOP/TAKE_PROFIT orders
    public long? TrailingDelta { get; set; }
    public decimal? IcebergQty { get; set; }
    public OrderResponseType? NewOrderRespType { get; set; } // ACK / RESULT / FULL
    public SelfTradePreventionMode? SelfTradePreventionMode { get; set; }
    public PegPriceType? PegPriceType { get; set; }
    public int? PegOffsetValue { get; set; }
    public PegOffsetType? PegOffsetType { get; set; }
    public long? RecvWindow { get; set; }
    public long Timestamp { get; set; }
}
