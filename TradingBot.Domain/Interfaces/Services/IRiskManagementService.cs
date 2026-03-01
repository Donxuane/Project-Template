using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Interfaces.Services;

public interface IRiskManagementService
{
    Task<RiskCheckResult> CheckOrderAsync(TradingSymbol symbol, OrderSide side, decimal quantity, decimal price, CancellationToken cancellationToken = default);
}

public sealed class RiskCheckResult
{
    public bool IsAllowed { get; init; }
    public string? Reason { get; init; }
}

