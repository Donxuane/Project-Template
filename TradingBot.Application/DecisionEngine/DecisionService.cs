using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;
using TradingBot.Shared.Configuration;

namespace TradingBot.Application.DecisionEngine;

public class DecisionService(
    IConfiguration configuration,
    IMarketDataProvider marketDataProvider,
    IMarketConditionService marketConditionService,
    IStrategy strategy,
    IRiskEvaluator riskEvaluator,
    IAIValidator aiValidator,
    IPositionRepository positionRepository,
    IPositionManager positionManager,
    ILogger<DecisionService> logger) : IDecisionService
{
    private const decimal ReducedPositionSizeFactor = 0.5m;
    private readonly DecisionEngineRuntimeSettings _decisionSettings = RuntimeTradingConfigResolver.ResolveDecisionEngine(configuration);
    private readonly TradingRuntimeSettings _tradingSettings = RuntimeTradingConfigResolver.ResolveTrading(configuration);
    private readonly bool _useAiValidator = configuration.GetValue<bool?>("DecisionEngine:UseAIValidator") ?? false;
    private decimal MinimumSignalConfidence => Math.Max(0m, _decisionSettings.MinimumSignalConfidence);
    private TradingMode RuntimeTradingMode => Enum.TryParse<TradingMode>(_tradingSettings.Mode, true, out var mode) ? mode : TradingMode.Spot;

    public async Task<DecisionResult> DecideAsync(TradingSymbol symbol, decimal quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            return FinalizeDecision(new DecisionResult
            {
                Action = TradeSignal.Hold,
                StrategyName = strategy.GetType().Name,
                RawSignal = TradeSignal.Hold,
                TradingMode = RuntimeTradingMode,
                ExecutionIntent = TradeExecutionIntent.None,
                Reason = "Quantity must be greater than zero.",
                Confidence = 0m
            });
        }

        var marketData = await marketDataProvider.GetLatestAsync(symbol, cancellationToken);
        if (marketData is null)
        {
            return FinalizeDecision(new DecisionResult
            {
                Action = TradeSignal.Hold,
                StrategyName = strategy.GetType().Name,
                RawSignal = TradeSignal.Hold,
                TradingMode = RuntimeTradingMode,
                ExecutionIntent = TradeExecutionIntent.None,
                Reason = $"No market data available for {symbol}.",
                Confidence = 0m
            });
        }

        var persistedOpenPosition = await positionRepository.GetOpenPositionAsync(symbol, cancellationToken);
        ReconcileStrategyPositionState(symbol, persistedOpenPosition);

        var signal = await strategy.GenerateSignalAsync(marketData, cancellationToken);
        var strategyName = string.IsNullOrWhiteSpace(signal.StrategyName)
            ? strategy.GetType().Name
            : signal.StrategyName;
        var executionIntent = MapExecutionIntent(signal.Signal, RuntimeTradingMode);
        logger.LogInformation(
            "DecisionEngine signal generated: StrategyName={StrategyName}, Symbol={Symbol}, RawSignal={RawSignal}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Confidence={Confidence:F4}, Reason={Reason}, Price={Price}, Quantity={Quantity}",
            strategyName, symbol, signal.Signal, RuntimeTradingMode, executionIntent, signal.Confidence, signal.Reason, marketData.CurrentPrice, quantity);
        var marketDataAgeSeconds = Math.Max(0m, marketData.MarketDataAgeSeconds ?? 0m);
        var maxMarketDataAgeSeconds = _decisionSettings.MaxMarketDataAgeSeconds;

        if (signal.Signal == TradeSignal.Hold)
        {
            return FinalizeDecision(new DecisionResult
            {
                Action = TradeSignal.Hold,
                StrategyName = strategyName,
                RawSignal = signal.Signal,
                TradingMode = RuntimeTradingMode,
                ExecutionIntent = executionIntent,
                Reason = signal.Reason,
                Confidence = signal.Confidence
            });
        }

        if (signal.Signal != TradeSignal.Buy && signal.Signal != TradeSignal.Sell)
        {
            return FinalizeDecision(new DecisionResult
            {
                Action = TradeSignal.Hold,
                StrategyName = strategyName,
                RawSignal = signal.Signal,
                TradingMode = RuntimeTradingMode,
                ExecutionIntent = executionIntent,
                Reason = "Signal rejected - unsupported trade signal.",
                Confidence = signal.Confidence
            });
        }

        if (signal.Confidence < MinimumSignalConfidence)
        {
            return FinalizeDecision(new DecisionResult
            {
                Action = TradeSignal.Hold,
                StrategyName = strategyName,
                RawSignal = signal.Signal,
                TradingMode = RuntimeTradingMode,
                ExecutionIntent = executionIntent,
                Reason = "Signal rejected - confidence below minimum threshold.",
                Confidence = signal.Confidence
            });
        }

        var marketCondition = marketConditionService.Evaluate(marketData);
        if (!marketCondition.IsValid || !marketCondition.AllowTrade)
        {
            return FinalizeDecision(new DecisionResult
            {
                Action = TradeSignal.Hold,
                StrategyName = strategyName,
                RawSignal = signal.Signal,
                TradingMode = RuntimeTradingMode,
                ExecutionIntent = executionIntent,
                Reason = marketCondition.Reason,
                Confidence = signal.Confidence
            });
        }

        if (marketCondition.MarketConditionScore < 60)
        {
            return FinalizeDecision(new DecisionResult
            {
                Action = TradeSignal.Hold,
                StrategyName = strategyName,
                RawSignal = signal.Signal,
                TradingMode = RuntimeTradingMode,
                ExecutionIntent = executionIntent,
                Reason = $"Market condition score too low ({marketCondition.MarketConditionScore}). {marketCondition.Reason}",
                Confidence = signal.Confidence
            });
        }

        if (marketData.CurrentPrice <= 0m)
        {
            return FinalizeDecision(new DecisionResult
            {
                Action = TradeSignal.Hold,
                StrategyName = strategyName,
                RawSignal = signal.Signal,
                TradingMode = RuntimeTradingMode,
                ExecutionIntent = executionIntent,
                Reason = "Invalid current market price.",
                Confidence = signal.Confidence
            });
        }

        var isSpotOpenLong = RuntimeTradingMode == TradingMode.Spot && executionIntent == TradeExecutionIntent.OpenLong;
        if (isSpotOpenLong && marketDataAgeSeconds > maxMarketDataAgeSeconds)
        {
            return FinalizeDecision(new DecisionResult
            {
                Action = TradeSignal.Hold,
                StrategyName = strategyName,
                RawSignal = signal.Signal,
                TradingMode = RuntimeTradingMode,
                ExecutionIntent = executionIntent,
                Reason = $"No entry signal - market data stale. AgeSeconds={Math.Round(marketDataAgeSeconds, 2)}, MaxAgeSeconds={maxMarketDataAgeSeconds}.",
                Confidence = signal.Confidence
            });
        }

        var isSpotCloseLong = RuntimeTradingMode == TradingMode.Spot && executionIntent == TradeExecutionIntent.CloseLong;
        if (isSpotCloseLong && marketDataAgeSeconds > maxMarketDataAgeSeconds)
        {
            logger.LogWarning(
                "DecisionEngine allowing stale market data for Spot CloseLong because exit is risk reducing. Symbol={Symbol}, AgeSeconds={AgeSeconds}, MaxAgeSeconds={MaxAgeSeconds}, PriceSource={PriceSource}, PriceAsOfUtc={PriceAsOfUtc}",
                symbol,
                marketDataAgeSeconds,
                maxMarketDataAgeSeconds,
                marketData.CurrentPriceSource,
                marketData.CurrentPriceAsOfUtc);
        }

        if (RuntimeTradingMode == TradingMode.Spot && executionIntent == TradeExecutionIntent.CloseLong)
        {
            var hasOpenLong = persistedOpenPosition is not null
                              && persistedOpenPosition.IsOpen
                              && persistedOpenPosition.Quantity > 0m
                              && persistedOpenPosition.Side == OrderSide.BUY;
            if (!hasOpenLong)
            {
                return FinalizeDecision(new DecisionResult
                {
                    Action = TradeSignal.Hold,
                    StrategyName = strategyName,
                    RawSignal = signal.Signal,
                    TradingMode = RuntimeTradingMode,
                    ExecutionIntent = executionIntent,
                    Reason = "Spot SELL skipped because no open long position exists.",
                    Confidence = signal.Confidence
                });
            }
        }

        var candidateQuantity = marketCondition.RequiresReducedPositionSize
            ? Math.Max(0m, quantity * ReducedPositionSizeFactor)
            : quantity;
        var requiresReducedPositionSize = marketCondition.RequiresReducedPositionSize;
        if (isSpotCloseLong)
        {
            candidateQuantity = Math.Abs(persistedOpenPosition!.Quantity);
            requiresReducedPositionSize = false;
            logger.LogInformation(
                "DecisionEngine resolved Spot CloseLong quantity from persisted position. Symbol={Symbol}, RequestedQuantity={RequestedQuantity}, PersistedOpenQuantity={PersistedOpenQuantity}, CandidateQuantity={CandidateQuantity}",
                symbol,
                quantity,
                persistedOpenPosition.Quantity,
                candidateQuantity);
        }

        var candidate = new TradeCandidate
        {
            Symbol = symbol,
            Side = MapOrderSide(executionIntent),
            Quantity = candidateQuantity,
            Price = marketData.CurrentPrice,
            RequiresReducedPositionSize = requiresReducedPositionSize,
            TradingMode = RuntimeTradingMode,
            RawSignal = signal.Signal,
            ExecutionIntent = executionIntent
        };

        var riskResult = await riskEvaluator.EvaluateAsync(candidate, cancellationToken);
        if (!riskResult.IsAllowed)
        {
            logger.LogWarning(
                "DecisionEngine candidate rejected by risk: Symbol={Symbol}, Side={Side}, Qty={Qty}, Price={Price}, Reason={Reason}",
                candidate.Symbol, candidate.Side, candidate.Quantity, candidate.Price, riskResult.Reason);

            return FinalizeDecision(new DecisionResult
            {
                Action = TradeSignal.Hold,
                StrategyName = strategyName,
                RawSignal = signal.Signal,
                TradingMode = RuntimeTradingMode,
                ExecutionIntent = executionIntent,
                Reason = $"Risk rejected candidate: {riskResult.Reason}",
                Candidate = candidate,
                Confidence = signal.Confidence
            });
        }

        // TODO: Add RiskScore/StopLossPrice/TakeProfitPrice/ExposurePercent to DecisionResult or TradeCandidate for downstream visibility.
        if (_useAiValidator)
        {
            var aiApproved = await aiValidator.ValidateAsync(candidate, marketData, cancellationToken);
            if (!aiApproved)
            {
                logger.LogWarning(
                    "DecisionEngine candidate rejected by AI validator: Symbol={Symbol}, Side={Side}, Qty={Qty}",
                    candidate.Symbol, candidate.Side, candidate.Quantity);

                return FinalizeDecision(new DecisionResult
                {
                    Action = TradeSignal.Hold,
                    StrategyName = strategyName,
                    RawSignal = signal.Signal,
                    TradingMode = RuntimeTradingMode,
                    ExecutionIntent = executionIntent,
                    Reason = "AI validator rejected candidate.",
                    Candidate = candidate,
                    Confidence = signal.Confidence
                });
            }
        }

        var acceptedReason = $"Signal accepted. Strategy: {signal.Reason}";
        if (!string.IsNullOrWhiteSpace(marketCondition.Warning))
            acceptedReason = $"{acceptedReason} Warning: {marketCondition.Warning}";
        var normalizedStrategyName = strategyName.EndsWith("Strategy", StringComparison.OrdinalIgnoreCase)
            ? strategyName[..^"Strategy".Length]
            : strategyName;
        var expectedExecutionThreshold = RuntimeTradingConfigResolver.ResolveConfidenceThreshold(
            configuration,
            normalizedStrategyName,
            signal.Signal.ToString(),
            RuntimeTradingMode.ToString(),
            executionIntent.ToString());

        logger.LogInformation(
            "DecisionEngine accepted candidate with market-data metadata: Symbol={Symbol}, Action={Action}, CandidatePrice={CandidatePrice}, CurrentPriceSource={CurrentPriceSource}, CurrentPriceAsOfUtc={CurrentPriceAsOfUtc}, MarketDataAgeSeconds={MarketDataAgeSeconds}, Confidence={Confidence}, SignalMinConfidence={SignalMinConfidence}, ExpectedExecutionMinConfidence={ExpectedExecutionMinConfidence}, ExpectedThresholdKind={ExpectedThresholdKind}, ExpectedThresholdSource={ExpectedThresholdSource}, Reason={Reason}",
            symbol,
            signal.Signal,
            candidate.Price,
            marketData.CurrentPriceSource,
            marketData.CurrentPriceAsOfUtc,
            marketDataAgeSeconds,
            signal.Confidence,
            MinimumSignalConfidence,
            expectedExecutionThreshold.MinConfidence,
            expectedExecutionThreshold.ThresholdKind,
            expectedExecutionThreshold.ThresholdSource,
            acceptedReason);

        return FinalizeDecision(new DecisionResult
        {
            Action = signal.Signal,
            StrategyName = strategyName,
            RawSignal = signal.Signal,
            TradingMode = RuntimeTradingMode,
            ExecutionIntent = executionIntent,
            Reason = acceptedReason,
            Candidate = candidate,
            Confidence = signal.Confidence
        });
    }

    private void ReconcileStrategyPositionState(TradingSymbol symbol, Domain.Models.Trading.Position? persistedOpenPosition)
    {
        var oldState = positionManager.GetState(symbol);
        var hasDbPosition = persistedOpenPosition is not null
                            && persistedOpenPosition.IsOpen
                            && persistedOpenPosition.Quantity > 0m;
        var persistedPositionType = ResolvePersistedPositionType(persistedOpenPosition);
        var persistedEntryPrice = hasDbPosition ? persistedOpenPosition!.AveragePrice : 0m;
        var persistedEntryTimeUtc = hasDbPosition ? persistedOpenPosition!.OpenedAt ?? DateTime.MinValue : DateTime.MinValue;

        positionManager.SyncWithPersistedPosition(
            symbol,
            hasDbPosition,
            persistedPositionType,
            persistedEntryPrice,
            persistedEntryTimeUtc);

        var newState = positionManager.GetState(symbol);
        if (!HasPositionStateChanged(oldState, newState))
            return;

        logger.LogInformation(
            "Strategy position state reconciled from persisted position state. Symbol={Symbol}, HadInMemoryPosition={HadInMemoryPosition}, HasDbPosition={HasDbPosition}, OldPositionType={OldPositionType}, NewPositionType={NewPositionType}",
            symbol,
            oldState.IsInPosition,
            hasDbPosition,
            oldState.PositionType,
            newState.PositionType);
    }

    private PositionType ResolvePersistedPositionType(Domain.Models.Trading.Position? persistedOpenPosition)
    {
        if (persistedOpenPosition is null || !persistedOpenPosition.IsOpen || persistedOpenPosition.Quantity <= 0m)
            return PositionType.None;

        if (RuntimeTradingMode == TradingMode.Spot)
            return PositionType.Long;

        return persistedOpenPosition.Side switch
        {
            OrderSide.BUY => PositionType.Long,
            OrderSide.SELL => PositionType.Short,
            _ => PositionType.None
        };
    }

    private static bool HasPositionStateChanged(SymbolPositionState oldState, SymbolPositionState newState)
    {
        return oldState.IsInPosition != newState.IsInPosition
               || oldState.PositionType != newState.PositionType
               || oldState.EntryPrice != newState.EntryPrice
               || oldState.EntryTimeUtc != newState.EntryTimeUtc;
    }

    private DecisionResult FinalizeDecision(DecisionResult result)
    {
        logger.LogInformation(
            "DecisionEngine final decision: StrategyName={StrategyName}, Action={Action}, RawSignal={RawSignal}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Reason={Reason}, Candidate={@Candidate}",
            result.StrategyName, result.Action, result.RawSignal, result.TradingMode, result.ExecutionIntent, result.Reason, result.Candidate);
        return result;
    }

    private static TradeExecutionIntent MapExecutionIntent(TradeSignal signal, TradingMode tradingMode)
    {
        return (tradingMode, signal) switch
        {
            (_, TradeSignal.Hold) => TradeExecutionIntent.None,
            (TradingMode.Spot, TradeSignal.Buy) => TradeExecutionIntent.OpenLong,
            (TradingMode.Spot, TradeSignal.Sell) => TradeExecutionIntent.CloseLong,
            (TradingMode.Futures, TradeSignal.Buy) => TradeExecutionIntent.OpenLong,
            (TradingMode.Futures, TradeSignal.Sell) => TradeExecutionIntent.OpenShort,
            _ => TradeExecutionIntent.None
        };
    }

    private static OrderSide MapOrderSide(TradeExecutionIntent intent)
    {
        return intent switch
        {
            TradeExecutionIntent.OpenLong => OrderSide.BUY,
            TradeExecutionIntent.CloseShort => OrderSide.BUY,
            TradeExecutionIntent.CloseLong => OrderSide.SELL,
            TradeExecutionIntent.OpenShort => OrderSide.SELL,
            _ => OrderSide.BUY
        };
    }

}
