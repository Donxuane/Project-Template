namespace TradingBot.Domain.Models.TradingEndpoints;

public class AmendOrderRequest
{
    public string Symbol { get; set; }                  // required
    public long? OrderId { get; set; }                  // one of OrderId or OrigClientOrderId required
    public string OrigClientOrderId { get; set; }
    public string NewClientOrderId { get; set; }        // optional
    public decimal NewQty { get; set; }                 // must be > 0 and < original order qty
    public long? RecvWindow { get; set; }
    public long Timestamp { get; set; }
}
