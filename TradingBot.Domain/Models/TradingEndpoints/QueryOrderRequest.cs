namespace TradingBot.Domain.Models.TradingEndpoints;

public class QueryOrderRequest
{
    public string Symbol { get; set; }
    public long? OrderId { get; set; }
    public string OrigClientOrderId { get; set; }
    public long? RecvWindow { get; set; }
    public long Timestamp { get; set; }
}
