using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Extentions;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Percistance.Services.Main;

public class RiskManagementService(
    IConfiguration configuration,
    IPositionRepository positionRepository,
    IOrderRepository orderRepository,
    IBalanceRepository balanceRepository,
    IPriceCacheService priceCacheService) : IRiskManagementService
{
    private decimal MaxPositionQuote => configuration.GetValue<decimal?>("RiskSettings:MaxPositionQuote") ?? 10_000m;
    private decimal MaxOrderQuote => configuration.GetValue<decimal?>("RiskSettings:MaxOrderQuote") ?? 5_000m;
    private decimal MaxExposurePercent => configuration.GetValue<decimal?>("RiskSettings:MaxExposurePercent") ?? 50m;

    public async Task<RiskCheckResult> CheckOrderAsync(TradingSymbol symbol, OrderSide side, decimal quantity, decimal? price = null, CancellationToken cancellationToken = default)
    {
        var resolvedPrice = price ?? await priceCacheService.GetCachedPriceAsync(symbol, cancellationToken);
        if (!resolvedPrice.HasValue || resolvedPrice <= 0)
        {
            return new RiskCheckResult
            {
                IsAllowed = false,
                Reason = "Price not available from cache. Ensure MarketDataWorker is running."
            };
        }

        var notional = quantity * resolvedPrice.Value;

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
        var newNotional = Math.Abs(newQty) * resolvedPrice.Value;

        if (newNotional > MaxPositionQuote)
        {
            return new RiskCheckResult
            {
                IsAllowed = false,
                Reason = $"Position notional {newNotional} exceeds MaxPositionQuote {MaxPositionQuote}."
            };
        }

        var balances = await balanceRepository.GetLatestForAllAsync(cancellationToken);
        var totalQuote = balances.Sum(b => (b.Symbol == symbol.ToAssests() ? (b.Free + b.Locked) * resolvedPrice.Value : 0m));

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

        var openOrders = await orderRepository.GetOpenOrdersAsync(symbol, null, cancellationToken);
        if (openOrders.Any(o => o.Side == side && o.Price == resolvedPrice.Value && o.Quantity == quantity))
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

    public async Task<RiskCheckResult> ValidateTrade(TradingSymbol symbol, decimal quantity, decimal price, OrderSide side, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0 || price <= 0)
        {
            return new RiskCheckResult
            {
                IsAllowed = false,
                Reason = "Quantity and price must be greater than zero."
            };
        }

        var maxPositionSize = configuration.GetValue<decimal?>("RiskSettings:MaxPositionSize") ?? 1m;
        var stopLossPercent = configuration.GetValue<decimal?>("RiskSettings:DefaultStopLossPercent") ?? 1.0m;
        var takeProfitPercent = configuration.GetValue<decimal?>("RiskSettings:DefaultTakeProfitPercent") ?? 2.0m;
        var usdtAsset = configuration.GetValue<string>("RiskSettings:QuoteAsset") ?? "USDT";
        var maxLeverage = configuration.GetValue<decimal?>("RiskSettings:MaxLeverage") ?? 1.0m;

        var currentPosition = await positionRepository.GetOpenPositionAsync(symbol, cancellationToken);
        var currentQuantity = currentPosition?.Quantity ?? 0m;
        var projectedQuantity = side == OrderSide.BUY
            ? currentQuantity + quantity
            : Math.Max(0m, currentQuantity - quantity);

        if (projectedQuantity > maxPositionSize)
        {
            return new RiskCheckResult
            {
                IsAllowed = false,
                Reason = $"Projected position size {projectedQuantity} exceeds MaxPositionSize {maxPositionSize}."
            };
        }

        var balances = await balanceRepository.GetLatestForAllAsync(cancellationToken);

        if (side == OrderSide.BUY)
        {
            var availableQuote = balances
                .Where(x => x.Asset.Equals(usdtAsset, StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.Free);
            var requiredQuote = quantity * price;

            if (availableQuote < requiredQuote)
            {
                return new RiskCheckResult
                {
                    IsAllowed = false,
                    Reason = $"Insufficient {usdtAsset} balance. Required={requiredQuote:F6}, Available={availableQuote:F6}."
                };
            }
        }
        else
        {
            var baseAsset = symbol.ToString().Replace(usdtAsset, string.Empty, StringComparison.OrdinalIgnoreCase);
            var availableBase = balances
                .Where(x => x.Asset.Equals(baseAsset, StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.Free);

            if (availableBase < quantity)
            {
                return new RiskCheckResult
                {
                    IsAllowed = false,
                    Reason = $"Insufficient {baseAsset} balance. Required={quantity:F6}, Available={availableBase:F6}."
                };
            }
        }

        // Simulated leverage guard for spot environments: projected quote exposure must stay under equity * max leverage.
        var quoteEquity = balances
            .Where(x => x.Asset.Equals(usdtAsset, StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.Free + x.Locked);
        var projectedExposure = projectedQuantity * price;
        var leverageLimit = quoteEquity * Math.Max(1m, maxLeverage);
        if (quoteEquity > 0m && projectedExposure > leverageLimit)
        {
            return new RiskCheckResult
            {
                IsAllowed = false,
                Reason = $"Projected exposure {projectedExposure:F6} exceeds leverage limit {leverageLimit:F6} ({maxLeverage:F2}x)."
            };
        }

        decimal? stopLossPrice = null;
        decimal? takeProfitPrice = null;
        var stopLossFactor = stopLossPercent / 100m;
        var takeProfitFactor = takeProfitPercent / 100m;

        if (side == OrderSide.BUY)
        {
            stopLossPrice = price * (1m - stopLossFactor);
            takeProfitPrice = price * (1m + takeProfitFactor);
        }
        else
        {
            stopLossPrice = price * (1m + stopLossFactor);
            takeProfitPrice = price * (1m - takeProfitFactor);
        }

        return new RiskCheckResult
        {
            IsAllowed = true,
            Reason = "Risk checks passed.",
            StopLossPrice = stopLossPrice,
            TakeProfitPrice = takeProfitPrice
        };
    }
}

