namespace TradingBot.Domain.Interfaces.Services;

public interface IMemoryCacheService
{
    public TRequest? SetCacheValue<TRequest>(string key, TRequest value);
    public TResponse? GetCacheValue<TResponse>(string key);
    public void RemoveCacheValue(string key);
    public List<object?>? GetAllCachedData(List<string> keys);
}
