using Microsoft.Extensions.Logging;
using Serilog.Core;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;
using TradingBot.Domain.Models.GeneralApis;

namespace TradingBot.Percistance.Services;

public class TimeSyncService(IRedisCacheService redisCacheService, 
    IToolService toolService) : ITimeSyncService
{
    public const string RedisKeyTimestampOffset = "Binance:TimestampOffsetMs";

    public async Task<long> GetAdjustedTimestampAsync(CancellationToken cancellationToken = default)
    {
        var offsetMs = await redisCacheService.GetCacheValue<long?>(RedisKeyTimestampOffset);
        if(offsetMs == null)
        {
            var serverTimeEndpoint = toolService.BinanceEndpointsService.GetEndpoint(GeneralApis.CheckServerTime);
            var response = await toolService.BinanceClientService.Call<ServerTimeResponse, EmptyRequest>(
                null, serverTimeEndpoint, false);

            var serverMs = response.ServerTime;
            var local = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var offset = serverMs - local;

            await redisCacheService.SetCacheValue(RedisKeyTimestampOffset, offset);
            return serverMs;
        }
        var localMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (offsetMs.HasValue)
            return localMs + offsetMs.Value;
        return localMs;
    }
}
