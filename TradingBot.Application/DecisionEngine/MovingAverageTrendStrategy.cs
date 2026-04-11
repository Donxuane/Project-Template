using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.DecisionEngine;

public class MovingAverageTrendStrategy : IMovingAverageStrategy
{
    private const string ConfigSection = "DecisionEngine:MovingAverageCrossoverStrategy";
    private static readonly ConcurrentDictionary<TradingSymbol, DateTime> LastEntrySignalTimesUtc = new();

    private readonly ITrendStateService _trendStateService;
    private readonly IPositionManager _positionManager;
    private readonly IMarketStateTracker _marketStateTracker;
    private readonly IMarketConditionService _marketConditionService;
    private readonly int _shortPeriod;
    private int _longPeriod;
    private readonly decimal _trendStrengthThreshold;
    private readonly decimal _momentumFlatteningThreshold;
    private readonly int _cooldownSeconds;

    public int RequiredPeriods => _longPeriod + 1;

    public MovingAverageTrendStrategy(
        ILogger<MovingAverageTrendStrategy> logger,
        ITrendStateService trendStateService,
        IPositionManager positionManager,
        IMarketStateTracker marketStateTracker,
        IMarketConditionService marketConditionService,
        IConfiguration configuration)
    {
        _trendStateService = trendStateService;
        _positionManager = positionManager;
        _marketStateTracker = marketStateTracker;
        _marketConditionService = marketConditionService;
        _shortPeriod = GetInt(configuration, $"{ConfigSection}:ShortPeriod", 10, 2);
        _longPeriod = GetInt(configuration, $"{ConfigSection}:LongPeriod", 30, _shortPeriod + 1);
        _trendStrengthThreshold = GetDecimal(configuration, $"{ConfigSection}:TrendStrengthThreshold", 0.001m, 0.000001m);
        _momentumFlatteningThreshold = GetDecimal(configuration, $"{ConfigSection}:MomentumFlatteningThreshold", 0.00005m, 0m);
        _cooldownSeconds = GetInt(configuration, $"{ConfigSection}:CooldownSeconds", 60, 0);

        if (_longPeriod <= _shortPeriod)
        {
            _longPeriod = _shortPeriod + 1;
            logger.LogWarning("MovingAverageTrendStrategy adjusted LongPeriod to {LongPeriod}.", _longPeriod);
        }
    }

    public Task<StrategySignalResult> GenerateSignalAsync(MarketSnapshot marketData, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (marketData.ClosePrices.Count < _longPeriod + 1 || marketData.Volumes.Count < _longPeriod)
            return Task.FromResult(CreateHold("MarketCondition.Unavailable: moving-average windows are warming up."));

        var trend = _trendStateService.Analyze(marketData, _shortPeriod, _longPeriod);
        if (!trend.IsValid)
            return Task.FromResult(CreateHold(trend.Reason));

        var marketCondition = _marketConditionService.Evaluate(marketData);
        if (!marketCondition.IsValid)
            return Task.FromResult(CreateHold(marketCondition.Reason));

        var confidence = CalculateConfidence(Math.Abs(trend.MaDistancePercent), Math.Abs(trend.ShortMaSlopePercent));
        var trackedState = _marketStateTracker.GetState(marketData.Symbol).LastTrendState;
        var bullishCrossover = trend.IsBullishCrossover && trackedState != TrendState.Bullish;
        var bearishCrossover = trend.IsBearishCrossover && trackedState != TrendState.Bearish;

        _marketStateTracker.Update(marketData.Symbol, trend.CurrentTrendState);
        _positionManager.UpdateTrend(marketData.Symbol, trend.CurrentTrendState);

        var position = _positionManager.GetState(marketData.Symbol);
        if (position.IsInPosition)
            return Task.FromResult(EvaluatePositionManagement(marketData, trend, confidence, bullishCrossover, bearishCrossover, position));

        if (!marketCondition.AllowTrade)
            return Task.FromResult(CreateHold("No entry signal - market in consolidation or low-volatility regime without breakout.", confidence));

        if (Math.Abs(trend.MaDistancePercent) <= _trendStrengthThreshold)
            return Task.FromResult(CreateHold("No entry signal - market in consolidation.", confidence));

        var currentVolume = marketData.Volumes[^1];
        var averageVolume = marketData.Volumes.TakeLast(_longPeriod).Average();
        if (currentVolume <= averageVolume)
            return Task.FromResult(CreateHold("No entry signal - trend exists but volume confirmation is weak.", confidence));

        if (IsCooldownActiveForEntry(marketData.Symbol, out var remainingSeconds))
            return Task.FromResult(CreateHold($"No entry signal - waiting for cooldown ({remainingSeconds}s remaining).", confidence));

        if (bullishCrossover)
        {
            MarkEntrySignal(marketData.Symbol);
            _positionManager.Enter(marketData.Symbol, PositionType.Long, marketData.CurrentPrice, trend.CurrentTrendState, DateTime.UtcNow);
            return Task.FromResult(new StrategySignalResult
            {
                Signal = TradeSignal.Buy,
                Reason = "Entry signal - bullish crossover confirmed.",
                Confidence = confidence
            });
        }

        if (bearishCrossover)
        {
            MarkEntrySignal(marketData.Symbol);
            _positionManager.Enter(marketData.Symbol, PositionType.Short, marketData.CurrentPrice, trend.CurrentTrendState, DateTime.UtcNow);
            return Task.FromResult(new StrategySignalResult
            {
                Signal = TradeSignal.Sell,
                Reason = "Entry signal - bearish crossover confirmed.",
                Confidence = confidence
            });
        }

        return Task.FromResult(CreateHold("No entry signal - waiting for a real crossover event.", confidence));
    }

