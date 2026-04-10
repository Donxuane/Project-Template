using Microsoft.AspNetCore.Mvc;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.GeneralApis;

namespace TradingBot.Application.API;

public class GeneralApi(IToolService toolService)
{
    public async Task<long> GetServerTime()
    {
        var serverTimeResponse = await toolService.RedisCacheService.GetCacheValue<long>("ServerTime");
        if (serverTimeResponse == 0)
        {
            var serverTimeEndpoint = toolService.BinanceEndpointsService.GetEndpoint(Domain.Enums.Endpoints.GeneralApis.CheckServerTime);
            var serverTime = await toolService.BinanceClientService.Call<ServerTimeResponse, EmptyRequest>(null, serverTimeEndpoint, false);
            serverTimeResponse = serverTime.ServerTime;
            await toolService.RedisCacheService.SetCacheValue("ServerTime",serverTimeResponse);
        }
        return serverTimeResponse;
    } 
}
