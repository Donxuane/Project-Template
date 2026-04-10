using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.DecisionEngine;

public class MovingAverageCrossoverStrategy : IStrategy
{
    private const string ConfigSection = "DecisionEngine:MovingAverageCrossoverStrategy";
    private static readonly ConcurrentDictionary<TradingSymbol, DateTime> LastSignalTimesUtc = new();

    private readonly ILogger<MovingAverageCrossoverStrategy> _logger;
    private readonly int _shortPeriod;
    private readonly int _longPeriod;
    private readonly decimal _trendStrengthThreshold;
    private readonly decimal _volatilityThreshold;
    private readonly int _cooldownSeconds;

    public MovingAverageCrossoverStrategy(
        ILogger<MovingAverageCrossoverStrategy> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _shortPeriod = GetInt(configuration, $"{ConfigSection}:ShortPeriod", 10, 2);
        _longPeriod = GetInt(configuration, $"{ConfigSection}:LongPeriod", 30, _shortPeriod + 1);
        _trendStrengthThreshold = GetDecimal(configuration, $"{ConfigSection}:TrendStrengthThreshold", 0.001m, 0.000001m);
        _volatilityThreshold = GetDecimal(configuration, $"{ConfigSection}:VolatilityThreshold", 0.002m, 0m);
        _cooldownSeconds = GetInt(configuration, $"{ConfigSection}:CooldownSeconds", 60, 0);

        if (_longPeriod <= _shortPeriod)
        {
            _longPeriod = _shortPeriod + 1;
            _logger.LogWarning(
                "MovingAverageCrossoverStrategy configuration adjusted: LongPeriod must be greater than ShortPeriod. Using ShortPeriod={ShortPeriod}, LongPeriod={LongPeriod}.",
                _shortPeriod, _longPeriod);
        }

        _logger.LogInformation(
            "MovingAverageCrossoverStrategy configured: ShortPeriod={ShortPeriod}, LongPeriod={LongPeriod}, TrendStrengthThreshold={TrendStrengthThreshold}, VolatilityThreshold={VolatilityThreshold}, CooldownSeconds={CooldownSeconds}",
            _shortPeriod, _longPeriod, _trendStrengthThreshold, _volatilityThreshold, _cooldownSeconds);
    }

