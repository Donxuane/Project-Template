using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class FuturesMarketDataQualityAnalyzer
{
    public static decimal CadenceMinutes(string sourceKey)
        => sourceKey switch
        {
            "funding" => 480m,
            _ => 30m
        };

    public static FuturesDataQualityRow Analyze(
        TradingSymbol symbol,
        string sourceKey,
        long[] timestamps,
        string fieldAvailability,
        DateTime? candleStartUtc,
        DateTime? candleEndUtc,
        IReadOnlyList<long> sampleEntryMs)
    {
        if (timestamps.Length == 0)
        {
            return new FuturesDataQualityRow
            {
                Symbol = symbol.ToString(),
                SourceKey = sourceKey,
                RecordCount = 0,
                FieldAvailability = fieldAvailability,
                Verdict = "NoData"
            };
        }

        var cadence = CadenceMinutes(sourceKey);
        var cadenceMs = (long)(cadence * 60_000m);

        var sorted = true;
        var duplicates = 0;
        var gapCount = 0;
        for (var i = 1; i < timestamps.Length; i++)
        {
            var diff = timestamps[i] - timestamps[i - 1];
            if (diff < 0)
                sorted = false;
            if (diff == 0)
                duplicates++;
            else if (diff > cadenceMs * 3 / 2)
                gapCount += (int)(diff / cadenceMs) - 1;
        }

        var startUtc = DateTimeOffset.FromUnixTimeMilliseconds(timestamps[0]).UtcDateTime;
        var endUtc = DateTimeOffset.FromUnixTimeMilliseconds(timestamps[^1]).UtcDateTime;
        var spanDays = Math.Round((decimal)(endUtc - startUtc).TotalDays, 2);
        var expected = cadenceMs > 0 ? (decimal)(timestamps[^1] - timestamps[0]) / cadenceMs + 1m : timestamps.Length;
        var coverage = expected > 0m ? Math.Round(Math.Min(100m, timestamps.Length / expected * 100m), 2) : 0m;

        var matched = 0;
        var samples = 0;
        foreach (var entry in sampleEntryMs)
        {
            samples++;
            var idx = AsOfIndex(timestamps, entry);
            if (idx >= 0 && entry - timestamps[idx] <= cadenceMs * 2)
                matched++;
        }

        var aligned = samples > 0 && matched >= samples * 0.8;
        var verdict = coverage >= 95m && gapCount == 0 && duplicates == 0
            ? "Clean"
            : coverage >= 80m ? "MinorIssues" : "Sparse";

        return new FuturesDataQualityRow
        {
            Symbol = symbol.ToString(),
            SourceKey = sourceKey,
            RecordCount = timestamps.Length,
            DuplicateTimestampCount = duplicates,
            GapCount = gapCount,
            ExpectedCadenceMinutes = cadence,
            StartUtc = startUtc,
            EndUtc = endUtc,
            SpanDays = spanDays,
            CoveragePercent = coverage,
            TimestampsSorted = sorted,
            AlignedWithCandles = aligned,
            AlignmentSampleCount = samples,
            AlignmentMatchedCount = matched,
            FieldAvailability = fieldAvailability,
            Verdict = verdict
        };
    }

    private static int AsOfIndex(long[] timestamps, long target)
    {
        if (timestamps.Length == 0 || timestamps[0] > target)
            return -1;
        var lo = 0;
        var hi = timestamps.Length - 1;
        var result = -1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (timestamps[mid] <= target)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return result;
    }
}
