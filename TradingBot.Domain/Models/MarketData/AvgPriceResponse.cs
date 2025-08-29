namespace TradingBot.Domain.Models.MarketData;

public class AvgPriceResponse
{
    public int Mins { get; set; }
    public decimal Price { get; set; }
    public long CloseTime { get; set; }
}
