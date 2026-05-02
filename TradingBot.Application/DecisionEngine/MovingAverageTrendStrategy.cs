using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.DecisionEngine;

public class MovingAverageTrendStrategy : IMovingAverageStrategy
{
    private const string StrategyName = "MovingAverageCrossover";
    private const string ConfigSection = "DecisionEngine:MovingAverageCrossoverStrategy";
    private const decimal TrendWeight = 0.70m;
    private const decimal MarketWeight = 0.30m;
    private static readonly ConcurrentDictionary<TradingSymbol, DateTime> LastEntrySignalTimesUtc = new();

    private readonly ITrendStateService _trendStateService;
    private readonly IPositionManager _positionManager;
    private readonly IMarketStateTracker _marketStateTracker;
    private readonly IMarketConditionService _marketConditionService;
    private readonly int _shortPeriod;
    private int _longPeriod;
    private readonly decimal _momentumFlatteningThreshold;
    private readonly int _cooldownSeconds;
    private readonly int _minimumTrendConfidenceScore;
    private readonly int _minimumMarketConditionScore;
    private readonly bool _allowShortSelling;
    private readonly bool _requireCrossoverForEntry;
    private readonly bool _exitOnWeakTrendConfidence;
    private readonly int _minimumExitTrendConfidenceScore;

    public int RequiredPeriods => Math.Max(
        _longPeriod + 1,
        Math.Max(_trendStateService.GetRequiredPeriods(_shortPeriod, _longPeriod), _marketConditionService.RequiredPeriods));

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
        _momentumFlatteningThreshold = GetDecimal(configuration, $"{ConfigSection}:MomentumFlatteningThreshold", 0.00005m, 0m);
        _cooldownSeconds = GetInt(configuration, $"{ConfigSection}:CooldownSeconds", 60, 0);
        _minimumTrendConfidenceScore = GetInt(configuration, $"{ConfigSection}:MinimumTrendConfidenceScore", 70, 0);
        _minimumMarketConditionScore = GetInt(configuration, $"{ConfigSection}:MinimumMarketConditionScore", 60, 0);
        _allowShortSelling = GetBool(configuration, $"{ConfigSection}:AllowShortSelling", false);
        _requireCrossoverForEntry = GetBool(configuration, $"{ConfigSection}:RequireCrossoverForEntry", false);
        _exitOnWeakTrendConfidence = GetBool(configuration, $"{ConfigSection}:ExitOnWeakTrendConfidence", true);
        _minimumExitTrendConfidenceScore = GetInt(configuration, $"{ConfigSection}:MinimumExitTrendConfidenceScore", 40, 0);

