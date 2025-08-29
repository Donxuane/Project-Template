namespace TradingBot.Domain.Models.TradingEndpoints;

public class OtocoOrderRequest
{
    // Common
    public string Symbol { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public string TimeInForce { get; set; } = "GTC";

    // Working order
    public string WorkingType { get; set; } = string.Empty;
    public decimal WorkingQuantity { get; set; }
    public decimal WorkingPrice { get; set; }

    // Pending Above (like take profit)
    public string PendingAboveType { get; set; } = string.Empty;
    public decimal? PendingAboveQuantity { get; set; }
    public decimal? PendingAbovePrice { get; set; }
    public decimal? PendingAboveStopPrice { get; set; }
    public decimal? PendingAboveStopLimitPrice { get; set; }

    // Pending Below (like stop loss)
    public string PendingBelowType { get; set; } = string.Empty;
    public decimal? PendingBelowQuantity { get; set; }
    public decimal? PendingBelowPrice { get; set; }
    public decimal? PendingBelowStopPrice { get; set; }
    public decimal? PendingBelowStopLimitPrice { get; set; }
}
