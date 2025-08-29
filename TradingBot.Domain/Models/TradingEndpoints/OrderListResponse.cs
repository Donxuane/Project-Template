namespace TradingBot.Domain.Models.TradingEndpoints;

public class OrderListResponse
{
    public long OrderListId { get; set; }
    public string ContingencyType { get; set; }        // OCO, OTO, OTOCO
    public string ListStatusType { get; set; }
    public string ListOrderStatus { get; set; }
    public string ListClientOrderId { get; set; }
    public long TransactionTime { get; set; }
    public string Symbol { get; set; }
    public List<OrderSummary> Orders { get; set; }
    public List<OrderReport> OrderReports { get; set; }
}

public class OrderSummary
{
    public string Symbol { get; set; }
    public long OrderId { get; set; }
    public string ClientOrderId { get; set; }
}

public class OrderReport
{
    public string Symbol { get; set; }
    public long OrderId { get; set; }
    public long OrderListId { get; set; }
    public string ClientOrderId { get; set; }
    public long TransactTime { get; set; }
    public string Price { get; set; }
    public string OrigQty { get; set; }
    public string ExecutedQty { get; set; }
    public string OrigQuoteOrderQty { get; set; }
    public string CummulativeQuoteQty { get; set; }
    public string Status { get; set; }
    public string TimeInForce { get; set; }
    public string Type { get; set; }
    public string Side { get; set; }
    public string StopPrice { get; set; }
    public long WorkingTime { get; set; }
    public string IcebergQty { get; set; }
    public string SelfTradePreventionMode { get; set; }
}
