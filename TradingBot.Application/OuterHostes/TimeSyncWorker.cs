using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TradingBot.Domain.Enums.General;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;
using TradingBot.Domain.Models.App;
using TradingBot.Percistance.Services.Shared;

namespace TradingBot.Application.OuterHostes
{
    public class TimeSyncWorker(IServiceScopeFactory scopeFactory, ILogger<TimeSyncWorker> logger) : BackgroundService
    {
        private const int IntervalSeconds = 20;
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
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
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
            var timeSyncService = scope.ServiceProvider.GetRequiredService<ITimeSyncService>();
            var serverMs = await timeSyncService.RefreshOffsetAsync(cancellationToken);
            logger.LogDebug(
                "TimeSyncWorker synchronized with Binance Futures testnet. ServerTimeMs={ServerTimeMs}",
                serverMs);
        }
    }
}
