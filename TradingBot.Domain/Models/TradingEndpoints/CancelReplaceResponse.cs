namespace TradingBot.Domain.Models.TradingEndpoints;

public class CancelReplaceResponse
{
    public string CancelResult { get; set; }
    public string NewOrderResult { get; set; }
    public OrderResponse CancelResponse { get; set; }
    public OrderResponse NewOrderResponse { get; set; }
}
