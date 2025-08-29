namespace TradingBot.Domain.Models.TradingEndpoints;

public class AmendOrderResponse
{
    public long TransactTime { get; set; }
    public long ExecutionId { get; set; }
    public AmendedOrder AmendedOrder { get; set; }
    public ListStatus ListStatus { get; set; }          // only present for order lists
}

public class AmendedOrder
{
    public string Symbol { get; set; }
    public long OrderId { get; set; }
    public long OrderListId { get; set; }
    public string OrigClientOrderId { get; set; }
    public string ClientOrderId { get; set; }
    public string Price { get; set; }
    public string Qty { get; set; }
    public string ExecutedQty { get; set; }
    public string PreventedQty { get; set; }
    public string QuoteOrderQty { get; set; }
    public string CumulativeQuoteQty { get; set; }
    public string Status { get; set; }
    public string TimeInForce { get; set; }
    public string Type { get; set; }
    public string Side { get; set; }
    public long WorkingTime { get; set; }
    public string SelfTradePreventionMode { get; set; }
}

public class ListStatus
{
    public long OrderListId { get; set; }
    public string ContingencyType { get; set; }
    public string ListOrderStatus { get; set; }
    public string ListClientOrderId { get; set; }
    public string Symbol { get; set; }
    public List<ListOrder> Orders { get; set; }
}

public class ListOrder
{
    public string Symbol { get; set; }
    public long OrderId { get; set; }
    public string ClientOrderId { get; set; }
}
