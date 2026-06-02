using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Application.BackgroundHostService;

public sealed class FeeProfitGuardExpectedMoveBlockReportWorker(
    IFeeProfitGuardExpectedMoveBlockObservability observability,
    IConfiguration configuration,
    ILogger<FeeProfitGuardExpectedMoveBlockReportWorker> logger) : BackgroundService
{
    private const int DefaultIntervalMinutes = 30;
    private const int MinIntervalMinutes = 30;
    private const int MaxIntervalMinutes = 60;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = Math.Clamp(
            configuration.GetValue<int?>("Trading:FeeProfitGuardExpectedMoveReportIntervalMinutes") ?? DefaultIntervalMinutes,
            MinIntervalMinutes,
            MaxIntervalMinutes);
        var reportingWindow = TimeSpan.FromMinutes(intervalMinutes);

        logger.LogInformation(
            "FeeProfitGuardExpectedMoveBlockReportWorker started. IntervalMinutes={IntervalMinutes}",
            intervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(reportingWindow, stoppingToken);

                var minExpectedMovePercent = Math.Max(
                    0m,
                    configuration.GetValue<decimal?>("Trading:MinExpectedMovePercent") ?? 0.3m);
                var minNetProfitPercent = Math.Max(
                    0m,
                    configuration.GetValue<decimal?>("Trading:MinNetProfitPercent") ?? 0.15m);

                observability.FlushAndLog(
                    minExpectedMovePercent,
                    minNetProfitPercent,
                    reportingWindow);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "FeeProfitGuardExpectedMoveBlockReportWorker cycle failed at {Time}", DateTime.UtcNow);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        logger.LogInformation("FeeProfitGuardExpectedMoveBlockReportWorker stopped.");
    }
}
