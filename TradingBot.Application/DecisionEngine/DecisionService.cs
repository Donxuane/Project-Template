using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.DecisionEngine;

public class DecisionService(
    IConfiguration configuration,
    IMarketDataProvider marketDataProvider,
    IMarketConditionService marketConditionService,
    IStrategy strategy,
    IRiskEvaluator riskEvaluator,
    IAIValidator aiValidator,
    IPositionRepository positionRepository,
    ILogger<DecisionService> logger) : IDecisionService
{
    private const string ConfigSection = "DecisionEngine";
    private const decimal ReducedPositionSizeFactor = 0.5m;
    private readonly decimal _minimumSignalConfidence = GetDecimal(configuration, $"{ConfigSection}:MinimumSignalConfidence", 0.60m, 0m);
    private readonly bool _useAiValidator = GetBool(configuration, $"{ConfigSection}:UseAIValidator", false);
    private readonly TradingMode _tradingMode = GetTradingMode(configuration);

    public async Task<DecisionResult> DecideAsync(TradingSymbol symbol, decimal quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            return FinalizeDecision(new DecisionResult
            {
                Action = TradeSignal.Hold,
                StrategyName = strategy.GetType().Name,
                RawSignal = TradeSignal.Hold,
                TradingMode = _tradingMode,
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
                TradingMode = _tradingMode,
                ExecutionIntent = TradeExecutionIntent.None,
                Reason = $"No market data available for {symbol}.",
                Confidence = 0m
            });
        }

        var signal = await strategy.GenerateSignalAsync(marketData, cancellationToken);
        var strategyName = string.IsNullOrWhiteSpace(signal.StrategyName)
            ? strategy.GetType().Name
            : signal.StrategyName;
        var executionIntent = MapExecutionIntent(signal.Signal, _tradingMode);
        logger.LogInformation(
            "DecisionEngine signal generated: StrategyName={StrategyName}, Symbol={Symbol}, RawSignal={RawSignal}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Confidence={Confidence:F4}, Reason={Reason}, Price={Price}, Quantity={Quantity}",
            strategyName, symbol, signal.Signal, _tradingMode, executionIntent, signal.Confidence, signal.Reason, marketData.CurrentPrice, quantity);

        if (signal.Signal == TradeSignal.Hold)
        {
            return FinalizeDecision(new DecisionResult
            {
                Action = TradeSignal.Hold,
                StrategyName = strategyName,
                RawSignal = signal.Signal,
                TradingMode = _tradingMode,
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
                TradingMode = _tradingMode,
                ExecutionIntent = executionIntent,
                Reason = "Signal rejected - unsupported trade signal.",
                Confidence = signal.Confidence
            });
        }

        if (signal.Confidence < _minimumSignalConfidence)
        {
            return FinalizeDecision(new DecisionResult
            {
                Action = TradeSignal.Hold,
                StrategyName = strategyName,
                RawSignal = signal.Signal,
                TradingMode = _tradingMode,
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
                TradingMode = _tradingMode,
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
                TradingMode = _tradingMode,
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
                TradingMode = _tradingMode,
                ExecutionIntent = executionIntent,
                Reason = "Invalid current market price.",
                Confidence = signal.Confidence
            });
        }

        if (_tradingMode == TradingMode.Spot && executionIntent == TradeExecutionIntent.CloseLong)
        {
            var openPosition = await positionRepository.GetOpenPositionAsync(symbol, cancellationToken);
            var hasOpenLong = openPosition is not null
                              && openPosition.IsOpen
                              && openPosition.Quantity > 0m
                              && openPosition.Side == OrderSide.BUY;
            if (!hasOpenLong)
            {
                return FinalizeDecision(new DecisionResult
                {
                    Action = TradeSignal.Hold,
                    StrategyName = strategyName,
                    RawSignal = signal.Signal,
                    TradingMode = _tradingMode,
                    ExecutionIntent = executionIntent,
                    Reason = "Spot SELL skipped because no open long position exists.",
                    Confidence = signal.Confidence
                });
            }
        }

        var candidateQuantity = marketCondition.RequiresReducedPositionSize
            ? Math.Max(0m, quantity * ReducedPositionSizeFactor)
            : quantity;

        var candidate = new TradeCandidate
        {
            Symbol = symbol,
            Side = MapOrderSide(executionIntent),
            Quantity = candidateQuantity,
            Price = marketData.CurrentPrice,
            RequiresReducedPositionSize = marketCondition.RequiresReducedPositionSize,
            TradingMode = _tradingMode,
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
                TradingMode = _tradingMode,
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
                    TradingMode = _tradingMode,
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

        return FinalizeDecision(new DecisionResult
        {
            Action = signal.Signal,
            StrategyName = strategyName,
            RawSignal = signal.Signal,
            TradingMode = _tradingMode,
            ExecutionIntent = executionIntent,
            Reason = acceptedReason,
            Candidate = candidate,
            Confidence = signal.Confidence
        });
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

    private static TradingMode GetTradingMode(IConfiguration configuration)
    {
        var configured = configuration["Trading:Mode"];
        return Enum.TryParse<TradingMode>(configured, true, out var mode)
            ? mode
            : TradingMode.Spot;
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
