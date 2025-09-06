using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.API;

namespace TradingBot.Application.Configuration;

public static class Configuration
{
    public static IServiceCollection ConfigApplication(this IServiceCollection services)
    {
        services.AddScoped<AccountApi>();
        services.AddScoped<TradingApi>();
        return services;
    }
}
