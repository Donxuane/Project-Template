using TradingBot.Domain.Enums.General;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;

namespace TradingBot.Percistance.Services.Shared;

public class TimeSyncService(IRedisCacheService redisCacheService, 
    IBinanceEndpointsService binanceEndpointsService) : ITimeSyncService
{
    public const string RedisKeyTimestampOffset = "Binance:TimestampOffsetMs";

    public async Task<long> GetAdjustedTimestampAsync(CancellationToken cancellationToken = default)
    {
        var offsetMs = await redisCacheService.GetCacheValue<long?>(RedisKeyTimestampOffset);
        if (offsetMs == null)
        {
            return await RefreshOffsetAsync(cancellationToken);
        }
        var localMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (offsetMs.HasValue)
            return localMs + offsetMs.Value;
        return localMs;
    }

    public async Task<long> RefreshOffsetAsync(CancellationToken cancellationToken = default)
    {
        var serverTimeEndpoint = binanceEndpointsService.GetEndpoint(GeneralApis.CheckServerTime);
        var response = await toolService.BinanceClientService.Call<ServerTimeResponse, EmptyRequest>(
            null, serverTimeEndpoint, false);

        var serverMs = response.ServerTime;
        var local = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var offset = serverMs - local;

        await redisCacheService.SetCacheValue(RedisKeyTimestampOffset, offset);
        return serverMs;
    }
}
