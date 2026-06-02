namespace TradingBot.Domain.Interfaces.Services;

public interface ITradeIdempotencyService
{
    Task<bool> IsDuplicateDecisionAsync(string decisionId, int windowSeconds, CancellationToken cancellationToken = default);

    Task MarkDecisionExecutedAsync(string decisionId, int windowSeconds, CancellationToken cancellationToken = default);

    Task<bool> TryRegisterDecisionAsync(string decisionId, int windowSeconds, CancellationToken cancellationToken = default);
}
