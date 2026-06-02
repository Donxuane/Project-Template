using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Binance;
using TradingBot.Domain.Models.Trading;
using TradingBot.Domain.Models.TradingEndpoints;
using TradingBot.Shared.Configuration;

namespace TradingBot.Application.BackgroundHostService.Services;

public class SpotPositionSizingService(
    IConfiguration configuration,
    IBalanceRepository balanceRepository,
    IPriceCacheService priceCacheService,
    IBinanceOrderNormalizationService binanceOrderNormalizationService,
    ILogger<SpotPositionSizingService> logger) : ISpotPositionSizingService
{
    private const decimal HighVolatilityReductionFactor = 0.5m;
    private readonly TradingRuntimeSettings _trading = RuntimeTradingConfigResolver.ResolveTrading(configuration);

    public async Task<SpotPositionSizingResult> ResolveOpenLongQuantityAsync(
        SpotPositionSizingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_trading.UseBalanceBasedSizing)
            return ResolveFixedQuantity(request);

        return await ResolveBalanceBasedQuantityAsync(request, cancellationToken);
    }

    public async Task<SpotMinNotionalValidationResult> ValidateMinNotionalAsync(
        TradingSymbol symbol,
        decimal quantity,
        decimal price,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0m || price <= 0m)
        {
            return new SpotMinNotionalValidationResult
            {
                IsValid = false,
                Reason = "Quantity and price must be greater than zero for min-notional validation.",
                Quantity = quantity,
                Price = price,
                Notional = 0m
            };
        }

        var notional = quantity * price;
        var filters = await binanceOrderNormalizationService.GetSymbolFiltersAsync(symbol.ToString(), cancellationToken);
        var minNotional = filters.MinNotional;

        if (minNotional.HasValue && notional < minNotional.Value)
        {
            var reason =
                $"High-volatility reduced quantity notional {notional:F4} is below Binance minNotional {minNotional.Value:F4}. " +
                $"Quantity={quantity:F8}, Price={price:F8}, ReductionFactor={HighVolatilityReductionFactor:F2}.";

            logger.LogWarning(
                "Spot position sizing rejected after volatility reduction: Symbol={Symbol}, Quantity={Quantity}, Price={Price}, Notional={Notional}, MinNotional={MinNotional}, Reason={Reason}",
                symbol,
                quantity,
                price,
                notional,
                minNotional,
                reason);

            return new SpotMinNotionalValidationResult
            {
                IsValid = false,
                Reason = reason,
                Quantity = quantity,
                Price = price,
                Notional = notional,
                MinNotional = minNotional
            };
        }

        return new SpotMinNotionalValidationResult
        {
            IsValid = true,
            Quantity = quantity,
            Price = price,
            Notional = notional,
            MinNotional = minNotional
        };
    }

    private SpotPositionSizingResult ResolveFixedQuantity(SpotPositionSizingRequest request)
    {
        if (request.SymbolQuantities.TryGetValue(request.Symbol, out var symbolQuantity) && symbolQuantity > 0m)
        {
            return new SpotPositionSizingResult
            {
                IsSuccess = true,
                Quantity = symbolQuantity,
                QuantitySource = SpotQuantitySource.SymbolOverride
            };
        }

        return new SpotPositionSizingResult
        {
            IsSuccess = request.GlobalQuantity > 0m,
            Quantity = request.GlobalQuantity,
            QuantitySource = SpotQuantitySource.GlobalFallback,
            Reason = request.GlobalQuantity > 0m ? null : "Global quantity must be greater than zero."
        };
    }

    private async Task<SpotPositionSizingResult> ResolveBalanceBasedQuantityAsync(
        SpotPositionSizingRequest request,
        CancellationToken cancellationToken)
    {
        var balanceAsset = _trading.BalanceAsset;
        var reservedQuoteBalance = _trading.ReservedQuoteBalance;
        var quoteAllocationPercent = _trading.QuoteAllocationPercentPerTrade;
        var maxQuotePerTrade = _trading.MaxQuotePerTrade;
        var minQuotePerTrade = _trading.MinQuotePerTrade;

        if (!Enum.TryParse<Assets>(balanceAsset, true, out var balanceAssetId))
        {
            return RejectBalanceSizing(
                request.Symbol,
                $"Invalid Trading:BalanceAsset value '{balanceAsset}'.",
                balanceAsset,
                availableQuoteBalance: 0m,
                reservedQuoteBalance,
                quoteAllocationPercent);
        }

        var balanceSnapshot = await balanceRepository.GetLatestByAssetAsync(balanceAsset, balanceAssetId, cancellationToken);
        var availableQuoteBalance = Math.Max(0m, balanceSnapshot?.Free ?? 0m);
        var usableQuoteBalance = availableQuoteBalance - reservedQuoteBalance;

        if (usableQuoteBalance <= 0m)
        {
            return RejectBalanceSizing(
                request.Symbol,
                $"Usable {balanceAsset} balance is zero or negative after reserved balance. Available={availableQuoteBalance:F4}, Reserved={reservedQuoteBalance:F4}.",
                balanceAsset,
                availableQuoteBalance,
                reservedQuoteBalance,
                quoteAllocationPercent,
                usableQuoteBalance: usableQuoteBalance);
        }

        var desiredQuoteAmount = usableQuoteBalance * quoteAllocationPercent / 100m;
        var cappedQuoteAmount = Math.Min(Math.Max(desiredQuoteAmount, minQuotePerTrade), maxQuotePerTrade);

        if (cappedQuoteAmount > usableQuoteBalance)
        {
            return RejectBalanceSizing(
                request.Symbol,
                $"Capped quote amount {cappedQuoteAmount:F4} exceeds usable {balanceAsset} balance {usableQuoteBalance:F4}.",
                balanceAsset,
                availableQuoteBalance,
                reservedQuoteBalance,
                quoteAllocationPercent,
                desiredQuoteAmount,
                cappedQuoteAmount,
                usableQuoteBalance: usableQuoteBalance);
        }

        var currentPrice = await priceCacheService.GetCachedPriceAsync(request.Symbol, cancellationToken);
        if (!currentPrice.HasValue || currentPrice.Value <= 0m)
        {
            return RejectBalanceSizing(
                request.Symbol,
                "Current price is unavailable from cache.",
                balanceAsset,
                availableQuoteBalance,
                reservedQuoteBalance,
                quoteAllocationPercent,
                desiredQuoteAmount,
                cappedQuoteAmount,
                usableQuoteBalance: usableQuoteBalance);
        }

        var rawQuantity = cappedQuoteAmount / currentPrice.Value;
        if (rawQuantity <= 0m)
        {
            return RejectBalanceSizing(
                request.Symbol,
                "Raw quantity computed from quote amount is zero or negative.",
                balanceAsset,
                availableQuoteBalance,
                reservedQuoteBalance,
                quoteAllocationPercent,
                desiredQuoteAmount,
                cappedQuoteAmount,
                currentPrice.Value,
                rawQuantity,
                usableQuoteBalance: usableQuoteBalance);
        }

        BinanceOrderNormalizationResult normalizationResult;
        try
        {
            normalizationResult = await binanceOrderNormalizationService.NormalizeNewOrderAsync(
                new NewOrderRequest
                {
                    Symbol = request.Symbol.ToString(),
                    Side = OrderSide.BUY,
                    Type = OrderTypes.MARKET,
                    Quantity = rawQuantity
                },
                currentPrice.Value,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            decimal? minNotional = null;
            try
            {
                var filters = await binanceOrderNormalizationService.GetSymbolFiltersAsync(request.Symbol.ToString(), cancellationToken);
                minNotional = filters.MinNotional;
            }
            catch
            {
                // Keep original normalization failure reason when filter lookup fails.
            }

            var result = RejectBalanceSizing(
                request.Symbol,
                ex.Message,
                balanceAsset,
                availableQuoteBalance,
                reservedQuoteBalance,
                quoteAllocationPercent,
                desiredQuoteAmount,
                cappedQuoteAmount,
                currentPrice.Value,
                rawQuantity,
                minNotional: minNotional,
                usableQuoteBalance: usableQuoteBalance);

            LogBalanceSizing(result);
            return result;
        }

        var normalizedQuantity = normalizationResult.NormalizedQuantity ?? normalizationResult.Request.Quantity ?? 0m;
        var finalNotional = normalizationResult.Notional ?? normalizedQuantity * currentPrice.Value;
        var minNotionalValue = normalizationResult.Filters.MinNotional;

        if (minNotionalValue.HasValue && finalNotional < minNotionalValue.Value)
        {
            var result = RejectBalanceSizing(
                request.Symbol,
                $"Normalized order notional {finalNotional:F4} is below Binance minNotional {minNotionalValue.Value:F4}.",
                balanceAsset,
                availableQuoteBalance,
                reservedQuoteBalance,
                quoteAllocationPercent,
                desiredQuoteAmount,
                cappedQuoteAmount,
                currentPrice.Value,
                rawQuantity,
                normalizedQuantity,
                finalNotional,
                minNotionalValue,
                usableQuoteBalance: usableQuoteBalance);

            LogBalanceSizing(result);
            return result;
        }

        var success = new SpotPositionSizingResult
        {
            IsSuccess = true,
            Quantity = normalizedQuantity,
            QuantitySource = SpotQuantitySource.BalanceBasedSizing,
            AvailableQuoteBalance = availableQuoteBalance,
            ReservedQuoteBalance = reservedQuoteBalance,
            UsableQuoteBalance = usableQuoteBalance,
            QuoteAllocationPercentPerTrade = quoteAllocationPercent,
            DesiredQuoteAmount = desiredQuoteAmount,
            CappedQuoteAmount = cappedQuoteAmount,
            CurrentPrice = currentPrice.Value,
            RawQuantity = rawQuantity,
            NormalizedQuantity = normalizedQuantity,
            FinalNotional = finalNotional,
            MinNotional = minNotionalValue
        };

        LogBalanceSizing(success);
        return success;
    }

    private SpotPositionSizingResult RejectBalanceSizing(
        TradingSymbol symbol,
        string reason,
        string balanceAsset,
        decimal availableQuoteBalance,
        decimal reservedQuoteBalance,
        decimal quoteAllocationPercent,
        decimal? desiredQuoteAmount = null,
        decimal? cappedQuoteAmount = null,
        decimal? currentPrice = null,
        decimal? rawQuantity = null,
        decimal? normalizedQuantity = null,
        decimal? finalNotional = null,
        decimal? minNotional = null,
        decimal? usableQuoteBalance = null)
    {
        logger.LogWarning(
            "Spot balance-based sizing rejected: Symbol={Symbol}, Reason={Reason}, BalanceAsset={BalanceAsset}, AvailableQuoteBalance={AvailableQuoteBalance}, ReservedQuoteBalance={ReservedQuoteBalance}, UsableQuoteBalance={UsableQuoteBalance}, QuoteAllocationPercentPerTrade={QuoteAllocationPercentPerTrade}, DesiredQuoteAmount={DesiredQuoteAmount}, CappedQuoteAmount={CappedQuoteAmount}, CurrentPrice={CurrentPrice}, RawQuantity={RawQuantity}, NormalizedQuantity={NormalizedQuantity}, FinalNotional={FinalNotional}, MinNotional={MinNotional}, QuantitySource={QuantitySource}",
            symbol,
            reason,
            balanceAsset,
            availableQuoteBalance,
            reservedQuoteBalance,
            usableQuoteBalance ?? availableQuoteBalance - reservedQuoteBalance,
            quoteAllocationPercent,
            desiredQuoteAmount,
            cappedQuoteAmount,
            currentPrice,
            rawQuantity,
            normalizedQuantity,
            finalNotional,
            minNotional,
            SpotQuantitySource.BalanceBasedSizing);

        return new SpotPositionSizingResult
        {
            IsSuccess = false,
            Reason = reason,
            QuantitySource = SpotQuantitySource.BalanceBasedSizing,
            AvailableQuoteBalance = availableQuoteBalance,
            ReservedQuoteBalance = reservedQuoteBalance,
            UsableQuoteBalance = usableQuoteBalance ?? availableQuoteBalance - reservedQuoteBalance,
            QuoteAllocationPercentPerTrade = quoteAllocationPercent,
            DesiredQuoteAmount = desiredQuoteAmount,
            CappedQuoteAmount = cappedQuoteAmount,
            CurrentPrice = currentPrice,
            RawQuantity = rawQuantity,
            NormalizedQuantity = normalizedQuantity,
            FinalNotional = finalNotional,
            MinNotional = minNotional
        };
    }

    private void LogBalanceSizing(SpotPositionSizingResult result)
    {
        logger.LogInformation(
            "Spot position sizing resolved: IsSuccess={IsSuccess}, QuantitySource={QuantitySource}, Quantity={Quantity}, AvailableQuoteBalance={AvailableQuoteBalance}, ReservedQuoteBalance={ReservedQuoteBalance}, UsableQuoteBalance={UsableQuoteBalance}, QuoteAllocationPercentPerTrade={QuoteAllocationPercentPerTrade}, DesiredQuoteAmount={DesiredQuoteAmount}, CappedQuoteAmount={CappedQuoteAmount}, CurrentPrice={CurrentPrice}, RawQuantity={RawQuantity}, NormalizedQuantity={NormalizedQuantity}, FinalNotional={FinalNotional}, MinNotional={MinNotional}, Reason={Reason}",
            result.IsSuccess,
            result.QuantitySource,
            result.Quantity,
            result.AvailableQuoteBalance,
            result.ReservedQuoteBalance,
            result.UsableQuoteBalance,
            result.QuoteAllocationPercentPerTrade,
            result.DesiredQuoteAmount,
            result.CappedQuoteAmount,
            result.CurrentPrice,
            result.RawQuantity,
            result.NormalizedQuantity,
            result.FinalNotional,
            result.MinNotional,
            result.Reason);
    }
}
