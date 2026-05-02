using Microsoft.Extensions.Logging;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.DecisionEngine;

public class RiskEvaluator(
    IRiskManagementService riskManagementService,
    ILogger<RiskEvaluator> logger) : IRiskEvaluator
{
    public async Task<RiskEvaluationResult> EvaluateAsync(TradeCandidate candidate, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "RiskEvaluator evaluating candidate: Symbol={Symbol}, RawSignal={RawSignal}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Side={Side}, Quantity={Quantity}, Price={Price}",
            candidate.Symbol,
            candidate.RawSignal,
            candidate.TradingMode,
            candidate.ExecutionIntent,
            candidate.Side,
            candidate.Quantity,
            candidate.Price);

        var result = await riskManagementService.CheckOrderAsync(
            candidate.Symbol,
            candidate.Side,
            candidate.Quantity,
            candidate.Price,
            cancellationToken,
            candidate.RequiresReducedPositionSize,
            candidate.TradingMode,
            candidate.RawSignal,
            candidate.ExecutionIntent);

        var baseReason = result.Reason ?? (result.IsAllowed ? "Allowed." : "Rejected.");
        var reason = candidate.RequiresReducedPositionSize
            ? $"{baseReason} Quantity reduced for high-volatility regime."
            : baseReason;

        return new RiskEvaluationResult
        {
            IsAllowed = result.IsAllowed,
            Reason = reason
        };
    }
}
