using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class RobustnessWindowResolver
{
    public static IReadOnlyList<RobustnessWindow> Resolve(
        DateTime dataStartUtc,
        DateTime dataEndUtc,
        IReadOnlyList<int> rollingDayWindows,
        DateTime? fixedWindowStartUtc,
        DateTime? fixedWindowEndUtc)
    {
        var windows = new List<RobustnessWindow>();

        foreach (var days in rollingDayWindows.OrderBy(x => x))
        {
            var end = dataEndUtc;
            var start = end.AddDays(-days);
            var skipped = start < dataStartUtc;
            windows.Add(new RobustnessWindow(
                $"{days}d",
                start,
                end,
                skipped,
                skipped ? $"Insufficient data: need {days}d but earliest candle is {dataStartUtc:O}." : null));
        }

        if (fixedWindowStartUtc.HasValue && fixedWindowEndUtc.HasValue)
        {
            var start = fixedWindowStartUtc.Value;
            var end = fixedWindowEndUtc.Value;
            var skipped = start < dataStartUtc || end > dataEndUtc.AddMinutes(1);
            windows.Add(new RobustnessWindow(
                $"fixed:{start:yyyyMMdd}-{end:yyyyMMdd}",
                start,
                end,
                skipped,
                skipped ? $"Fixed window {start:O}..{end:O} exceeds loaded data {dataStartUtc:O}..{dataEndUtc:O}." : null));
        }

        return windows;
    }

    public static (DateTime DataStartUtc, DateTime DataEndUtc) ResolveDataBounds(
        IReadOnlyDictionary<TradingSymbol, SymbolValidationResult> validatedDataBySymbol)
    {
        var starts = validatedDataBySymbol.Values
            .Where(x => x.Candles.Count > 0)
            .Select(x => x.Candles[0].OpenTimeUtc)
            .ToArray();
        var ends = validatedDataBySymbol.Values
            .Where(x => x.Candles.Count > 0)
            .Select(x => x.Candles[^1].OpenTimeUtc)
            .ToArray();

        if (starts.Length == 0 || ends.Length == 0)
            return (DateTime.UtcNow, DateTime.UtcNow);

        return (starts.Min(), ends.Max());
    }
}
