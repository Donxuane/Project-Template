namespace TradingBot.Domain.Models.AccountInformation;

public sealed class AccountCommissionRequest
{
    public string Symbol { get; set; } = string.Empty;
    public int RecvWindow { get; set; } = 30000;
    public long Timestamp { get; set; }
}