    private StrategySignalResult EvaluatePositionManagement(
        MarketSnapshot marketData,
        TrendAnalysisResult trend,
        decimal confidence,
        bool bullishCrossover,
        bool bearishCrossover,
        SymbolPositionState position)
    {
        if (position.PositionType == PositionType.Long)
        {
            if (trend.CurrentTrendState == TrendState.Bearish || bearishCrossover)
            {
                _positionManager.Exit(marketData.Symbol, trend.CurrentTrendState);
                return new StrategySignalResult { Signal = TradeSignal.Sell, Reason = "Exit signal - trend reversal detected for long position.", Confidence = confidence };
            }

            if (trend.ShortMaSlopePercent <= _momentumFlatteningThreshold)
            {
                _positionManager.Exit(marketData.Symbol, trend.CurrentTrendState);
                return new StrategySignalResult { Signal = TradeSignal.Sell, Reason = "Exit signal - long momentum is weakening.", Confidence = confidence };
            }

            return CreateHold("Holding long position - trend still bullish.", confidence);
        }

        if (position.PositionType == PositionType.Short)
        {
            if (trend.CurrentTrendState == TrendState.Bullish || bullishCrossover)
            {
                _positionManager.Exit(marketData.Symbol, trend.CurrentTrendState);
                return new StrategySignalResult { Signal = TradeSignal.Buy, Reason = "Exit signal - trend reversal detected for short position.", Confidence = confidence };
            }

            if (trend.ShortMaSlopePercent >= -_momentumFlatteningThreshold)
            {
                _positionManager.Exit(marketData.Symbol, trend.CurrentTrendState);
                return new StrategySignalResult { Signal = TradeSignal.Buy, Reason = "Exit signal - short momentum is weakening.", Confidence = confidence };
            }

            return CreateHold("Holding short position - trend still bearish.", confidence);
        }

        return CreateHold("No entry signal - strategy position state reset to flat.", confidence);
    }

    private bool IsCooldownActiveForEntry(TradingSymbol symbol, out int remainingSeconds)
    {
        remainingSeconds = 0;
        if (_cooldownSeconds <= 0)
            return false;

        if (!LastEntrySignalTimesUtc.TryGetValue(symbol, out var lastSignalUtc))
            return false;

        var elapsed = DateTime.UtcNow - lastSignalUtc;
        if (elapsed >= TimeSpan.FromSeconds(_cooldownSeconds))
            return false;

        remainingSeconds = Math.Max(0, _cooldownSeconds - (int)elapsed.TotalSeconds);
        return true;
    }

    private static void MarkEntrySignal(TradingSymbol symbol)
    {
        var nowUtc = DateTime.UtcNow;
        LastEntrySignalTimesUtc.AddOrUpdate(symbol, nowUtc, (_, _) => nowUtc);
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
