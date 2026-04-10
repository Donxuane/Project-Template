using Microsoft.Extensions.DependencyInjection;
using MediatR;
using TradingBot.Application.API;
using TradingBot.Application.BackgroundHostService;
using TradingBot.Application.BackgroundHostService.Services;
using TradingBot.Application.DecisionEngine;
using TradingBot.Application.Services;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Decision;

namespace TradingBot.Application.Configuration;

public static class Configuration
{
    public static IServiceCollection ConfigApplication(this IServiceCollection services)
    {
        services.AddScoped<AccountApi>();
        services.AddScoped<TradingApi>();
        services.AddScoped<GeneralApi>();
        //Background Services
        //services.AddHostedService<OrderSyncWorker>();
        //services.AddHostedService<TradeSyncWorker>();
        //services.AddHostedService<PositionWorker>();
        //services.AddHostedService<BalanceSyncWorker>();
        //services.AddHostedService<TimeSyncWorker>();
        //services.AddHostedService<MarketDataWorker>();
        services.AddHostedService<PositionReconciliationWorker>();
        services.AddHostedService<DecisionWorker>();

        services.AddHostedService<TradeMonitorWorker>();
        services.AddHostedService<AnalyticsWorker>();

        services.AddScoped<IDecisionService, DecisionService>();
        services.AddScoped<IStrategy, MovingAverageCrossoverStrategy>();
        services.AddScoped<IMarketDataProvider, BinanceMarketDataProvider>();
        services.AddScoped<IRiskEvaluator, RiskEvaluator>();
        services.AddScoped<IAIValidator, NoOpAIValidator>();
        services.AddScoped<TradeDesicionService>();
        services.AddScoped<ITradeExecutionService, TradeExecutionService>();
        services.AddScoped<ITradeCooldownService, TradeCooldownService>();
        services.AddScoped<ITradeAnalyticsService, TradeAnalyticsService>();

        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssemblies(typeof(Configuration).Assembly);
        });
        services.AddAutoMapper(typeof(Configuration).Assembly);
        return services;
    }
}
