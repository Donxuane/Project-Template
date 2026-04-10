using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Application.BackgroundHostService;

/// <summary>
/// Periodically computes and logs trading performance analytics.
/// </summary>
public class AnalyticsWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AnalyticsWorker> logger) : BackgroundService
{
    private const int DefaultIntervalMinutes = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = configuration.GetValue<bool?>("TradeAnalytics:Enabled") ?? false;
        if (!enabled)
        {
            logger.LogInformation("AnalyticsWorker disabled by configuration (TradeAnalytics:Enabled=false).");
            return;
        }

        var intervalMinutes = Math.Max(1, configuration.GetValue<int?>("TradeAnalytics:IntervalMinutes") ?? DefaultIntervalMinutes);
        logger.LogInformation("AnalyticsWorker started. Interval={IntervalMinutes}m", intervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var analyticsService = scope.ServiceProvider.GetRequiredService<ITradeAnalyticsService>();
                var analyticsRepository = scope.ServiceProvider.GetRequiredService<IAnalyticsRepository>();
                var summary = await analyticsService.GetSummary(stoppingToken);
                await analyticsRepository.StoreAnalytics(summary);
                logger.LogInformation(
                    "Trade analytics summary: Trades={TotalTrades} | WinRate={WinRate:F2}% | PnL={TotalPnl:+0.####;-0.####;0} USDT | AvgWin={AverageWin:F4} | AvgLoss={AverageLoss:F4} | MaxDD={MaxDrawdown:F4}",
                    summary.TotalTrades,
                    summary.WinRate,
                    summary.TotalPnl,
                    summary.AverageWin,
                    summary.AverageLoss,
                    summary.MaxDrawdown);

                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AnalyticsWorker cycle failed at {Time}", DateTime.UtcNow);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        logger.LogInformation("AnalyticsWorker stopped.");
    }
}
