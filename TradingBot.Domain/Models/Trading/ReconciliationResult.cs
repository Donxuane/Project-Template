using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Models.Trading;

public sealed class ReconciliationResult
{
    public TradingSymbol? Symbol { get; init; }
    public string Asset { get; init; } = string.Empty;
    public decimal LocalOpenQuantity { get; init; }
    public decimal ExchangeFree { get; init; }
    public decimal ExchangeLocked { get; init; }
    public decimal ExchangeTotal { get; init; }
    public decimal Difference { get; init; }
    public bool IsMatched { get; init; }
    public string Severity { get; init; } = "Info";
    public string Reason { get; init; } = string.Empty;
}
