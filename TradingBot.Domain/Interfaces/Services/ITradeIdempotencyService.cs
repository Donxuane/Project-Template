namespace TradingBot.Domain.Interfaces.Services;

public interface ITradeIdempotencyService
{
    Task<bool> TryRegisterDecisionAsync(string decisionId, int windowSeconds, CancellationToken cancellationToken = default);
}
