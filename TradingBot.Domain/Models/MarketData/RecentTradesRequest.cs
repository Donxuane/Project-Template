namespace TradingBot.Domain.Models.MarketData;

public class RecentTradesRequest
{
    public string Symbol { get; set; }
    public int? Limit { get; set; }
}

