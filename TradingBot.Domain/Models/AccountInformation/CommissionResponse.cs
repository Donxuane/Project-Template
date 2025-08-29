namespace TradingBot.Domain.Models.AccountInformation;

public class CommissionResponse
{
    public string Symbol { get; set; } = string.Empty;
    public CommissionBlock StandardCommission { get; set; } = new();
    public CommissionBlock SpecialCommission { get; set; } = new();
    public CommissionBlock TaxCommission { get; set; } = new();
    public DiscountInfo Discount { get; set; } = new();
}

public class CommissionBlock
{
    public string Maker { get; set; } = string.Empty;
    public string Taker { get; set; } = string.Empty;
    public string Buyer { get; set; } = string.Empty;
    public string Seller { get; set; } = string.Empty;
}

public class DiscountInfo
{
    public bool EnabledForAccount { get; set; }
    public bool EnabledForSymbol { get; set; }
    public string DiscountAsset { get; set; } = string.Empty;
    public string Discount { get; set; } = string.Empty;
}
