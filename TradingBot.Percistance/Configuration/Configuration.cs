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
        services.AddScoped<ISlicerService, SlicerService>();
        services.AddScoped<IMemoryCacheService, MemoryCacheService>();
        services.AddScoped<IOrderValidator, OrderValidator>();

        services.AddScoped<Func<IBinanceClientService>>(x=>x.GetRequiredService<IBinanceClientService>);
        services.AddScoped<Func<IBinanceSettingsService>>(x => x.GetRequiredService<IBinanceSettingsService>);
        services.AddScoped<Func<ISlicerService>>(x => x.GetRequiredService<ISlicerService>);
        services.AddScoped<Func<IBinanceEndpointsService>>(x => x.GetRequiredService<IBinanceEndpointsService>);
        services.AddScoped<Func<IMemoryCacheService>>(x => x.GetRequiredService<IMemoryCacheService>);
        services.AddScoped<Func<IOrderValidator>>(x => x.GetRequiredService<IOrderValidator>);

        services.AddScoped<IToolService,ToolService>();
        services.AddMemoryCache();
        services.AddHttpClient<IBinanceClientService, BinanceClientService>();
        return services;
    }
}
