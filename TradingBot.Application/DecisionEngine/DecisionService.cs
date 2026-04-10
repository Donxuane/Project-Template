using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.DecisionEngine;

public class DecisionService(
    IMarketDataProvider marketDataProvider,
    IStrategy strategy,
    IRiskEvaluator riskEvaluator,
    IAIValidator aiValidator,
    ILogger<DecisionService> logger) : IDecisionService
{
    public async Task<DecisionResult> DecideAsync(TradingSymbol symbol, decimal quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            return FinalizeDecision(new DecisionResult
            {
                Action = TradeSignal.Hold,
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
                Reason = $"No market data available for {symbol}.",
                Confidence = 0m
            });
        }

        var signal = await strategy.GenerateSignalAsync(marketData, cancellationToken);
        logger.LogInformation(
            "DecisionEngine signal generated: Symbol={Symbol}, Signal={Signal}, Reason={Reason}, Price={Price}",
            symbol, signal.Signal, signal.Reason, marketData.CurrentPrice);

        if (signal.Signal == TradeSignal.Hold)
        {
            return FinalizeDecision(new DecisionResult
            {
                Action = TradeSignal.Hold,
                Reason = signal.Reason,
                Confidence = signal.Confidence
            });
        }

        var candidate = new TradeCandidate
        {
            Symbol = symbol,
            Side = signal.Signal == TradeSignal.Buy ? OrderSide.BUY : OrderSide.SELL,
            Quantity = quantity,
            Price = marketData.CurrentPrice
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
                Reason = $"Risk rejected candidate: {riskResult.Reason}",
                Candidate = candidate,
                Confidence = signal.Confidence
            });
        }

        var aiApproved = await aiValidator.ValidateAsync(candidate, marketData, cancellationToken);
        if (!aiApproved)
        {
            logger.LogWarning(
                "DecisionEngine candidate rejected by AI validator: Symbol={Symbol}, Side={Side}, Qty={Qty}",
                candidate.Symbol, candidate.Side, candidate.Quantity);

            return FinalizeDecision(new DecisionResult
            {
                Action = TradeSignal.Hold,
                Reason = "AI validator rejected candidate.",
                Candidate = candidate,
                Confidence = signal.Confidence
            });
        }

        return FinalizeDecision(new DecisionResult
        {
            Action = signal.Signal,
            Reason = "Signal accepted by risk and AI validators.",
            Candidate = candidate,
            Confidence = signal.Confidence
        });
    }

    private DecisionResult FinalizeDecision(DecisionResult result)
    {
        logger.LogInformation(
            "DecisionEngine final decision: Action={Action}, Reason={Reason}, Candidate={@Candidate}",
            result.Action, result.Reason, result.Candidate);
        return result;
    }
}
