using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;
using TradingBot.Shared.Configuration;

namespace TradingBot.Application.DecisionEngine;

public class MovingAverageTrendStrategy : IMovingAverageStrategy
{
    private const string StrategyName = "MovingAverageCrossover";
    private const decimal TrendWeight = 0.70m;
    private const decimal MarketWeight = 0.30m;
    private static readonly ConcurrentDictionary<TradingSymbol, DateTime> LastEntrySignalTimesUtc = new();
    private static readonly ConcurrentDictionary<TradingSymbol, PendingBreakoutState> PendingBreakouts = new();

    private readonly ITrendStateService _trendStateService;
    private readonly ILogger<MovingAverageTrendStrategy> _logger;
    private readonly IPositionManager _positionManager;
    private readonly IMarketStateTracker _marketStateTracker;
    private readonly IMarketConditionService _marketConditionService;
    private readonly TradingMode _tradingMode;
    private readonly int _shortPeriod;
    private int _longPeriod;
    private readonly decimal _momentumFlatteningThreshold;
    private readonly int _cooldownSeconds;
    private readonly int _minimumTrendConfidenceScore;
    private readonly int _minimumMarketConditionScore;
    private readonly decimal _minSlopePercent;
    private readonly decimal _minTrendStrengthPercent;
    private readonly bool _allowShortSelling;
    private readonly bool _requireCrossoverForEntry;
    private readonly bool _exitOnWeakTrendConfidence;
    private readonly int _minimumExitTrendConfidenceScore;
    private readonly bool _enableLowVolatilityBreakoutEntry;
    private readonly int _breakoutLookbackCandles;
    private readonly decimal _breakoutBufferPercent;
    private readonly decimal _minBreakoutSlopePercent;
    private readonly bool _requirePositiveShortSlopeForBreakout;
    private readonly bool _requireTrendStrengthExpansion;
    private readonly bool _requireBreakoutConfirmation;
    private readonly int _breakoutConfirmationCandles;
    private readonly decimal _breakoutHoldBufferPercent;
    private readonly bool _requireCloseAboveBreakoutThreshold;
    private readonly bool _requireShortSlopeStillPositiveOnConfirmation;
    private readonly bool _requireNoImmediateBearishCandleAfterBreakout;
    private bool IsSpotMode => _tradingMode == TradingMode.Spot;

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
        _logger = logger;
        _trendStateService = trendStateService;
        _positionManager = positionManager;
        _marketStateTracker = marketStateTracker;
        _marketConditionService = marketConditionService;
        var tradingSettings = RuntimeTradingConfigResolver.ResolveTrading(configuration);
        var strategySettings = RuntimeTradingConfigResolver.ResolveMovingAverageStrategy(configuration);
        _tradingMode = Enum.TryParse<TradingMode>(tradingSettings.Mode, true, out var mode) ? mode : TradingMode.Spot;
        _shortPeriod = strategySettings.ShortPeriod;
        _longPeriod = Math.Max(strategySettings.LongPeriod, _shortPeriod + 1);
        _momentumFlatteningThreshold = strategySettings.MomentumFlatteningThreshold;
        _cooldownSeconds = strategySettings.CooldownSeconds;
        _minimumTrendConfidenceScore = strategySettings.MinimumTrendConfidenceScore;
        _minimumMarketConditionScore = strategySettings.MinimumMarketConditionScore;
        _minSlopePercent = Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:TrendState:MinSlopePercent") ?? 0.0005m);
        _minTrendStrengthPercent = Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:TrendState:MinTrendStrengthPercent") ?? 0.001m);
        _allowShortSelling = strategySettings.AllowShortSelling;
        _requireCrossoverForEntry = strategySettings.RequireCrossoverForEntry;
        _exitOnWeakTrendConfidence = strategySettings.ExitOnWeakTrendConfidence;
        _minimumExitTrendConfidenceScore = strategySettings.MinimumExitTrendConfidenceScore;
        _enableLowVolatilityBreakoutEntry = strategySettings.EnableLowVolatilityBreakoutEntry;
        _breakoutLookbackCandles = Math.Max(2, strategySettings.BreakoutLookbackCandles);
        _breakoutBufferPercent = Math.Max(0m, strategySettings.BreakoutBufferPercent);
        _minBreakoutSlopePercent = Math.Max(0m, strategySettings.MinBreakoutSlopePercent);
        _requirePositiveShortSlopeForBreakout = strategySettings.RequirePositiveShortSlopeForBreakout;
        _requireTrendStrengthExpansion = strategySettings.RequireTrendStrengthExpansion;
        _requireBreakoutConfirmation = strategySettings.RequireBreakoutConfirmation;
        _breakoutConfirmationCandles = Math.Max(1, strategySettings.BreakoutConfirmationCandles);
        _breakoutHoldBufferPercent = Math.Max(0m, strategySettings.BreakoutHoldBufferPercent);
        _requireCloseAboveBreakoutThreshold = strategySettings.RequireCloseAboveBreakoutThreshold;
        _requireShortSlopeStillPositiveOnConfirmation = strategySettings.RequireShortSlopeStillPositiveOnConfirmation;
        _requireNoImmediateBearishCandleAfterBreakout = strategySettings.RequireNoImmediateBearishCandleAfterBreakout;

