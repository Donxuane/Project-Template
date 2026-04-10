using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Interfaces.Services;

public interface ITradeCooldownService
{
    Task<CooldownCheckResult> CheckCooldownAsync(TradingSymbol symbol, int cooldownSeconds, CancellationToken cancellationToken = default);
    Task MarkTradeExecutedAsync(TradingSymbol symbol, CancellationToken cancellationToken = default);
}

public sealed class CooldownCheckResult
{
    public bool IsInCooldown { get; init; }
    public int RemainingSeconds { get; init; }
    public DateTime? LastTradeAtUtc { get; init; }
}
