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
    private const string LowVolBreakoutExpectedTargetSource = "MovingAverageTrendStrategy.LowVolBreakoutExpectedTarget";
    private const string NormalTrendExpectedTargetSource = "MovingAverageTrendStrategy.NormalTrendExpectedTarget";
    private const decimal TrendWeight = 0.70m;
    private const decimal MarketWeight = 0.30m;
    private static readonly ConcurrentDictionary<TradingSymbol, DateTime> LastEntrySignalTimesUtc = new();
    private static readonly ConcurrentDictionary<TradingSymbol, PendingBreakoutState> PendingBreakouts = new();
    private static readonly ConcurrentDictionary<TradingSymbol, BreakoutRejectionAggregationWindow> BreakoutRejectionWindows = new();
    private static readonly ConcurrentDictionary<TradingSymbol, NormalEntryRejectionAggregationWindow> NormalEntryRejectionWindows = new();
    private static readonly ConcurrentDictionary<TradingSymbol, BreakoutEntryState> BreakoutEntryStates = new();

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
    private readonly bool _enableNormalTrendFallbackWhenLowVolBreakoutFails;
    private readonly bool _enableNetAwareMomentumExit;
    private readonly int _momentumExitMinTradeAgeMinutes;
    private readonly decimal _momentumExitAllowIfUnrealizedLossPercentBelow;
    private readonly bool _momentumExitRequireBearishConfirmationWhenFeeNegative;
    private readonly decimal _momentumExitMinNetProfitPercent;
    private readonly bool _enableNormalTrendBullishPersistenceFilter;
    private readonly int _normalTrendMinBullishPersistenceCandles;
    private readonly bool _enableNormalTrendCloseAboveRecentHighFilter;
    private readonly bool _enableNormalTrendMinDistanceToInvalidationFilter;
    private readonly decimal _normalTrendMinDistanceToInvalidationPercent;
    private readonly bool _enableNormalTrendRejectPreviousBearishCandleFilter;
    private readonly bool _enableNormalTrendRewardRiskFilter;
    private readonly decimal _normalTrendMinExpectedRewardRisk;
    private readonly bool _enableNormalTrendNearRecentHighRejection;
    private readonly decimal _normalTrendNearRecentHighRequiresRewardRisk;
    private readonly decimal? _normalTrendNearRecentHighRequiresTrendStrengthPercent;
    private readonly decimal _normalTrendNearRecentHighPercent;
    private readonly decimal _normalTrendAtrExtensionMultiplier;
    private readonly decimal _normalTrendStructureExtensionMultiplier;
    private readonly int _normalTrendExpectedTargetLookbackCandles;
    private readonly bool _normalTrendUseMinAtrStructureExtension;
    private readonly bool _useConfirmedClosedCandlesForEntryQuality;
    private readonly bool _useConfirmedClosedCandlesForLowVolBreakout;
    private readonly bool _enableNormalTrendPullbackContinuationOverride;
    private readonly decimal _normalTrendPullbackMinExpectedRewardRisk;
    private readonly bool _normalTrendPullbackRequireCloseAboveShortAndLongMa;
    private readonly bool _normalTrendPullbackRequirePositiveShortSlope;
    private readonly bool _normalTrendPullbackRejectPreviousBearishCandle;
    private readonly decimal _estimatedRoundTripCostPercent;
    private readonly TimeSpan _breakoutRejectionAggregationInterval;
    private readonly TimeSpan _normalEntryRejectionAggregationInterval;
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
        _enableNormalTrendFallbackWhenLowVolBreakoutFails = strategySettings.EnableNormalTrendFallbackWhenLowVolBreakoutFails;
        _enableNetAwareMomentumExit = strategySettings.EnableNetAwareMomentumExit;
        _momentumExitMinTradeAgeMinutes = Math.Max(0, strategySettings.MomentumExitMinTradeAgeMinutes);
        _momentumExitAllowIfUnrealizedLossPercentBelow = Math.Min(0m, strategySettings.MomentumExitAllowIfUnrealizedLossPercentBelow);
        _momentumExitRequireBearishConfirmationWhenFeeNegative = strategySettings.MomentumExitRequireBearishConfirmationWhenFeeNegative;
        _momentumExitMinNetProfitPercent = Math.Max(0m, strategySettings.MomentumExitMinNetProfitPercent);
        _enableNormalTrendBullishPersistenceFilter = strategySettings.EnableNormalTrendBullishPersistenceFilter;
        _normalTrendMinBullishPersistenceCandles = Math.Max(1, strategySettings.NormalTrendMinBullishPersistenceCandles);
        _enableNormalTrendCloseAboveRecentHighFilter = strategySettings.EnableNormalTrendCloseAboveRecentHighFilter;
        _enableNormalTrendMinDistanceToInvalidationFilter = strategySettings.EnableNormalTrendMinDistanceToInvalidationFilter;
        _normalTrendMinDistanceToInvalidationPercent = Math.Max(0m, strategySettings.NormalTrendMinDistanceToInvalidationPercent);
        _enableNormalTrendRejectPreviousBearishCandleFilter = strategySettings.EnableNormalTrendRejectPreviousBearishCandleFilter;
        _enableNormalTrendRewardRiskFilter = strategySettings.EnableNormalTrendRewardRiskFilter;
        _normalTrendMinExpectedRewardRisk = Math.Max(0m, strategySettings.NormalTrendMinExpectedRewardRisk);
        _enableNormalTrendNearRecentHighRejection = strategySettings.EnableNormalTrendNearRecentHighRejection;
        _normalTrendNearRecentHighRequiresRewardRisk = Math.Max(0m, strategySettings.NormalTrendNearRecentHighRequiresRewardRisk);
        _normalTrendNearRecentHighRequiresTrendStrengthPercent = strategySettings.NormalTrendNearRecentHighRequiresTrendStrengthPercent.HasValue
            ? Math.Max(0m, strategySettings.NormalTrendNearRecentHighRequiresTrendStrengthPercent.Value)
            : null;
        _normalTrendNearRecentHighPercent = Math.Max(0m, strategySettings.NormalTrendNearRecentHighPercent);
        _normalTrendAtrExtensionMultiplier = Math.Max(0m, strategySettings.NormalTrendAtrExtensionMultiplier);
        _normalTrendStructureExtensionMultiplier = Math.Max(0m, strategySettings.NormalTrendStructureExtensionMultiplier);
        _normalTrendExpectedTargetLookbackCandles = Math.Max(2, strategySettings.NormalTrendExpectedTargetLookbackCandles);
        _normalTrendUseMinAtrStructureExtension = strategySettings.NormalTrendUseMinAtrStructureExtension;
        _useConfirmedClosedCandlesForEntryQuality = strategySettings.UseConfirmedClosedCandlesForEntryQuality;
        _useConfirmedClosedCandlesForLowVolBreakout = strategySettings.UseConfirmedClosedCandlesForLowVolBreakout;
        _enableNormalTrendPullbackContinuationOverride = strategySettings.EnableNormalTrendPullbackContinuationOverride;
        _normalTrendPullbackMinExpectedRewardRisk = Math.Max(0m, strategySettings.NormalTrendPullbackMinExpectedRewardRisk);
        _normalTrendPullbackRequireCloseAboveShortAndLongMa = strategySettings.NormalTrendPullbackRequireCloseAboveShortAndLongMa;
        _normalTrendPullbackRequirePositiveShortSlope = strategySettings.NormalTrendPullbackRequirePositiveShortSlope;
        _normalTrendPullbackRejectPreviousBearishCandle = strategySettings.NormalTrendPullbackRejectPreviousBearishCandle;
        var feeRatePercent = Math.Max(0m, configuration.GetValue<decimal?>("Trading:FeeRatePercent") ?? 0.1m);
        var estimatedSpreadPercent = Math.Max(0m, configuration.GetValue<decimal?>("Trading:EstimatedSpreadPercent") ?? 0.05m);
        _estimatedRoundTripCostPercent = (feeRatePercent * 2m) + estimatedSpreadPercent;
        var aggregationIntervalMinutes = Math.Max(
            1,
            configuration.GetValue<int?>("DecisionEngine:MovingAverageCrossoverStrategy:BreakoutRejectionAggregationIntervalMinutes") ?? 60);
        _breakoutRejectionAggregationInterval = TimeSpan.FromMinutes(aggregationIntervalMinutes);
        var normalRejectionAggregationIntervalMinutes = Math.Max(
            1,
            configuration.GetValue<int?>("DecisionEngine:MovingAverageCrossoverStrategy:NormalEntryRejectionAggregationIntervalMinutes") ?? 60);
        _normalEntryRejectionAggregationInterval = TimeSpan.FromMinutes(normalRejectionAggregationIntervalMinutes);

        if (_longPeriod <= _shortPeriod)
        {
            _longPeriod = _shortPeriod + 1;
            _logger.LogWarning("MovingAverageTrendStrategy adjusted LongPeriod to {LongPeriod}.", _longPeriod);
        }
    }

    public Task<StrategySignalResult> GenerateSignalAsync(
        MarketSnapshot marketData,
        CancellationToken cancellationToken = default,
        bool allowStateMutation = true)
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
        var trendConfidenceScore = trend.ConfidenceScore;
        var marketConditionScore = marketCondition.MarketConditionScore;
        var volatilityRegime = marketCondition.Regime.ToString();
        StrategySignalResult HoldWithContext(string reason, decimal score = 0m, NormalTrendEntryQualityMetrics? normalTrendQuality = null) =>
            CreateHold(
                reason,
                score,
                trendConfidenceScore,
                marketConditionScore,
                volatilityRegime,
                trend.ShortMaSlopePercent,
                trend.TrendStrengthPercent,
                projectionMode: null,
                projectedExtension: null,
                normalTrendQuality);
        StrategySignalResult BuyWithContext(
            string reason,
            decimal score,
            decimal? expectedTargetPrice = null,
            decimal? expectedMovePercent = null,
            string? expectedTargetSource = null,
            decimal? breakoutRangeHigh = null,
            decimal? breakoutRangeLow = null,
            decimal? breakoutThresholdPrice = null,
            decimal? expectedTargetStructureExtensionUsed = null,
            decimal? expectedTargetAtrUsed = null,
            string? projectionMode = null,
            decimal? projectedExtension = null,
            NormalTrendEntryQualityMetrics? normalTrendQuality = null) =>
            CreateBuy(
                reason,
                score,
                trendConfidenceScore,
                marketConditionScore,
                volatilityRegime,
                expectedTargetPrice,
                expectedMovePercent,
                expectedTargetSource,
                breakoutRangeHigh,
                breakoutRangeLow,
                breakoutThresholdPrice,
                expectedTargetStructureExtensionUsed,
                expectedTargetAtrUsed,
                trend.ShortMaSlopePercent,
                trend.TrendStrengthPercent,
                projectionMode,
                projectedExtension,
                normalTrendQuality);

        StrategySignalResult BuyWithExpectedTarget(
            string reason,
            decimal score,
            BreakoutExpectedTargetContext expectedTarget,
            NormalTrendEntryQualityMetrics? normalTrendQuality = null) =>
            BuyWithContext(
                reason,
                score,
                expectedTarget.ExpectedTargetPrice,
                expectedTarget.ExpectedMovePercent,
                expectedTarget.ExpectedTargetSource,
                expectedTarget.BreakoutRangeHigh,
                expectedTarget.BreakoutRangeLow,
                expectedTarget.BreakoutThresholdPrice,
                expectedTarget.StructureExtensionUsed,
                expectedTarget.AtrUsed,
                expectedTarget.ProjectionMode,
                expectedTarget.ProjectedExtension,
                normalTrendQuality);

        if (allowStateMutation)
        {
            _marketStateTracker.Update(marketData.Symbol, trend.CurrentTrendState);
            _positionManager.UpdateTrend(marketData.Symbol, trend.CurrentTrendState);
        }

        var position = _positionManager.GetState(marketData.Symbol);
        if (position.IsInPosition)
        {
            if (allowStateMutation)
                PendingBreakouts.TryRemove(marketData.Symbol, out _);
            return Task.FromResult(EvaluatePositionManagement(marketData, trend, marketCondition, confidence, position, allowStateMutation));
        }

        var isLowVolatilityContext = trend.MarketRegime is MarketRegime.LowVolatility or MarketRegime.Ranging
                                     || marketCondition.Regime == VolatilityRegime.Low
                                     || trend.CurrentTrendState == TrendState.Neutral;
        var normalTrendFallbackUsed = false;
        string? lowVolBreakoutFailureReason = null;
        if (isLowVolatilityContext)
        {
            var breakoutEvaluation = EvaluateLowVolatilityBreakout(marketData, trend);
            PendingBreakouts.TryGetValue(marketData.Symbol, out var pendingBreakout);
            var confirmationEvaluation = EvaluateBreakoutConfirmation(marketData, trend, breakoutEvaluation, pendingBreakout);
            LogLowVolatilityBreakoutDiagnostics(marketData, trend, marketCondition, breakoutEvaluation, confirmationEvaluation, pendingBreakout, confidence);

            if (!_enableLowVolatilityBreakoutEntry)
            {
                if (allowStateMutation)
                    PendingBreakouts.TryRemove(marketData.Symbol, out _);
                const string disabledReason = "No entry signal - low-volatility market without breakout.";
                RecordLowVolatilityBreakoutRejection(
                    marketData.Symbol,
                    BreakoutRejectionCategory.ContextOrRegimeFail,
                    breakoutEvaluation,
                    marketData);
                LogEntryRejectionDiagnostics(
                    marketData,
                    trend,
                    marketCondition,
                    confidence,
                    disabledReason,
                    lowVolBreakoutPathEvaluated: true,
                    lowVolBreakoutPassed: breakoutEvaluation.Passed,
                    normalTrendEntryPathEvaluated: false,
                    normalTrendEntrySkippedReason: "Low-volatility breakout feature disabled.");
                return Task.FromResult(HoldWithContext(disabledReason, confidence));
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
                    LogEntryRejectionDiagnostics(
                        marketData,
                        trend,
                        marketCondition,
                        confidence,
                        waitNewClosedCandleReason,
                        lowVolBreakoutPathEvaluated: true,
                        lowVolBreakoutPassed: breakoutEvaluation.Passed,
                        normalTrendEntryPathEvaluated: false,
                        normalTrendEntrySkippedReason: "Waiting for newer completed candle in low-volatility breakout confirmation.");
                    return Task.FromResult(HoldWithContext(waitNewClosedCandleReason, confidence));
                }

                if (!confirmationEvaluation.Passed)
                {
                    if (allowStateMutation)
                        PendingBreakouts.TryRemove(marketData.Symbol, out _);
                    const string failedReason = "No entry signal - pending breakout failed confirmation.";
                    RecordLowVolatilityBreakoutRejection(
                        marketData.Symbol,
                        BreakoutRejectionCategory.PendingConfirmationFail,
                        breakoutEvaluation,
                        marketData);
                    LogEntryRejectionDiagnostics(
                        marketData,
                        trend,
                        marketCondition,
                        confidence,
                        failedReason,
                        lowVolBreakoutPathEvaluated: true,
                        lowVolBreakoutPassed: breakoutEvaluation.Passed,
                        normalTrendEntryPathEvaluated: false,
                        normalTrendEntrySkippedReason: "Pending low-volatility breakout confirmation failed.");
                    return Task.FromResult(HoldWithContext(failedReason, confidence));
                }

                var confirmedClosedCandleCount = pendingBreakout.ConfirmedClosedCandleCount + 1;
                if (_requireBreakoutConfirmation && confirmedClosedCandleCount < _breakoutConfirmationCandles)
                {
                    if (allowStateMutation)
                    {
                        PendingBreakouts[marketData.Symbol] = pendingBreakout with
                        {
                            LastConfirmationCandleTimeUtc = latestClosedCandleTimeUtc.Value,
                            ConfirmedClosedCandleCount = confirmedClosedCandleCount
                        };
                    }
                    const string waitReason = "No entry signal - breakout detected, waiting for new closed candle confirmation.";
                    LogEntryRejectionDiagnostics(
                        marketData,
                        trend,
                        marketCondition,
                        confidence,
                        waitReason,
                        lowVolBreakoutPathEvaluated: true,
                        lowVolBreakoutPassed: breakoutEvaluation.Passed,
                        normalTrendEntryPathEvaluated: false,
                        normalTrendEntrySkippedReason: "Pending low-volatility breakout confirmation requires additional completed candles.");
                    return Task.FromResult(HoldWithContext(waitReason, confidence));
                }

                if (allowStateMutation)
                {
                    PendingBreakouts.TryRemove(marketData.Symbol, out _);
                    MarkEntrySignal(marketData.Symbol);
                    _positionManager.Enter(marketData.Symbol, PositionType.Long, marketData.CurrentPrice, trend.CurrentTrendState, DateTime.UtcNow);
                }
                var expectedTarget = BuildLowVolBreakoutExpectedTarget(
                    marketData.CurrentPrice,
                    pendingBreakout.BreakoutThresholdPrice,
                    pendingBreakout.RecentRangeHigh,
                    pendingBreakout.RecentRangeLow);
                if (allowStateMutation)
                {
                    BreakoutEntryStates[marketData.Symbol] = new BreakoutEntryState(
                        pendingBreakout.BreakoutThresholdPrice,
                        DateTime.UtcNow);
                }
                return Task.FromResult(BuyWithExpectedTarget(
                    "Entry signal - low-volatility breakout confirmed after follow-through.",
                    confidence,
                    expectedTarget));
            }

            if (breakoutEvaluation.Passed)
            {
                if (_requireBreakoutConfirmation)
                {
                    var detectedClosedCandleTimeUtc = marketData.LatestClosedCandleCloseTimeUtc ?? DateTime.MinValue;
                    if (allowStateMutation)
                    {
                        PendingBreakouts[marketData.Symbol] = new PendingBreakoutState(
                            DateTime.UtcNow,
                            detectedClosedCandleTimeUtc,
                            detectedClosedCandleTimeUtc,
                            0,
                            breakoutEvaluation.BreakoutThresholdPrice,
                            breakoutEvaluation.RecentRangeHigh,
                            breakoutEvaluation.RecentRangeLow,
                            breakoutEvaluation.TrendStrengthCurrent);
                    }
                    const string waitingForConfirmationReason = "No entry signal - breakout detected, waiting for new closed candle confirmation.";
                    LogEntryRejectionDiagnostics(
                        marketData,
                        trend,
                        marketCondition,
                        confidence,
                        waitingForConfirmationReason,
                        lowVolBreakoutPathEvaluated: true,
                        lowVolBreakoutPassed: breakoutEvaluation.Passed,
                        normalTrendEntryPathEvaluated: false,
                        normalTrendEntrySkippedReason: "Pending low-volatility breakout confirmation created.");
                    return Task.FromResult(HoldWithContext(waitingForConfirmationReason, confidence));
                }

                if (allowStateMutation)
                {
                    MarkEntrySignal(marketData.Symbol);
                    _positionManager.Enter(marketData.Symbol, PositionType.Long, marketData.CurrentPrice, trend.CurrentTrendState, DateTime.UtcNow);
                }
                var expectedTarget = BuildLowVolBreakoutExpectedTarget(
                    marketData.CurrentPrice,
                    breakoutEvaluation.BreakoutThresholdPrice,
                    breakoutEvaluation.RecentRangeHigh,
                    breakoutEvaluation.RecentRangeLow);
                if (allowStateMutation)
                {
                    BreakoutEntryStates[marketData.Symbol] = new BreakoutEntryState(
                        breakoutEvaluation.BreakoutThresholdPrice,
                        DateTime.UtcNow);
                }
                return Task.FromResult(BuyWithExpectedTarget(
                    "Entry signal - low-volatility breakout confirmed.",
                    confidence,
                    expectedTarget));
            }

            var lowVolatilityReason = BuildLowVolatilityNoBreakoutReason(breakoutEvaluation);
            RecordLowVolatilityBreakoutRejection(
                marketData.Symbol,
                ClassifyBreakoutFailure(breakoutEvaluation.FailureReason),
                breakoutEvaluation,
                marketData);
            lowVolBreakoutFailureReason = breakoutEvaluation.FailureReason;

            if (_enableNormalTrendFallbackWhenLowVolBreakoutFails)
            {
                normalTrendFallbackUsed = true;
                _logger.LogInformation(
                    "MovingAverageTrendStrategy low-vol breakout failed; evaluating normal trend fallback path. Symbol={Symbol}, NormalTrendFallbackAfterFailedBreakoutEnabled={NormalTrendFallbackAfterFailedBreakoutEnabled}, NormalTrendFallbackUsed={NormalTrendFallbackUsed}, LowVolBreakoutFailureReason={LowVolBreakoutFailureReason}",
                    marketData.Symbol,
                    true,
                    true,
                    lowVolBreakoutFailureReason);
            }
            else
            {
            LogEntryRejectionDiagnostics(
                marketData,
                trend,
                marketCondition,
                confidence,
                lowVolatilityReason,
                lowVolBreakoutPathEvaluated: true,
                lowVolBreakoutPassed: breakoutEvaluation.Passed,
                normalTrendEntryPathEvaluated: false,
                normalTrendEntrySkippedReason: "Low-volatility breakout failed and returned hold.",
                normalTrendFallbackAfterFailedBreakoutEnabled: false,
                normalTrendFallbackUsed: false,
                lowVolBreakoutFailureReason: lowVolBreakoutFailureReason);
            return Task.FromResult(HoldWithContext(lowVolatilityReason, confidence));
            }
        }

        if (allowStateMutation)
            PendingBreakouts.TryRemove(marketData.Symbol, out _);

        if (trend.MarketRegime == MarketRegime.Ranging)
        {
            const string reason = "No entry signal - ranging market.";
            LogEntryRejectionDiagnostics(
                marketData,
                trend,
                marketCondition,
                confidence,
                reason,
                lowVolBreakoutPathEvaluated: false,
                lowVolBreakoutPassed: null,
                normalTrendEntryPathEvaluated: true,
                normalTrendEntrySkippedReason: reason,
                normalTrendFallbackAfterFailedBreakoutEnabled: _enableNormalTrendFallbackWhenLowVolBreakoutFails,
                normalTrendFallbackUsed: normalTrendFallbackUsed,
                lowVolBreakoutFailureReason: lowVolBreakoutFailureReason);
            return Task.FromResult(HoldWithContext(reason, confidence));
        }

        if (marketCondition.IsUnsafeVolatility)
        {
            const string reason = "No entry signal - unsafe volatility regime.";
            LogEntryRejectionDiagnostics(
                marketData,
                trend,
                marketCondition,
                confidence,
                reason,
                lowVolBreakoutPathEvaluated: false,
                lowVolBreakoutPassed: null,
                normalTrendEntryPathEvaluated: true,
                normalTrendEntrySkippedReason: reason,
                normalTrendFallbackAfterFailedBreakoutEnabled: _enableNormalTrendFallbackWhenLowVolBreakoutFails,
                normalTrendFallbackUsed: normalTrendFallbackUsed,
                lowVolBreakoutFailureReason: lowVolBreakoutFailureReason);
            return Task.FromResult(HoldWithContext(reason, confidence));
        }

        if (!marketCondition.AllowTrade)
        {
            LogEntryRejectionDiagnostics(
                marketData,
                trend,
                marketCondition,
                confidence,
                marketCondition.Reason,
                lowVolBreakoutPathEvaluated: false,
                lowVolBreakoutPassed: null,
                normalTrendEntryPathEvaluated: true,
                normalTrendEntrySkippedReason: marketCondition.Reason,
                normalTrendFallbackAfterFailedBreakoutEnabled: _enableNormalTrendFallbackWhenLowVolBreakoutFails,
                normalTrendFallbackUsed: normalTrendFallbackUsed,
                lowVolBreakoutFailureReason: lowVolBreakoutFailureReason);
            return Task.FromResult(HoldWithContext(marketCondition.Reason, confidence));
        }

        if (marketCondition.MarketConditionScore < _minimumMarketConditionScore)
        {
            const string reason = "No entry signal - market condition score too weak.";
            LogEntryRejectionDiagnostics(
                marketData,
                trend,
                marketCondition,
                confidence,
                reason,
                lowVolBreakoutPathEvaluated: false,
                lowVolBreakoutPassed: null,
                normalTrendEntryPathEvaluated: true,
                normalTrendEntrySkippedReason: reason,
                normalTrendFallbackAfterFailedBreakoutEnabled: _enableNormalTrendFallbackWhenLowVolBreakoutFails,
                normalTrendFallbackUsed: normalTrendFallbackUsed,
                lowVolBreakoutFailureReason: lowVolBreakoutFailureReason);
            return Task.FromResult(HoldWithContext(reason, confidence));
        }

        if (trend.ConfidenceScore < _minimumTrendConfidenceScore)
        {
            const string reason = "No entry signal - trend confidence too weak.";
            LogEntryRejectionDiagnostics(
                marketData,
                trend,
                marketCondition,
                confidence,
                reason,
                lowVolBreakoutPathEvaluated: false,
                lowVolBreakoutPassed: null,
                normalTrendEntryPathEvaluated: true,
                normalTrendEntrySkippedReason: reason,
                normalTrendFallbackAfterFailedBreakoutEnabled: _enableNormalTrendFallbackWhenLowVolBreakoutFails,
                normalTrendFallbackUsed: normalTrendFallbackUsed,
                lowVolBreakoutFailureReason: lowVolBreakoutFailureReason);
            return Task.FromResult(HoldWithContext(reason, confidence));
        }

        if (IsCooldownActiveForEntry(marketData.Symbol, out var remainingSeconds))
        {
            var reason = $"No entry signal - waiting for cooldown ({remainingSeconds}s remaining).";
            LogEntryRejectionDiagnostics(
                marketData,
                trend,
                marketCondition,
                confidence,
                reason,
                lowVolBreakoutPathEvaluated: false,
                lowVolBreakoutPassed: null,
                normalTrendEntryPathEvaluated: true,
                normalTrendEntrySkippedReason: reason,
                normalTrendFallbackAfterFailedBreakoutEnabled: _enableNormalTrendFallbackWhenLowVolBreakoutFails,
                normalTrendFallbackUsed: normalTrendFallbackUsed,
                lowVolBreakoutFailureReason: lowVolBreakoutFailureReason);
            return Task.FromResult(HoldWithContext(reason, confidence));
        }

        var canEnterLong = TryCanEnterLong(trend, out var canEnterLongFailureReason);
        if (canEnterLong)
        {
            var expectedTarget = BuildNormalTrendExpectedTarget(marketData, marketCondition);
            var normalTrendQuality = EvaluateNormalTrendEntryQuality(marketData, expectedTarget, trend.TrendStrengthPercent);
            LogNormalTrendEntryQualityDiagnostics(marketData.Symbol, normalTrendQuality, qualityFilterRejected: false);
            if (!TryPassNormalTrendEntryQualityFilters(
                    normalTrendQuality,
                    out var qualityRejectionReason,
                    out var rewardRiskRejected,
                    out var nearHighRejected))
            {
                var pullbackOverride = EvaluatePullbackContinuationOverride(
                    marketData,
                    trend,
                    normalTrendQuality,
                    qualityRejectionReason);
                var rejectedQuality = normalTrendQuality with
                {
                    EntryRejectedReason = qualityRejectionReason,
                    RewardRiskRejected = rewardRiskRejected,
                    NearHighRejected = nearHighRejected,
                    PullbackContinuationOverrideEvaluated = pullbackOverride.Evaluated,
                    PullbackContinuationOverrideAllowed = pullbackOverride.Allowed,
                    PullbackContinuationOverrideRejectedReason = pullbackOverride.RejectedReason,
                    CloseAboveShortAndLongMaForPullback = pullbackOverride.CloseAboveShortAndLongMaForPullback,
                    ShortSlopePositiveForPullback = pullbackOverride.ShortSlopePositiveForPullback
                };
                if (pullbackOverride.Allowed)
                {
                    LogNormalTrendEntryQualityDiagnostics(
                        marketData.Symbol,
                        rejectedQuality,
                        qualityFilterRejected: false);
                    if (allowStateMutation)
                    {
                        MarkEntrySignal(marketData.Symbol);
                        _positionManager.Enter(marketData.Symbol, PositionType.Long, marketData.CurrentPrice, trend.CurrentTrendState, DateTime.UtcNow);
                        BreakoutEntryStates.TryRemove(marketData.Symbol, out _);
                    }
                    var overrideReason = marketCondition.RequiresReducedPositionSize
                        ? "Entry signal - bullish pullback continuation override confirmed, but high volatility requires reduced position size."
                        : "Entry signal - bullish pullback continuation override confirmed.";
                    return Task.FromResult(BuyWithExpectedTarget(overrideReason, confidence, expectedTarget, rejectedQuality));
                }

                LogNormalTrendEntryQualityDiagnostics(
                    marketData.Symbol,
                    rejectedQuality,
                    qualityFilterRejected: true);
                LogEntryRejectionDiagnostics(
                    marketData,
                    trend,
                    marketCondition,
                    confidence,
                    qualityRejectionReason,
                    lowVolBreakoutPathEvaluated: false,
                    lowVolBreakoutPassed: null,
                    normalTrendEntryPathEvaluated: true,
                    normalTrendEntrySkippedReason: qualityRejectionReason,
                    canEnterLongResult: true,
                    canEnterLongFailureReason: null,
                    normalTrendFallbackAfterFailedBreakoutEnabled: _enableNormalTrendFallbackWhenLowVolBreakoutFails,
                    normalTrendFallbackUsed: normalTrendFallbackUsed,
                    lowVolBreakoutFailureReason: lowVolBreakoutFailureReason,
                    normalTrendQuality: rejectedQuality);
                return Task.FromResult(HoldWithContext(
                    qualityRejectionReason,
                    confidence,
                    rejectedQuality));
            }

            if (allowStateMutation)
            {
                MarkEntrySignal(marketData.Symbol);
                _positionManager.Enter(marketData.Symbol, PositionType.Long, marketData.CurrentPrice, trend.CurrentTrendState, DateTime.UtcNow);
                BreakoutEntryStates.TryRemove(marketData.Symbol, out _);
            }
            var reason = marketCondition.RequiresReducedPositionSize
                ? "Entry signal - bullish trend confirmed, but high volatility requires reduced position size."
                : "Entry signal - bullish trend confirmed.";
            // TODO: RiskManagementService should reduce quantity when reduced-position flag is present.
            return Task.FromResult(BuyWithExpectedTarget(reason, confidence, expectedTarget, normalTrendQuality));
        }

        if (CanEnterShort(trend))
        {
            if (IsSpotMode)
            {
                _logger.LogWarning("Ignoring short state transition because TradingMode is Spot.");
                return Task.FromResult(HoldWithContext("Spot bearish signal ignored because short selling is disabled and no long position is open.", confidence));
            }

            if (allowStateMutation)
            {
                MarkEntrySignal(marketData.Symbol);
                _positionManager.Enter(marketData.Symbol, PositionType.Short, marketData.CurrentPrice, trend.CurrentTrendState, DateTime.UtcNow);
                BreakoutEntryStates.TryRemove(marketData.Symbol, out _);
            }
            var reason = marketCondition.RequiresReducedPositionSize
                ? "Entry signal - bearish trend confirmed, but high volatility requires reduced position size."
                : "Entry signal - bearish trend confirmed.";
            // TODO: RiskManagementService should reduce quantity when reduced-position flag is present.
            return Task.FromResult(CreateSell(reason, confidence, trendConfidenceScore, marketConditionScore));
        }

        if (IsSpotMode && trend.IsBearishTrendConfirmed)
            return Task.FromResult(HoldWithContext("Spot bearish signal ignored because short selling is disabled and no long position is open.", confidence));

        const string waitingReason = "No entry signal - waiting for confirmed trend direction.";
        LogEntryRejectionDiagnostics(
            marketData,
            trend,
            marketCondition,
            confidence,
            waitingReason,
            lowVolBreakoutPathEvaluated: false,
            lowVolBreakoutPassed: null,
            normalTrendEntryPathEvaluated: true,
            normalTrendEntrySkippedReason: waitingReason,
            canEnterLongResult: canEnterLong,
            canEnterLongFailureReason: canEnterLongFailureReason,
            normalTrendFallbackAfterFailedBreakoutEnabled: _enableNormalTrendFallbackWhenLowVolBreakoutFails,
            normalTrendFallbackUsed: normalTrendFallbackUsed,
            lowVolBreakoutFailureReason: lowVolBreakoutFailureReason);
        return Task.FromResult(HoldWithContext(waitingReason, confidence));
    }

    private StrategySignalResult EvaluatePositionManagement(
        MarketSnapshot marketData,
        TrendAnalysisResult trend,
        MarketConditionResult marketCondition,
        decimal confidence,
        SymbolPositionState position,
        bool allowStateMutation)
    {
        if (position.PositionType == PositionType.Long)
        {
            var entryPrice = position.EntryPrice;
            var unrealizedPnlPercent = CalculateUnrealizedPnlPercent(entryPrice, marketData.CurrentPrice);
            var netAfterEstimatedCostPercent = unrealizedPnlPercent - _estimatedRoundTripCostPercent;
            var tradeAgeMinutes = position.EntryTimeUtc == DateTime.MinValue
                ? 0m
                : Math.Max(0m, (decimal)(DateTime.UtcNow - position.EntryTimeUtc).TotalMinutes);
            var bearishReversalConfirmed = trend.IsBearishTrendConfirmed || trend.IsBearishCrossover;
            var momentumWeakening = trend.ShortMaSlopePercent <= _momentumFlatteningThreshold;
            var breakoutThesisInvalidated = IsBreakoutThesisInvalidated(marketData.Symbol, marketData.CurrentPrice);
            var highVolatilityWarning = marketCondition.Regime == VolatilityRegime.High || marketCondition.RequiresReducedPositionSize;
            const bool stopLossHit = false;
            const bool takeProfitHit = false;
            const string trendReversalExitReason = "Bearish trend reversal detected for long position.";

            if (bearishReversalConfirmed)
            {
                LogExitCandidateDiagnostics(
                    marketData.Symbol,
                    entryPrice,
                    marketData.CurrentPrice,
                    unrealizedPnlPercent,
                    netAfterEstimatedCostPercent,
                    tradeAgeMinutes,
                    "TrendReversal",
                    momentumWeakening,
                    bearishReversalConfirmed,
                    trend.IsBearishCrossover,
                    stopLossHit,
                    takeProfitHit,
                    breakoutThesisInvalidated,
                    highVolatilityWarning,
                    exitAllowed: true,
                    exitBlockedReason: null,
                    exitAllowedReason: trendReversalExitReason,
                    reversalStrength: trend.TrendStrengthPercent);
                if (allowStateMutation)
                {
                    _positionManager.Exit(marketData.Symbol, trend.CurrentTrendState);
                    BreakoutEntryStates.TryRemove(marketData.Symbol, out _);
                }
                return CreateSell("Exit signal - trend reversal detected for long position.", confidence);
            }

            if (_exitOnWeakTrendConfidence && trend.ConfidenceScore < _minimumExitTrendConfidenceScore)
            {
                LogExitCandidateDiagnostics(
                    marketData.Symbol,
                    entryPrice,
                    marketData.CurrentPrice,
                    unrealizedPnlPercent,
                    netAfterEstimatedCostPercent,
                    tradeAgeMinutes,
                    "WeakTrendConfidence",
                    momentumWeakening,
                    bearishReversalConfirmed,
                    trend.IsBearishCrossover,
                    stopLossHit,
                    takeProfitHit,
                    breakoutThesisInvalidated,
                    highVolatilityWarning,
                    exitAllowed: true,
                    exitBlockedReason: null,
                    exitAllowedReason: "Trend confidence dropped below configured exit threshold.",
                    reversalStrength: null);
                if (allowStateMutation)
                {
                    _positionManager.Exit(marketData.Symbol, trend.CurrentTrendState);
                    BreakoutEntryStates.TryRemove(marketData.Symbol, out _);
                }
                return CreateSell("Exit signal - trend confidence weakened for long position.", confidence);
            }

            if (momentumWeakening)
            {
                var momentumDecision = EvaluateMomentumWeakeningExitDecision(
                    unrealizedPnlPercent,
                    netAfterEstimatedCostPercent,
                    tradeAgeMinutes,
                    bearishReversalConfirmed,
                    breakoutThesisInvalidated,
                    stopLossHit,
                    takeProfitHit);
                LogExitCandidateDiagnostics(
                    marketData.Symbol,
                    entryPrice,
                    marketData.CurrentPrice,
                    unrealizedPnlPercent,
                    netAfterEstimatedCostPercent,
                    tradeAgeMinutes,
                    "MomentumWeakening",
                    momentumWeakening,
                    bearishReversalConfirmed,
                    trend.IsBearishCrossover,
                    stopLossHit,
                    takeProfitHit,
                    breakoutThesisInvalidated,
                    highVolatilityWarning,
                    momentumDecision.ExitAllowed,
                    momentumDecision.ExitBlockedReason,
                    momentumDecision.ExitAllowed ? "Momentum weakening exit passed guard." : null,
                    reversalStrength: null);

                if (momentumDecision.ExitAllowed)
                {
                    if (allowStateMutation)
                    {
                        _positionManager.Exit(marketData.Symbol, trend.CurrentTrendState);
                        BreakoutEntryStates.TryRemove(marketData.Symbol, out _);
                    }
                    return CreateSell("Exit signal - long momentum is weakening.", confidence);
                }
            }

            LogExitCandidateDiagnostics(
                marketData.Symbol,
                entryPrice,
                marketData.CurrentPrice,
                unrealizedPnlPercent,
                netAfterEstimatedCostPercent,
                tradeAgeMinutes,
                "None",
                momentumWeakening,
                bearishReversalConfirmed,
                trend.IsBearishCrossover,
                stopLossHit,
                takeProfitHit,
                breakoutThesisInvalidated,
                highVolatilityWarning,
                exitAllowed: false,
                exitBlockedReason: "No active strategy exit signal.",
                exitAllowedReason: null,
                reversalStrength: null);
            return CreateHold("Holding long position - trend still bullish.", confidence);
        }

        if (position.PositionType == PositionType.Short)
        {
            if (IsSpotMode)
            {
                _logger.LogWarning("Ignoring short state transition because TradingMode is Spot.");
                if (allowStateMutation)
                {
                    _positionManager.Exit(marketData.Symbol, trend.CurrentTrendState);
                    BreakoutEntryStates.TryRemove(marketData.Symbol, out _);
                }
                return CreateHold("Spot short state ignored because short selling is disabled.", confidence);
            }

            if (trend.IsBullishTrendConfirmed || trend.IsBullishCrossover)
            {
                if (allowStateMutation)
                {
                    _positionManager.Exit(marketData.Symbol, trend.CurrentTrendState);
                    BreakoutEntryStates.TryRemove(marketData.Symbol, out _);
                }
                return CreateBuy("Exit signal - trend reversal detected for short position.", confidence);
            }

            if (_exitOnWeakTrendConfidence && trend.ConfidenceScore < _minimumExitTrendConfidenceScore)
            {
                if (allowStateMutation)
                {
                    _positionManager.Exit(marketData.Symbol, trend.CurrentTrendState);
                    BreakoutEntryStates.TryRemove(marketData.Symbol, out _);
                }
                return CreateBuy("Exit signal - trend confidence weakened for short position.", confidence);
            }

            if (trend.ShortMaSlopePercent >= -_momentumFlatteningThreshold)
            {
                if (allowStateMutation)
                {
                    _positionManager.Exit(marketData.Symbol, trend.CurrentTrendState);
                    BreakoutEntryStates.TryRemove(marketData.Symbol, out _);
                }
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

    private bool TryCanEnterLong(TrendAnalysisResult trend, out string? failureReason)
    {
        if (!trend.IsBullishTrendConfirmed)
        {
            failureReason = "Trend is not bullish-confirmed.";
            return false;
        }

        if (_requireCrossoverForEntry && !trend.IsBullishCrossover)
        {
            failureReason = "Bullish crossover is required but not present.";
            return false;
        }

        failureReason = null;
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

    private bool HasNormalTrendEntryQualityFiltersEnabled()
        => _enableNormalTrendBullishPersistenceFilter
           || _enableNormalTrendCloseAboveRecentHighFilter
           || _enableNormalTrendMinDistanceToInvalidationFilter
           || _enableNormalTrendRejectPreviousBearishCandleFilter
           || _enableNormalTrendRewardRiskFilter
           || _enableNormalTrendNearRecentHighRejection;

    private NormalTrendEntryQualityMetrics EvaluateNormalTrendEntryQuality(
        MarketSnapshot marketData,
        BreakoutExpectedTargetContext expectedTarget,
        decimal trendStrengthPercent)
    {
        var closes = marketData.ClosePrices;
        var confirmedClosedCount = closes.Count;
        if (_useConfirmedClosedCandlesForEntryQuality
            && marketData.LatestClosedCandleClosePrice.HasValue
            && closes.Count >= 2
            && closes[^1] != marketData.LatestClosedCandleClosePrice.Value)
        {
            confirmedClosedCount = closes.Count - 1;
        }

        var patternCloses = _useConfirmedClosedCandlesForEntryQuality
            ? closes.Take(Math.Max(0, confirmedClosedCount)).ToArray()
            : closes.ToArray();
        if (patternCloses.Length == 0)
            patternCloses = closes.ToArray();

        var entryQualityMode = _useConfirmedClosedCandlesForEntryQuality ? "ConfirmedClosed" : "CurrentSeries";
        var currentPrice = marketData.CurrentPrice > 0m
            ? marketData.CurrentPrice
            : closes.LastOrDefault();
        var rangeWindowCandleCount = Math.Max(
            1,
            Math.Min(
                patternCloses.Length,
                expectedTarget.RangeWindowCandleCount > 0
                    ? expectedTarget.RangeWindowCandleCount
                    : _normalTrendExpectedTargetLookbackCandles));

        decimal recentRangeHigh;
        decimal recentRangeLow;
        if (_useConfirmedClosedCandlesForEntryQuality)
        {
            var effectiveHighs = marketData.HighPrices.Take(patternCloses.Length).ToArray();
            var effectiveLows = marketData.LowPrices.Take(patternCloses.Length).ToArray();
            var rangeHighSeries = effectiveHighs.Length >= rangeWindowCandleCount
                ? effectiveHighs.TakeLast(rangeWindowCandleCount).ToArray()
                : patternCloses.TakeLast(rangeWindowCandleCount).ToArray();
            var rangeLowSeries = effectiveLows.Length >= rangeWindowCandleCount
                ? effectiveLows.TakeLast(rangeWindowCandleCount).ToArray()
                : patternCloses.TakeLast(rangeWindowCandleCount).ToArray();
            recentRangeHigh = rangeHighSeries.Length > 0 ? rangeHighSeries.Max() : expectedTarget.BreakoutRangeHigh;
            recentRangeLow = rangeLowSeries.Length > 0 ? rangeLowSeries.Min() : expectedTarget.BreakoutRangeLow;
        }
        else
        {
            recentRangeHigh = expectedTarget.BreakoutRangeHigh;
            recentRangeLow = expectedTarget.BreakoutRangeLow;
        }

        var consecutiveBullishTrendCandles = 0;
        if (patternCloses.Length >= 2)
        {
            for (var i = patternCloses.Length - 1; i >= 1; i--)
            {
                if (patternCloses[i] <= patternCloses[i - 1])
                    break;

                consecutiveBullishTrendCandles++;
            }
        }

        var latestClose = patternCloses.Length > 0 ? patternCloses[^1] : currentPrice;
        var previousCandleBearish = patternCloses.Length >= 2 && patternCloses[^1] < patternCloses[^2];
        var distanceToRecentHighPercent = currentPrice > 0m && recentRangeHigh > 0m
            ? Math.Max(0m, ((recentRangeHigh - currentPrice) / currentPrice) * 100m)
            : 0m;
        var distanceToInvalidationPercent = currentPrice > 0m
            ? Math.Max(0m, ((currentPrice - recentRangeLow) / currentPrice) * 100m)
            : 0m;
        decimal? expectedRewardRisk = expectedTarget.ExpectedMovePercent is > 0m
                                      && distanceToInvalidationPercent > 0m
            ? expectedTarget.ExpectedMovePercent.Value / distanceToInvalidationPercent
            : null;
        var entryNearRecentHigh = distanceToRecentHighPercent <= _normalTrendNearRecentHighPercent;
        var currentCloseAboveRecentHigh = latestClose > recentRangeHigh;
        var latestCloseFromConfirmedClosedCandle = marketData.LatestClosedCandleClosePrice.HasValue
                                                   && latestClose == marketData.LatestClosedCandleClosePrice.Value;
        var latestClosePotentiallyInProgressCandle = marketData.LatestClosedCandleClosePrice.HasValue
                                                     && !latestCloseFromConfirmedClosedCandle;
        var entryQualityLatestCloseTimeUtc = _useConfirmedClosedCandlesForEntryQuality
            ? marketData.LatestClosedCandleCloseTimeUtc
            : (latestCloseFromConfirmedClosedCandle ? marketData.LatestClosedCandleCloseTimeUtc : null);

        return new NormalTrendEntryQualityMetrics(
            ConsecutiveBullishTrendCandles: consecutiveBullishTrendCandles,
            EntryNearRecentHigh: entryNearRecentHigh,
            DistanceToRecentHighPercent: distanceToRecentHighPercent,
            DistanceToInvalidationPercent: distanceToInvalidationPercent,
            ExpectedRewardRisk: expectedRewardRisk,
            TrendStrengthPercent: trendStrengthPercent,
            CurrentCloseAboveRecentHigh: currentCloseAboveRecentHigh,
            PreviousCandleBearish: previousCandleBearish,
            RewardRiskRejected: false,
            NearHighRejected: false,
            EntryRejectedReason: null,
            UseConfirmedClosedCandlesForEntryQuality: _useConfirmedClosedCandlesForEntryQuality,
            EntryQualityCandleMode: entryQualityMode,
            EntryQualityLatestClose: latestClose,
            EntryQualityLatestCloseTimeUtc: entryQualityLatestCloseTimeUtc,
            EntryQualityRangeHigh: recentRangeHigh,
            EntryQualityRangeLow: recentRangeLow,
            EntryQualityRangeWindowCandleCount: rangeWindowCandleCount,
            LatestCloseFromConfirmedClosedCandle: latestCloseFromConfirmedClosedCandle,
            LatestClosePotentiallyInProgressCandle: latestClosePotentiallyInProgressCandle,
            RangeWindowCandleCount: rangeWindowCandleCount,
            RangeRecentHigh: recentRangeHigh,
            RangeRecentLow: recentRangeLow,
            LatestClosedCandleCloseTimeUtc: marketData.LatestClosedCandleCloseTimeUtc,
            PullbackContinuationOverrideEvaluated: false,
            PullbackContinuationOverrideAllowed: false,
            PullbackContinuationOverrideRejectedReason: null,
            CloseAboveShortAndLongMaForPullback: null,
            ShortSlopePositiveForPullback: null);
    }

    private bool TryPassNormalTrendEntryQualityFilters(
        NormalTrendEntryQualityMetrics metrics,
        out string rejectionReason,
        out bool rewardRiskRejected,
        out bool nearHighRejected)
    {
        rejectionReason = string.Empty;
        rewardRiskRejected = false;
        nearHighRejected = false;
        if (!HasNormalTrendEntryQualityFiltersEnabled())
            return true;

        if (_enableNormalTrendBullishPersistenceFilter
            && metrics.ConsecutiveBullishTrendCandles < _normalTrendMinBullishPersistenceCandles)
        {
            rejectionReason =
                $"No entry signal - normal trend requires {_normalTrendMinBullishPersistenceCandles} consecutive bullish closes (have {metrics.ConsecutiveBullishTrendCandles}).";
            return false;
        }

        if (_enableNormalTrendCloseAboveRecentHighFilter && !metrics.CurrentCloseAboveRecentHigh)
        {
            rejectionReason = "No entry signal - normal trend requires close above recent range high.";
            return false;
        }

        if (_enableNormalTrendMinDistanceToInvalidationFilter
            && metrics.DistanceToInvalidationPercent < _normalTrendMinDistanceToInvalidationPercent)
        {
            rejectionReason =
                $"No entry signal - normal trend distance to invalidation {metrics.DistanceToInvalidationPercent:F4}% is below minimum {_normalTrendMinDistanceToInvalidationPercent:F4}%.";
            return false;
        }

        if (_enableNormalTrendRejectPreviousBearishCandleFilter && metrics.PreviousCandleBearish)
        {
            rejectionReason = "No entry signal - normal trend rejects immediate bearish closed candle before entry.";
            return false;
        }

        if (_enableNormalTrendRewardRiskFilter)
        {
            if (!metrics.ExpectedRewardRisk.HasValue || metrics.ExpectedRewardRisk.Value < _normalTrendMinExpectedRewardRisk)
            {
                rewardRiskRejected = true;
                var actual = metrics.ExpectedRewardRisk.HasValue ? $"{metrics.ExpectedRewardRisk.Value:F4}" : "n/a";
                rejectionReason =
                    $"No entry signal - normal trend expected reward:risk {actual} is below minimum {_normalTrendMinExpectedRewardRisk:F4}.";
                return false;
            }
        }

        if (_enableNormalTrendNearRecentHighRejection && metrics.EntryNearRecentHigh)
        {
            var rewardRiskPasses = metrics.ExpectedRewardRisk.HasValue
                                   && metrics.ExpectedRewardRisk.Value >= _normalTrendNearRecentHighRequiresRewardRisk;
            var trendStrengthPasses = !_normalTrendNearRecentHighRequiresTrendStrengthPercent.HasValue
                                      || metrics.TrendStrengthPercent >= _normalTrendNearRecentHighRequiresTrendStrengthPercent.Value;
            if (!rewardRiskPasses || !trendStrengthPasses)
            {
                nearHighRejected = true;
                var rr = metrics.ExpectedRewardRisk.HasValue ? $"{metrics.ExpectedRewardRisk.Value:F4}" : "n/a";
                var trendStrengthRequirementText = _normalTrendNearRecentHighRequiresTrendStrengthPercent.HasValue
                    ? _normalTrendNearRecentHighRequiresTrendStrengthPercent.Value.ToString("F6")
                    : "n/a";
                rejectionReason =
                    $"No entry signal - normal trend near-recent-high entry requires reward:risk >= {_normalTrendNearRecentHighRequiresRewardRisk:F4} and trend strength >= {trendStrengthRequirementText}. Actual reward:risk={rr}, trend strength={metrics.TrendStrengthPercent:F6}.";
                return false;
            }
        }

        return true;
    }

    private PullbackContinuationOverrideEvaluation EvaluatePullbackContinuationOverride(
        MarketSnapshot marketData,
        TrendAnalysisResult trend,
        NormalTrendEntryQualityMetrics metrics,
        string qualityRejectionReason)
    {
        const string closeAboveRecentHighReason = "No entry signal - normal trend requires close above recent range high.";
        if (!_enableNormalTrendPullbackContinuationOverride || !IsSpotMode)
        {
            return new PullbackContinuationOverrideEvaluation(
                false,
                false,
                "Pullback continuation override disabled.",
                null,
                null);
        }

        if (!trend.IsBullishTrendConfirmed)
        {
            return new PullbackContinuationOverrideEvaluation(
                true,
                false,
                "Trend is not bullish-confirmed.",
                null,
                null);
        }

        if (!string.Equals(qualityRejectionReason, closeAboveRecentHighReason, StringComparison.Ordinal))
        {
            return new PullbackContinuationOverrideEvaluation(
                true,
                false,
                $"Primary rejection is not close-above-recent-high. Reason={qualityRejectionReason}",
                null,
                null);
        }

        var closes = marketData.ClosePrices;
        var confirmedClosedCount = closes.Count;
        if (marketData.LatestClosedCandleClosePrice.HasValue
            && closes.Count >= 2
            && closes[^1] != marketData.LatestClosedCandleClosePrice.Value)
        {
            confirmedClosedCount = closes.Count - 1;
        }

        var confirmedCloses = closes.Take(Math.Max(0, confirmedClosedCount)).ToArray();
        if (confirmedCloses.Length == 0)
            confirmedCloses = closes.ToArray();

        var latestConfirmedClose = confirmedCloses.LastOrDefault();
        var closeAboveShortAndLongMa = latestConfirmedClose > trend.CurrentShortMa && latestConfirmedClose > trend.CurrentLongMa;
        var shortSlopePositive = trend.ShortMaSlopePercent > 0m;
        var previousConfirmedCandleBearish = confirmedCloses.Length >= 2
                                             && confirmedCloses[^1] < confirmedCloses[^2];

        if (_normalTrendPullbackRequireCloseAboveShortAndLongMa && !closeAboveShortAndLongMa)
        {
            return new PullbackContinuationOverrideEvaluation(
                true,
                false,
                "Pullback continuation requires latest confirmed close above short and long moving averages.",
                closeAboveShortAndLongMa,
                shortSlopePositive);
        }

        if (_normalTrendPullbackRequirePositiveShortSlope && !shortSlopePositive)
        {
            return new PullbackContinuationOverrideEvaluation(
                true,
                false,
                "Pullback continuation requires positive short moving-average slope.",
                closeAboveShortAndLongMa,
                shortSlopePositive);
        }

        if (_normalTrendPullbackRejectPreviousBearishCandle && previousConfirmedCandleBearish)
        {
            return new PullbackContinuationOverrideEvaluation(
                true,
                false,
                "Pullback continuation rejects immediate bearish confirmed closed candle before entry.",
                closeAboveShortAndLongMa,
                shortSlopePositive);
        }

        if (metrics.DistanceToInvalidationPercent < _normalTrendMinDistanceToInvalidationPercent)
        {
            return new PullbackContinuationOverrideEvaluation(
                true,
                false,
                $"Pullback continuation distance to invalidation {metrics.DistanceToInvalidationPercent:F4}% is below minimum {_normalTrendMinDistanceToInvalidationPercent:F4}%.",
                closeAboveShortAndLongMa,
                shortSlopePositive);
        }

        if (!metrics.ExpectedRewardRisk.HasValue
            || metrics.ExpectedRewardRisk.Value < _normalTrendPullbackMinExpectedRewardRisk)
        {
            var actual = metrics.ExpectedRewardRisk.HasValue ? $"{metrics.ExpectedRewardRisk.Value:F4}" : "n/a";
            return new PullbackContinuationOverrideEvaluation(
                true,
                false,
                $"Pullback continuation expected reward:risk {actual} is below minimum {_normalTrendPullbackMinExpectedRewardRisk:F4}.",
                closeAboveShortAndLongMa,
                shortSlopePositive);
        }

        return new PullbackContinuationOverrideEvaluation(
            true,
            true,
            null,
            closeAboveShortAndLongMa,
            shortSlopePositive);
    }

    private void LogNormalTrendEntryQualityDiagnostics(
        TradingSymbol symbol,
        NormalTrendEntryQualityMetrics metrics,
        bool qualityFilterRejected)
    {
        _logger.LogInformation(
            "MovingAverageTrendStrategy normal trend entry quality: Symbol={Symbol}, ConsecutiveBullishTrendCandles={ConsecutiveBullishTrendCandles}, EntryNearRecentHigh={EntryNearRecentHigh}, DistanceToRecentHighPercent={DistanceToRecentHighPercent}, DistanceToInvalidationPercent={DistanceToInvalidationPercent}, ExpectedRewardRisk={ExpectedRewardRisk}, TrendStrengthPercent={TrendStrengthPercent}, CurrentCloseAboveRecentHigh={CurrentCloseAboveRecentHigh}, PreviousCandleBearish={PreviousCandleBearish}, RewardRiskRejected={RewardRiskRejected}, NearHighRejected={NearHighRejected}, EntryRejectedReason={EntryRejectedReason}, UseConfirmedClosedCandlesForEntryQuality={UseConfirmedClosedCandlesForEntryQuality}, EntryQualityCandleMode={EntryQualityCandleMode}, EntryQualityLatestClose={EntryQualityLatestClose}, EntryQualityLatestCloseTimeUtc={EntryQualityLatestCloseTimeUtc}, EntryQualityRangeHigh={EntryQualityRangeHigh}, EntryQualityRangeLow={EntryQualityRangeLow}, EntryQualityRangeWindowCandleCount={EntryQualityRangeWindowCandleCount}, LatestCloseFromConfirmedClosedCandle={LatestCloseFromConfirmedClosedCandle}, LatestClosePotentiallyInProgressCandle={LatestClosePotentiallyInProgressCandle}, RangeWindowCandleCount={RangeWindowCandleCount}, RangeRecentHigh={RangeRecentHigh}, RangeRecentLow={RangeRecentLow}, LatestClosedCandleCloseTimeUtc={LatestClosedCandleCloseTimeUtc}, PullbackContinuationOverrideEvaluated={PullbackContinuationOverrideEvaluated}, PullbackContinuationOverrideAllowed={PullbackContinuationOverrideAllowed}, PullbackContinuationOverrideRejectedReason={PullbackContinuationOverrideRejectedReason}, CloseAboveShortAndLongMaForPullback={CloseAboveShortAndLongMaForPullback}, ShortSlopePositiveForPullback={ShortSlopePositiveForPullback}, QualityFilterRejected={QualityFilterRejected}, EnableNormalTrendPullbackContinuationOverride={EnableNormalTrendPullbackContinuationOverride}, NormalTrendPullbackMinExpectedRewardRisk={NormalTrendPullbackMinExpectedRewardRisk}, EnableNormalTrendBullishPersistenceFilter={EnableNormalTrendBullishPersistenceFilter}, NormalTrendMinBullishPersistenceCandles={NormalTrendMinBullishPersistenceCandles}, EnableNormalTrendCloseAboveRecentHighFilter={EnableNormalTrendCloseAboveRecentHighFilter}, EnableNormalTrendMinDistanceToInvalidationFilter={EnableNormalTrendMinDistanceToInvalidationFilter}, NormalTrendMinDistanceToInvalidationPercent={NormalTrendMinDistanceToInvalidationPercent}, EnableNormalTrendRejectPreviousBearishCandleFilter={EnableNormalTrendRejectPreviousBearishCandleFilter}, EnableNormalTrendRewardRiskFilter={EnableNormalTrendRewardRiskFilter}, NormalTrendMinExpectedRewardRisk={NormalTrendMinExpectedRewardRisk}, EnableNormalTrendNearRecentHighRejection={EnableNormalTrendNearRecentHighRejection}, NormalTrendNearRecentHighRequiresRewardRisk={NormalTrendNearRecentHighRequiresRewardRisk}, NormalTrendNearRecentHighRequiresTrendStrengthPercent={NormalTrendNearRecentHighRequiresTrendStrengthPercent}, NormalTrendNearRecentHighPercent={NormalTrendNearRecentHighPercent}",
            symbol,
            metrics.ConsecutiveBullishTrendCandles,
            metrics.EntryNearRecentHigh,
            metrics.DistanceToRecentHighPercent,
            metrics.DistanceToInvalidationPercent,
            metrics.ExpectedRewardRisk,
            metrics.TrendStrengthPercent,
            metrics.CurrentCloseAboveRecentHigh,
            metrics.PreviousCandleBearish,
            metrics.RewardRiskRejected,
            metrics.NearHighRejected,
            metrics.EntryRejectedReason,
            metrics.UseConfirmedClosedCandlesForEntryQuality,
            metrics.EntryQualityCandleMode,
            metrics.EntryQualityLatestClose,
            metrics.EntryQualityLatestCloseTimeUtc,
            metrics.EntryQualityRangeHigh,
            metrics.EntryQualityRangeLow,
            metrics.EntryQualityRangeWindowCandleCount,
            metrics.LatestCloseFromConfirmedClosedCandle,
            metrics.LatestClosePotentiallyInProgressCandle,
            metrics.RangeWindowCandleCount,
            metrics.RangeRecentHigh,
            metrics.RangeRecentLow,
            metrics.LatestClosedCandleCloseTimeUtc,
            metrics.PullbackContinuationOverrideEvaluated,
            metrics.PullbackContinuationOverrideAllowed,
            metrics.PullbackContinuationOverrideRejectedReason,
            metrics.CloseAboveShortAndLongMaForPullback,
            metrics.ShortSlopePositiveForPullback,
            qualityFilterRejected,
            _enableNormalTrendPullbackContinuationOverride,
            _normalTrendPullbackMinExpectedRewardRisk,
            _enableNormalTrendBullishPersistenceFilter,
            _normalTrendMinBullishPersistenceCandles,
            _enableNormalTrendCloseAboveRecentHighFilter,
            _enableNormalTrendMinDistanceToInvalidationFilter,
            _normalTrendMinDistanceToInvalidationPercent,
            _enableNormalTrendRejectPreviousBearishCandleFilter,
            _enableNormalTrendRewardRiskFilter,
            _normalTrendMinExpectedRewardRisk,
            _enableNormalTrendNearRecentHighRejection,
            _normalTrendNearRecentHighRequiresRewardRisk,
            _normalTrendNearRecentHighRequiresTrendStrengthPercent,
            _normalTrendNearRecentHighPercent);
    }

    private static decimal CalculateCombinedConfidence(int trendScore, int marketScore)
    {
        var combinedScore = (trendScore * TrendWeight) + (marketScore * MarketWeight);
        return Math.Clamp(combinedScore / 100m, 0m, 1m);
    }

    private static decimal CalculateUnrealizedPnlPercent(decimal entryPrice, decimal currentPrice)
    {
        if (entryPrice <= 0m)
            return 0m;

        return ((currentPrice - entryPrice) / entryPrice) * 100m;
    }

    private bool IsBreakoutThesisInvalidated(TradingSymbol symbol, decimal currentPrice)
    {
        if (!BreakoutEntryStates.TryGetValue(symbol, out var breakoutState))
            return false;

        return currentPrice <= breakoutState.BreakoutThresholdPrice;
    }

    private MomentumExitDecision EvaluateMomentumWeakeningExitDecision(
        decimal unrealizedPnlPercent,
        decimal netAfterEstimatedCostPercent,
        decimal tradeAgeMinutes,
        bool bearishReversalConfirmed,
        bool breakoutThesisInvalidated,
        bool stopLossHit,
        bool takeProfitHit)
    {
        if (!_enableNetAwareMomentumExit)
            return new MomentumExitDecision(true, null);

        if (stopLossHit || takeProfitHit || bearishReversalConfirmed || breakoutThesisInvalidated)
            return new MomentumExitDecision(true, null);

        var withinDevelopmentWindow = tradeAgeMinutes < _momentumExitMinTradeAgeMinutes;
        var isTinyLoss = unrealizedPnlPercent > _momentumExitAllowIfUnrealizedLossPercentBelow;
        if (withinDevelopmentWindow && isTinyLoss)
        {
            return new MomentumExitDecision(
                false,
                $"Trade age {tradeAgeMinutes:F2}m is below development window {_momentumExitMinTradeAgeMinutes}m while loss {unrealizedPnlPercent:F4}% is above allowed cut {_momentumExitAllowIfUnrealizedLossPercentBelow:F4}%.");
        }

        var netBelowFloor = netAfterEstimatedCostPercent < _momentumExitMinNetProfitPercent;
        if (_momentumExitRequireBearishConfirmationWhenFeeNegative && netBelowFloor && !bearishReversalConfirmed)
        {
            return new MomentumExitDecision(
                false,
                $"Net after estimated cost {netAfterEstimatedCostPercent:F4}% is below floor {_momentumExitMinNetProfitPercent:F4}% without bearish confirmation.");
        }

        return new MomentumExitDecision(true, null);
    }

    private void LogExitCandidateDiagnostics(
        TradingSymbol symbol,
        decimal entryPrice,
        decimal currentPrice,
        decimal unrealizedPnlPercent,
        decimal netAfterEstimatedCostPercent,
        decimal tradeAgeMinutes,
        string exitSignalType,
        bool momentumWeakening,
        bool bearishReversalConfirmed,
        bool bearishCrossover,
        bool stopLossHit,
        bool takeProfitHit,
        bool breakoutThesisInvalidated,
        bool highVolatilityWarning,
        bool exitAllowed,
        string? exitBlockedReason,
        string? exitAllowedReason,
        decimal? reversalStrength)
    {
        _logger.LogInformation(
            "MovingAverageTrendStrategy exit candidate: Symbol={Symbol}, EntryPrice={EntryPrice}, CurrentPrice={CurrentPrice}, UnrealizedPnLPercent={UnrealizedPnLPercent}, EstimatedRoundTripCostPercent={EstimatedRoundTripCostPercent}, NetAfterEstimatedCostPercent={NetAfterEstimatedCostPercent}, EstimatedNetAfterCostPercent={EstimatedNetAfterCostPercent}, TradeAgeMinutes={TradeAgeMinutes}, ExitSignalType={ExitSignalType}, ExitType={ExitType}, MomentumWeakening={MomentumWeakening}, ReversalStrength={ReversalStrength}, BearishReversalConfirmed={BearishReversalConfirmed}, BearishCrossover={BearishCrossover}, StopLossHit={StopLossHit}, TakeProfitHit={TakeProfitHit}, BreakoutThesisInvalidated={BreakoutThesisInvalidated}, HighVolatilityWarning={HighVolatilityWarning}, ExitAllowed={ExitAllowed}, ExitAllowedReason={ExitAllowedReason}, ExitBlockedReason={ExitBlockedReason}",
            symbol,
            entryPrice,
            currentPrice,
            unrealizedPnlPercent,
            _estimatedRoundTripCostPercent,
            netAfterEstimatedCostPercent,
            netAfterEstimatedCostPercent,
            tradeAgeMinutes,
            exitSignalType,
            exitSignalType,
            momentumWeakening,
            reversalStrength,
            bearishReversalConfirmed,
            bearishCrossover,
            stopLossHit,
            takeProfitHit,
            breakoutThesisInvalidated,
            highVolatilityWarning,
            exitAllowed,
            exitAllowedReason,
            exitBlockedReason);
    }

    private LowVolatilityBreakoutEvaluation EvaluateLowVolatilityBreakout(
        MarketSnapshot marketData,
        TrendAnalysisResult trend)
    {
        var closePrices = marketData.ClosePrices;
        var confirmedClosedCount = closePrices.Count;
        if (_useConfirmedClosedCandlesForLowVolBreakout
            && marketData.LatestClosedCandleClosePrice.HasValue
            && closePrices.Count >= 2
            && closePrices[^1] != marketData.LatestClosedCandleClosePrice.Value)
        {
            confirmedClosedCount = closePrices.Count - 1;
        }

        var patternCloses = _useConfirmedClosedCandlesForLowVolBreakout
            ? closePrices.Take(Math.Max(0, confirmedClosedCount)).ToArray()
            : closePrices.ToArray();
        if (patternCloses.Length == 0)
            patternCloses = closePrices.ToArray();

        var breakoutCandleMode = _useConfirmedClosedCandlesForLowVolBreakout ? "ConfirmedClosed" : "CurrentSeries";
        var latestCloseTimeUtc = _useConfirmedClosedCandlesForLowVolBreakout
            ? marketData.LatestClosedCandleCloseTimeUtc
            : null;
        if (patternCloses.Length < _breakoutLookbackCandles + 1)
        {
            var provisionalLatestClose = patternCloses.LastOrDefault();
            var provisionalLatestCloseFromConfirmedClosedCandle = marketData.LatestClosedCandleClosePrice.HasValue
                                                                  && provisionalLatestClose == marketData.LatestClosedCandleClosePrice.Value;
            return new LowVolatilityBreakoutEvaluation(
                false,
                0m,
                0m,
                marketData.CurrentPrice > 0m ? marketData.CurrentPrice : closePrices.LastOrDefault(),
                provisionalLatestClose,
                closePrices.Count >= 2 ? closePrices[^2] : null,
                provisionalLatestCloseFromConfirmedClosedCandle,
                marketData.LatestClosedCandleClosePrice.HasValue && !provisionalLatestCloseFromConfirmedClosedCandle,
                closePrices.Count,
                0m,
                trend.ShortMaSlopePercent,
                trend.TrendStrengthPercent,
                null,
                false,
                false,
                false,
                false,
                false,
                _useConfirmedClosedCandlesForLowVolBreakout,
                breakoutCandleMode,
                latestCloseTimeUtc,
                "Insufficient closes for breakout lookback window.");
        }

        var window = patternCloses.TakeLast(_breakoutLookbackCandles + 1).ToArray();
        var latestClose = window[^1];
        var previousClose = patternCloses.Length >= 2 ? patternCloses[^2] : (decimal?)null;
        var rangeHigh = window.Take(_breakoutLookbackCandles).Max();
        var lowSeries = _useConfirmedClosedCandlesForLowVolBreakout
            ? marketData.LowPrices.Take(patternCloses.Length).ToArray()
            : marketData.LowPrices.ToArray();
        var rangeLow = lowSeries.Length >= _breakoutLookbackCandles + 1
            ? lowSeries.TakeLast(_breakoutLookbackCandles + 1).Take(_breakoutLookbackCandles).Min()
            : window.Take(_breakoutLookbackCandles).Min();
        var referencePrice = marketData.CurrentPrice > 0m ? marketData.CurrentPrice : latestClose;
        var latestCloseFromConfirmedClosedCandle = marketData.LatestClosedCandleClosePrice.HasValue
                                                   && latestClose == marketData.LatestClosedCandleClosePrice.Value;
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
            rangeLow,
            referencePrice,
            latestClose,
            previousClose,
            latestCloseFromConfirmedClosedCandle,
            marketData.LatestClosedCandleClosePrice.HasValue && !latestCloseFromConfirmedClosedCandle,
            _breakoutLookbackCandles,
            breakoutThresholdPrice,
            trend.ShortMaSlopePercent,
            trend.TrendStrengthPercent,
            previousTrendStrengthPercent,
            isPriceBreakout,
            isSlopeAboveMinimum,
            hasPositiveSlope,
            closeAboveMovingAverages,
            hasTrendStrengthExpansion,
            _useConfirmedClosedCandlesForLowVolBreakout,
            breakoutCandleMode,
            latestCloseTimeUtc,
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

    private string BuildLowVolatilityNoBreakoutReason(LowVolatilityBreakoutEvaluation breakout)
    {
        return
            $"No entry signal - low-volatility market without breakout. BreakoutFailure={breakout.FailureReason} " +
            $"CurrentPrice={breakout.CurrentPrice}, BreakoutThresholdPrice={breakout.BreakoutThresholdPrice}, RecentRangeHigh={breakout.RecentRangeHigh}, " +
            $"ShortMaSlopePercent={breakout.ShortMaSlopePercent}, MinBreakoutSlopePercent={_minBreakoutSlopePercent}, BreakoutBufferPercent={_breakoutBufferPercent}.";
    }

    private BreakoutRejectionCategory ClassifyBreakoutFailure(string failureReason)
    {
        if (failureReason.Contains("Price did not break above buffered range high.", StringComparison.OrdinalIgnoreCase))
            return BreakoutRejectionCategory.PriceBreakoutFail;
        if (failureReason.Contains("Short MA slope below minimum breakout slope.", StringComparison.OrdinalIgnoreCase)
            || failureReason.Contains("Short MA slope is not positive.", StringComparison.OrdinalIgnoreCase))
            return BreakoutRejectionCategory.SlopeFail;
        if (failureReason.Contains("Trend strength did not expand.", StringComparison.OrdinalIgnoreCase))
            return BreakoutRejectionCategory.TrendStrengthExpansionFail;
        if (failureReason.Contains("Latest close is not above both moving averages.", StringComparison.OrdinalIgnoreCase))
            return BreakoutRejectionCategory.CloseAboveMaFail;
        if (failureReason.Contains("Insufficient closes for breakout lookback window.", StringComparison.OrdinalIgnoreCase))
            return BreakoutRejectionCategory.ContextOrRegimeFail;
        return BreakoutRejectionCategory.OtherOrUnknown;
    }

    private void RecordLowVolatilityBreakoutRejection(
        TradingSymbol symbol,
        BreakoutRejectionCategory category,
        LowVolatilityBreakoutEvaluation breakout,
        MarketSnapshot marketData)
    {
        var nowUtc = DateTime.UtcNow;
        var aggregation = BreakoutRejectionWindows.GetOrAdd(symbol, _ => new BreakoutRejectionAggregationWindow(nowUtc));
        BreakoutRejectionAggregationLogSnapshot? snapshotToLog = null;

        lock (aggregation.Sync)
        {
            aggregation.TotalChecks++;
            aggregation.Counts[category] = aggregation.Counts.TryGetValue(category, out var current)
                ? current + 1
                : 1;
            IncrementPredicateFailureCounters(aggregation, category, breakout);

            aggregation.CurrentPriceSum += breakout.CurrentPrice;
            aggregation.BreakoutThresholdPriceSum += breakout.BreakoutThresholdPrice;
            aggregation.RecentRangeHighSum += breakout.RecentRangeHigh;
            aggregation.ShortMaSlopePercentSum += breakout.ShortMaSlopePercent;

            if (nowUtc - aggregation.WindowStartUtc >= _breakoutRejectionAggregationInterval)
            {
                snapshotToLog = new BreakoutRejectionAggregationLogSnapshot(
                    aggregation.WindowStartUtc,
                    nowUtc,
                    aggregation.TotalChecks,
                    new Dictionary<BreakoutRejectionCategory, int>(aggregation.Counts),
                    new Dictionary<BreakoutPredicateFailureCounter, int>(aggregation.PredicateFailureCounts),
                    aggregation.CurrentPriceSum / Math.Max(1, aggregation.TotalChecks),
                    aggregation.BreakoutThresholdPriceSum / Math.Max(1, aggregation.TotalChecks),
                    aggregation.RecentRangeHighSum / Math.Max(1, aggregation.TotalChecks),
                    aggregation.ShortMaSlopePercentSum / Math.Max(1, aggregation.TotalChecks),
                    marketData.CurrentPrice,
                    breakout.BreakoutThresholdPrice,
                    breakout.RecentRangeHigh,
                    breakout.ShortMaSlopePercent,
                    _minBreakoutSlopePercent,
                    _breakoutBufferPercent);

                aggregation.Reset(nowUtc);
            }
        }

        if (snapshotToLog is null)
            return;

        _logger.LogInformation(
            "Low-volatility breakout rejection aggregation: Symbol={Symbol}, WindowStartUtc={WindowStartUtc}, WindowEndUtc={WindowEndUtc}, TotalLowVolBreakoutChecks={TotalLowVolBreakoutChecks}, PriceBreakoutFail={PriceBreakoutFail}, SlopeFail={SlopeFail}, TrendStrengthExpansionFail={TrendStrengthExpansionFail}, CloseAboveMaFail={CloseAboveMaFail}, ContextOrRegimeFail={ContextOrRegimeFail}, PendingConfirmationFail={PendingConfirmationFail}, OtherOrUnknown={OtherOrUnknown}, PriceBreakoutPredicateFailed={PriceBreakoutPredicateFailed}, SlopePredicateFailed={SlopePredicateFailed}, PositiveSlopePredicateFailed={PositiveSlopePredicateFailed}, CloseAboveMaPredicateFailed={CloseAboveMaPredicateFailed}, TrendStrengthExpansionPredicateFailed={TrendStrengthExpansionPredicateFailed}, ContextOrRegimePredicateFailed={ContextOrRegimePredicateFailed}, PendingConfirmationPredicateFailed={PendingConfirmationPredicateFailed}, AvgCurrentPrice={AvgCurrentPrice}, AvgBreakoutThresholdPrice={AvgBreakoutThresholdPrice}, AvgRecentRangeHigh={AvgRecentRangeHigh}, AvgShortMaSlopePercent={AvgShortMaSlopePercent}, CurrentPrice={CurrentPrice}, BreakoutThresholdPrice={BreakoutThresholdPrice}, RecentRangeHigh={RecentRangeHigh}, ShortMaSlopePercent={ShortMaSlopePercent}, MinBreakoutSlopePercent={MinBreakoutSlopePercent}, BreakoutBufferPercent={BreakoutBufferPercent}",
            symbol,
            snapshotToLog.WindowStartUtc,
            snapshotToLog.WindowEndUtc,
            snapshotToLog.TotalChecks,
            GetCategoryCount(snapshotToLog.Counts, BreakoutRejectionCategory.PriceBreakoutFail),
            GetCategoryCount(snapshotToLog.Counts, BreakoutRejectionCategory.SlopeFail),
            GetCategoryCount(snapshotToLog.Counts, BreakoutRejectionCategory.TrendStrengthExpansionFail),
            GetCategoryCount(snapshotToLog.Counts, BreakoutRejectionCategory.CloseAboveMaFail),
            GetCategoryCount(snapshotToLog.Counts, BreakoutRejectionCategory.ContextOrRegimeFail),
            GetCategoryCount(snapshotToLog.Counts, BreakoutRejectionCategory.PendingConfirmationFail),
            GetCategoryCount(snapshotToLog.Counts, BreakoutRejectionCategory.OtherOrUnknown),
            GetPredicateFailureCount(snapshotToLog.PredicateFailureCounts, BreakoutPredicateFailureCounter.PriceBreakoutPredicateFailed),
            GetPredicateFailureCount(snapshotToLog.PredicateFailureCounts, BreakoutPredicateFailureCounter.SlopePredicateFailed),
            GetPredicateFailureCount(snapshotToLog.PredicateFailureCounts, BreakoutPredicateFailureCounter.PositiveSlopePredicateFailed),
            GetPredicateFailureCount(snapshotToLog.PredicateFailureCounts, BreakoutPredicateFailureCounter.CloseAboveMaPredicateFailed),
            GetPredicateFailureCount(snapshotToLog.PredicateFailureCounts, BreakoutPredicateFailureCounter.TrendStrengthExpansionPredicateFailed),
            GetPredicateFailureCount(snapshotToLog.PredicateFailureCounts, BreakoutPredicateFailureCounter.ContextOrRegimePredicateFailed),
            GetPredicateFailureCount(snapshotToLog.PredicateFailureCounts, BreakoutPredicateFailureCounter.PendingConfirmationPredicateFailed),
            snapshotToLog.AvgCurrentPrice,
            snapshotToLog.AvgBreakoutThresholdPrice,
            snapshotToLog.AvgRecentRangeHigh,
            snapshotToLog.AvgShortMaSlopePercent,
            snapshotToLog.CurrentPrice,
            snapshotToLog.BreakoutThresholdPrice,
            snapshotToLog.RecentRangeHigh,
            snapshotToLog.ShortMaSlopePercent,
            snapshotToLog.MinBreakoutSlopePercent,
            snapshotToLog.BreakoutBufferPercent);
    }

    private static int GetCategoryCount(
        IReadOnlyDictionary<BreakoutRejectionCategory, int> counts,
        BreakoutRejectionCategory category)
    {
        return counts.TryGetValue(category, out var value) ? value : 0;
    }

    private static int GetPredicateFailureCount(
        IReadOnlyDictionary<BreakoutPredicateFailureCounter, int> counts,
        BreakoutPredicateFailureCounter category)
    {
        return counts.TryGetValue(category, out var value) ? value : 0;
    }

    private static void IncrementPredicateFailure(
        BreakoutRejectionAggregationWindow aggregation,
        BreakoutPredicateFailureCounter predicate)
    {
        aggregation.PredicateFailureCounts[predicate] = aggregation.PredicateFailureCounts.TryGetValue(predicate, out var current)
            ? current + 1
            : 1;
    }

    private static void IncrementPredicateFailureCounters(
        BreakoutRejectionAggregationWindow aggregation,
        BreakoutRejectionCategory primaryCategory,
        LowVolatilityBreakoutEvaluation breakout)
    {
        if (!breakout.PriceBreakout)
            IncrementPredicateFailure(aggregation, BreakoutPredicateFailureCounter.PriceBreakoutPredicateFailed);
        if (!breakout.SlopeAboveMinimum)
            IncrementPredicateFailure(aggregation, BreakoutPredicateFailureCounter.SlopePredicateFailed);
        if (!breakout.PositiveSlopeCheck)
            IncrementPredicateFailure(aggregation, BreakoutPredicateFailureCounter.PositiveSlopePredicateFailed);
        if (!breakout.CloseAboveShortAndLongMa)
            IncrementPredicateFailure(aggregation, BreakoutPredicateFailureCounter.CloseAboveMaPredicateFailed);
        if (!breakout.TrendStrengthExpansionCheck)
            IncrementPredicateFailure(aggregation, BreakoutPredicateFailureCounter.TrendStrengthExpansionPredicateFailed);

        if (primaryCategory == BreakoutRejectionCategory.ContextOrRegimeFail)
            IncrementPredicateFailure(aggregation, BreakoutPredicateFailureCounter.ContextOrRegimePredicateFailed);

        if (primaryCategory == BreakoutRejectionCategory.PendingConfirmationFail)
            IncrementPredicateFailure(aggregation, BreakoutPredicateFailureCounter.PendingConfirmationPredicateFailed);
    }

    private void LogEntryRejectionDiagnostics(
        MarketSnapshot marketData,
        TrendAnalysisResult trend,
        MarketConditionResult marketCondition,
        decimal confidence,
        string reason,
        bool? lowVolBreakoutPathEvaluated = null,
        bool? lowVolBreakoutPassed = null,
        bool? normalTrendEntryPathEvaluated = null,
        string? normalTrendEntrySkippedReason = null,
        bool? canEnterLongResult = null,
        string? canEnterLongFailureReason = null,
        bool? normalTrendFallbackAfterFailedBreakoutEnabled = null,
        bool? normalTrendFallbackUsed = null,
        string? lowVolBreakoutFailureReason = null,
        NormalTrendEntryQualityMetrics? normalTrendQuality = null)
    {
        var isRanging = trend.MarketRegime == MarketRegime.Ranging;
        var isLowVolatility = trend.MarketRegime == MarketRegime.LowVolatility || marketCondition.Regime == VolatilityRegime.Low;

        if (normalTrendEntryPathEvaluated == true)
        {
            RecordNormalEntryRejection(
                marketData.Symbol,
                reason,
                canEnterLongFailureReason,
                trend,
                marketCondition);
        }

        _logger.LogInformation(
            "MovingAverageTrendStrategy entry rejected: Symbol={Symbol}, Reason={Reason}, LowVolBreakoutPathEvaluated={LowVolBreakoutPathEvaluated}, LowVolBreakoutPassed={LowVolBreakoutPassed}, LowVolBreakoutFailureReason={LowVolBreakoutFailureReason}, NormalTrendFallbackAfterFailedBreakoutEnabled={NormalTrendFallbackAfterFailedBreakoutEnabled}, NormalTrendFallbackUsed={NormalTrendFallbackUsed}, NormalTrendEntryPathEvaluated={NormalTrendEntryPathEvaluated}, NormalTrendEntrySkippedReason={NormalTrendEntrySkippedReason}, CanEnterLongResult={CanEnterLongResult}, CanEnterLongFailureReason={CanEnterLongFailureReason}, ConsecutiveBullishTrendCandles={ConsecutiveBullishTrendCandles}, EntryNearRecentHigh={EntryNearRecentHigh}, DistanceToRecentHighPercent={DistanceToRecentHighPercent}, DistanceToInvalidationPercent={DistanceToInvalidationPercent}, CurrentCloseAboveRecentHigh={CurrentCloseAboveRecentHigh}, PreviousCandleBearish={PreviousCandleBearish}, NormalTrendEntryRejectedReason={NormalTrendEntryRejectedReason}, PullbackContinuationOverrideEvaluated={PullbackContinuationOverrideEvaluated}, PullbackContinuationOverrideAllowed={PullbackContinuationOverrideAllowed}, PullbackContinuationOverrideRejectedReason={PullbackContinuationOverrideRejectedReason}, CloseAboveShortAndLongMaForPullback={CloseAboveShortAndLongMaForPullback}, ShortSlopePositiveForPullback={ShortSlopePositiveForPullback}, EnableNormalTrendPullbackContinuationOverride={EnableNormalTrendPullbackContinuationOverride}, NormalTrendPullbackMinExpectedRewardRisk={NormalTrendPullbackMinExpectedRewardRisk}, CurrentShortMa={CurrentShortMa}, CurrentLongMa={CurrentLongMa}, PreviousShortMa={PreviousShortMa}, PreviousLongMa={PreviousLongMa}, ShortMaSlopePercent={ShortMaSlopePercent}, LongMaSlopePercent={LongMaSlopePercent}, TrendStrengthPercent={TrendStrengthPercent}, MinSlopePercent={MinSlopePercent}, MinTrendStrengthPercent={MinTrendStrengthPercent}, TrendConfidenceScore={TrendConfidenceScore}, MinimumTrendConfidenceScore={MinimumTrendConfidenceScore}, BullishCrossover={BullishCrossover}, BearishCrossover={BearishCrossover}, CurrentTrendDirection={CurrentTrendDirection}, MarketRegime={MarketRegime}, VolatilityRegime={VolatilityRegime}, IsLowVolatility={IsLowVolatility}, IsRanging={IsRanging}, RequireCrossoverForEntry={RequireCrossoverForEntry}, MarketConditionScore={MarketConditionScore}, MinimumMarketConditionScore={MinimumMarketConditionScore}, Confidence={Confidence}",
            marketData.Symbol,
            reason,
            lowVolBreakoutPathEvaluated,
            lowVolBreakoutPassed,
            lowVolBreakoutFailureReason,
            normalTrendFallbackAfterFailedBreakoutEnabled,
            normalTrendFallbackUsed,
            normalTrendEntryPathEvaluated,
            normalTrendEntrySkippedReason,
            canEnterLongResult,
            canEnterLongFailureReason,
            normalTrendQuality?.ConsecutiveBullishTrendCandles,
            normalTrendQuality?.EntryNearRecentHigh,
            normalTrendQuality?.DistanceToRecentHighPercent,
            normalTrendQuality?.DistanceToInvalidationPercent,
            normalTrendQuality?.CurrentCloseAboveRecentHigh,
            normalTrendQuality?.PreviousCandleBearish,
            normalTrendQuality?.EntryRejectedReason,
            normalTrendQuality?.PullbackContinuationOverrideEvaluated,
            normalTrendQuality?.PullbackContinuationOverrideAllowed,
            normalTrendQuality?.PullbackContinuationOverrideRejectedReason,
            normalTrendQuality?.CloseAboveShortAndLongMaForPullback,
            normalTrendQuality?.ShortSlopePositiveForPullback,
            _enableNormalTrendPullbackContinuationOverride,
            _normalTrendPullbackMinExpectedRewardRisk,
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

    private void RecordNormalEntryRejection(
        TradingSymbol symbol,
        string reason,
        string? canEnterLongFailureReason,
        TrendAnalysisResult trend,
        MarketConditionResult marketCondition)
    {
        var nowUtc = DateTime.UtcNow;
        var aggregation = NormalEntryRejectionWindows.GetOrAdd(symbol, _ => new NormalEntryRejectionAggregationWindow(nowUtc));
        NormalEntryRejectionAggregationLogSnapshot? snapshotToLog = null;

        lock (aggregation.Sync)
        {
            aggregation.TotalChecks++;
            aggregation.TrendConfidenceScoreSum += trend.ConfidenceScore;
            aggregation.MarketConditionScoreSum += marketCondition.MarketConditionScore;
            aggregation.ShortMaSlopePercentSum += trend.ShortMaSlopePercent;
            aggregation.TrendStrengthPercentSum += trend.TrendStrengthPercent;

            IncrementNormalEntryCategoryCount(aggregation.Counts, ClassifyPrimaryNormalEntryRejection(reason));

            if (!string.IsNullOrWhiteSpace(canEnterLongFailureReason)
                && canEnterLongFailureReason.Contains("Bullish crossover is required", StringComparison.OrdinalIgnoreCase))
            {
                IncrementNormalEntryCategoryCount(aggregation.Counts, NormalEntryRejectionCategory.CrossoverMissing);
            }

            if (Math.Abs(trend.ShortMaSlopePercent) < _minSlopePercent)
                IncrementNormalEntryCategoryCount(aggregation.Counts, NormalEntryRejectionCategory.SlopeTooWeak);

            if (trend.TrendStrengthPercent < _minTrendStrengthPercent)
                IncrementNormalEntryCategoryCount(aggregation.Counts, NormalEntryRejectionCategory.TrendStrengthTooWeak);

            if (nowUtc - aggregation.WindowStartUtc >= _normalEntryRejectionAggregationInterval)
            {
                snapshotToLog = new NormalEntryRejectionAggregationLogSnapshot(
                    aggregation.WindowStartUtc,
                    nowUtc,
                    aggregation.TotalChecks,
                    new Dictionary<NormalEntryRejectionCategory, int>(aggregation.Counts),
                    aggregation.TrendConfidenceScoreSum / Math.Max(1, aggregation.TotalChecks),
                    aggregation.MarketConditionScoreSum / Math.Max(1, aggregation.TotalChecks),
                    aggregation.ShortMaSlopePercentSum / Math.Max(1, aggregation.TotalChecks),
                    aggregation.TrendStrengthPercentSum / Math.Max(1, aggregation.TotalChecks),
                    trend.ConfidenceScore,
                    marketCondition.MarketConditionScore,
                    trend.ShortMaSlopePercent,
                    trend.TrendStrengthPercent,
                    _minimumTrendConfidenceScore,
                    _minimumMarketConditionScore,
                    _minSlopePercent,
                    _minTrendStrengthPercent);

                aggregation.Reset(nowUtc);
            }
        }

        if (snapshotToLog is null)
            return;

        _logger.LogInformation(
            "Normal-entry rejection aggregation: Symbol={Symbol}, WindowStartUtc={WindowStartUtc}, WindowEndUtc={WindowEndUtc}, TotalNormalEntryChecks={TotalNormalEntryChecks}, TrendConfidenceTooWeak={TrendConfidenceTooWeak}, WaitingForConfirmedTrendDirection={WaitingForConfirmedTrendDirection}, CrossoverMissing={CrossoverMissing}, SlopeTooWeak={SlopeTooWeak}, TrendStrengthTooWeak={TrendStrengthTooWeak}, MarketConditionTooWeak={MarketConditionTooWeak}, VolatilityUnsafe={VolatilityUnsafe}, OtherOrUnknown={OtherOrUnknown}, AvgTrendConfidenceScore={AvgTrendConfidenceScore}, AvgMarketConditionScore={AvgMarketConditionScore}, AvgShortMaSlopePercent={AvgShortMaSlopePercent}, AvgTrendStrengthPercent={AvgTrendStrengthPercent}, TrendConfidenceScore={TrendConfidenceScore}, MarketConditionScore={MarketConditionScore}, ShortMaSlopePercent={ShortMaSlopePercent}, TrendStrengthPercent={TrendStrengthPercent}, MinimumTrendConfidenceScore={MinimumTrendConfidenceScore}, MinimumMarketConditionScore={MinimumMarketConditionScore}, MinSlopePercent={MinSlopePercent}, MinTrendStrengthPercent={MinTrendStrengthPercent}",
            symbol,
            snapshotToLog.WindowStartUtc,
            snapshotToLog.WindowEndUtc,
            snapshotToLog.TotalChecks,
            GetNormalEntryCategoryCount(snapshotToLog.Counts, NormalEntryRejectionCategory.TrendConfidenceTooWeak),
            GetNormalEntryCategoryCount(snapshotToLog.Counts, NormalEntryRejectionCategory.WaitingForConfirmedTrendDirection),
            GetNormalEntryCategoryCount(snapshotToLog.Counts, NormalEntryRejectionCategory.CrossoverMissing),
            GetNormalEntryCategoryCount(snapshotToLog.Counts, NormalEntryRejectionCategory.SlopeTooWeak),
            GetNormalEntryCategoryCount(snapshotToLog.Counts, NormalEntryRejectionCategory.TrendStrengthTooWeak),
            GetNormalEntryCategoryCount(snapshotToLog.Counts, NormalEntryRejectionCategory.MarketConditionTooWeak),
            GetNormalEntryCategoryCount(snapshotToLog.Counts, NormalEntryRejectionCategory.VolatilityUnsafe),
            GetNormalEntryCategoryCount(snapshotToLog.Counts, NormalEntryRejectionCategory.OtherOrUnknown),
            snapshotToLog.AvgTrendConfidenceScore,
            snapshotToLog.AvgMarketConditionScore,
            snapshotToLog.AvgShortMaSlopePercent,
            snapshotToLog.AvgTrendStrengthPercent,
            snapshotToLog.TrendConfidenceScore,
            snapshotToLog.MarketConditionScore,
            snapshotToLog.ShortMaSlopePercent,
            snapshotToLog.TrendStrengthPercent,
            snapshotToLog.MinimumTrendConfidenceScore,
            snapshotToLog.MinimumMarketConditionScore,
            snapshotToLog.MinSlopePercent,
            snapshotToLog.MinTrendStrengthPercent);
    }

    private static NormalEntryRejectionCategory ClassifyPrimaryNormalEntryRejection(string reason)
    {
        if (reason.Contains("trend confidence too weak", StringComparison.OrdinalIgnoreCase))
            return NormalEntryRejectionCategory.TrendConfidenceTooWeak;
        if (reason.Contains("waiting for confirmed trend direction", StringComparison.OrdinalIgnoreCase))
            return NormalEntryRejectionCategory.WaitingForConfirmedTrendDirection;
        if (reason.Contains("market condition score too weak", StringComparison.OrdinalIgnoreCase))
            return NormalEntryRejectionCategory.MarketConditionTooWeak;
        if (reason.Contains("unsafe volatility regime", StringComparison.OrdinalIgnoreCase))
            return NormalEntryRejectionCategory.VolatilityUnsafe;

        return NormalEntryRejectionCategory.OtherOrUnknown;
    }

    private static void IncrementNormalEntryCategoryCount(
        Dictionary<NormalEntryRejectionCategory, int> counts,
        NormalEntryRejectionCategory category)
    {
        counts[category] = counts.TryGetValue(category, out var current)
            ? current + 1
            : 1;
    }

    private static int GetNormalEntryCategoryCount(
        IReadOnlyDictionary<NormalEntryRejectionCategory, int> counts,
        NormalEntryRejectionCategory category)
    {
        return counts.TryGetValue(category, out var value) ? value : 0;
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
            "MovingAverageTrendStrategy low-volatility breakout diagnostics: Symbol={Symbol}, BreakoutEnabled={BreakoutEnabled}, Passed={Passed}, FailureReason={FailureReason}, RecentRangeHigh={RecentRangeHigh}, RecentRangeLow={RecentRangeLow}, CurrentPrice={CurrentPrice}, LatestClose={LatestClose}, PreviousClose={PreviousClose}, UseConfirmedClosedCandlesForLowVolBreakout={UseConfirmedClosedCandlesForLowVolBreakout}, BreakoutCandleMode={BreakoutCandleMode}, BreakoutLatestClose={BreakoutLatestClose}, BreakoutLatestCloseTimeUtc={BreakoutLatestCloseTimeUtc}, BreakoutRangeHigh={BreakoutRangeHigh}, BreakoutRangeLow={BreakoutRangeLow}, BreakoutRangeWindowCandleCount={BreakoutRangeWindowCandleCount}, LatestCloseFromConfirmedClosedCandle={LatestCloseFromConfirmedClosedCandle}, LatestClosePotentiallyInProgressCandle={LatestClosePotentiallyInProgressCandle}, RangeWindowCandleCount={RangeWindowCandleCount}, BreakoutBufferPercent={BreakoutBufferPercent}, BreakoutThresholdPrice={BreakoutThresholdPrice}, ShortMaSlopePercent={ShortMaSlopePercent}, MinBreakoutSlopePercent={MinBreakoutSlopePercent}, RequirePositiveShortSlopeForBreakout={RequirePositiveShortSlopeForBreakout}, RequireTrendStrengthExpansion={RequireTrendStrengthExpansion}, TrendStrengthCurrent={TrendStrengthCurrent}, TrendStrengthPrevious={TrendStrengthPrevious}, CloseAboveShortAndLongMa={CloseAboveShortAndLongMa}, PriceBreakout={PriceBreakout}, SlopeAboveMinimum={SlopeAboveMinimum}, PositiveSlopeCheck={PositiveSlopeCheck}, LatestClosedCandleOpenTimeUtc={LatestClosedCandleOpenTimeUtc}, LatestClosedCandleCloseTimeUtc={LatestClosedCandleCloseTimeUtc}, LatestClosedCandleClosePrice={LatestClosedCandleClosePrice}, PendingBreakoutExists={PendingBreakoutExists}, PendingDetectedAtUtc={PendingDetectedAtUtc}, PendingDetectedCandleTimeUtc={PendingDetectedCandleTimeUtc}, PendingLastConfirmationCandleTimeUtc={PendingLastConfirmationCandleTimeUtc}, PendingDetectedTrendStrength={PendingDetectedTrendStrength}, PendingConfirmedClosedCandleCount={PendingConfirmedClosedCandleCount}, RequireBreakoutConfirmation={RequireBreakoutConfirmation}, BreakoutConfirmationCandles={BreakoutConfirmationCandles}, ConfirmationPassed={ConfirmationPassed}, ConfirmationFailureReason={ConfirmationFailureReason}, ConfirmationThresholdPrice={ConfirmationThresholdPrice}, ConfirmationHoldBufferThresholdPrice={ConfirmationHoldBufferThresholdPrice}, ConfirmationClosedCandleClosePrice={ConfirmationClosedCandleClosePrice}, ConfirmationReferencePrice={ConfirmationReferencePrice}, ConfirmationHoldsAboveThreshold={ConfirmationHoldsAboveThreshold}, ConfirmationSlopeStillPositive={ConfirmationSlopeStillPositive}, ConfirmationNoImmediateBearishCandle={ConfirmationNoImmediateBearishCandle}, TrendStrengthCollapsed={TrendStrengthCollapsed}, MarketRegime={MarketRegime}, VolatilityRegime={VolatilityRegime}, Confidence={Confidence}",
            marketData.Symbol,
            _enableLowVolatilityBreakoutEntry,
            breakout.Passed,
            breakout.FailureReason,
            breakout.RecentRangeHigh,
            breakout.RecentRangeLow,
            breakout.CurrentPrice,
            breakout.LatestClose,
            breakout.PreviousClose,
            breakout.UseConfirmedClosedCandlesForLowVolBreakout,
            breakout.BreakoutCandleMode,
            breakout.LatestClose,
            breakout.BreakoutLatestCloseTimeUtc,
            breakout.RecentRangeHigh,
            breakout.RecentRangeLow,
            breakout.RangeWindowCandleCount,
            breakout.LatestCloseFromConfirmedClosedCandle,
            breakout.LatestClosePotentiallyInProgressCandle,
            breakout.RangeWindowCandleCount,
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

    private static StrategySignalResult CreateBuy(
        string reason,
        decimal confidence,
        int? trendConfidenceScore = null,
        int? marketConditionScore = null,
        string? volatilityRegime = null,
        decimal? expectedTargetPrice = null,
        decimal? expectedMovePercent = null,
        string? expectedTargetSource = null,
        decimal? breakoutRangeHigh = null,
        decimal? breakoutRangeLow = null,
        decimal? breakoutThresholdPrice = null,
        decimal? expectedTargetStructureExtensionUsed = null,
        decimal? expectedTargetAtrUsed = null,
        decimal? shortMaSlopePercent = null,
        decimal? trendStrengthPercent = null,
        string? projectionMode = null,
        decimal? projectedExtension = null,
        NormalTrendEntryQualityMetrics? normalTrendQuality = null)
    {
        return new StrategySignalResult
        {
            StrategyName = StrategyName,
            Signal = TradeSignal.Buy,
            Reason = reason,
            Confidence = Math.Clamp(confidence, 0m, 1m),
            TrendConfidenceScore = trendConfidenceScore,
            MarketConditionScore = marketConditionScore,
            VolatilityRegime = volatilityRegime,
            ExpectedTargetPrice = expectedTargetPrice,
            ExpectedMovePercent = expectedMovePercent,
            ExpectedTargetSource = expectedTargetSource,
            BreakoutRangeHigh = breakoutRangeHigh,
            BreakoutRangeLow = breakoutRangeLow,
            BreakoutThresholdPrice = breakoutThresholdPrice,
            ExpectedTargetStructureExtensionUsed = expectedTargetStructureExtensionUsed,
            ExpectedTargetAtrUsed = expectedTargetAtrUsed,
            ShortMaSlopePercent = shortMaSlopePercent,
            TrendStrengthPercent = trendStrengthPercent,
            ProjectionMode = projectionMode,
            ProjectedExtension = projectedExtension,
            ConsecutiveBullishTrendCandles = normalTrendQuality?.ConsecutiveBullishTrendCandles,
            EntryNearRecentHigh = normalTrendQuality?.EntryNearRecentHigh,
            DistanceToRecentHighPercent = normalTrendQuality?.DistanceToRecentHighPercent,
            DistanceToInvalidationPercent = normalTrendQuality?.DistanceToInvalidationPercent,
            CurrentCloseAboveRecentHigh = normalTrendQuality?.CurrentCloseAboveRecentHigh,
            PreviousCandleBearish = normalTrendQuality?.PreviousCandleBearish,
            NormalTrendEntryRejectedReason = normalTrendQuality?.EntryRejectedReason
        };
    }

    private static StrategySignalResult CreateSell(
        string reason,
        decimal confidence,
        int? trendConfidenceScore = null,
        int? marketConditionScore = null)
    {
        return new StrategySignalResult
        {
            StrategyName = StrategyName,
            Signal = TradeSignal.Sell,
            Reason = reason,
            Confidence = Math.Clamp(confidence, 0m, 1m),
            TrendConfidenceScore = trendConfidenceScore,
            MarketConditionScore = marketConditionScore
        };
    }

    private static StrategySignalResult CreateHold(
        string reason,
        decimal confidence = 0m,
        int? trendConfidenceScore = null,
        int? marketConditionScore = null,
        string? volatilityRegime = null,
        decimal? shortMaSlopePercent = null,
        decimal? trendStrengthPercent = null,
        string? projectionMode = null,
        decimal? projectedExtension = null,
        NormalTrendEntryQualityMetrics? normalTrendQuality = null)
    {
        return new StrategySignalResult
        {
            StrategyName = StrategyName,
            Signal = TradeSignal.Hold,
            Reason = reason,
            Confidence = Math.Clamp(confidence, 0m, 1m),
            TrendConfidenceScore = trendConfidenceScore,
            MarketConditionScore = marketConditionScore,
            VolatilityRegime = volatilityRegime,
            ShortMaSlopePercent = shortMaSlopePercent,
            TrendStrengthPercent = trendStrengthPercent,
            ProjectionMode = projectionMode,
            ProjectedExtension = projectedExtension,
            ConsecutiveBullishTrendCandles = normalTrendQuality?.ConsecutiveBullishTrendCandles,
            EntryNearRecentHigh = normalTrendQuality?.EntryNearRecentHigh,
            DistanceToRecentHighPercent = normalTrendQuality?.DistanceToRecentHighPercent,
            DistanceToInvalidationPercent = normalTrendQuality?.DistanceToInvalidationPercent,
            CurrentCloseAboveRecentHigh = normalTrendQuality?.CurrentCloseAboveRecentHigh,
            PreviousCandleBearish = normalTrendQuality?.PreviousCandleBearish,
            NormalTrendEntryRejectedReason = normalTrendQuality?.EntryRejectedReason
        };
    }

    private BreakoutExpectedTargetContext BuildNormalTrendExpectedTarget(
        MarketSnapshot marketData,
        MarketConditionResult marketCondition)
    {
        var currentPrice = marketData.CurrentPrice > 0m
            ? marketData.CurrentPrice
            : marketData.ClosePrices.LastOrDefault();
        if (currentPrice <= 0m)
        {
            return new BreakoutExpectedTargetContext(
                ExpectedTargetPrice: null,
                ExpectedMovePercent: null,
                ExpectedTargetSource: NormalTrendExpectedTargetSource,
                BreakoutRangeHigh: 0m,
                BreakoutRangeLow: 0m,
                RangeWindowCandleCount: 0,
                BreakoutThresholdPrice: 0m);
        }

        var lookback = Math.Min(
            _normalTrendExpectedTargetLookbackCandles,
            Math.Max(2, marketData.ClosePrices.Count - 1));
        if (lookback < 2)
        {
            return new BreakoutExpectedTargetContext(
                ExpectedTargetPrice: null,
                ExpectedMovePercent: null,
                ExpectedTargetSource: NormalTrendExpectedTargetSource,
                BreakoutRangeHigh: currentPrice,
                BreakoutRangeLow: currentPrice,
                RangeWindowCandleCount: lookback,
                BreakoutThresholdPrice: currentPrice);
        }

        var highs = marketData.HighPrices.Count >= lookback + 1
            ? marketData.HighPrices.TakeLast(lookback + 1).Take(lookback).ToArray()
            : marketData.ClosePrices.TakeLast(lookback + 1).Take(lookback).ToArray();
        var lows = marketData.LowPrices.Count >= lookback + 1
            ? marketData.LowPrices.TakeLast(lookback + 1).Take(lookback).ToArray()
            : marketData.ClosePrices.TakeLast(lookback + 1).Take(lookback).ToArray();

        var recentRangeHigh = highs.Max();
        var recentRangeLow = lows.Min();
        var structureRange = Math.Max(0m, recentRangeHigh - recentRangeLow);
        var atr = marketCondition.IsValid && marketCondition.Atr > 0m ? marketCondition.Atr : 0m;
        var atrExtension = atr > 0m ? atr * _normalTrendAtrExtensionMultiplier : 0m;
        var structureExtension = structureRange > 0m
            ? structureRange * _normalTrendStructureExtensionMultiplier
            : currentPrice * 0.0015m;
        var projectedExtension = _normalTrendUseMinAtrStructureExtension
            ? (atr > 0m
                ? Math.Min(atrExtension, structureRange > 0m ? structureRange * 0.5m : atrExtension)
                : structureExtension)
            // less compressed mode: use the larger of ATR-projected and structure-projected extensions
            : (atr > 0m ? Math.Max(atrExtension, structureExtension) : structureExtension);

        var swingReference = Math.Max(recentRangeHigh, currentPrice);
        var expectedTargetPrice = Math.Max(swingReference + projectedExtension, currentPrice);
        var expectedMovePercent = expectedTargetPrice > currentPrice
            ? ((expectedTargetPrice - currentPrice) / currentPrice) * 100m
            : 0m;

        var projectionMode = _normalTrendUseMinAtrStructureExtension ? "LegacyMin" : "MaxAtrStructure";
        LogNormalTrendExpectedTargetDiagnostics(
            marketData.Symbol,
            currentPrice,
            recentRangeHigh,
            recentRangeLow,
            structureRange,
            lookback,
            atrExtension,
            structureExtension,
            projectedExtension,
            projectionMode,
            atr > 0m ? atr : null,
            expectedTargetPrice,
            expectedMovePercent);

        return new BreakoutExpectedTargetContext(
            ExpectedTargetPrice: expectedTargetPrice,
            ExpectedMovePercent: expectedMovePercent,
            ExpectedTargetSource: NormalTrendExpectedTargetSource,
            BreakoutRangeHigh: recentRangeHigh,
            BreakoutRangeLow: recentRangeLow,
            RangeWindowCandleCount: lookback,
            BreakoutThresholdPrice: recentRangeHigh,
            StructureExtensionUsed: projectedExtension,
            AtrUsed: atr > 0m ? atr : null,
            ProjectionMode: projectionMode,
            ProjectedExtension: projectedExtension);
    }

    private void LogNormalTrendExpectedTargetDiagnostics(
        TradingSymbol symbol,
        decimal currentPrice,
        decimal recentRangeHigh,
        decimal recentRangeLow,
        decimal structureRange,
        int normalTrendExpectedTargetLookbackCandles,
        decimal atrExtension,
        decimal structureExtension,
        decimal projectedExtension,
        string projectionMode,
        decimal? atrUsed,
        decimal expectedTargetPrice,
        decimal expectedMovePercent)
    {
        _logger.LogInformation(
            "MovingAverageTrendStrategy normal trend expected target: Symbol={Symbol}, NormalTrendAtrExtensionMultiplier={NormalTrendAtrExtensionMultiplier}, NormalTrendStructureExtensionMultiplier={NormalTrendStructureExtensionMultiplier}, NormalTrendExpectedTargetLookbackCandles={NormalTrendExpectedTargetLookbackCandles}, NormalTrendUseMinAtrStructureExtension={NormalTrendUseMinAtrStructureExtension}, AtrExtension={AtrExtension}, StructureExtension={StructureExtension}, ProjectedExtension={ProjectedExtension}, ProjectionMode={ProjectionMode}, CurrentPrice={CurrentPrice}, RecentRangeHigh={RecentRangeHigh}, RecentRangeLow={RecentRangeLow}, StructureRange={StructureRange}, AtrUsed={AtrUsed}, ExpectedTargetPrice={ExpectedTargetPrice}, ExpectedMovePercent={ExpectedMovePercent}, NormalTrendTargetSource={NormalTrendTargetSource}, FallbackTargetUsed={FallbackTargetUsed}",
            symbol,
            _normalTrendAtrExtensionMultiplier,
            _normalTrendStructureExtensionMultiplier,
            normalTrendExpectedTargetLookbackCandles,
            _normalTrendUseMinAtrStructureExtension,
            atrExtension,
            structureExtension,
            projectedExtension,
            projectionMode,
            currentPrice,
            recentRangeHigh,
            recentRangeLow,
            structureRange,
            atrUsed,
            expectedTargetPrice,
            expectedMovePercent,
            NormalTrendExpectedTargetSource,
            false);
    }

    private static BreakoutExpectedTargetContext BuildLowVolBreakoutExpectedTarget(
        decimal currentPrice,
        decimal breakoutThresholdPrice,
        decimal recentRangeHigh,
        decimal recentRangeLow)
    {
        if (currentPrice <= 0m || breakoutThresholdPrice <= 0m)
        {
            return new BreakoutExpectedTargetContext(
                ExpectedTargetPrice: null,
                ExpectedMovePercent: null,
                ExpectedTargetSource: LowVolBreakoutExpectedTargetSource,
                BreakoutRangeHigh: recentRangeHigh,
                BreakoutRangeLow: recentRangeLow,
                RangeWindowCandleCount: 0,
                BreakoutThresholdPrice: breakoutThresholdPrice,
                ProjectionMode: "LowVolatilityConservative",
                ProjectedExtension: null);
        }

        var structureRange = Math.Max(0m, recentRangeHigh - recentRangeLow);
        var conservativeExtension = structureRange * 0.5m;
        var baselineTarget = breakoutThresholdPrice + conservativeExtension;
        var expectedTargetPrice = baselineTarget;

        if (currentPrice > baselineTarget && structureRange > 0m)
            expectedTargetPrice = currentPrice + (structureRange * 0.25m);

        expectedTargetPrice = Math.Max(expectedTargetPrice, currentPrice);
        var expectedMovePercent = expectedTargetPrice > currentPrice
            ? ((expectedTargetPrice - currentPrice) / currentPrice) * 100m
            : 0m;

        return new BreakoutExpectedTargetContext(
            ExpectedTargetPrice: expectedTargetPrice,
            ExpectedMovePercent: expectedMovePercent,
            ExpectedTargetSource: LowVolBreakoutExpectedTargetSource,
            BreakoutRangeHigh: recentRangeHigh,
            BreakoutRangeLow: recentRangeLow,
            RangeWindowCandleCount: 0,
            BreakoutThresholdPrice: breakoutThresholdPrice,
            StructureExtensionUsed: conservativeExtension,
            AtrUsed: null,
            ProjectionMode: "LowVolatilityConservative",
            ProjectedExtension: conservativeExtension);
    }

    private sealed record LowVolatilityBreakoutEvaluation(
        bool Passed,
        decimal RecentRangeHigh,
        decimal RecentRangeLow,
        decimal CurrentPrice,
        decimal LatestClose,
        decimal? PreviousClose,
        bool LatestCloseFromConfirmedClosedCandle,
        bool LatestClosePotentiallyInProgressCandle,
        int RangeWindowCandleCount,
        decimal BreakoutThresholdPrice,
        decimal ShortMaSlopePercent,
        decimal TrendStrengthCurrent,
        decimal? TrendStrengthPrevious,
        bool PriceBreakout,
        bool SlopeAboveMinimum,
        bool PositiveSlopeCheck,
        bool CloseAboveShortAndLongMa,
        bool TrendStrengthExpansionCheck,
        bool UseConfirmedClosedCandlesForLowVolBreakout,
        string BreakoutCandleMode,
        DateTime? BreakoutLatestCloseTimeUtc,
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
        decimal RecentRangeHigh,
        decimal RecentRangeLow,
        decimal DetectedTrendStrength);

    private sealed record BreakoutEntryState(
        decimal BreakoutThresholdPrice,
        DateTime ConfirmedAtUtc);

    private sealed record MomentumExitDecision(
        bool ExitAllowed,
        string? ExitBlockedReason);

    private sealed record BreakoutExpectedTargetContext(
        decimal? ExpectedTargetPrice,
        decimal? ExpectedMovePercent,
        string ExpectedTargetSource,
        decimal BreakoutRangeHigh,
        decimal BreakoutRangeLow,
        int RangeWindowCandleCount,
        decimal BreakoutThresholdPrice,
        decimal? StructureExtensionUsed = null,
        decimal? AtrUsed = null,
        string? ProjectionMode = null,
        decimal? ProjectedExtension = null);

    private sealed record PullbackContinuationOverrideEvaluation(
        bool Evaluated,
        bool Allowed,
        string? RejectedReason,
        bool? CloseAboveShortAndLongMaForPullback,
        bool? ShortSlopePositiveForPullback);

    private sealed record NormalTrendEntryQualityMetrics(
        int ConsecutiveBullishTrendCandles,
        bool EntryNearRecentHigh,
        decimal DistanceToRecentHighPercent,
        decimal DistanceToInvalidationPercent,
        decimal? ExpectedRewardRisk,
        decimal TrendStrengthPercent,
        bool CurrentCloseAboveRecentHigh,
        bool PreviousCandleBearish,
        bool RewardRiskRejected,
        bool NearHighRejected,
        string? EntryRejectedReason,
        bool UseConfirmedClosedCandlesForEntryQuality,
        string EntryQualityCandleMode,
        decimal EntryQualityLatestClose,
        DateTime? EntryQualityLatestCloseTimeUtc,
        decimal EntryQualityRangeHigh,
        decimal EntryQualityRangeLow,
        int EntryQualityRangeWindowCandleCount,
        bool LatestCloseFromConfirmedClosedCandle,
        bool LatestClosePotentiallyInProgressCandle,
        int RangeWindowCandleCount,
        decimal RangeRecentHigh,
        decimal RangeRecentLow,
        DateTime? LatestClosedCandleCloseTimeUtc,
        bool PullbackContinuationOverrideEvaluated,
        bool PullbackContinuationOverrideAllowed,
        string? PullbackContinuationOverrideRejectedReason,
        bool? CloseAboveShortAndLongMaForPullback,
        bool? ShortSlopePositiveForPullback);

    private sealed class NormalEntryRejectionAggregationWindow(DateTime nowUtc)
    {
        public object Sync { get; } = new();
        public DateTime WindowStartUtc { get; private set; } = nowUtc;
        public int TotalChecks { get; set; }
        public Dictionary<NormalEntryRejectionCategory, int> Counts { get; } = new();
        public decimal TrendConfidenceScoreSum { get; set; }
        public decimal MarketConditionScoreSum { get; set; }
        public decimal ShortMaSlopePercentSum { get; set; }
        public decimal TrendStrengthPercentSum { get; set; }

        public void Reset(DateTime now)
        {
            WindowStartUtc = now;
            TotalChecks = 0;
            Counts.Clear();
            TrendConfidenceScoreSum = 0m;
            MarketConditionScoreSum = 0m;
            ShortMaSlopePercentSum = 0m;
            TrendStrengthPercentSum = 0m;
        }
    }

    private sealed record NormalEntryRejectionAggregationLogSnapshot(
        DateTime WindowStartUtc,
        DateTime WindowEndUtc,
        int TotalChecks,
        IReadOnlyDictionary<NormalEntryRejectionCategory, int> Counts,
        decimal AvgTrendConfidenceScore,
        decimal AvgMarketConditionScore,
        decimal AvgShortMaSlopePercent,
        decimal AvgTrendStrengthPercent,
        int TrendConfidenceScore,
        int MarketConditionScore,
        decimal ShortMaSlopePercent,
        decimal TrendStrengthPercent,
        int MinimumTrendConfidenceScore,
        int MinimumMarketConditionScore,
        decimal MinSlopePercent,
        decimal MinTrendStrengthPercent);

    private sealed class BreakoutRejectionAggregationWindow(DateTime nowUtc)
    {
        public object Sync { get; } = new();
        public DateTime WindowStartUtc { get; private set; } = nowUtc;
        public int TotalChecks { get; set; }
        public Dictionary<BreakoutRejectionCategory, int> Counts { get; } = new();
        public Dictionary<BreakoutPredicateFailureCounter, int> PredicateFailureCounts { get; } = new();
        public decimal CurrentPriceSum { get; set; }
        public decimal BreakoutThresholdPriceSum { get; set; }
        public decimal RecentRangeHighSum { get; set; }
        public decimal ShortMaSlopePercentSum { get; set; }

        public void Reset(DateTime now)
        {
            WindowStartUtc = now;
            TotalChecks = 0;
            Counts.Clear();
            PredicateFailureCounts.Clear();
            CurrentPriceSum = 0m;
            BreakoutThresholdPriceSum = 0m;
            RecentRangeHighSum = 0m;
            ShortMaSlopePercentSum = 0m;
        }
    }

    private sealed record BreakoutRejectionAggregationLogSnapshot(
        DateTime WindowStartUtc,
        DateTime WindowEndUtc,
        int TotalChecks,
        IReadOnlyDictionary<BreakoutRejectionCategory, int> Counts,
        IReadOnlyDictionary<BreakoutPredicateFailureCounter, int> PredicateFailureCounts,
        decimal AvgCurrentPrice,
        decimal AvgBreakoutThresholdPrice,
        decimal AvgRecentRangeHigh,
        decimal AvgShortMaSlopePercent,
        decimal CurrentPrice,
        decimal BreakoutThresholdPrice,
        decimal RecentRangeHigh,
        decimal ShortMaSlopePercent,
        decimal MinBreakoutSlopePercent,
        decimal BreakoutBufferPercent);

    private enum BreakoutRejectionCategory
    {
        PriceBreakoutFail,
        SlopeFail,
        TrendStrengthExpansionFail,
        CloseAboveMaFail,
        ContextOrRegimeFail,
        PendingConfirmationFail,
        OtherOrUnknown
    }

    private enum BreakoutPredicateFailureCounter
    {
        PriceBreakoutPredicateFailed,
        SlopePredicateFailed,
        PositiveSlopePredicateFailed,
        CloseAboveMaPredicateFailed,
        TrendStrengthExpansionPredicateFailed,
        ContextOrRegimePredicateFailed,
        PendingConfirmationPredicateFailed
    }

    private enum NormalEntryRejectionCategory
    {
        TrendConfidenceTooWeak,
        WaitingForConfirmedTrendDirection,
        CrossoverMissing,
        SlopeTooWeak,
        TrendStrengthTooWeak,
        MarketConditionTooWeak,
        VolatilityUnsafe,
        OtherOrUnknown
    }

}