        if (_longPeriod <= _shortPeriod)
        {
            _longPeriod = _shortPeriod + 1;
            _logger.LogWarning("MovingAverageTrendStrategy adjusted LongPeriod to {LongPeriod}.", _longPeriod);
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
        {
            PendingBreakouts.TryRemove(marketData.Symbol, out _);
            return Task.FromResult(EvaluatePositionManagement(marketData, trend, confidence, position));
        }

        var isLowVolatilityContext = trend.MarketRegime is MarketRegime.LowVolatility or MarketRegime.Ranging
                                     || marketCondition.Regime == VolatilityRegime.Low
                                     || trend.CurrentTrendState == TrendState.Neutral;
        if (isLowVolatilityContext)
        {
            var breakoutEvaluation = EvaluateLowVolatilityBreakout(marketData, trend);
            PendingBreakouts.TryGetValue(marketData.Symbol, out var pendingBreakout);
            var confirmationEvaluation = EvaluateBreakoutConfirmation(marketData, trend, breakoutEvaluation, pendingBreakout);
            LogLowVolatilityBreakoutDiagnostics(marketData, trend, marketCondition, breakoutEvaluation, confirmationEvaluation, pendingBreakout, confidence);

            if (!_enableLowVolatilityBreakoutEntry)
            {
                PendingBreakouts.TryRemove(marketData.Symbol, out _);
                const string disabledReason = "No entry signal - low-volatility market without breakout.";
                LogEntryRejectionDiagnostics(marketData, trend, marketCondition, confidence, disabledReason);
                return Task.FromResult(CreateHold(disabledReason, confidence));
            }

            if (pendingBreakout is not null)
            {
                var latestClosedCandleTimeUtc = marketData.LatestClosedCandleCloseTimeUtc;
                var confirmationBaselineTimeUtc = pendingBreakout.ConfirmedClosedCandleCount > 0
                    ? pendingBreakout.LastConfirmationCandleTimeUtc
                    : pendingBreakout.DetectedClosedCandleTimeUtc;
                if (!latestClosedCandleTimeUtc.HasValue || latestClosedCandleTimeUtc.Value <= confirmationBaselineTimeUtc)
                {
                    const string waitNewClosedCandleReason = "No entry signal - breakout detected, waiting for new closed candle confirmation.";
                    LogEntryRejectionDiagnostics(marketData, trend, marketCondition, confidence, waitNewClosedCandleReason);
                    return Task.FromResult(CreateHold(waitNewClosedCandleReason, confidence));
                }

                if (!confirmationEvaluation.Passed)
                {
                    PendingBreakouts.TryRemove(marketData.Symbol, out _);
                    const string failedReason = "No entry signal - pending breakout failed confirmation.";
                    LogEntryRejectionDiagnostics(marketData, trend, marketCondition, confidence, failedReason);
                    return Task.FromResult(CreateHold(failedReason, confidence));
                }

                var confirmedClosedCandleCount = pendingBreakout.ConfirmedClosedCandleCount + 1;
                if (_requireBreakoutConfirmation && confirmedClosedCandleCount < _breakoutConfirmationCandles)
                {
                    PendingBreakouts[marketData.Symbol] = pendingBreakout with
                    {
                        LastConfirmationCandleTimeUtc = latestClosedCandleTimeUtc.Value,
                        ConfirmedClosedCandleCount = confirmedClosedCandleCount
                    };
                    const string waitReason = "No entry signal - breakout detected, waiting for new closed candle confirmation.";
                    LogEntryRejectionDiagnostics(marketData, trend, marketCondition, confidence, waitReason);
                    return Task.FromResult(CreateHold(waitReason, confidence));
                }

                PendingBreakouts.TryRemove(marketData.Symbol, out _);
                MarkEntrySignal(marketData.Symbol);
                _positionManager.Enter(marketData.Symbol, PositionType.Long, marketData.CurrentPrice, trend.CurrentTrendState, DateTime.UtcNow);
                return Task.FromResult(CreateBuy("Entry signal - low-volatility breakout confirmed after follow-through.", confidence));
            }

            if (breakoutEvaluation.Passed)
            {
                if (_requireBreakoutConfirmation)
                {
                    var detectedClosedCandleTimeUtc = marketData.LatestClosedCandleCloseTimeUtc ?? DateTime.MinValue;
                    PendingBreakouts[marketData.Symbol] = new PendingBreakoutState(
                        DateTime.UtcNow,
                        detectedClosedCandleTimeUtc,
                        detectedClosedCandleTimeUtc,
                        0,
                        breakoutEvaluation.BreakoutThresholdPrice,
                        breakoutEvaluation.TrendStrengthCurrent);
                    const string waitingForConfirmationReason = "No entry signal - breakout detected, waiting for new closed candle confirmation.";
                    LogEntryRejectionDiagnostics(marketData, trend, marketCondition, confidence, waitingForConfirmationReason);
                    return Task.FromResult(CreateHold(waitingForConfirmationReason, confidence));
                }

                MarkEntrySignal(marketData.Symbol);
                _positionManager.Enter(marketData.Symbol, PositionType.Long, marketData.CurrentPrice, trend.CurrentTrendState, DateTime.UtcNow);
                return Task.FromResult(CreateBuy("Entry signal - low-volatility breakout confirmed.", confidence));
            }

            const string lowVolatilityReason = "No entry signal - low-volatility market without breakout.";
            LogEntryRejectionDiagnostics(marketData, trend, marketCondition, confidence, lowVolatilityReason);
            return Task.FromResult(CreateHold(lowVolatilityReason, confidence));
        }

        PendingBreakouts.TryRemove(marketData.Symbol, out _);

        if (trend.MarketRegime == MarketRegime.Ranging)
        {
            const string reason = "No entry signal - ranging market.";
            LogEntryRejectionDiagnostics(marketData, trend, marketCondition, confidence, reason);
            return Task.FromResult(CreateHold(reason, confidence));
        }

        if (marketCondition.IsUnsafeVolatility)
        {
            const string reason = "No entry signal - unsafe volatility regime.";
            LogEntryRejectionDiagnostics(marketData, trend, marketCondition, confidence, reason);
            return Task.FromResult(CreateHold(reason, confidence));
        }

        if (!marketCondition.AllowTrade)
        {
            LogEntryRejectionDiagnostics(marketData, trend, marketCondition, confidence, marketCondition.Reason);
            return Task.FromResult(CreateHold(marketCondition.Reason, confidence));
        }

        if (marketCondition.MarketConditionScore < _minimumMarketConditionScore)
        {
            const string reason = "No entry signal - market condition score too weak.";
            LogEntryRejectionDiagnostics(marketData, trend, marketCondition, confidence, reason);
            return Task.FromResult(CreateHold(reason, confidence));
        }

        if (trend.ConfidenceScore < _minimumTrendConfidenceScore)
        {
            const string reason = "No entry signal - trend confidence too weak.";
            LogEntryRejectionDiagnostics(marketData, trend, marketCondition, confidence, reason);
            return Task.FromResult(CreateHold(reason, confidence));
        }

        if (IsCooldownActiveForEntry(marketData.Symbol, out var remainingSeconds))
        {
            var reason = $"No entry signal - waiting for cooldown ({remainingSeconds}s remaining).";
            LogEntryRejectionDiagnostics(marketData, trend, marketCondition, confidence, reason);
            return Task.FromResult(CreateHold(reason, confidence));
        }

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
            if (IsSpotMode)
            {
                _logger.LogWarning("Ignoring short state transition because TradingMode is Spot.");
                return Task.FromResult(CreateHold("Spot bearish signal ignored because short selling is disabled and no long position is open.", confidence));
            }

            MarkEntrySignal(marketData.Symbol);
            _positionManager.Enter(marketData.Symbol, PositionType.Short, marketData.CurrentPrice, trend.CurrentTrendState, DateTime.UtcNow);
            var reason = marketCondition.RequiresReducedPositionSize
                ? "Entry signal - bearish trend confirmed, but high volatility requires reduced position size."
                : "Entry signal - bearish trend confirmed.";
            // TODO: RiskManagementService should reduce quantity when reduced-position flag is present.
            return Task.FromResult(CreateSell(reason, confidence));
        }

