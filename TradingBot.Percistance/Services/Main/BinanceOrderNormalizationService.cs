using System.Globalization;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;
using TradingBot.Domain.Models;
using TradingBot.Domain.Models.Binance;
using TradingBot.Domain.Models.GeneralApis;
using TradingBot.Domain.Models.TradingEndpoints;

namespace TradingBot.Percistance.Services.Main;

public class BinanceOrderNormalizationService(
    IBinanceClientService binanceClientService,
    IBinanceEndpointsService endpointService,
    IRedisCacheService cacheService,
    ILogger<BinanceOrderNormalizationService> logger) : IBinanceOrderNormalizationService
{
    public async Task<BinanceSymbolFilters> GetSymbolFiltersAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var exchangeInfo = await GetExchangeInfoAsync(symbol);
        var symbolInfo = exchangeInfo.Symbols.FirstOrDefault(s => s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
        if (symbolInfo is null)
            throw new InvalidOperationException($"Exchange info for symbol '{symbol}' was not found.");

        var lotSize = symbolInfo.Filters.OfType<LotSizeFilter>().FirstOrDefault(f => f.FilterType == "LOT_SIZE");
        var priceFilter = symbolInfo.Filters.OfType<PriceFilter>().FirstOrDefault(f => f.FilterType == "PRICE_FILTER");
        var notionalFilter = symbolInfo.Filters.OfType<NotionalFilter>().FirstOrDefault(f => f.FilterType == "NOTIONAL");

        return new BinanceSymbolFilters
        {
            Symbol = symbolInfo.Symbol,
            StepSize = ParsePositiveDecimalOrNull(lotSize?.StepSize),
            MinQty = ParsePositiveDecimalOrNull(lotSize?.MinQty),
            MaxQty = ParsePositiveDecimalOrNull(lotSize?.MaxQty),
            TickSize = ParsePositiveDecimalOrNull(priceFilter?.TickSize),
            MinPrice = ParsePositiveDecimalOrNull(priceFilter?.MinPrice),
            MaxPrice = ParsePositiveDecimalOrNull(priceFilter?.MaxPrice),
            MinNotional = ParsePositiveDecimalOrNull(notionalFilter?.MinNotional),
            MaxNotional = ParsePositiveDecimalOrNull(notionalFilter?.MaxNotional)
        };
    }

    public async Task<BinanceOrderNormalizationResult> NormalizeNewOrderAsync(
        NewOrderRequest request,
        decimal? marketPrice,
        CancellationToken cancellationToken = default)
    {
        var filters = await GetSymbolFiltersAsync(request.Symbol, cancellationToken);
        return NormalizeNewOrder(request, filters, marketPrice);
    }

    public static BinanceOrderNormalizationResult NormalizeNewOrder(
        NewOrderRequest request,
        BinanceSymbolFilters filters,
        decimal? marketPrice)
    {
        var normalized = CloneRequest(request);
        var originalQuantity = request.Quantity;
        var originalPrice = request.Price;

        if (normalized.Type == Domain.Enums.Binance.OrderTypes.MARKET)
        {
            normalized.Price = null;
            normalized.TimeInForce = null;

            var hasQty = normalized.Quantity.HasValue;
            var hasQuoteQty = normalized.QuoteOrderQty.HasValue;
            if (hasQty == hasQuoteQty)
                throw new InvalidOperationException("MARKET order must include either quantity or quoteOrderQty (exactly one).");
        }
        else if (normalized.Type == Domain.Enums.Binance.OrderTypes.LIMIT)
        {
            if (!normalized.Price.HasValue || normalized.Price.Value <= 0m)
                throw new InvalidOperationException("LIMIT order requires a positive price.");
            if (!normalized.Quantity.HasValue || normalized.Quantity.Value <= 0m)
                throw new InvalidOperationException("LIMIT order requires a positive quantity.");
            if (!normalized.TimeInForce.HasValue)
                throw new InvalidOperationException("LIMIT order requires timeInForce.");
            normalized.QuoteOrderQty = null;
        }

        decimal? normalizedQuantity = null;
        if (normalized.Quantity.HasValue)
        {
            if (normalized.Quantity.Value <= 0m)
                throw new InvalidOperationException("Quantity must be greater than zero.");

            normalizedQuantity = FloorToStep(normalized.Quantity.Value, filters.StepSize);
            if (normalizedQuantity <= 0m)
                throw new InvalidOperationException("Quantity becomes zero after LOT_SIZE normalization.");

            normalized.Quantity = normalizedQuantity;
        }

        decimal? normalizedPrice = null;
        if (normalized.Type == Domain.Enums.Binance.OrderTypes.LIMIT)
        {
            normalizedPrice = FloorToStep(normalized.Price!.Value, filters.TickSize);
            if (normalizedPrice <= 0m)
                throw new InvalidOperationException("Price becomes zero after PRICE_FILTER normalization.");

            normalized.Price = normalizedPrice;
        }

        var effectivePrice = normalized.Type == Domain.Enums.Binance.OrderTypes.LIMIT
            ? normalizedPrice
            : marketPrice;
        var notional = normalized.Quantity.HasValue && effectivePrice.HasValue
            ? normalized.Quantity.Value * effectivePrice.Value
            : normalized.QuoteOrderQty;

        ValidateNormalizedOrder(normalized, filters, effectivePrice, notional);

        return new BinanceOrderNormalizationResult
        {
            Filters = filters,
            Request = normalized,
            OriginalQuantity = originalQuantity,
            NormalizedQuantity = normalizedQuantity,
            OriginalPrice = originalPrice,
            NormalizedPrice = normalizedPrice,
            EffectivePrice = effectivePrice,
            Notional = notional
        };
    }

    private async Task<ExchangeInfoResponse> GetExchangeInfoAsync(string symbol)
    {
        var cacheKey = $"{symbol}_ExchangeInformation";
        var cacheData = await cacheService.GetCacheValue<ExchangeInfoResponse>(cacheKey);
        if (cacheData is not null)
            return cacheData;

        var endpoint = endpointService.GetEndpoint(GeneralApis.ExchangeInformation);
        var exchangeInformation = await binanceClientService.Call<ExchangeInfoResponse, CurrencyPairs>(
            new CurrencyPairs { Symbol = symbol }, endpoint, false);
        await cacheService.SetCacheValue(cacheKey, exchangeInformation);

        logger.LogDebug("Fetched and cached exchange info for {Symbol}", symbol);
        return exchangeInformation;
    }

    private static void ValidateNormalizedOrder(
        NewOrderRequest request,
        BinanceSymbolFilters filters,
        decimal? effectivePrice,
        decimal? notional)
    {
        if (request.Quantity.HasValue)
        {
            var quantity = request.Quantity.Value;
            if (filters.MinQty.HasValue && quantity < filters.MinQty.Value)
                throw new InvalidOperationException($"Normalized quantity {quantity} is below minQty {filters.MinQty.Value}.");
            if (filters.MaxQty.HasValue && quantity > filters.MaxQty.Value)
                throw new InvalidOperationException($"Normalized quantity {quantity} is above maxQty {filters.MaxQty.Value}.");
            if (!IsStepAligned(quantity, filters.StepSize))
                throw new InvalidOperationException($"Normalized quantity {quantity} does not align with stepSize {filters.StepSize}.");
        }

        if (request.Type == Domain.Enums.Binance.OrderTypes.LIMIT && request.Price.HasValue)
        {
            var price = request.Price.Value;
            if (filters.MinPrice.HasValue && price < filters.MinPrice.Value)
                throw new InvalidOperationException($"Normalized price {price} is below minPrice {filters.MinPrice.Value}.");
            if (filters.MaxPrice.HasValue && price > filters.MaxPrice.Value)
                throw new InvalidOperationException($"Normalized price {price} is above maxPrice {filters.MaxPrice.Value}.");
            if (!IsStepAligned(price, filters.TickSize))
                throw new InvalidOperationException($"Normalized price {price} does not align with tickSize {filters.TickSize}.");
        }

        if (request.Quantity.HasValue && !effectivePrice.HasValue)
            throw new InvalidOperationException("Effective price is required for notional validation.");

        if (notional.HasValue)
        {
            if (filters.MinNotional.HasValue && notional.Value < filters.MinNotional.Value)
                throw new InvalidOperationException($"Order notional {notional.Value} is below minNotional {filters.MinNotional.Value}.");

            if (filters.MaxNotional.HasValue && notional.Value > filters.MaxNotional.Value)
                throw new InvalidOperationException($"Order notional {notional.Value} is above maxNotional {filters.MaxNotional.Value}.");
        }
    }

    private static NewOrderRequest CloneRequest(NewOrderRequest request)
    {
        return new NewOrderRequest
        {
            Symbol = request.Symbol,
            Side = request.Side,
            Type = request.Type,
            TimeInForce = request.TimeInForce,
            Quantity = request.Quantity,
            QuoteOrderQty = request.QuoteOrderQty,
            Price = request.Price,
            NewClientOrderId = request.NewClientOrderId,
            StrategyId = request.StrategyId,
            StrategyType = request.StrategyType,
            StopPrice = request.StopPrice,
            TrailingDelta = request.TrailingDelta,
            IcebergQty = request.IcebergQty,
            NewOrderRespType = request.NewOrderRespType,
            SelfTradePreventionMode = request.SelfTradePreventionMode,
            PegPriceType = request.PegPriceType,
            PegOffsetValue = request.PegOffsetValue,
            PegOffsetType = request.PegOffsetType,
            RecvWindow = request.RecvWindow,
            Timestamp = request.Timestamp
        };
    }

    private static bool IsStepAligned(decimal value, decimal? step)
    {
        if (!step.HasValue || step.Value <= 0m)
            return true;

        return value == FloorToStep(value, step);
    }

    private static decimal FloorToStep(decimal value, decimal? step)
    {
        if (!step.HasValue || step.Value <= 0m)
            return value;

        var normalizedSteps = Math.Floor(value / step.Value);
        return normalizedSteps * step.Value;
    }

    private static decimal? ParsePositiveDecimalOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return null;
        return parsed > 0m ? parsed : null;
    }
}
