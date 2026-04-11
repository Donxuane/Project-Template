using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.DecisionEngine;

public class MarketConditionService(
    IConfiguration configuration,
    IVolatilityService volatilityService,
    IAtrService atrService) : IMarketConditionService
{
    private const string ConfigSection = "DecisionEngine:MarketCondition";

    private readonly decimal _lowMultiplier = GetDecimal(configuration, $"{ConfigSection}:LowVolatilityMultiplier", 0.8m, 0.1m);
    private readonly decimal _highMultiplier = GetDecimal(configuration, $"{ConfigSection}:HighVolatilityMultiplier", 1.2m, 1.0m);
    private readonly int _breakoutLookback = GetInt(configuration, $"{ConfigSection}:BreakoutLookback", 20, 2);
    private readonly decimal _volumeSpikeMultiplier = GetDecimal(configuration, $"{ConfigSection}:VolumeSpikeMultiplier", 1.5m, 1.0m);
    private readonly bool _normalizeAtr = GetBool(configuration, $"{ConfigSection}:NormalizeAtr", true);
    public int RequiredPeriods => new[] { _breakoutLookback + 1, volatilityService.RequiredPeriods, atrService.RequiredPeriods }.Max();

    public MarketConditionResult Evaluate(MarketSnapshot snapshot)
    {
        var volatility = volatilityService.Assess(
            snapshot.Symbol,
            snapshot.ClosePrices);

        if (!volatility.IsValid)
        {
            return new MarketConditionResult
            {
                IsValid = false,
                Availability = MarketConditionAvailability.Unavailable,
                AllowTrade = false,
                Reason = $"MarketCondition.Unavailable: {volatility.Reason}"
            };
        }

        var atr = atrService.Calculate(
            snapshot.HighPrices,
            snapshot.LowPrices,
            snapshot.ClosePrices,
            normalize: false,
            currentPrice: snapshot.CurrentPrice);

        var normalizedAtr = _normalizeAtr
            ? atrService.Calculate(
                snapshot.HighPrices,
                snapshot.LowPrices,
                snapshot.ClosePrices,
                normalize: true,
                currentPrice: snapshot.CurrentPrice)
            : (snapshot.CurrentPrice > 0m ? atr / snapshot.CurrentPrice : 0m);

        var currentVolatility = Math.Max(volatility.CurrentVolatility, normalizedAtr);
        var rollingAverage = volatility.RollingAverageVolatility;

        var symbolSensitivity = GetDecimal(
            configuration,
            $"{ConfigSection}:SymbolSensitivity:{snapshot.Symbol}",
            1.0m,
            0.1m);

        var lowThreshold = rollingAverage * _lowMultiplier * symbolSensitivity;
        var highThreshold = rollingAverage * _highMultiplier * symbolSensitivity;

        var regime = VolatilityRegime.Normal;
        if (currentVolatility < lowThreshold)
            regime = VolatilityRegime.Low;
        else if (currentVolatility > highThreshold)
            regime = VolatilityRegime.High;

        var isBreakout = IsPriceBreakout(snapshot);
        var hasVolumeSpike = HasVolumeSpike(snapshot);
        var hasEnoughVolumeData = snapshot.Volumes.Count >= _breakoutLookback + 1;
        var allowTrade = regime != VolatilityRegime.Low || (isBreakout && (!hasEnoughVolumeData || hasVolumeSpike));

        var reason = regime switch
        {
            VolatilityRegime.Low when !allowTrade => "Low-volatility regime without confirmed breakout.",
            VolatilityRegime.Low when allowTrade => "Low-volatility regime with breakout confirmation.",
            VolatilityRegime.High => "High-volatility regime.",
            _ => "Normal-volatility regime."
        };

        return new MarketConditionResult
        {
            IsValid = true,
            Availability = MarketConditionAvailability.Available,
            Regime = regime,
            IsBreakout = isBreakout,
            HasVolumeSpike = hasVolumeSpike,
            AllowTrade = allowTrade,
            CurrentVolatility = currentVolatility,
            RollingAverageVolatility = rollingAverage,
            Atr = atr,
            NormalizedAtr = normalizedAtr,
            SymbolSensitivity = symbolSensitivity,
            Reason = reason
        };
    }

    private bool IsPriceBreakout(MarketSnapshot snapshot)
    {
        if (snapshot.HighPrices.Count < _breakoutLookback + 1 || snapshot.LowPrices.Count < _breakoutLookback + 1)
            return false;

        var highWindow = snapshot.HighPrices.TakeLast(_breakoutLookback + 1).ToArray();
        var lowWindow = snapshot.LowPrices.TakeLast(_breakoutLookback + 1).ToArray();
        var recentHigh = highWindow.Take(_breakoutLookback).Max();
        var recentLow = lowWindow.Take(_breakoutLookback).Min();
        var currentPrice = snapshot.CurrentPrice > 0m ? snapshot.CurrentPrice : snapshot.ClosePrices.LastOrDefault();

        return currentPrice > recentHigh || currentPrice < recentLow;
    }

    private bool HasVolumeSpike(MarketSnapshot snapshot)
    {
        if (snapshot.Volumes.Count < _breakoutLookback + 1)
            return false;

        var volumeWindow = snapshot.Volumes.TakeLast(_breakoutLookback + 1).ToArray();
        var currentVolume = volumeWindow[^1];
        var averageVolume = volumeWindow.Take(_breakoutLookback).Average();
        if (averageVolume <= 0m)
            return false;

        return currentVolume >= averageVolume * _volumeSpikeMultiplier;
    }

    private static int GetInt(IConfiguration configuration, string key, int defaultValue, int minValue)
    {
        var raw = configuration[key];
        if (!int.TryParse(raw, out var value))
            return defaultValue;

        return Math.Max(value, minValue);
    }

    private static decimal GetDecimal(IConfiguration configuration, string key, decimal defaultValue, decimal minValue)
    {
        var raw = configuration[key];
        if (!decimal.TryParse(raw, out var value))
            return defaultValue;

        return Math.Max(value, minValue);
    }

    private static bool GetBool(IConfiguration configuration, string key, bool defaultValue)
    {
        var raw = configuration[key];
        return bool.TryParse(raw, out var value) ? value : defaultValue;
    }
}
