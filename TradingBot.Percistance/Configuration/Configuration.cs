using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;
using System.Data;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Percistance.Services;
using TradingBot.Percistance.Services.Main;
using TradingBot.Percistance.Services.Shared;
using TradingBot.Percistance.Repositories;
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
        services.AddScoped<IRedisCacheService, RedisCacheService>();
        services.AddScoped<IOrderValidator, OrderValidator>();
        services.AddScoped<IOrderStatusService, OrderStatusService>();
        services.AddScoped<ITimeSyncService, TimeSyncService>();
        services.AddSingleton<IBinanceRateLimiter, BinanceRateLimiter>();
        services.AddSingleton<ITradeIdempotencyService, TradeIdempotencyService>();
        services.AddScoped<IPriceCacheService, PriceCacheService>();
        services.AddScoped<IBinanceOrderNormalizationService, BinanceOrderNormalizationService>();

        //repositories
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ITradeExecutionRepository, TradeExecutionRepository>();
        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IBalanceRepository, BalanceRepository>();
        services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();
        services.AddScoped<IReportingsRepository, ReportingsRepository>();
        services.AddScoped<ITradeExecutionDesicionsRepository, TradeExecutionDesicionsRepository>();
        services.AddScoped<ITradingHealthDiagnosticsRepository, TradingHealthDiagnosticsRepository>();

        //risk management
        services.AddScoped<IRiskManagementService, RiskManagementService>();

        //factories
        services.AddScoped<Func<IBinanceClientService>>(x => x.GetRequiredService<IBinanceClientService>);
        services.AddScoped<Func<IBinanceSettingsService>>(x => x.GetRequiredService<IBinanceSettingsService>);
        services.AddScoped<Func<ISlicerService>>(x => x.GetRequiredService<ISlicerService>);
        services.AddScoped<Func<IBinanceEndpointsService>>(x => x.GetRequiredService<IBinanceEndpointsService>);
        services.AddScoped<Func<IOrderValidator>>(x => x.GetRequiredService<IOrderValidator>);
        services.AddScoped<Func<IAICLinetService>>(x => x.GetRequiredService<IAICLinetService>);
        services.AddScoped<Func<IRedisCacheService>>(x => x.GetRequiredService<IRedisCacheService>);

        //orchestrators
        services.AddScoped<IToolService, ToolService>();
        //client services
        services.AddHttpClient<IBinanceClientService, BinanceClientService>((sp, client) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var baseUrl = cfg.GetValue<string>("BaseURL");
            if (!string.IsNullOrWhiteSpace(baseUrl))
                client.BaseAddress = new Uri(baseUrl);

            var timeoutSeconds = Math.Max(1, cfg.GetValue<int?>("Binance:Http:TimeoutSeconds") ?? 15);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });
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

        services.AddScoped<IDbConnection>(x =>
        {
            var mainStorage = configuration.GetSection("ConnectionStrings").Get<ConnectionStrings>()!;
            return new NpgsqlConnection(mainStorage.MainStorage);
        });
        return services;
    }

    public static IServiceCollection AddSettings(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ConnectionStrings>(configuration.GetSection("ConnectionStrings"));
        return services;
    }
}
