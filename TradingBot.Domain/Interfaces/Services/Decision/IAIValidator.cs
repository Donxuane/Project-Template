using TradingBot.Domain.Models.Decision;

namespace TradingBot.Domain.Interfaces.Services.Decision;

public interface IAIValidator
{
    Task<bool> ValidateAsync(TradeCandidate candidate, MarketSnapshot marketData, CancellationToken cancellationToken = default);
}
