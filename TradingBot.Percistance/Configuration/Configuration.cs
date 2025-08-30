using Microsoft.Extensions.DependencyInjection;
using TradingBot.Domain.Interfaces.ExternalServices;
using TradingBot.Percistance.ExternalServices;
using TradingBot.Percistance.Services;

namespace TradingBot.Percistance.Configuration;

public static class Configuration
{
    public static IServiceCollection ConfigureServices(this IServiceCollection services)
    {
        services.AddScoped<IBinanceEndpointsService, BinanceEndpointService>();
        services.AddScoped<IBinanceSettingsService, BinanceSettingsService>();
        services.AddScoped<ISlicerService, SlicerService>();
        services.AddScoped<IMemoryCacheService, MemoryCacheService>();
        services.AddScoped<IOrderValidator, OrderValidator>();
        services.AddMemoryCache();
        services.AddHttpClient<IBinanceClientService, BinanceClientService>();
        return services;
    }
}