        if (IsSpotMode && trend.IsBearishTrendConfirmed)
            return Task.FromResult(CreateHold("Spot bearish signal ignored because short selling is disabled and no long position is open.", confidence));

        const string waitingReason = "No entry signal - waiting for confirmed trend direction.";
        LogEntryRejectionDiagnostics(marketData, trend, marketCondition, confidence, waitingReason);
        return Task.FromResult(CreateHold(waitingReason, confidence));
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
            if (IsSpotMode)
            {
                _logger.LogWarning("Ignoring short state transition because TradingMode is Spot.");
                _positionManager.Exit(marketData.Symbol, trend.CurrentTrendState);
                return CreateHold("Spot short state ignored because short selling is disabled.", confidence);
            }

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

    private LowVolatilityBreakoutEvaluation EvaluateLowVolatilityBreakout(
        MarketSnapshot marketData,
        TrendAnalysisResult trend)
    {
        var closePrices = marketData.ClosePrices;
        if (closePrices.Count < _breakoutLookbackCandles + 1)
        {
            return new LowVolatilityBreakoutEvaluation(
                false,
                0m,
                marketData.CurrentPrice > 0m ? marketData.CurrentPrice : closePrices.LastOrDefault(),
                closePrices.LastOrDefault(),
                closePrices.Count >= 2 ? closePrices[^2] : null,
                0m,
                trend.ShortMaSlopePercent,
                trend.TrendStrengthPercent,
                null,
                false,
                false,
                false,
                false,
                "Insufficient closes for breakout lookback window.");
        }

        var window = closePrices.TakeLast(_breakoutLookbackCandles + 1).ToArray();
        var latestClose = window[^1];
        var previousClose = closePrices.Count >= 2 ? closePrices[^2] : (decimal?)null;
        var rangeHigh = window.Take(_breakoutLookbackCandles).Max();
        var referencePrice = marketData.CurrentPrice > 0m ? marketData.CurrentPrice : latestClose;
        var breakoutThresholdPrice = rangeHigh * (1m + _breakoutBufferPercent);
        var isPriceBreakout = referencePrice > breakoutThresholdPrice;
        var isSlopeAboveMinimum = trend.ShortMaSlopePercent >= _minBreakoutSlopePercent;
        var hasPositiveSlope = !_requirePositiveShortSlopeForBreakout || trend.ShortMaSlopePercent > 0m;
        var closeAboveMovingAverages = latestClose > trend.CurrentShortMa && latestClose > trend.CurrentLongMa;

        decimal? previousTrendStrengthPercent = null;
        var hasTrendStrengthExpansion = true;
        if (_requireTrendStrengthExpansion)
        {
            if (trend.PreviousLongMa > 0m)
            {
                previousTrendStrengthPercent = Math.Abs(trend.PreviousShortMa - trend.PreviousLongMa) / trend.PreviousLongMa;
                hasTrendStrengthExpansion = trend.TrendStrengthPercent > previousTrendStrengthPercent.Value;
            }
            else
            {
                hasTrendStrengthExpansion = false;
            }
        }

        var passed = isPriceBreakout
                     && isSlopeAboveMinimum
                     && hasPositiveSlope
                     && closeAboveMovingAverages
                     && hasTrendStrengthExpansion;
        var failureReason = passed
            ? "Breakout criteria passed."
            : BuildBreakoutFailureReason(
                isPriceBreakout,
                isSlopeAboveMinimum,
                hasPositiveSlope,
                closeAboveMovingAverages,
                hasTrendStrengthExpansion);

        return new LowVolatilityBreakoutEvaluation(
            passed,
            rangeHigh,
            referencePrice,
            latestClose,
            previousClose,
            breakoutThresholdPrice,
            trend.ShortMaSlopePercent,
            trend.TrendStrengthPercent,
            previousTrendStrengthPercent,
            isPriceBreakout,
            isSlopeAboveMinimum,
            hasPositiveSlope,
            closeAboveMovingAverages,
            failureReason);
    }

