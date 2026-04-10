using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.DecisionEngine;

public class RiskEvaluator(IRiskManagementService riskManagementService) : IRiskEvaluator
{
    public async Task<RiskEvaluationResult> EvaluateAsync(TradeCandidate candidate, CancellationToken cancellationToken = default)
    {
        var result = await riskManagementService.CheckOrderAsync(
            candidate.Symbol,
            candidate.Side,
            candidate.Quantity,
            candidate.Price,
            cancellationToken);

        return new RiskEvaluationResult
        {
            IsAllowed = result.IsAllowed,
            Reason = result.Reason ?? (result.IsAllowed ? "Allowed." : "Rejected.")
        };
    }
}
