using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;
using TradingBot.Domain.Models.GeneralApis;
using TradingBot.Percistance.Services;

namespace TradingBot.Application.BackgroundHostService;

/// <summary>
/// Calls GET /api/v3/time every 60s, computes timestamp offset (server - local), stores in Redis.
/// Signed requests use the offset via ITimeSyncService.
/// </summary>
public class TimeSyncWorker(IServiceScopeFactory scopeFactory, ILogger<TimeSyncWorker> logger) : BackgroundService
{
    private const int IntervalSeconds = 60;
    private const int RetryDelaySeconds = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TimeSyncWorker started. Interval: {Interval}s", IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncTimeAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "TimeSyncWorker cycle failed at {Time}. Retrying in {Delay}s",
                    DateTime.UtcNow, RetryDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(IntervalSeconds), stoppingToken);
        }

        logger.LogInformation("TimeSyncWorker stopped.");
    }

    private async Task SyncTimeAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var toolService = scope.ServiceProvider.GetRequiredService<IToolService>();
        var redisCacheService = scope.ServiceProvider.GetRequiredService<IRedisCacheService>();

        var serverTimeEndpoint = toolService.BinanceEndpointsService.GetEndpoint(GeneralApis.CheckServerTime);
        var response = await toolService.BinanceClientService.Call<ServerTimeResponse, EmptyRequest>(
            null, serverTimeEndpoint, false);

        var serverMs = response.ServerTime;
        var localMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var offsetMs = serverMs - localMs;

        await redisCacheService.SetCacheValue(TimeSyncService.RedisKeyTimestampOffset, offsetMs);
        logger.LogDebug("TimeSyncWorker: offset {OffsetMs} ms (server {ServerMs}, local {LocalMs})", offsetMs, serverMs, localMs);
    }
}