    private BreakoutConfirmationEvaluation EvaluateBreakoutConfirmation(
        MarketSnapshot marketData,
        TrendAnalysisResult trend,
        LowVolatilityBreakoutEvaluation breakout,
        PendingBreakoutState? pendingBreakout)
    {
        if (pendingBreakout is null)
        {
            return new BreakoutConfirmationEvaluation(
                false,
                false,
                false,
                false,
                false,
                false,
                "No pending breakout state.",
                0m,
                0m,
                breakout.LatestClose);
        }

        var confirmationThresholdPrice = pendingBreakout.BreakoutThresholdPrice * (1m + _breakoutHoldBufferPercent);
        var closedCandleClosePrice = marketData.LatestClosedCandleClosePrice ?? breakout.LatestClose;
        var confirmationReferencePrice = _requireCloseAboveBreakoutThreshold
            ? closedCandleClosePrice
            : breakout.CurrentPrice;
        var holdsAboveThreshold = confirmationReferencePrice > confirmationThresholdPrice;
        var slopeStillPositive = !_requireShortSlopeStillPositiveOnConfirmation || trend.ShortMaSlopePercent > 0m;
        var noImmediateBearishCandle = !_requireNoImmediateBearishCandleAfterBreakout
                                       || !breakout.PreviousClose.HasValue
                                       || breakout.LatestClose >= breakout.PreviousClose.Value;
        var trendStrengthCollapsed = pendingBreakout.DetectedTrendStrength > 0m
                                     && trend.TrendStrengthPercent < pendingBreakout.DetectedTrendStrength * 0.5m;
        var hasTrendStrength = !trendStrengthCollapsed;
        var passed = holdsAboveThreshold && slopeStillPositive && noImmediateBearishCandle && hasTrendStrength;
        var failureReason = passed
            ? "Pending breakout confirmation passed."
            : BuildConfirmationFailureReason(
                holdsAboveThreshold,
                slopeStillPositive,
                noImmediateBearishCandle,
                hasTrendStrength);

        return new BreakoutConfirmationEvaluation(
            passed,
            holdsAboveThreshold,
            slopeStillPositive,
            noImmediateBearishCandle,
            trendStrengthCollapsed,
            _requireCloseAboveBreakoutThreshold,
            failureReason,
            confirmationThresholdPrice,
            closedCandleClosePrice,
            confirmationReferencePrice);
    }