    public Task<StrategySignalResult> GenerateSignalAsync(MarketSnapshot marketData, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var closes = marketData.ClosePrices;
        if (closes.Count < _longPeriod + 1)
        {
            return Task.FromResult(CreateHold($"Not enough candles. Required: {_longPeriod + 1}, got: {closes.Count}."));
        }

        var volumes = marketData.Volumes;
        if (volumes.Count < _longPeriod)
        {
            _logger.LogInformation(
                "MovingAverageCrossoverStrategy rejected signal: Symbol={Symbol}, Reason=Not enough volume points, Required={Required}, Got={Got}",
                marketData.Symbol, _longPeriod, volumes.Count);
            return Task.FromResult(CreateHold($"Not enough volume points. Required: {_longPeriod}, got: {volumes.Count}."));
        }

        var currentShort = closes.TakeLast(_shortPeriod).Average();
        var currentLong = closes.TakeLast(_longPeriod).Average();

        if (currentLong <= 0m)
        {
            _logger.LogInformation(
                "MovingAverageCrossoverStrategy rejected signal: Symbol={Symbol}, Reason=Invalid long MA, CurrentLong={CurrentLong}",
                marketData.Symbol, currentLong);
            return Task.FromResult(CreateHold("Invalid long moving average (<= 0)."));
        }

        var previousShort = closes.Skip(closes.Count - _shortPeriod - 1).Take(_shortPeriod).Average();
        var previousLong = closes.Skip(closes.Count - _longPeriod - 1).Take(_longPeriod).Average();

        var distance = Math.Abs(currentShort - currentLong) / currentLong;
        var trendStrength = previousLong <= 0m
            ? 0m
            : Math.Abs(currentLong - previousLong) / previousLong;
        var confidence = CalculateConfidence(distance, trendStrength);

        var longWindow = closes.TakeLast(_longPeriod).ToArray();
        var volatility = longWindow.Max() - longWindow.Min();
        var volatilityRatio = volatility / currentLong;
        if (volatilityRatio < _volatilityThreshold)
        {
            _logger.LogInformation(
                "MovingAverageCrossoverStrategy rejected signal: Symbol={Symbol}, Reason=Low volatility, VolatilityRatio={VolatilityRatio:F6}, Threshold={Threshold:F6}",
                marketData.Symbol, volatilityRatio, _volatilityThreshold);
            return Task.FromResult(CreateHold(
                $"Signal rejected: low volatility {volatilityRatio:F6} < {_volatilityThreshold:F6}.",
                confidence));
        }

        var bullishCrossover = previousShort <= previousLong && currentShort > currentLong;
        var bearishCrossover = previousShort >= previousLong && currentShort < currentLong;
        if (!bullishCrossover && !bearishCrossover)
        {
            return Task.FromResult(CreateHold(
                $"No crossover. shortMA={currentShort:F6}, longMA={currentLong:F6}.",
                confidence));
        }

        if (distance <= _trendStrengthThreshold)
        {
            _logger.LogInformation(
                "MovingAverageCrossoverStrategy rejected signal: Symbol={Symbol}, Reason=Weak crossover distance, Distance={Distance:F6}, Threshold={Threshold:F6}",
                marketData.Symbol, distance, _trendStrengthThreshold);
            return Task.FromResult(CreateHold(
                $"Signal rejected: weak MA distance {distance:F6} <= {_trendStrengthThreshold:F6}.",
                confidence));
        }

        if (trendStrength <= _trendStrengthThreshold)
        {
            _logger.LogInformation(
                "MovingAverageCrossoverStrategy rejected signal: Symbol={Symbol}, Reason=Weak trend, TrendStrength={TrendStrength:F6}, Threshold={Threshold:F6}",
                marketData.Symbol, trendStrength, _trendStrengthThreshold);
            return Task.FromResult(CreateHold(
                $"Signal rejected: weak trend strength {trendStrength:F6} <= {_trendStrengthThreshold:F6}.",
                confidence));
        }

        var currentVolume = volumes[^1];
        var averageVolume = volumes.TakeLast(_longPeriod).Average();
        if (currentVolume <= averageVolume)
        {
            _logger.LogInformation(
                "MovingAverageCrossoverStrategy rejected signal: Symbol={Symbol}, Reason=Low volume confirmation, CurrentVolume={CurrentVolume:F6}, AverageVolume={AverageVolume:F6}",
                marketData.Symbol, currentVolume, averageVolume);
            return Task.FromResult(CreateHold(
                $"Signal rejected: volume confirmation failed ({currentVolume:F6} <= {averageVolume:F6}).",
                confidence));
        }

        var nowUtc = DateTime.UtcNow;
        if (_cooldownSeconds > 0 &&
            LastSignalTimesUtc.TryGetValue(marketData.Symbol, out var lastSignalUtc) &&
            nowUtc - lastSignalUtc < TimeSpan.FromSeconds(_cooldownSeconds))
        {
            var remainingSeconds = (_cooldownSeconds - (int)(nowUtc - lastSignalUtc).TotalSeconds).ToString();
            _logger.LogInformation(
                "MovingAverageCrossoverStrategy rejected signal: Symbol={Symbol}, Reason=Cooldown active, LastSignalUtc={LastSignalUtc}, CooldownSeconds={CooldownSeconds}",
                marketData.Symbol, lastSignalUtc, _cooldownSeconds);
            return Task.FromResult(CreateHold(
                $"Signal rejected: cooldown active ({remainingSeconds}s remaining).",
                confidence));
        }

        LastSignalTimesUtc.AddOrUpdate(marketData.Symbol, nowUtc, (_, _) => nowUtc);
        var signal = bullishCrossover ? TradeSignal.Buy : TradeSignal.Sell;
        _logger.LogInformation(
            "MovingAverageCrossoverStrategy accepted signal: Symbol={Symbol}, Signal={Signal}, Confidence={Confidence:F4}, CurrentShort={CurrentShort:F6}, CurrentLong={CurrentLong:F6}, Distance={Distance:F6}, TrendStrength={TrendStrength:F6}",
            marketData.Symbol, signal, confidence, currentShort, currentLong, distance, trendStrength);

        return Task.FromResult(new StrategySignalResult
        {
            Signal = signal,
            Reason = bullishCrossover
                ? $"Bullish crossover: shortMA {currentShort:F6} crossed above longMA {currentLong:F6}."
                : $"Bearish crossover: shortMA {currentShort:F6} crossed below longMA {currentLong:F6}.",
            Confidence = confidence
        });
    }

    private static StrategySignalResult CreateHold(string reason, decimal confidence = 0m)
    {
        return new StrategySignalResult
        {
            Signal = TradeSignal.Hold,
            Reason = reason,
            Confidence = Math.Clamp(confidence, 0m, 1m)
        };
    }

    private decimal CalculateConfidence(decimal distance, decimal trendStrength)
    {
        var distanceScore = Normalize(distance, _trendStrengthThreshold * 2m);
        var trendScore = Normalize(trendStrength, _trendStrengthThreshold);
        return Math.Clamp((distanceScore + trendScore) / 2m, 0m, 1m);
    }

    private static decimal Normalize(decimal value, decimal maxReference)
    {
        if (maxReference <= 0m)
            return 0m;

        return Math.Clamp(value / maxReference, 0m, 1m);
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
}
