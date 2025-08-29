namespace TradingBot.Domain.Models.TradingEndpoints;

public class OtoOrderRequest
{
    // Common
    public string Symbol { get; set; } = string.Empty;   // Trading pair, e.g. "BTCUSDT"
    public string Side { get; set; } = string.Empty;     // "BUY" or "SELL"
    public string TimeInForce { get; set; } = "GTC";     // Good-Til-Cancelled, IOC, FOK

    // Working order (must be LIMIT or LIMIT_MAKER)
    public string WorkingType { get; set; } = string.Empty;
    public decimal WorkingQuantity { get; set; }
    public decimal WorkingPrice { get; set; }

    // Pending order (placed only after Working order fills)
    public string PendingType { get; set; } = string.Empty;  // Any type except MARKET w/ quoteOrderQty
    public decimal? PendingQuantity { get; set; }
    public decimal? PendingPrice { get; set; }
    public decimal? PendingStopPrice { get; set; } // used for STOP_LOSS / TAKE_PROFIT
    public decimal? PendingStopLimitPrice { get; set; } // for STOP_LIMIT
}
