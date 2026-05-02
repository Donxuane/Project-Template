using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Models.Trading;

public class TradeExecution
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public long? ExchangeOrderId { get; set; }
    public long? ExchangeTradeId { get; set; }
    public TradingSymbol Symbol { get; set; }
    public OrderSide Side { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal QuoteQuantity { get; set; }
    public decimal Fee { get; set; }
    public string? FeeAsset { get; set; }
    public DateTime? PositionProcessedAt { get; set; }
    public DateTime ExecutedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

