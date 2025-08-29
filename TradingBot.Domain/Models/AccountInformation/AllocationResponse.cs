namespace TradingBot.Domain.Models.AccountInformation;

public class AllocationResponse
{
    public string Symbol { get; set; } = string.Empty;
    public long AllocationId { get; set; }
    public string AllocationType { get; set; } = string.Empty;
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
    public bool IsAllocator { get; set; }
}
