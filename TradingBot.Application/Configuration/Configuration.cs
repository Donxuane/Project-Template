using Microsoft.Extensions.DependencyInjection;
using MediatR;
using TradingBot.Application.API;
using TradingBot.Application.BackgroundHostService;

namespace TradingBot.Application.Configuration;

public static class Configuration
{
    public static IServiceCollection ConfigApplication(this IServiceCollection services)
    {
        services.AddScoped<AccountApi>();
        services.AddScoped<TradingApi>();
        services.AddScoped<GeneralApi>();
        services.AddHostedService<OrderSyncWorker>();

        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssemblies(typeof(Configuration).Assembly);
        });
        services.AddAutoMapper(typeof(Configuration).Assembly);
        return services;
    }
}
