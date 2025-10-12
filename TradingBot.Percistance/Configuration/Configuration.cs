using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;
using TradingBot.Percistance.Services;
using TradingBot.Percistance.Services.Main;
using TradingBot.Percistance.Services.Shared;
using TradingBot.Shared.Shared.Enums;
using TradingBot.Shared.Shared.Settings;

namespace TradingBot.Percistance.Configuration;

public static class Configuration
{
    public static IServiceCollection ConfigureServices(this IServiceCollection services, IConfiguration configuration)
    {
        //services
        services.AddScoped<IBinanceEndpointsService, BinanceEndpointService>();
        services.AddScoped<IBinanceSettingsService, BinanceSettingsService>();
        services.AddScoped<ISlicerService, SlicerService>();
        services.AddScoped<IMemoryCacheService, MemoryCacheService>();
        services.AddScoped<IRedisCacheService, RedisCacheService>();
        services.AddScoped<IOrderValidator, OrderValidator>();
        services.AddScoped<ICacheService, CacheService>();

        //factories
        services.AddScoped<Func<IBinanceClientService>>(x=>x.GetRequiredService<IBinanceClientService>);
        services.AddScoped<Func<IBinanceSettingsService>>(x => x.GetRequiredService<IBinanceSettingsService>);
        services.AddScoped<Func<ISlicerService>>(x => x.GetRequiredService<ISlicerService>);
        services.AddScoped<Func<IBinanceEndpointsService>>(x => x.GetRequiredService<IBinanceEndpointsService>);
        services.AddScoped<Func<CacheType,IBaseCacheService>>(x => key =>
        {
            return key switch
            {
                CacheType.Memory => x.GetRequiredService<IMemoryCacheService>(),
                CacheType.Redis => x.GetRequiredService<IRedisCacheService>(),
                _ => throw new NotImplementedException("Service not found")
            };
        });
        services.AddScoped<Func<ICacheService>>(x => x.GetRequiredService<ICacheService>);
        services.AddScoped<Func<IOrderValidator>>(x => x.GetRequiredService<IOrderValidator>);
        services.AddScoped<Func<IAICLinetService>>(x => x.GetRequiredService<IAICLinetService>);

        //orchestrators
        services.AddScoped<IToolService,ToolService>();

        services.AddMemoryCache();
        //client services
        services.AddHttpClient<IBinanceClientService, BinanceClientService>();
        services.AddHttpClient<IAICLinetService, AIClientService>();

        services.AddSingleton<IConnectionMultiplexer>(x =>
        {
            var settings = configuration.GetSection("RedisSettings").Get<RedisSettings>()!;
            var configOptions = new ConfigurationOptions
            {
                EndPoints = { settings.Host },
                Password = settings.Password,
            };
            return ConnectionMultiplexer.Connect(configOptions);
        });
        return services;
    }
}
