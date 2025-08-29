namespace TradingBot.Domain.Models.MarketData;

public class BookTickerRequest
{
    public string Symbol { get; set; }
    public List<string> Symbols { get; set; }
}
