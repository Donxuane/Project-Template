using TradingBot.Shared.Shared.Enums;

namespace TradingBot.Domain.Interfaces.Services.Cache;

public interface ICacheService
{
    public Task<T?> SetCacheValueAsync<T>(string key, T value, CacheType cacheType = CacheType.Memory);
    public Task<T?> GetCacheValueAsync<T>(string key, CacheType cacheType = CacheType.Memory);
    public Task RemoveCacheValueAsync(string key, CacheType cacheType = CacheType.Memory);
    public Task<List<object?>?> GetAllCachedDataAsync(List<string> keys, CacheType cacheType = CacheType.Memory);
}
