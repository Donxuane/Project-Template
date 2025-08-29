namespace TradingBot.Domain.Models.AccountInformation;

public class TradeResponse
{
    public string Symbol { get; set; } = string.Empty;
    public long Id { get; set; }
    public long OrderId { get; set; }
    public long OrderListId { get; set; }
    public string Price { get; set; } = string.Empty;
    public string Qty { get; set; } = string.Empty;
    public string QuoteQty { get; set; } = string.Empty;
    public string Commission { get; set; } = string.Empty;
    public string CommissionAsset { get; set; } = string.Empty;
    public long Time { get; set; }
    public bool IsBuyer { get; set; }
    public bool IsMaker { get; set; }
    public bool IsBestMatch { get; set; }
}
