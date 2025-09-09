using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.GeneralApis;

namespace TradingBot.Application.BackgroundHostService.Services;

public class TradeDesicionService(IToolService toolService, ILogger<TradeDesicionService> logger)
{
    public async Task MakeDesicion()
    {

    }

    public async Task<ExchangeInfoResponse> GetExchangeInformation(TradingSymbol symbol)
    {

        var exchangeInformation = toolService.MemoryCacheService.GetCacheValue<ExchangeInfoResponse>($"{symbol}_ExchangeInformation");
        if (exchangeInformation == null)
        {
            var exchangeInformationEndpoint = toolService.BinanceEndpointsService.GetEndpoint(GeneralApis.ExchangeInformation);
            exchangeInformation = await toolService.BinanceClientService.Call<ExchangeInfoResponse, CurrencyPairs>(new CurrencyPairs
            {
                Symbol = symbol.ToString()
            }, exchangeInformationEndpoint, false);
            var cache = toolService.MemoryCacheService.SetCacheValue($"{symbol}_ExchangeInformation", exchangeInformation);
        }
        return exchangeInformation;
    }

    private async Task<TimeSpan> TimeStampoffset()
    {
        var offset = toolService.MemoryCacheService.GetCacheValue<TimeSpan>("TimeStampOffset");
        if (offset == null)
        {
            var serverTimeEndpoint = toolService.BinanceEndpointsService.GetEndpoint(GeneralApis.CheckServerTime);
            var serverTimeResult = await toolService.BinanceClientService.Call<ServerTimeResponse, EmptyResult>(null, serverTimeEndpoint, false);
            var serverTime = DateTimeOffset.FromUnixTimeMilliseconds(serverTimeResult.ServerTime).UtcDateTime;
            var time = serverTime - DateTime.UtcNow;
            toolService.MemoryCacheService.SetCacheValue("TimeStampOffset", time);
            offset = time;
        }
        return offset;
    }
}
