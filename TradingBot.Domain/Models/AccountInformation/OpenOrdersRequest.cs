namespace TradingBot.Domain.Models.AccountInformation;

public class OpenOrdersRequest
{
    public string? Symbol { get; set; }
    public long? RecvWindow { get; set; }
    public long Timestamp { get; set; }
}
