namespace TradingBot.Domain.Interfaces.ExternalServices;

public interface IMemoryCacheService
{
    public object? SetCacheValue(string key, object value);
    public object? GetCacheValue(string key);
    public void RemoveCacheValue(string key);
    public List<object?>? GetAllCachedData(List<string> keys);
}
