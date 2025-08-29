namespace TradingBot.Domain.Models.MarketData;

public class Trade
{
    public long Id { get; set; }
    public decimal Price { get; set; }
    public decimal Qty { get; set; }
    public decimal QuoteQty { get; set; }
    public long Time { get; set; }
    public bool IsBuyerMaker { get; set; }
    public bool IsBestMatch { get; set; }
}
