namespace TradingBot.Domain.Models.TradingEndpoints;

public class CancelAllOrdersRequest
{
    public string Symbol { get; set; }
    public long? RecvWindow { get; set; }
    public long Timestamp { get; set; }
}
