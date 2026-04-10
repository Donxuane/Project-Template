using System.Collections.Concurrent;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;

namespace TradingBot.Application.BackgroundHostService.Services;

public class TradeCooldownService(IRedisCacheService redisCacheService) : ITradeCooldownService
{
    private const string RedisKeyPrefix = "Trading:LastTradeUtc";
    private static readonly ConcurrentDictionary<TradingSymbol, DateTime> LastTradeBySymbolUtc = new();

    public async Task<CooldownCheckResult> CheckCooldownAsync(
        TradingSymbol symbol,
        int cooldownSeconds,
        CancellationToken cancellationToken = default)
    {
        if (cooldownSeconds <= 0)
            return new CooldownCheckResult { IsInCooldown = false, RemainingSeconds = 0 };

        var nowUtc = DateTime.UtcNow;
        if (LastTradeBySymbolUtc.TryGetValue(symbol, out var localLastTradeUtc))
        {
            var localRemaining = RemainingSeconds(nowUtc, localLastTradeUtc, cooldownSeconds);
            if (localRemaining > 0)
            {
                return new CooldownCheckResult
                {
                    IsInCooldown = true,
                    RemainingSeconds = localRemaining,
                    LastTradeAtUtc = localLastTradeUtc
                };
            }
        }

        var redisLastTradeEpochMs = await redisCacheService.GetCacheValue<long?>(GetRedisKey(symbol));
        if (!redisLastTradeEpochMs.HasValue || redisLastTradeEpochMs.Value <= 0)
            return new CooldownCheckResult { IsInCooldown = false, RemainingSeconds = 0 };

        var redisLastTradeUtc = DateTimeOffset.FromUnixTimeMilliseconds(redisLastTradeEpochMs.Value).UtcDateTime;
        LastTradeBySymbolUtc.AddOrUpdate(symbol, redisLastTradeUtc, (_, _) => redisLastTradeUtc);

        var redisRemaining = RemainingSeconds(nowUtc, redisLastTradeUtc, cooldownSeconds);
        return new CooldownCheckResult
        {
            IsInCooldown = redisRemaining > 0,
            RemainingSeconds = Math.Max(0, redisRemaining),
            LastTradeAtUtc = redisLastTradeUtc
        };
    }

    public async Task MarkTradeExecutedAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;
        LastTradeBySymbolUtc.AddOrUpdate(symbol, nowUtc, (_, _) => nowUtc);

        var epochMs = new DateTimeOffset(nowUtc).ToUnixTimeMilliseconds();
        await redisCacheService.SetCacheValue(GetRedisKey(symbol), epochMs);
    }

    private static int RemainingSeconds(DateTime nowUtc, DateTime lastTradeUtc, int cooldownSeconds)
    {
        var elapsed = nowUtc - lastTradeUtc;
        if (elapsed >= TimeSpan.FromSeconds(cooldownSeconds))
            return 0;

        return Math.Max(1, cooldownSeconds - (int)elapsed.TotalSeconds);
    }

    private static string GetRedisKey(TradingSymbol symbol) => $"{RedisKeyPrefix}:{symbol}";
}
