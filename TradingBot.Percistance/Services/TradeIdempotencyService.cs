using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Percistance.Services;

public class TradeIdempotencyService(
    IConnectionMultiplexer redis,
    ILogger<TradeIdempotencyService> logger) : ITradeIdempotencyService
{
    private const string RedisKeyPrefix = "Trading:DecisionIdempotency";
    private static readonly ConcurrentDictionary<string, DateTime> DecisionCacheUtc = new();

    public async Task<bool> TryRegisterDecisionAsync(string decisionId, int windowSeconds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(decisionId))
            return false;

        if (windowSeconds <= 0)
            return true;

        var nowUtc = DateTime.UtcNow;
        var cutoff = nowUtc.AddSeconds(-windowSeconds);
        foreach (var item in DecisionCacheUtc)
        {
            if (item.Value < cutoff)
                DecisionCacheUtc.TryRemove(item.Key, out _);
        }

        if (!DecisionCacheUtc.TryAdd(decisionId, nowUtc))
            return false;

        try
        {
            var db = redis.GetDatabase();
            var redisKey = $"{RedisKeyPrefix}:{decisionId}";
            var set = await db.StringSetAsync(
                redisKey,
                nowUtc.ToString("O"),
                TimeSpan.FromSeconds(windowSeconds),
                when: When.NotExists);

            if (!set)
            {
                DecisionCacheUtc.TryRemove(decisionId, out _);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // Fail-open to local cache so runtime can continue even if Redis temporarily fails.
            logger.LogWarning(ex, "TradeIdempotencyService Redis unavailable. Falling back to local idempotency cache.");
            return true;
        }
    }
}