    private static string BuildBreakoutFailureReason(
        bool isPriceBreakout,
        bool isSlopeAboveMinimum,
        bool hasPositiveSlope,
        bool closeAboveMovingAverages,
        bool hasTrendStrengthExpansion)
    {
        var failures = new List<string>();
        if (!isPriceBreakout)
            failures.Add("Price did not break above buffered range high.");
        if (!isSlopeAboveMinimum)
            failures.Add("Short MA slope below minimum breakout slope.");
        if (!hasPositiveSlope)
            failures.Add("Short MA slope is not positive.");
        if (!closeAboveMovingAverages)
            failures.Add("Latest close is not above both moving averages.");
        if (!hasTrendStrengthExpansion)
            failures.Add("Trend strength did not expand.");
        return string.Join(" ", failures);
    }

    private static string BuildConfirmationFailureReason(
        bool holdsAboveThreshold,
        bool slopeStillPositive,
        bool noImmediateBearishCandle,
        bool hasTrendStrength)
    {
        var failures = new List<string>();
        if (!holdsAboveThreshold)
            failures.Add("Price/close fell below confirmation threshold.");
        if (!slopeStillPositive)
            failures.Add("Short MA slope is not positive during confirmation.");
        if (!noImmediateBearishCandle)
            failures.Add("Immediate bearish follow-up candle detected.");
        if (!hasTrendStrength)
            failures.Add("Trend strength collapsed during confirmation.");
        return string.Join(" ", failures);
    }

