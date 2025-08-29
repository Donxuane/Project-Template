namespace TradingBot.Domain.Models.MarketData;

public class KlineRequest
{
    public string Symbol { get; set; }
    public string Interval { get; set; } // ENUM: 1m, 5m, 1h, 1d, etc.
    public long? StartTime { get; set; }
    public long? EndTime { get; set; }
    public string TimeZone { get; set; }
    public int? Limit { get; set; }
}
