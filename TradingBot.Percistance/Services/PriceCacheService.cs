using System.Globalization;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;
using TradingBot.Domain.Models.MarketData;

namespace TradingBot.Percistance.Services;

public class PriceCacheService(IRedisCacheService redisCacheService, ILogger<PriceCacheService> logger) : IPriceCacheService
{
    public static string RedisKeyPrice(TradingSymbol symbol) => $"MarketData:Price:{symbol}";

    public async Task<decimal?> GetCachedPriceAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetCachedPriceSnapshotAsync(symbol, cancellationToken);
        return snapshot?.Price;
    }

    public async Task<PriceSnapshot?> GetCachedPriceSnapshotAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
    {
        var key = RedisKeyPrice(symbol);
        var snapshot = await redisCacheService.GetCacheValue<PriceSnapshot>(key);
        if (snapshot is not null && snapshot.Price > 0m)
            return snapshot;

        // Backward compatibility for older cache payloads that only stored raw numeric strings.
        var raw = await redisCacheService.GetCacheValue<string>(key);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) || price <= 0m)
            return null;

        return new PriceSnapshot
        {
            Price = price,
            AsOfUtc = DateTime.UtcNow,
            Source = "RedisTicker"
        };
    }

    public async Task SetCachedPriceAsync(TradingSymbol symbol, decimal price, CancellationToken cancellationToken = default)
    {
        await SetCachedPriceSnapshotAsync(symbol, price, "RedisTicker", cancellationToken);
    }

    public async Task SetCachedPriceSnapshotAsync(
        TradingSymbol symbol,
        decimal price,
        string source,
        CancellationToken cancellationToken = default)
    {
        var key = RedisKeyPrice(symbol);
        await redisCacheService.SetCacheValue(
            key,
            new PriceSnapshot
            {
                Price = price,
                AsOfUtc = DateTime.UtcNow,
                Source = source
            });
    }
}
