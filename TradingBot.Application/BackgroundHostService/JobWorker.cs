using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.GeneralApis;
using TradingBot.Percistance.Services.Main;
using TradingBot.Shared.Shared.Models;

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
                var toolService = scope.ServiceProvider.GetRequiredService<IToolService>();

                var serverTimeEndpoint = toolService.BinanceEndpointsService.GetEndpoint(Domain.Enums.Endpoints.GeneralApis.CheckServerTime);
                var serverTime = await toolService.BinanceClientService.Call<ServerTimeResponse,EmptyResult>(null,serverTimeEndpoint,false);
                Console.WriteLine($"Server time: {serverTime.ServerTime}");

                await Task.Delay(TimeSpan.FromSeconds(50), stoppingToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Worker error: {ex.Message}");
            }

        }
    }
}