    private void LogEntryRejectionDiagnostics(
        MarketSnapshot marketData,
        TrendAnalysisResult trend,
        MarketConditionResult marketCondition,
        decimal confidence,
        string reason)
    {
        var isRanging = trend.MarketRegime == MarketRegime.Ranging;
        var isLowVolatility = trend.MarketRegime == MarketRegime.LowVolatility || marketCondition.Regime == VolatilityRegime.Low;

        _logger.LogInformation(
            "MovingAverageTrendStrategy entry rejected: Symbol={Symbol}, Reason={Reason}, CurrentShortMa={CurrentShortMa}, CurrentLongMa={CurrentLongMa}, PreviousShortMa={PreviousShortMa}, PreviousLongMa={PreviousLongMa}, ShortMaSlopePercent={ShortMaSlopePercent}, LongMaSlopePercent={LongMaSlopePercent}, TrendStrengthPercent={TrendStrengthPercent}, MinSlopePercent={MinSlopePercent}, MinTrendStrengthPercent={MinTrendStrengthPercent}, TrendConfidenceScore={TrendConfidenceScore}, MinimumTrendConfidenceScore={MinimumTrendConfidenceScore}, BullishCrossover={BullishCrossover}, BearishCrossover={BearishCrossover}, CurrentTrendDirection={CurrentTrendDirection}, MarketRegime={MarketRegime}, VolatilityRegime={VolatilityRegime}, IsLowVolatility={IsLowVolatility}, IsRanging={IsRanging}, RequireCrossoverForEntry={RequireCrossoverForEntry}, MarketConditionScore={MarketConditionScore}, MinimumMarketConditionScore={MinimumMarketConditionScore}, Confidence={Confidence}",
            marketData.Symbol,
            reason,
            trend.CurrentShortMa,
            trend.CurrentLongMa,
            trend.PreviousShortMa,
            trend.PreviousLongMa,
            trend.ShortMaSlopePercent,
            trend.LongMaSlopePercent,
            trend.TrendStrengthPercent,
            _minSlopePercent,
            _minTrendStrengthPercent,
            trend.ConfidenceScore,
            _minimumTrendConfidenceScore,
            trend.IsBullishCrossover,
            trend.IsBearishCrossover,
            trend.CurrentTrendState,
            trend.MarketRegime,
            marketCondition.Regime,
            isLowVolatility,
            isRanging,
            _requireCrossoverForEntry,
            marketCondition.MarketConditionScore,
            _minimumMarketConditionScore,
            confidence);
    }

