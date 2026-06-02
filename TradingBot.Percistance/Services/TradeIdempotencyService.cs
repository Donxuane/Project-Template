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

    public Task<bool> IsDuplicateDecisionAsync(string decisionId, int windowSeconds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(decisionId))
            return Task.FromResult(false);

        if (windowSeconds <= 0)
            return Task.FromResult(false);

        if (IsCachedDuplicate(decisionId, windowSeconds))
            return Task.FromResult(true);

        return IsRedisDuplicateAsync(decisionId);
    }

    public async Task MarkDecisionExecutedAsync(string decisionId, int windowSeconds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(decisionId))
            return;

        if (windowSeconds <= 0)
            return;

        var nowUtc = DateTime.UtcNow;
        DecisionCacheUtc[decisionId] = nowUtc;
        PurgeExpiredCacheEntries(nowUtc.AddSeconds(-windowSeconds));

        try
        {
            var db = redis.GetDatabase();
            var redisKey = $"{RedisKeyPrefix}:{decisionId}";
            await db.StringSetAsync(
                redisKey,
                nowUtc.ToString("O"),
                TimeSpan.FromSeconds(windowSeconds));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TradeIdempotencyService Redis unavailable while marking executed decision. Local cache retained.");
        }
    }

    public async Task<bool> TryRegisterDecisionAsync(string decisionId, int windowSeconds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(decisionId))
            return false;

        if (windowSeconds <= 0)
            return true;

        var nowUtc = DateTime.UtcNow;
        PurgeExpiredCacheEntries(nowUtc.AddSeconds(-windowSeconds));

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

    private static bool IsCachedDuplicate(string decisionId, int windowSeconds)
    {
        var nowUtc = DateTime.UtcNow;
        PurgeExpiredCacheEntries(nowUtc.AddSeconds(-windowSeconds));
        return DecisionCacheUtc.TryGetValue(decisionId, out var markedUtc)
               && markedUtc >= nowUtc.AddSeconds(-windowSeconds);
    }

    private async Task<bool> IsRedisDuplicateAsync(string decisionId)
    {
        try
        {
            var db = redis.GetDatabase();
            var redisKey = $"{RedisKeyPrefix}:{decisionId}";
            return await db.KeyExistsAsync(redisKey);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TradeIdempotencyService Redis unavailable during duplicate check. Falling back to local cache only.");
            return false;
        }
    }

    private static void PurgeExpiredCacheEntries(DateTime cutoffUtc)
    {
        foreach (var item in DecisionCacheUtc)
        {
            if (item.Value < cutoffUtc)
                DecisionCacheUtc.TryRemove(item.Key, out _);
        }
    }
}
