using Microsoft.Extensions.DependencyInjection;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Percistance.Services;

namespace TradingBot.Percistance.Configuration;

public static class Configuration
{
    public static IServiceCollection ConfigureServices(this IServiceCollection services)
    {
        services.AddScoped<IBinanceEndpointsService, BinanceEndpointService>();
        services.AddScoped<IBinanceSettingsService, BinanceSettingsService>();
        services.AddHttpClient<IBinanceClientService, BinanceClientService>();
        return services;
    }
}
