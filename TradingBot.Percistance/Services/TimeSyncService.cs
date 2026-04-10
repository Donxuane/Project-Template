using Microsoft.Extensions.Logging;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;

namespace TradingBot.Percistance.Services;

public class TimeSyncService(IRedisCacheService redisCacheService, ILogger<TimeSyncService> logger) : ITimeSyncService
{
    public const string RedisKeyTimestampOffset = "Binance:TimestampOffsetMs";

    public async Task<long> GetAdjustedTimestampAsync(CancellationToken cancellationToken = default)
    {
        var offsetMs = await redisCacheService.GetCacheValue<long?>(RedisKeyTimestampOffset);
        var localMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (offsetMs.HasValue)
            return localMs + offsetMs.Value;
        return localMs;
    }
}
