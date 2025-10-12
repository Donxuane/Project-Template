using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using System.Threading.Tasks;
using TradingBot.Domain.Interfaces.Services.Cache;

namespace TradingBot.Percistance.Services.Shared;

public class RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger): IRedisCacheService
{
    public async Task<List<object?>?> GetAllCachedData(List<string> keys)
    {
        try
        {
            var allData = new List<object?>();
            foreach (var key in keys)
            {
                var database = redis.GetDatabase();
                var value = await database.StringGetAsync(key);
                allData.Add(value);
            }
            return allData;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                @"Exception: {ex},  
                  DateTime: {date}",
                ex.Message,
                DateTime.Now
            );
            return null;
        }
    }

    public async Task<TResponse?> GetCacheValue<TResponse>(string key)
    {
        try
        {
            var database = redis.GetDatabase();
            var value = await database.StringGetAsync(key);
            logger.LogInformation(
                @"Get Cached Data:
                model: {model}
                DateTime: {time}",
                value,
                DateTime.Now
            );
            return value.HasValue ? JsonSerializer.Deserialize<TResponse>(value!) : default;
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
            return default;
        }
    }

    public async Task RemoveCacheValue(string key)
    {
        try
        {
            var database = redis.GetDatabase();
            await database.KeyDeleteAsync(key);
            logger.LogInformation(
                @"removed key: {key}
                    DateTime: {time}",
                key,
                DateTime.Now
            );

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
        }
    }

    public async Task<TRequest?> SetCacheValue<TRequest>(string key, TRequest value)
    {
        try
        {
            var database = redis.GetDatabase();
            var serilized = JsonSerializer.Serialize(value);
            var set = await database.StringSetAsync(key, serilized, TimeSpan.FromHours(24));
            logger.LogInformation(
                @"Cached Data
                model: {model}
                DateTime: {time}",
                value,
                DateTime.Now
            );
            return value;
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
            return default;
        }
    }
}
