namespace TradingBot.Domain.Models;

public class RecentTrades
{
    public decimal Price { get; set; }
    public decimal Qty { get; set; }
    public string Side { get; set; }
}
