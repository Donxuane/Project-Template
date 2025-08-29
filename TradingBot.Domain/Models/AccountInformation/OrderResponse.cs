namespace TradingBot.Domain.Models.AccountInformation;

public class OrderResponse
{
    public string Symbol { get; set; } = string.Empty;
    public long OrderId { get; set; }
    public long OrderListId { get; set; }
    public string ClientOrderId { get; set; } = string.Empty;
    public string Price { get; set; } = string.Empty;
    public string OrigQty { get; set; } = string.Empty;
    public string ExecutedQty { get; set; } = string.Empty;
    public string CummulativeQuoteQty { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;   // enum
    public string TimeInForce { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;     // enum
    public string Side { get; set; } = string.Empty;     // BUY/SELL
    public string StopPrice { get; set; } = string.Empty;
    public string IcebergQty { get; set; } = string.Empty;
    public long Time { get; set; }
    public long UpdateTime { get; set; }
    public bool IsWorking { get; set; }
    public string OrigQuoteOrderQty { get; set; } = string.Empty;
    public long WorkingTime { get; set; }
    public string SelfTradePreventionMode { get; set; } = string.Empty;
}