        if (_longPeriod <= _shortPeriod)
        {
            _longPeriod = _shortPeriod + 1;
            logger.LogWarning("MovingAverageTrendStrategy adjusted LongPeriod to {LongPeriod}.", _longPeriod);
        }
    }

    public Task<StrategySignalResult> GenerateSignalAsync(MarketSnapshot marketData, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (marketData.ClosePrices.Count < _longPeriod + 1)
            return Task.FromResult(CreateHold("MarketCondition.Unavailable: moving-average windows are warming up."));

        var trend = _trendStateService.Analyze(marketData, _shortPeriod, _longPeriod);
        if (!trend.IsValid)
            return Task.FromResult(CreateHold(trend.Reason));

        var marketCondition = _marketConditionService.Evaluate(marketData);
        if (!marketCondition.IsValid)
            return Task.FromResult(CreateHold(marketCondition.Reason));

        var confidence = CalculateCombinedConfidence(trend.ConfidenceScore, marketCondition.MarketConditionScore);

        _marketStateTracker.Update(marketData.Symbol, trend.CurrentTrendState);
        _positionManager.UpdateTrend(marketData.Symbol, trend.CurrentTrendState);

        var position = _positionManager.GetState(marketData.Symbol);
        if (position.IsInPosition)
            return Task.FromResult(EvaluatePositionManagement(marketData, trend, confidence, position));

        if (trend.MarketRegime == MarketRegime.Ranging)
            return Task.FromResult(CreateHold("No entry signal - ranging market.", confidence));

        if (marketCondition.IsUnsafeVolatility)
            return Task.FromResult(CreateHold("No entry signal - unsafe volatility regime.", confidence));

        if (!marketCondition.AllowTrade)
            return Task.FromResult(CreateHold(marketCondition.Reason, confidence));

        if (marketCondition.MarketConditionScore < _minimumMarketConditionScore)
            return Task.FromResult(CreateHold("No entry signal - market condition score too weak.", confidence));

        if (trend.ConfidenceScore < _minimumTrendConfidenceScore)
            return Task.FromResult(CreateHold("No entry signal - trend confidence too weak.", confidence));

        if (IsCooldownActiveForEntry(marketData.Symbol, out var remainingSeconds))
            return Task.FromResult(CreateHold($"No entry signal - waiting for cooldown ({remainingSeconds}s remaining).", confidence));

        if (CanEnterLong(trend))
        {
            MarkEntrySignal(marketData.Symbol);
            _positionManager.Enter(marketData.Symbol, PositionType.Long, marketData.CurrentPrice, trend.CurrentTrendState, DateTime.UtcNow);
            var reason = marketCondition.RequiresReducedPositionSize
                ? "Entry signal - bullish trend confirmed, but high volatility requires reduced position size."
                : "Entry signal - bullish trend confirmed.";
            // TODO: RiskManagementService should reduce quantity when reduced-position flag is present.
            return Task.FromResult(CreateBuy(reason, confidence));
        }

        if (CanEnterShort(trend))
        {
            MarkEntrySignal(marketData.Symbol);
            _positionManager.Enter(marketData.Symbol, PositionType.Short, marketData.CurrentPrice, trend.CurrentTrendState, DateTime.UtcNow);
            var reason = marketCondition.RequiresReducedPositionSize
                ? "Entry signal - bearish trend confirmed, but high volatility requires reduced position size."
                : "Entry signal - bearish trend confirmed.";
            // TODO: RiskManagementService should reduce quantity when reduced-position flag is present.
            return Task.FromResult(CreateSell(reason, confidence));
        }

        return Task.FromResult(CreateHold("No entry signal - waiting for confirmed trend direction.", confidence));
    }

    private StrategySignalResult EvaluatePositionManagement(
        MarketSnapshot marketData,
        TrendAnalysisResult trend,
        decimal confidence,
        SymbolPositionState position)
    {
        if (position.PositionType == PositionType.Long)
        {
            if (trend.IsBearishTrendConfirmed || trend.IsBearishCrossover)
            {
                _positionManager.Exit(marketData.Symbol, trend.CurrentTrendState);
                return CreateSell("Exit signal - trend reversal detected for long position.", confidence);
            }

            if (_exitOnWeakTrendConfidence && trend.ConfidenceScore < _minimumExitTrendConfidenceScore)
            {
                _positionManager.Exit(marketData.Symbol, trend.CurrentTrendState);
                return CreateSell("Exit signal - trend confidence weakened for long position.", confidence);
            }

            if (trend.ShortMaSlopePercent <= _momentumFlatteningThreshold)
            {
                _positionManager.Exit(marketData.Symbol, trend.CurrentTrendState);
                return CreateSell("Exit signal - long momentum is weakening.", confidence);
            }

            return CreateHold("Holding long position - trend still bullish.", confidence);
        }

        if (position.PositionType == PositionType.Short)
        {
            if (trend.IsBullishTrendConfirmed || trend.IsBullishCrossover)
            {
                _positionManager.Exit(marketData.Symbol, trend.CurrentTrendState);
                return CreateBuy("Exit signal - trend reversal detected for short position.", confidence);
            }

            if (_exitOnWeakTrendConfidence && trend.ConfidenceScore < _minimumExitTrendConfidenceScore)
            {
                _positionManager.Exit(marketData.Symbol, trend.CurrentTrendState);
                return CreateBuy("Exit signal - trend confidence weakened for short position.", confidence);
            }

            if (trend.ShortMaSlopePercent >= -_momentumFlatteningThreshold)
            {
                _positionManager.Exit(marketData.Symbol, trend.CurrentTrendState);
                return CreateBuy("Exit signal - short momentum is weakening.", confidence);
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

    private bool CanEnterLong(TrendAnalysisResult trend)
    {
        if (!trend.IsBullishTrendConfirmed)
            return false;

        if (_requireCrossoverForEntry && !trend.IsBullishCrossover)
            return false;

        return true;
    }

    private bool CanEnterShort(TrendAnalysisResult trend)
    {
        if (!_allowShortSelling || !trend.IsBearishTrendConfirmed)
            return false;

        if (_requireCrossoverForEntry && !trend.IsBearishCrossover)
            return false;

        return true;
    }

    private static decimal CalculateCombinedConfidence(int trendScore, int marketScore)
    {
        var combinedScore = (trendScore * TrendWeight) + (marketScore * MarketWeight);
        return Math.Clamp(combinedScore / 100m, 0m, 1m);
    }

    private static StrategySignalResult CreateBuy(string reason, decimal confidence)
    {
        return new StrategySignalResult
        {
            StrategyName = StrategyName,
            Signal = TradeSignal.Buy,
            Reason = reason,
            Confidence = Math.Clamp(confidence, 0m, 1m)
        };
    }

    private static StrategySignalResult CreateSell(string reason, decimal confidence)
    {
        return new StrategySignalResult
        {
            StrategyName = StrategyName,
            Signal = TradeSignal.Sell,
            Reason = reason,
            Confidence = Math.Clamp(confidence, 0m, 1m)
        };
    }

    private static StrategySignalResult CreateHold(string reason, decimal confidence = 0m)
    {
        return new StrategySignalResult
        {
            StrategyName = StrategyName,
            Signal = TradeSignal.Hold,
            Reason = reason,
            Confidence = Math.Clamp(confidence, 0m, 1m)
        };
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
