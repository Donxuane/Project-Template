using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Interfaces.Services;

public interface IRiskManagementService
{
    /// <param name="price">When null, price is resolved from cached prices (e.g. for market orders).</param>
    Task<RiskCheckResult> CheckOrderAsync(TradingSymbol symbol, OrderSide side, decimal quantity, decimal? price = null, CancellationToken cancellationToken = default);
    Task<RiskCheckResult> ValidateTrade(TradingSymbol symbol, decimal quantity, decimal price, OrderSide side, CancellationToken cancellationToken = default);
}

public sealed class RiskCheckResult
{
    public bool IsAllowed { get; init; }
    public string Reason { get; init; } = string.Empty;
    public decimal? StopLossPrice { get; init; }
    public decimal? TakeProfitPrice { get; init; }
}

