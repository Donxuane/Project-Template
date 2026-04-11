using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.DecisionEngine;

public class VolatilityService(IConfiguration configuration) : IVolatilityService
{
    private static readonly ConcurrentDictionary<TradingSymbol, VolatilityCacheEntry> Cache = new();
    private const string ConfigSection = "DecisionEngine:MarketCondition";

    private readonly int _currentWindow = GetInt(configuration, $"{ConfigSection}:VolatilityWindow", 14, 2);
    private readonly int _rollingWindow = GetInt(configuration, $"{ConfigSection}:RollingVolatilityWindow", 50, 10);
    public int RequiredPeriods => Math.Max(_rollingWindow, _currentWindow + 1) + 1;

    public VolatilityAssessment Assess(
        TradingSymbol symbol,
        IReadOnlyList<decimal> closePrices)
    {
        if (closePrices.Count < 3)
        {
            return new VolatilityAssessment
            {
                IsValid = false,
                Reason = "Insufficient close data for volatility."
            };
        }

        var effectiveCurrentWindow = Math.Max(_currentWindow, 2);
        var effectiveRollingWindow = Math.Max(_rollingWindow, effectiveCurrentWindow + 1);
        var requiredPrices = effectiveRollingWindow + 1;

        if (closePrices.Count < requiredPrices)
        {
            return new VolatilityAssessment
            {
                IsValid = false,
                Reason = "Rolling volatility window is not ready yet."
            };
        }

        var prices = closePrices.TakeLast(requiredPrices).ToArray();
        var signature = BuildSignature(prices);
        if (Cache.TryGetValue(symbol, out var cached) && cached.Signature == signature)
            return cached.Assessment;

        var returns = BuildReturns(prices);
        if (returns.Length < effectiveCurrentWindow)
        {
            return new VolatilityAssessment
            {
                IsValid = false,
                Reason = "Return series is not ready yet."
            };
        }

        var currentSlice = returns.TakeLast(effectiveCurrentWindow).ToArray();
        var currentVolatility = CalculateStandardDeviation(currentSlice);

        var rollingAverageVolatility = CalculateRollingAverageStd(returns, effectiveCurrentWindow);
        if (rollingAverageVolatility <= 0m)
            rollingAverageVolatility = currentVolatility;

        var assessment = new VolatilityAssessment
        {
            IsValid = true,
            CurrentVolatility = currentVolatility,
            RollingAverageVolatility = rollingAverageVolatility,
            ReturnsUsed = returns.Length
        };

        Cache.AddOrUpdate(symbol, _ => new VolatilityCacheEntry(signature, assessment), (_, _) => new VolatilityCacheEntry(signature, assessment));
        return assessment;
    }

    private static decimal[] BuildReturns(IReadOnlyList<decimal> prices)
    {
        var result = new decimal[Math.Max(prices.Count - 1, 0)];
        for (var i = 1; i < prices.Count; i++)
        {
            var prev = prices[i - 1];
            result[i - 1] = prev <= 0m ? 0m : (prices[i] - prev) / prev;
        }

        return result;
    }

    private static decimal CalculateRollingAverageStd(IReadOnlyList<decimal> returns, int window)
    {
        if (window <= 1 || returns.Count < window)
            return 0m;

        var sums = new decimal[returns.Count + 1];
        var sumsSquared = new decimal[returns.Count + 1];
        for (var i = 0; i < returns.Count; i++)
        {
            sums[i + 1] = sums[i] + returns[i];
            sumsSquared[i + 1] = sumsSquared[i] + (returns[i] * returns[i]);
        }

        decimal totalStd = 0m;
        var windows = 0;
        for (var end = window; end <= returns.Count; end++)
        {
            var start = end - window;
            var sum = sums[end] - sums[start];
            var sqSum = sumsSquared[end] - sumsSquared[start];
            var mean = sum / window;
            var variance = (sqSum / window) - (mean * mean);
            if (variance < 0m)
                variance = 0m;

            totalStd += (decimal)Math.Sqrt((double)variance);
            windows++;
        }

        return windows == 0 ? 0m : totalStd / windows;
    }

    private static decimal CalculateStandardDeviation(IReadOnlyList<decimal> values)
    {
        if (values.Count <= 1)
            return 0m;

        var average = values.Average();
        var variance = values.Sum(v => (v - average) * (v - average)) / values.Count;
        if (variance < 0m)
            variance = 0m;

        return (decimal)Math.Sqrt((double)variance);
    }

    private static string BuildSignature(IReadOnlyList<decimal> prices)
    {
        if (prices.Count == 0)
            return "0";

        return $"{prices.Count}:{prices[0]:F10}:{prices[^1]:F10}:{prices[^2]:F10}";
    }

    private sealed record VolatilityCacheEntry(string Signature, VolatilityAssessment Assessment);

    private static int GetInt(IConfiguration configuration, string key, int defaultValue, int minValue)
    {
        var raw = configuration[key];
        if (!int.TryParse(raw, out var value))
            return defaultValue;

        return Math.Max(value, minValue);
    }
}
