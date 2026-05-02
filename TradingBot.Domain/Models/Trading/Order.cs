using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Models.Trading;

public class Order
{
    public long Id { get; set; }
    public long? ExchangeOrderId { get; set; }
    public string? CorrelationId { get; set; }
    public long? ParentPositionId { get; set; }
    public OrderSource OrderSource { get; set; } = OrderSource.Unknown;
    public CloseReason CloseReason { get; set; } = CloseReason.None;
    public TradingSymbol Symbol { get; set; }
    public OrderSide Side { get; set; }
    public OrderStatuses Status { get; set; }
    public ProcessingStatus ProcessingStatus { get; set; }
    /// <summary>Incremented when transitioned to TradesSyncFailed; reset when TradesSynced.</summary>
    public int SyncRetryCount { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

