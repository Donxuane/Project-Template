using TradingBot.Domain.Enums.General;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;
using TradingBot.Domain.Models.App;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;

namespace TradingBot.Percistance.Services.Shared;

public sealed class TimeSyncService(
    IRedisCacheService redisCacheService,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : ITimeSyncService
{
    public const string RedisKeyTimestampOffset = "Binance:FuturesTestnet:TimestampOffsetMs";

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
        var baseUrl = configuration["FuturesTestnet:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "https://demo-fapi.binance.com";

        var client = httpClientFactory.CreateClient();
        var beforeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var response = await client.GetFromJsonAsync<ServerTimeResponse>(
                           $"{baseUrl.TrimEnd('/')}/fapi/v1/time",
                           cancellationToken)
                       ?? throw new InvalidOperationException("Binance Futures testnet did not return its server time.");
        var afterMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var serverMs = response.ServerTime;
        var localMidpointMs = beforeMs + (afterMs - beforeMs) / 2;
        var offset = serverMs - localMidpointMs;

        await redisCacheService.SetCacheValue(RedisKeyTimestampOffset, offset);
        return serverMs;
    }
}
