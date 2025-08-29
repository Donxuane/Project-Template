namespace TradingBot.Domain.Models.TradingEndpoints;

public class OrderResponse
{
    public string Symbol { get; set; }
    public long OrderId { get; set; }
    public long OrderListId { get; set; }
    public string ClientOrderId { get; set; }
    public long TransactTime { get; set; }
    public string Price { get; set; }
    public string OrigQty { get; set; }
    public string ExecutedQty { get; set; }
    public string CummulativeQuoteQty { get; set; }
    public string OrigQuoteOrderQty { get; set; }
    public string Status { get; set; }
    public string TimeInForce { get; set; }
    public string Type { get; set; }
    public string Side { get; set; }
    public string StopPrice { get; set; }
    public string IcebergQty { get; set; }
    public bool IsWorking { get; set; }
    public long WorkingTime { get; set; }
    public string SelfTradePreventionMode { get; set; }
    public List<Fill> Fills { get; set; }
}

public class Fill
{
    public string Price { get; set; }
    public string Qty { get; set; }
    public string Commission { get; set; }
    public string CommissionAsset { get; set; }
    public long TradeId { get; set; }
}
