using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.SpotFuturesCrossMarket;

public sealed class AdaptiveRollingFuturesFeeService(
    IFuturesTestnetClient futuresClient,
    IAdaptiveRollingProfitExitRepository repository,
    IConnectionMultiplexer redis,
    ILogger<AdaptiveRollingFuturesFeeService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<FuturesCommissionRate> ResolveAsync(
        TradingSymbol symbol,
        AdaptiveRollingProfitExitV1Settings settings,
        CancellationToken cancellationToken = default)
    {
        var cached = await TryGetCachedRateAsync(symbol, settings);
        if (IsFresh(cached, settings))
            return cached!;

        var latest = cached ?? await repository.GetLatestCommissionRateAsync(
            settings.ApplicationId,
            SpotFuturesCrossMarketSettings.ExecutionEnvironment,
            settings.AccountKey,
            symbol,
            cancellationToken);

        if (IsFresh(latest, settings))
        {
            await CacheRateAsync(latest!, settings);
            return latest!;
        }

        var refreshed = await TryRefreshAsync(symbol, settings, cancellationToken);
        if (refreshed is not null)
            return refreshed;

        var fallback = BuildFallback(symbol, settings, latest is null ? "NoFreshCommissionRate" : "CommissionRateExpiredOrRefreshFailed");
        await repository.InsertCommissionRateAsync(fallback, cancellationToken);
        await CacheRateAsync(fallback, settings);
        logger.LogWarning(
            "AdaptiveRollingProfitExitV1 using conservative futures fee fallback. Symbol={Symbol} TakerRate={TakerRate} Reason={Reason}",
            symbol,
            fallback.TakerCommissionRate,
            fallback.FallbackReason);
        return fallback;
    }

    public async Task<FuturesCommissionRate?> TryRefreshAsync(
        TradingSymbol symbol,
        AdaptiveRollingProfitExitV1Settings settings,
        CancellationToken cancellationToken = default)
    {
        var db = redis.GetDatabase();
        var lockKey = FeeRefreshLockKey(symbol, settings);
        var lockValue = Guid.NewGuid().ToString("N");
        var lockAcquired = false;

        try
        {
            lockAcquired = await db.StringSetAsync(
                lockKey,
                lockValue,
                TimeSpan.FromSeconds(settings.FeeRefreshLockSeconds),
                when: When.NotExists);

            if (!lockAcquired)
            {
                logger.LogInformation("AdaptiveRollingProfitExitV1 fee refresh skipped; lock held. Symbol={Symbol}", symbol);
                return null;
            }

            for (var attempt = 1; attempt <= settings.FeeRefreshMaxRetries; attempt++)
            {
                try
                {
                    var response = await futuresClient.GetCommissionRateAsync(symbol.ToString(), cancellationToken);
                    var now = DateTime.UtcNow;
                    var rate = new FuturesCommissionRate
                    {
                        ApplicationId = settings.ApplicationId,
                        ExecutionEnvironment = SpotFuturesCrossMarketSettings.ExecutionEnvironment,
                        AccountKey = settings.AccountKey,
                        Symbol = symbol,
                        MakerCommissionRate = response.MakerCommissionRate > 0m
                            ? response.MakerCommissionRate
                            : settings.ConservativeFallbackMakerCommissionRate,
                        TakerCommissionRate = response.TakerCommissionRate > 0m
                            ? response.TakerCommissionRate
                            : settings.ConservativeFallbackTakerCommissionRate,
                        RpiCommissionRate = response.RpiCommissionRate,
                        Source = "BinanceFuturesCommissionRate",
                        IsFallback = response.TakerCommissionRate <= 0m,
                        FallbackReason = response.TakerCommissionRate <= 0m ? "EndpointReturnedNonPositiveTakerRate" : null,
                        FetchedAtUtc = now
                    };

                    await repository.InsertCommissionRateAsync(rate, cancellationToken);
                    await CacheRateAsync(rate, settings);
                    logger.LogInformation(
                        "AdaptiveRollingProfitExitV1 futures fee refreshed. Symbol={Symbol} MakerRate={MakerRate} TakerRate={TakerRate} Source={Source}",
                        symbol,
                        rate.MakerCommissionRate,
                        rate.TakerCommissionRate,
                        rate.Source);
                    return rate;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var delayMs = 250 * attempt + Random.Shared.Next(0, 250);
                    logger.LogWarning(
                        ex,
                        "AdaptiveRollingProfitExitV1 futures fee refresh failed. Symbol={Symbol} Attempt={Attempt}/{Attempts} DelayMs={DelayMs}",
                        symbol,
                        attempt,
                        settings.FeeRefreshMaxRetries,
                        delayMs);

                    if (attempt < settings.FeeRefreshMaxRetries)
                        await Task.Delay(delayMs, cancellationToken);
                }
            }

            return null;
        }
        finally
        {
            if (lockAcquired)
            {
                try
                {
                    var current = await db.StringGetAsync(lockKey);
                    if (current == lockValue)
                        await db.KeyDeleteAsync(lockKey);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "AdaptiveRollingProfitExitV1 fee refresh lock cleanup failed. Symbol={Symbol}", symbol);
                }
            }
        }
    }

    public async Task<decimal> ResolveSignedFundingAsync(
        Position position,
        AdaptiveRollingProfitExitV1Settings settings,
        decimal lastKnownFunding,
        CancellationToken cancellationToken = default)
    {
        if (!position.OpenedAt.HasValue)
            return lastKnownFunding;

        var db = redis.GetDatabase();
        var cacheKey = FundingCacheKey(position, settings);
        try
        {
            var cached = await db.StringGetAsync(cacheKey);
            if (cached.HasValue && decimal.TryParse(cached.ToString(), out var cachedFunding))
                return cachedFunding;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AdaptiveRollingProfitExitV1 funding cache read failed. PositionId={PositionId}", position.Id);
        }

        try
        {
            var rows = await futuresClient.GetIncomeAsync(
                position.Symbol.ToString(),
                "FUNDING_FEE",
                position.OpenedAt.Value,
                DateTime.UtcNow.AddMinutes(1),
                1000,
                cancellationToken);

            var funding = rows.Sum(x => x.Income);
            await db.StringSetAsync(
                cacheKey,
                funding.ToString(System.Globalization.CultureInfo.InvariantCulture),
                TimeSpan.FromMinutes(settings.FundingRefreshIntervalMinutes));
            return funding;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "AdaptiveRollingProfitExitV1 funding income refresh failed; using last known funding. PositionId={PositionId} Symbol={Symbol}",
                position.Id,
                position.Symbol);
            return lastKnownFunding;
        }
    }

    private async Task<FuturesCommissionRate?> TryGetCachedRateAsync(TradingSymbol symbol, AdaptiveRollingProfitExitV1Settings settings)
    {
        try
        {
            var db = redis.GetDatabase();
            var raw = await db.StringGetAsync(FeeCacheKey(symbol, settings));
            if (!raw.HasValue)
                return null;

            return JsonSerializer.Deserialize<FuturesCommissionRate>(raw!, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AdaptiveRollingProfitExitV1 fee cache read failed. Symbol={Symbol}", symbol);
            return null;
        }
    }

    private async Task CacheRateAsync(FuturesCommissionRate rate, AdaptiveRollingProfitExitV1Settings settings)
    {
        try
        {
            var db = redis.GetDatabase();
            await db.StringSetAsync(
                FeeCacheKey(rate.Symbol, settings),
                JsonSerializer.Serialize(rate, JsonOptions),
                TimeSpan.FromMinutes(settings.FeeCacheTtlMinutes));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AdaptiveRollingProfitExitV1 fee cache write failed. Symbol={Symbol}", rate.Symbol);
        }
    }

    private static bool IsFresh(FuturesCommissionRate? rate, AdaptiveRollingProfitExitV1Settings settings)
        => rate is not null
           && rate.TakerCommissionRate > 0m
           && DateTime.UtcNow - rate.FetchedAtUtc <= TimeSpan.FromMinutes(settings.FeeFreshMaxAgeMinutes);

    private static FuturesCommissionRate BuildFallback(
        TradingSymbol symbol,
        AdaptiveRollingProfitExitV1Settings settings,
        string reason)
        => new()
        {
            ApplicationId = settings.ApplicationId,
            ExecutionEnvironment = SpotFuturesCrossMarketSettings.ExecutionEnvironment,
            AccountKey = settings.AccountKey,
            Symbol = symbol,
            MakerCommissionRate = settings.ConservativeFallbackMakerCommissionRate,
            TakerCommissionRate = settings.ConservativeFallbackTakerCommissionRate,
            RpiCommissionRate = null,
            Source = "ConfiguredConservativeFallback",
            IsFallback = true,
            FallbackReason = reason,
            FetchedAtUtc = DateTime.UtcNow
        };

    private static string FeeCacheKey(TradingSymbol symbol, AdaptiveRollingProfitExitV1Settings settings)
        => $"{AdaptiveRollingProfitExitV1Settings.FeatureName}:{settings.ApplicationId}:{SpotFuturesCrossMarketSettings.ExecutionEnvironment}:{settings.AccountKey}:Fee:{symbol}";

    private static string FeeRefreshLockKey(TradingSymbol symbol, AdaptiveRollingProfitExitV1Settings settings)
        => $"{AdaptiveRollingProfitExitV1Settings.FeatureName}:{settings.ApplicationId}:{SpotFuturesCrossMarketSettings.ExecutionEnvironment}:{settings.AccountKey}:FeeRefreshLock:{symbol}";

    private static string FundingCacheKey(Position position, AdaptiveRollingProfitExitV1Settings settings)
        => $"{AdaptiveRollingProfitExitV1Settings.FeatureName}:{settings.ApplicationId}:{SpotFuturesCrossMarketSettings.ExecutionEnvironment}:{settings.AccountKey}:Funding:{position.Id}";
}

public sealed class AdaptiveRollingFuturesFeeRefreshWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AdaptiveRollingFuturesFeeRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = AdaptiveRollingProfitExitV1Settings.Load(configuration);
        if (!settings.Enabled)
        {
            logger.LogInformation("AdaptiveRollingProfitExitV1 fee refresh worker disabled by config.");
            return;
        }

        var crossSettings = SpotFuturesCrossMarketSettings.Load(configuration, AppContext.BaseDirectory);
        logger.LogInformation(
            "AdaptiveRollingProfitExitV1 fee refresh worker started. Symbols={Symbols} RefreshMinutes={RefreshMinutes}",
            string.Join(",", crossSettings.Symbols),
            settings.FeeRefreshIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var feeService = scope.ServiceProvider.GetRequiredService<AdaptiveRollingFuturesFeeService>();
                foreach (var symbol in crossSettings.Symbols)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    await feeService.TryRefreshAsync(symbol, settings, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AdaptiveRollingProfitExitV1 fee refresh cycle failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(settings.FeeRefreshIntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
