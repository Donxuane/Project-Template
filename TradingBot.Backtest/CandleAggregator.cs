using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class CandleAggregator
{
    public static AggregationResult Aggregate(
        TradingSymbol symbol,
        IReadOnlyList<KlineCandle> sourceCandles,
        string sourceInterval,
        string targetInterval)
    {
        if (string.Equals(sourceInterval, targetInterval, StringComparison.OrdinalIgnoreCase))
        {
            return new AggregationResult(
                Symbol: symbol,
                SourceInterval: sourceInterval,
                TargetInterval: targetInterval,
                InputCandleCount: sourceCandles.Count,
                OutputCandleCount: sourceCandles.Count,
                DroppedIncompleteFinalBucketCount: 0,
                Candles: sourceCandles.ToArray());
        }

        var sourceMinutes = ParseIntervalMinutes(sourceInterval);
        var targetMinutes = ParseIntervalMinutes(targetInterval);
        if (targetMinutes <= sourceMinutes)
            throw new ArgumentException($"Target interval '{targetInterval}' must be larger than source interval '{sourceInterval}'.");
        if (targetMinutes % sourceMinutes != 0)
            throw new ArgumentException($"Target interval '{targetInterval}' must be a multiple of source interval '{sourceInterval}'.");

        var expectedPerBucket = targetMinutes / sourceMinutes;
        var buckets = sourceCandles
            .OrderBy(x => x.OpenTimeUtc)
            .GroupBy(c => AlignOpenTime(c.OpenTimeUtc, targetMinutes))
            .OrderBy(g => g.Key);

        var output = new List<KlineCandle>();
        var dropped = 0;
        foreach (var bucket in buckets)
        {
            var bucketCandles = bucket.OrderBy(x => x.OpenTimeUtc).ToArray();
            if (bucketCandles.Length < expectedPerBucket)
            {
                dropped++;
                continue;
            }

            var first = bucketCandles[0];
            var last = bucketCandles[^1];
            output.Add(new KlineCandle(
                symbol,
                bucket.Key,
                Open: first.Open,
                High: bucketCandles.Max(x => x.High),
                Low: bucketCandles.Min(x => x.Low),
                Close: last.Close,
                Volume: bucketCandles.Sum(x => x.Volume)));
        }

        return new AggregationResult(
            Symbol: symbol,
            SourceInterval: sourceInterval,
            TargetInterval: targetInterval,
            InputCandleCount: sourceCandles.Count,
            OutputCandleCount: output.Count,
            DroppedIncompleteFinalBucketCount: dropped,
            Candles: output);
    }

    public static int ParseIntervalMinutes(string interval)
    {
        return interval.Trim().ToLowerInvariant() switch
        {
            "1m" => 1,
            "3m" => 3,
            "5m" => 5,
            _ => throw new ArgumentException($"Unsupported interval '{interval}'.")
        };
    }

    public static DateTime AlignOpenTime(DateTime utcOpenTime, int intervalMinutes)
    {
        var normalized = utcOpenTime.Kind == DateTimeKind.Utc
            ? utcOpenTime
            : DateTime.SpecifyKind(utcOpenTime, DateTimeKind.Utc);
        var minute = normalized.Minute - (normalized.Minute % intervalMinutes);
        return new DateTime(
            normalized.Year,
            normalized.Month,
            normalized.Day,
            normalized.Hour,
            minute,
            0,
            DateTimeKind.Utc);
    }
}

public sealed record AggregationResult(
    TradingSymbol Symbol,
    string SourceInterval,
    string TargetInterval,
    int InputCandleCount,
    int OutputCandleCount,
    int DroppedIncompleteFinalBucketCount,
    IReadOnlyList<KlineCandle> Candles);
