using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.DecisionEngine;

public class NoOpAIValidator : IAIValidator
{
    public Task<bool> ValidateAsync(TradeCandidate candidate, MarketSnapshot marketData, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}
