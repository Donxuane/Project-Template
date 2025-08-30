using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.ExternalServices;
using TradingBot.Domain.Models;
using TradingBot.Domain.Models.GeneralApis;
using TradingBot.Domain.Models.MarketData;

namespace TradingBot.Percistance.ExternalServices;

public class BinanceSettingsService(
    ISlicerService slicerService, 
    IBinanceClientService client,
    IBinanceEndpointsService endpointService,
    IMemoryCacheService cache,
    ILogger<BinanceSettingsService> logger,
    IOrderValidator orderValidator) : IBinanceSettingsService
{
    public async Task<List<RateLimit>?>? GetRateLimitterSettings(RateLimitType type, TradingSymbol symbol)
    {
        var key = $"{symbol}_ExchangeInformation";
        try
        {
            var cacheData = cache.GetCacheValue(key);
            if (cacheData == null) 
            {
                var endpoint = endpointService.GetEndpoint(GeneralApis.ExchangeInformation);
                var exchangeInformation = await client.Call<ExchangeInfoResponse, CurrencyPairs>(
                    new CurrencyPairs
                    {
                        Symbol = symbol.ToString()
                    }, endpoint, false
                );
                var data = cache.SetCacheValue(key, exchangeInformation);
                cacheData = data;
            }
            if (cacheData.GetType() == typeof(ExchangeInfoResponse) && cacheData != null)
            {
                var retunValue = (ExchangeInfoResponse)cacheData;
                return retunValue.RateLimits.Where(x => x.RateLimitType == type.ToString()).ToList();
            }
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                @"Exception: {ex},  
                  DateTime: {date}
                  Key: {key}",
                ex.Message,
                DateTime.Now,
                key
            );
            return null;
        }
    }

    public async Task<(decimal? price, decimal? qty)> ValidatePrice(TradingSymbol symbol, decimal price)
    {
        var key = $"{symbol}_ExchangeInformation";
        try
        {
            var cacheData = cache.GetCacheValue(key);
            if (cacheData == null)
            {
                var endpoint = endpointService.GetEndpoint(GeneralApis.ExchangeInformation);
                var exchangeInformation = await client.Call<ExchangeInfoResponse, CurrencyPairs>(
                    new CurrencyPairs
                    {
                        Symbol = symbol.ToString()
                    }, endpoint, false
                );
                var data = cache.SetCacheValue(key, exchangeInformation);
                cacheData = data;
            }
            if(cacheData.GetType() == typeof(ExchangeInfoResponse) && cacheData != null)
            {
                var value = (ExchangeInfoResponse)cacheData;
                var filter = value.Symbols.FirstOrDefault().Filters;
                var endpoint = endpointService.GetEndpoint(MarketData.SymbolPriceTicker);
                var response = await client.Call<SymbolPriceTickerResponse, SymbolPriceTickerRequest>(
                    new SymbolPriceTickerRequest
                    {
                        Symbol = symbol.ToString()
                    }, endpoint, false);
                var slice = slicerService.GetSliceAmount(response, price);
                var validateOrder = orderValidator.ValidatedOrder(price, slice, new OrderFilters
                {
                    LotSize = filter?.FirstOrDefault(x => x.FilterType == FilterTypes.LOT_SIZE.ToString()) as LotSizeFilter,
                    MinNotional = filter?.FirstOrDefault(x => x.FilterType == FilterTypes.NOTIONAL.ToString()) as NotionalFilter,
                    PriceFilter = filter?.FirstOrDefault(x => x.FilterType == FilterTypes.PRICE_FILTER.ToString()) as PriceFilter
                });
                var checkSlice = slicerService.GetSliceAmount(response, validateOrder.price);
                return (validateOrder.price, checkSlice);
            }
            return (null, null);
        }
        catch(Exception ex)
        {
            logger.LogError(
                ex,
                @"Exception: {ex},  
                  DateTime: {date}
                  Key: {key}",
                ex.Message,
                DateTime.Now,
                key
            );
            return (null,null);
        }
    }
}
