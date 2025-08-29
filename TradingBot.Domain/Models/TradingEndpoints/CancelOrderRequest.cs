using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Models.TradingEndpoints;

public class CancelOrderRequest
{
    public string Symbol { get; set; }
    public long? OrderId { get; set; }
    public string OrigClientOrderId { get; set; }
    public string NewClientOrderId { get; set; }
    public CancelRestriction? CancelRestrictions { get; set; }
    public long? RecvWindow { get; set; }
    public long Timestamp { get; set; }
}
