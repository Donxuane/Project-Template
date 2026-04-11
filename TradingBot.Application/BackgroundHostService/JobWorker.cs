using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Application.BackgroundHostService;

public class JobWorker(IServiceProvider serviceProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try 
            {
                using var scope = serviceProvider.CreateScope();
                var timeSyncService = scope.ServiceProvider.GetRequiredService<ITimeSyncService>();

                var adjustedTimestamp = await timeSyncService.GetAdjustedTimestampAsync(stoppingToken);
                Console.WriteLine($"Adjusted timestamp: {adjustedTimestamp}");

                await Task.Delay(TimeSpan.FromSeconds(50), stoppingToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Worker error: {ex.Message}");
            }

        }
    }
}
