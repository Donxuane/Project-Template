namespace TradingBot.Domain.Models.MarketData;

public class Ticker24hrRequest
{
    public string Symbol { get; set; }
    public List<string> Symbols { get; set; }
    public string Type { get; set; } // FULL or MINI
}
