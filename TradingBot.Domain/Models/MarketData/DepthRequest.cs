namespace TradingBot.Domain.Models.MarketData;

public class DepthRequest
{
    public string Symbol { get; set; }  // e.g. BTCUSDT
    public int? Limit { get; set; }     // optional, default 100, max 5000
}
