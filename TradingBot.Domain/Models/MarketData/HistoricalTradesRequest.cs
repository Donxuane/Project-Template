namespace TradingBot.Domain.Models.MarketData;

public class HistoricalTradesRequest
{
    public string Symbol { get; set; }
    public int? Limit { get; set; }
    public long? FromId { get; set; }
}
