using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Application.BackgroundHostService;

public class JobWorker(IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var toolService = scope.ServiceProvider.GetRequiredService<IToolService>();

        }
    }
}
