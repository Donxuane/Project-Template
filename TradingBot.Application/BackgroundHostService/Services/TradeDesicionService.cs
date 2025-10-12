using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.GeneralApis;
using TradingBot.Shared.Shared.Enums;

namespace TradingBot.Application.BackgroundHostService.Services;

public class TradeDesicionService(IToolService toolService, ILogger<TradeDesicionService> logger)
{
    public async Task MakeDesicion()
    {

    }

    public async Task<ExchangeInfoResponse> GetExchangeInformation(TradingSymbol symbol)
    {

        var exchangeInformation = await toolService.CacheService.GetCacheValueAsync<ExchangeInfoResponse>($"{symbol}_ExchangeInformation",CacheType.Redis);
        if (exchangeInformation == null)
        {
            var exchangeInformationEndpoint = toolService.BinanceEndpointsService.GetEndpoint(GeneralApis.ExchangeInformation);
            exchangeInformation = await toolService.BinanceClientService.Call<ExchangeInfoResponse, CurrencyPairs>(new CurrencyPairs
            {
                Symbol = symbol.ToString()
            }, exchangeInformationEndpoint, false);
            var cache = await toolService.CacheService.SetCacheValueAsync($"{symbol}_ExchangeInformation", exchangeInformation, CacheType.Redis);
        }
        return exchangeInformation;
    }

    private async Task<TimeSpan> TimeStampoffset()
    {
        var offset = await toolService.CacheService.GetCacheValueAsync<TimeSpan>("TimeStampOffset");
        if (offset == null)
        {
            var serverTimeEndpoint = toolService.BinanceEndpointsService.GetEndpoint(GeneralApis.CheckServerTime);
            var serverTimeResult = await toolService.BinanceClientService.Call<ServerTimeResponse, EmptyResult>(null, serverTimeEndpoint, false);
            var serverTime = DateTimeOffset.FromUnixTimeMilliseconds(serverTimeResult.ServerTime).UtcDateTime;
            var time = serverTime - DateTime.UtcNow;
            await toolService.CacheService.SetCacheValueAsync("TimeStampOffset", time);
            offset = time;
        }
        return offset;
    }
}
