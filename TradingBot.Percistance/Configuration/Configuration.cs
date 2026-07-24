using System.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;
using TradingBot.Percistance.Repositories;
using TradingBot.Percistance.Services.Main;
using TradingBot.Percistance.Services.Shared;

namespace TradingBot.Percistance.Configuration;

public static class Configuration
{
    public static IServiceCollection AddSpotFuturesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ITradeExecutionRepository, TradeExecutionRepository>();
        services.AddScoped<ITradeExecutionDecisionsRepository, TradeExecutionDecisionsRepository>();
        services.AddScoped<ISpotFuturesCrossMarketEvaluationRepository, SpotFuturesCrossMarketEvaluationRepository>();
        services.AddScoped<IAdaptiveRollingProfitExitRepository, AdaptiveRollingProfitExitRepository>();
        services.AddScoped<IRedisCacheService, RedisCacheService>();

        services.AddHttpClient<IFuturesTestnetClient, FuturesTestnetClient>((sp, client) =>
        {
            var currentConfiguration = sp.GetRequiredService<IConfiguration>();
            var baseUrl = currentConfiguration["FuturesTestnet:BaseUrl"];
            client.BaseAddress = new Uri(string.IsNullOrWhiteSpace(baseUrl)
                ? "https://demo-fapi.binance.com"
                : baseUrl);
            client.Timeout = TimeSpan.FromSeconds(
                Math.Max(1, currentConfiguration.GetValue<int?>("FuturesTestnet:HttpTimeoutSeconds") ?? 15));
        });

        services.AddHttpClient<ISpotMarketDataClient, SpotMarketDataClient>((sp, client) =>
        {
            var currentConfiguration = sp.GetRequiredService<IConfiguration>();
            var baseUrl = currentConfiguration["SpotMarketData:BaseUrl"];
            client.BaseAddress = new Uri(string.IsNullOrWhiteSpace(baseUrl)
                ? "https://testnet.binance.vision"
                : baseUrl);
            client.Timeout = TimeSpan.FromSeconds(
                Math.Max(1, currentConfiguration.GetValue<int?>("SpotMarketData:HttpTimeoutSeconds") ?? 15));
        });

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var host = configuration["Redis:Host"];
            if (string.IsNullOrWhiteSpace(host))
                throw new InvalidOperationException("Redis:Host is required.");

            var options = ConfigurationOptions.Parse(host);
            var password = configuration["Redis:Password"];
            if (!string.IsNullOrWhiteSpace(password))
                options.Password = password;
            return ConnectionMultiplexer.Connect(options);
        });

        services.AddScoped<IDbConnection>(_ =>
        {
            var connectionString = configuration.GetConnectionString("MainStorage");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("ConnectionStrings:MainStorage is required.");
            return new NpgsqlConnection(connectionString);
        });

        return services;
    }
}
