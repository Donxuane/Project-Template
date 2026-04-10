using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Models.Decision;

public sealed class MarketSnapshot
{
    public TradingSymbol Symbol { get; init; }
    public decimal CurrentPrice { get; init; }
    public IReadOnlyList<decimal> ClosePrices { get; init; } = [];
    public IReadOnlyList<decimal> Volumes { get; init; } = [];
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
