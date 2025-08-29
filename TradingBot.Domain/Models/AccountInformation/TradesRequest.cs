namespace TradingBot.Domain.Models.AccountInformation;

public class TradesRequest
{
    public string Symbol { get; set; } = string.Empty;
    public long? OrderId { get; set; }
    public long? StartTime { get; set; }
    public long? EndTime { get; set; }
    public long? FromId { get; set; }
    public int? Limit { get; set; }
    public long? RecvWindow { get; set; }
    public long Timestamp { get; set; }
}
