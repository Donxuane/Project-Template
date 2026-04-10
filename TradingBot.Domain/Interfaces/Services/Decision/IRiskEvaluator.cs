using TradingBot.Domain.Models.Decision;

namespace TradingBot.Domain.Interfaces.Services.Decision;

public interface IRiskEvaluator
{
    Task<RiskEvaluationResult> EvaluateAsync(TradeCandidate candidate, CancellationToken cancellationToken = default);
}
