using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace TradingBot.Configuration;

public static class Configuration
{
    public static IServiceCollection LoggerConfigure(this IServiceCollection services)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateLogger();
        services.AddLogging(options =>
        {
            options.ClearProviders();
            options.AddSerilog();
        });
        return services;
    }

    public static void ConfigurationExtention(this WebApplicationBuilder builder)
    {
        builder.Configuration
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory()))
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile("platformSettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();
    }
}