    private void LogLowVolatilityBreakoutDiagnostics(
        MarketSnapshot marketData,
        TrendAnalysisResult trend,
        MarketConditionResult marketCondition,
        LowVolatilityBreakoutEvaluation breakout,
        BreakoutConfirmationEvaluation confirmation,
        PendingBreakoutState? pendingBreakout,
        decimal confidence)
    {
        _logger.LogInformation(
            "MovingAverageTrendStrategy low-volatility breakout diagnostics: Symbol={Symbol}, BreakoutEnabled={BreakoutEnabled}, Passed={Passed}, FailureReason={FailureReason}, RecentRangeHigh={RecentRangeHigh}, CurrentPrice={CurrentPrice}, LatestClose={LatestClose}, PreviousClose={PreviousClose}, BreakoutBufferPercent={BreakoutBufferPercent}, BreakoutThresholdPrice={BreakoutThresholdPrice}, ShortMaSlopePercent={ShortMaSlopePercent}, MinBreakoutSlopePercent={MinBreakoutSlopePercent}, RequirePositiveShortSlopeForBreakout={RequirePositiveShortSlopeForBreakout}, RequireTrendStrengthExpansion={RequireTrendStrengthExpansion}, TrendStrengthCurrent={TrendStrengthCurrent}, TrendStrengthPrevious={TrendStrengthPrevious}, CloseAboveShortAndLongMa={CloseAboveShortAndLongMa}, PriceBreakout={PriceBreakout}, SlopeAboveMinimum={SlopeAboveMinimum}, PositiveSlopeCheck={PositiveSlopeCheck}, LatestClosedCandleOpenTimeUtc={LatestClosedCandleOpenTimeUtc}, LatestClosedCandleCloseTimeUtc={LatestClosedCandleCloseTimeUtc}, LatestClosedCandleClosePrice={LatestClosedCandleClosePrice}, PendingBreakoutExists={PendingBreakoutExists}, PendingDetectedAtUtc={PendingDetectedAtUtc}, PendingDetectedCandleTimeUtc={PendingDetectedCandleTimeUtc}, PendingLastConfirmationCandleTimeUtc={PendingLastConfirmationCandleTimeUtc}, PendingDetectedTrendStrength={PendingDetectedTrendStrength}, PendingConfirmedClosedCandleCount={PendingConfirmedClosedCandleCount}, RequireBreakoutConfirmation={RequireBreakoutConfirmation}, BreakoutConfirmationCandles={BreakoutConfirmationCandles}, ConfirmationPassed={ConfirmationPassed}, ConfirmationFailureReason={ConfirmationFailureReason}, ConfirmationThresholdPrice={ConfirmationThresholdPrice}, ConfirmationHoldBufferThresholdPrice={ConfirmationHoldBufferThresholdPrice}, ConfirmationClosedCandleClosePrice={ConfirmationClosedCandleClosePrice}, ConfirmationReferencePrice={ConfirmationReferencePrice}, ConfirmationHoldsAboveThreshold={ConfirmationHoldsAboveThreshold}, ConfirmationSlopeStillPositive={ConfirmationSlopeStillPositive}, ConfirmationNoImmediateBearishCandle={ConfirmationNoImmediateBearishCandle}, TrendStrengthCollapsed={TrendStrengthCollapsed}, MarketRegime={MarketRegime}, VolatilityRegime={VolatilityRegime}, Confidence={Confidence}",
            marketData.Symbol,
            _enableLowVolatilityBreakoutEntry,
            breakout.Passed,
            breakout.FailureReason,
            breakout.RecentRangeHigh,
            breakout.CurrentPrice,
            breakout.LatestClose,
            breakout.PreviousClose,
            _breakoutBufferPercent,
            breakout.BreakoutThresholdPrice,
            breakout.ShortMaSlopePercent,
            _minBreakoutSlopePercent,
            _requirePositiveShortSlopeForBreakout,
            _requireTrendStrengthExpansion,
            breakout.TrendStrengthCurrent,
            breakout.TrendStrengthPrevious,
            breakout.CloseAboveShortAndLongMa,
            breakout.PriceBreakout,
            breakout.SlopeAboveMinimum,
            breakout.PositiveSlopeCheck,
            marketData.LatestClosedCandleOpenTimeUtc,
            marketData.LatestClosedCandleCloseTimeUtc,
            marketData.LatestClosedCandleClosePrice,
            pendingBreakout is not null,
            pendingBreakout?.DetectedAtUtc,
            pendingBreakout?.DetectedClosedCandleTimeUtc,
            pendingBreakout?.LastConfirmationCandleTimeUtc,
            pendingBreakout?.DetectedTrendStrength,
            pendingBreakout?.ConfirmedClosedCandleCount,
            _requireBreakoutConfirmation,
            _breakoutConfirmationCandles,
            confirmation.Passed,
            confirmation.FailureReason,
            confirmation.ConfirmationThresholdPrice,
            confirmation.ConfirmationThresholdPrice,
            confirmation.ClosedCandleClosePrice,
            confirmation.ConfirmationReferencePrice,
            confirmation.HoldsAboveThreshold,
            confirmation.SlopeStillPositive,
            confirmation.NoImmediateBearishCandle,
            confirmation.TrendStrengthCollapsed,
            trend.MarketRegime,
            marketCondition.Regime,
            confidence);
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

    private sealed record LowVolatilityBreakoutEvaluation(
        bool Passed,
        decimal RecentRangeHigh,
        decimal CurrentPrice,
        decimal LatestClose,
        decimal? PreviousClose,
        decimal BreakoutThresholdPrice,
        decimal ShortMaSlopePercent,
        decimal TrendStrengthCurrent,
        decimal? TrendStrengthPrevious,
        bool PriceBreakout,
        bool SlopeAboveMinimum,
        bool PositiveSlopeCheck,
        bool CloseAboveShortAndLongMa,
        string FailureReason);

    private sealed record BreakoutConfirmationEvaluation(
        bool Passed,
        bool HoldsAboveThreshold,
        bool SlopeStillPositive,
        bool NoImmediateBearishCandle,
        bool TrendStrengthCollapsed,
        bool RequireCloseAboveBreakoutThreshold,
        string FailureReason,
        decimal ConfirmationThresholdPrice,
        decimal ClosedCandleClosePrice,
        decimal ConfirmationReferencePrice);

    private sealed record PendingBreakoutState(
        DateTime DetectedAtUtc,
        DateTime DetectedClosedCandleTimeUtc,
        DateTime LastConfirmationCandleTimeUtc,
        int ConfirmedClosedCandleCount,
        decimal BreakoutThresholdPrice,
        decimal DetectedTrendStrength);

}
