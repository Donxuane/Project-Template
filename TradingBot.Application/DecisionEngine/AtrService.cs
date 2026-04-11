using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Interfaces.Services.Decision;

namespace TradingBot.Application.DecisionEngine;

public class AtrService(IConfiguration configuration) : IAtrService
{
    private const string ConfigSection = "DecisionEngine:MarketCondition";
    private readonly int _period = GetInt(configuration, $"{ConfigSection}:AtrPeriod", 14, 2);

    public int RequiredPeriods => _period + 1;

    public decimal Calculate(
        IReadOnlyList<decimal> highs,
        IReadOnlyList<decimal> lows,
        IReadOnlyList<decimal> closes,
        bool normalize,
        decimal currentPrice)
    {
        if (highs.Count == 0 || lows.Count == 0 || closes.Count == 0)
            return 0m;

        var candleCount = Math.Min(highs.Count, Math.Min(lows.Count, closes.Count));
        if (candleCount < 2)
            return 0m;

        var trueRanges = new List<decimal>(candleCount - 1);
        for (var i = 1; i < candleCount; i++)
        {
            var high = highs[i];
            var low = lows[i];
            var prevClose = closes[i - 1];

            var range = high - low;
            var highGap = Math.Abs(high - prevClose);
            var lowGap = Math.Abs(low - prevClose);
            trueRanges.Add(Math.Max(range, Math.Max(highGap, lowGap)));
        }

        if (trueRanges.Count == 0)
            return 0m;

        var atrPeriod = Math.Min(_period, trueRanges.Count);
        decimal atr = trueRanges.Take(atrPeriod).Average();
        for (var i = atrPeriod; i < trueRanges.Count; i++)
        {
            atr = ((atr * (atrPeriod - 1)) + trueRanges[i]) / atrPeriod;
        }

        if (!normalize)
            return atr;

        if (currentPrice <= 0m)
            return 0m;

        return atr / currentPrice;
    }

    private static int GetInt(IConfiguration configuration, string key, int defaultValue, int minValue)
    {
        var raw = configuration[key];
        if (!int.TryParse(raw, out var value))
            return defaultValue;

        return Math.Max(value, minValue);
    }
}
