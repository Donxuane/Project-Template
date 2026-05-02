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
    private readonly decimal _extremeVolatilityMultiplier = GetDecimal(configuration, $"{ConfigSection}:ExtremeVolatilityMultiplier", 2.0m, 1.5m);
    private readonly bool _requireVolumeSpikeForBreakout = GetBool(configuration, $"{ConfigSection}:RequireVolumeSpikeForBreakout", true);
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
                Reason = $"MarketCondition.Unavailable: {volatility.Reason}",
                IsUnsafeVolatility = true,
                MarketConditionScore = 0
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
        var effectiveRollingAverage = rollingAverage > 0m ? rollingAverage : Math.Max(currentVolatility, 0m);

        var symbolSensitivity = GetDecimal(
            configuration,
            $"{ConfigSection}:SymbolSensitivity:{snapshot.Symbol}",
            1.0m,
            0.1m);

        var lowThreshold = effectiveRollingAverage * _lowMultiplier * symbolSensitivity;
        var highThreshold = effectiveRollingAverage * _highMultiplier * symbolSensitivity;
        var extremeThreshold = highThreshold * _extremeVolatilityMultiplier;

        var regime = VolatilityRegime.Normal;
        if (currentVolatility < lowThreshold)
            regime = VolatilityRegime.Low;
        else if (currentVolatility > highThreshold)
            regime = VolatilityRegime.High;

        var isExtremeVolatility = currentVolatility > extremeThreshold;
        var isBreakout = IsPriceBreakout(snapshot);
        var hasVolumeSpike = HasVolumeSpike(snapshot);
        var hasEnoughVolumeData = snapshot.Volumes.Count >= _breakoutLookback + 1;
        var breakoutVolumeCondition = !_requireVolumeSpikeForBreakout || !hasEnoughVolumeData || hasVolumeSpike;
        var isConfirmedBreakout = isBreakout && breakoutVolumeCondition;

        var requiresBreakoutConfirmation = false;
        var requiresReducedPositionSize = false;
        var isUnsafeVolatility = false;
        string? warning = null;
        var allowTrade = false;
        var score = 0;
        string reason;

        if (isExtremeVolatility)
        {
            allowTrade = false;
            isUnsafeVolatility = true;
            score = 10;
            reason = "Extreme-volatility regime - trading blocked for risk protection.";
        }
        else if (regime == VolatilityRegime.Low)
        {
            requiresBreakoutConfirmation = true;
            allowTrade = isConfirmedBreakout;
            score = allowTrade ? 65 : 20;
            reason = allowTrade
                ? "Low-volatility regime with confirmed breakout."
                : "No entry signal - market in consolidation or low-volatility regime without breakout.";
        }
        else if (regime == VolatilityRegime.High)
        {
            allowTrade = true;
            requiresReducedPositionSize = true;
            score = 60;
            warning = "High volatility detected. Reduce position size and monitor risk closely.";
            reason = "High-volatility regime - trading allowed with reduced position size.";
        }
        else
        {
            allowTrade = true;
            score = 85;
            reason = "Normal-volatility regime - trading allowed.";
        }

        if (hasVolumeSpike)
            score += 10;

        score = Math.Clamp(score, 0, 100);

        return new MarketConditionResult
        {
            IsValid = true,
            Availability = MarketConditionAvailability.Available,
            Regime = regime,
            IsBreakout = isBreakout,
            IsConfirmedBreakout = isConfirmedBreakout,
            HasVolumeSpike = hasVolumeSpike,
            AllowTrade = allowTrade,
            CurrentVolatility = currentVolatility,
            RollingAverageVolatility = rollingAverage,
            Atr = atr,
            NormalizedAtr = normalizedAtr,
            SymbolSensitivity = symbolSensitivity,
            MarketConditionScore = score,
            RequiresBreakoutConfirmation = requiresBreakoutConfirmation,
            RequiresReducedPositionSize = requiresReducedPositionSize,
            IsUnsafeVolatility = isUnsafeVolatility,
            Warning = warning,
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
