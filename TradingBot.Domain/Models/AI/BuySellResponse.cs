namespace TradingBot.Domain.Models.AI;

public class BuySellResponse
{
    public string Action { get; set; }
    public string Symbol { get; set; }
    public string Type { get; set; }
    public decimal Price { get; set; }
    public decimal Qty { get; set; }
}
