namespace TradingBot.Domain.Models.MarketData;

public class SymbolPriceTickerRequest
{
    public string Symbol { get; set; }
    public List<string> Symbols { get; set; }
}
