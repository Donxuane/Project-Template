using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.SpotFuturesCrossMarket;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Application.Configuration;

public static class Configuration
{
    public static IServiceCollection AddSpotFuturesFeature(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.AddScoped<SpotFuturesCrossMarket.SpotFuturesCrossMarketDataService>();
        services.AddScoped<SpotFuturesCrossMarket.SpotFuturesCrossMarketSignalEngine>();
        services.AddScoped<SpotFuturesCrossMarket.SpotFuturesCrossMarketAccounting>();
        services.AddScoped<SpotFuturesCrossMarket.SpotFuturesCrossMarketCloseOrderService>();
        services.AddScoped<SpotFuturesCrossMarket.SpotFuturesCrossMarketReportWriter>();
        services.AddSingleton<SpotFuturesCrossMarket.AdaptiveRollingFuturesMarketDataService>();
        services.AddScoped<SpotFuturesCrossMarket.AdaptiveRollingFuturesFeeService>();
        services.AddHostedService<SpotFuturesCrossMarket.SpotFuturesCrossMarketStartupValidator>();
        services.AddHostedService<SpotFuturesCrossMarket.SpotFuturesCrossMarketTestnetV1Worker>();
        services.AddHostedService<SpotFuturesCrossMarket.AdaptiveRollingFuturesFeeRefreshWorker>();
        services.AddHostedService<SpotFuturesCrossMarket.AdaptiveRollingProfitExitV1Worker>();

        services.Configure<TrendStateSettings>(configuration.GetSection(TrendStateSettings.SectionName));
        services.AddScoped<ITrendStateService, TrendStateService>();
        services.AddScoped<IAtrService, AtrService>();
        return services;
    }
}
