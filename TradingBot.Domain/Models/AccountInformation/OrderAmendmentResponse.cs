namespace TradingBot.Domain.Models.AccountInformation;

public class OrderAmendmentResponse
{
    public string Symbol { get; set; } = string.Empty;
    public long OrderId { get; set; }
    public long ExecutionId { get; set; }
    public string OrigClientOrderId { get; set; } = string.Empty;
    public string NewClientOrderId { get; set; } = string.Empty;
    public string OrigQty { get; set; } = string.Empty;
    public string NewQty { get; set; } = string.Empty;
    public long Time { get; set; }
}
