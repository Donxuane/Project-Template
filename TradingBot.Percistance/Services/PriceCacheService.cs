using System.Globalization;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;

namespace TradingBot.Percistance.Services;

public class PriceCacheService(IRedisCacheService redisCacheService, ILogger<PriceCacheService> logger) : IPriceCacheService
{
    public static string RedisKeyPrice(TradingSymbol symbol) => $"MarketData:Price:{symbol}";

    public async Task<decimal?> GetCachedPriceAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
    {
        var key = RedisKeyPrice(symbol);
        var value = await redisCacheService.GetCacheValue<string>(key);
        if (string.IsNullOrEmpty(value))
            return null;
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) ? price : null;
    }

    public async Task SetCachedPriceAsync(TradingSymbol symbol, decimal price, CancellationToken cancellationToken = default)
    {
        var key = RedisKeyPrice(symbol);
        await redisCacheService.SetCacheValue(key, price.ToString(CultureInfo.InvariantCulture));
    }
}
