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
        services.AddHostedService<OrderSyncWorker>();
        services.AddHostedService<TradeSyncWorker>();
        services.AddHostedService<PositionWorker>();
        services.AddHostedService<BalanceSyncWorker>();
        services.AddHostedService<TimeSyncWorker>();
        services.AddHostedService<MarketDataWorker>();
        services.AddHostedService<PositionReconciliationWorker>();
        services.AddHostedService<DecisionWorker>();

        services.AddHostedService<TradeMonitorWorker>();
        services.AddHostedService<AnalyticsWorker>();

        services.AddScoped<IDecisionService, DecisionService>();
        services.AddScoped<IMovingAverageStrategy, MovingAverageTrendStrategy>();
        services.AddScoped<IStrategy>(sp => sp.GetRequiredService<IMovingAverageStrategy>());
        services.AddScoped<IMarketDataProvider, BinanceMarketDataProvider>();
        services.AddSingleton<ICandleService, CandleService>();
        services.AddSingleton<ICandleWarmupService, CandleWarmupService>();
        services.AddSingleton<IMarketStateTracker, MarketStateTracker>();
        services.AddSingleton<IPositionManager, PositionManager>();
        services.AddScoped<IDataRequirementResolver, DataRequirementResolver>();
        services.AddScoped<ITrendStateService, TrendStateService>();
        services.AddScoped<IAtrService, AtrService>();
        services.AddScoped<IVolatilityService, VolatilityService>();
        services.AddScoped<IMarketConditionService, MarketConditionService>();
        services.AddScoped<IRiskEvaluator, RiskEvaluator>();
        services.AddScoped<IAIValidator, NoOpAIValidator>();
        services.AddScoped<TradeDecisionService>();
        services.AddScoped<ITradeExecutionService, TradeExecutionService>();
        services.AddScoped<ITradeCooldownService, TradeCooldownService>();
        services.AddScoped<IPositionExecutionGuard, PositionExecutionGuard>();
        services.AddScoped<IFeeProfitGuard, FeeProfitGuard>();
        services.AddScoped<IConfidenceGate, ConfidenceGate>();
        services.AddScoped<ITradeAnalyticsService, TradeAnalyticsService>();
        services.AddScoped<IPositionAccountingService, PositionAccountingService>();
        services.AddScoped<IPositionReconciliationService, PositionReconciliationService>();

        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssemblies(typeof(Configuration).Assembly);
        });
        services.AddAutoMapper(typeof(Configuration).Assembly);
        return services;
    }
}
