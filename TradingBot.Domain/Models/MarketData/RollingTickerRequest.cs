namespace TradingBot.Domain.Models.MarketData;

public class RollingTickerRequest
{
    public string Symbol { get; set; }
    public List<string> Symbols { get; set; }
    public string WindowSize { get; set; } // 1m, 1h, 1d, etc.
    public string Type { get; set; }       // FULL or MINI
}
