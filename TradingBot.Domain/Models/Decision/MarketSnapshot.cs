using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Models.Decision;

public sealed class MarketSnapshot
{
    public TradingSymbol Symbol { get; init; }
    public decimal CurrentPrice { get; init; }
    public string CurrentPriceSource { get; init; } = "Unknown";
    public DateTime? CurrentPriceAsOfUtc { get; init; }
    public decimal? MarketDataAgeSeconds { get; init; }
    public DateTime? LatestClosedCandleOpenTimeUtc { get; init; }
    public DateTime? LatestClosedCandleCloseTimeUtc { get; init; }
    public decimal? LatestClosedCandleClosePrice { get; init; }
    public IReadOnlyList<decimal> HighPrices { get; init; } = [];
    public IReadOnlyList<decimal> LowPrices { get; init; } = [];
    public IReadOnlyList<decimal> ClosePrices { get; init; } = [];
    public IReadOnlyList<decimal> Volumes { get; init; } = [];
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
