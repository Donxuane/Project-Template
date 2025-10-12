using TradingBot.Domain.Interfaces.Services.Cache;
using TradingBot.Shared.Shared.Enums;

namespace TradingBot.Percistance.Services.Shared
{
    public class CacheService(Func<CacheType, IBaseCacheService> _baseCahceService) : ICacheService
    {
       
        public async Task<T?> SetCacheValueAsync<T>(string key, T value, CacheType cacheType = CacheType.Memory)
        {
            var _inner = _baseCahceService(cacheType);
            return _inner switch
            {
                IRedisCacheService redis => await redis.SetCacheValue(key, value),
                IMemoryCacheService memory => memory.SetCacheValue(key, value),
                _ => throw new InvalidOperationException($"Unsupported cache service type: {_inner.GetType().Name}"),
            };
        }

        public async Task<T?> GetCacheValueAsync<T>(string key, CacheType cacheType = CacheType.Memory)
        {
            var _inner = _baseCahceService(cacheType);
            return _inner switch
            {
                IRedisCacheService redis => await redis.GetCacheValue<T>(key),
                IMemoryCacheService memory => memory.GetCacheValue<T>(key),
                _ => throw new InvalidOperationException($"Unsupported cache service type: {_inner.GetType().Name}"),
            };
        }

        public async Task RemoveCacheValueAsync(string key, CacheType cacheType = CacheType.Memory)
        {
            var _inner = _baseCahceService(cacheType);
            switch (_inner)
            {
                case IRedisCacheService redis:
                    await redis.RemoveCacheValue(key);
                    break;

                case IMemoryCacheService memory:
                    memory.RemoveCacheValue(key);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported cache service type: {_inner.GetType().Name}");
            }
        }

        public async Task<List<object?>?> GetAllCachedDataAsync(List<string> keys, CacheType cacheType = CacheType.Memory)
        {
            var _inner = _baseCahceService(cacheType);
            return _inner switch
            {
                IRedisCacheService redis => await redis.GetAllCachedData(keys),
                IMemoryCacheService memory => memory.GetAllCachedData(keys),
                _ => throw new InvalidOperationException($"Unsupported cache service type: {_inner.GetType().Name}"),
            };
        }
    }
}
