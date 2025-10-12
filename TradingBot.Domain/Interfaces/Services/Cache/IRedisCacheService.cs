namespace TradingBot.Domain.Interfaces.Services.Cache;

public interface IRedisCacheService: IBaseCacheService
{
    public Task<TRequest?> SetCacheValue<TRequest>(string key, TRequest value);
    public Task<TResponse?> GetCacheValue<TResponse>(string key);
    public Task RemoveCacheValue(string key);
    public Task<List<object?>?> GetAllCachedData(List<string> keys);
}
