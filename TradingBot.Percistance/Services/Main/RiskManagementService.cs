using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Percistance.Services.Main;

public class RiskManagementService(
    IConfiguration configuration,
    IPositionRepository positionRepository,
    IOrderRepository orderRepository,
    IBalanceRepository balanceRepository) : IRiskManagementService
{
    private decimal MaxPositionQuote => configuration.GetValue<decimal?>("RiskSettings:MaxPositionQuote") ?? 10_000m;
    private decimal MaxOrderQuote => configuration.GetValue<decimal?>("RiskSettings:MaxOrderQuote") ?? 5_000m;
    private decimal MaxExposurePercent => configuration.GetValue<decimal?>("RiskSettings:MaxExposurePercent") ?? 50m;

    public async Task<RiskCheckResult> CheckOrderAsync(TradingSymbol symbol, OrderSide side, decimal quantity, decimal price, CancellationToken cancellationToken = default)
    {
        var notional = quantity * price;

        if (notional > MaxOrderQuote)
        {
            return new RiskCheckResult
            {
                IsAllowed = false,
                Reason = $"Order notional {notional} exceeds MaxOrderQuote {MaxOrderQuote}."
            };
        }

        var existingPosition = await positionRepository.GetOpenPositionAsync(symbol, cancellationToken);
        var existingQty = existingPosition?.Quantity ?? 0m;
        var newQty = side == OrderSide.BUY ? existingQty + quantity : existingQty - quantity;
        var newNotional = Math.Abs(newQty) * price;

        if (newNotional > MaxPositionQuote)
        {
            return new RiskCheckResult
            {
                IsAllowed = false,
                Reason = $"Position notional {newNotional} exceeds MaxPositionQuote {MaxPositionQuote}."
            };
        }

        var balances = await balanceRepository.GetLatestForAllAsync(cancellationToken);
        var totalQuote = balances.Sum(b => (b.Symbol == symbol ? (b.Free + b.Locked) * price : 0m));

        if (totalQuote > 0)
        {
            var exposurePercent = (newNotional / totalQuote) * 100m;
            if (exposurePercent > MaxExposurePercent)
            {
                return new RiskCheckResult
                {
                    IsAllowed = false,
                    Reason = $"Exposure {exposurePercent:F2}% exceeds MaxExposurePercent {MaxExposurePercent}%."
                };
            }
        }

        var openOrders = await orderRepository.GetOpenOrdersAsync(symbol, cancellationToken);
        if (openOrders.Any(o => o.Side == side && o.Price == price && o.Quantity == quantity))
        {
            return new RiskCheckResult
            {
                IsAllowed = false,
                Reason = "Duplicate open order detected for same symbol/side/price/quantity."
            };
        }

        return new RiskCheckResult
        {
            IsAllowed = true
        };
    }
}

