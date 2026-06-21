namespace TradingBot.Backtest;

public static class CandleWindowSlicer
{
    public static IReadOnlyList<KlineCandle> Slice(
        IReadOnlyList<KlineCandle> candles,
        DateTime windowStartUtc,
        DateTime windowEndUtc)
    {
        if (candles.Count == 0)
            return [];

        return candles
            .Where(c => c.OpenTimeUtc >= windowStartUtc && c.OpenTimeUtc < windowEndUtc)
            .ToArray();
    }
}
