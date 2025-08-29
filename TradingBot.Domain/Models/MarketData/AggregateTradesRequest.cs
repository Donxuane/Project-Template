

namespace TradingBot.Domain.Models.MarketData;

public class AggregateTradesRequest
{
    public string Symbol { get; set; }
    public long? FromId { get; set; }
    public long? StartTime { get; set; }
    public long? EndTime { get; set; }
    public int? Limit { get; set; }
}
