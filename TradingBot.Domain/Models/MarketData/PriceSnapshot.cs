namespace TradingBot.Domain.Models.MarketData;

public sealed class PriceSnapshot
{
    public decimal Price { get; init; }
    public DateTime AsOfUtc { get; init; }
    public string Source { get; init; } = "Unknown";
}
