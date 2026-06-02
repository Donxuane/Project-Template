using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public static class MarketSnapshotFactory
{
    public static MarketSnapshot Build(IReadOnlyList<KlineCandle> candles, int lastIndexInclusive)
    {
        if (candles.Count == 0 || lastIndexInclusive < 0 || lastIndexInclusive >= candles.Count)
            throw new ArgumentOutOfRangeException(nameof(lastIndexInclusive));

        const int maxWindow = 600;
        var start = Math.Max(0, lastIndexInclusive - maxWindow + 1);
        var window = candles.Skip(start).Take(lastIndexInclusive - start + 1).ToArray();
        var latest = window[^1];

        return new MarketSnapshot
        {
            Symbol = latest.Symbol,
            CurrentPrice = latest.Close,
            CurrentPriceSource = "ReplayClosePrice",
            CurrentPriceAsOfUtc = latest.CloseTimeUtc,
            MarketDataAgeSeconds = 0m,
            LatestClosedCandleOpenTimeUtc = latest.OpenTimeUtc,
            LatestClosedCandleCloseTimeUtc = latest.CloseTimeUtc,
            LatestClosedCandleAgeSeconds = 0m,
            LatestClosedCandleClosePrice = latest.Close,
            HighPrices = window.Select(x => x.High).ToArray(),
            LowPrices = window.Select(x => x.Low).ToArray(),
            ClosePrices = window.Select(x => x.Close).ToArray(),
            Volumes = window.Select(x => x.Volume).ToArray(),
            TimestampUtc = latest.CloseTimeUtc
        };
    }
}
