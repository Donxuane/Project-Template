using Serilog;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Sinks.PostgreSQL;
using NpgsqlTypes;

namespace TradingBot.Configuration;

public static class Configuration
{
    public static IServiceCollection LoggerConfigure(this IServiceCollection services, IConfiguration configuration)
    {

       

        SelfLog.Enable(Console.Error);

        Log.Logger = new LoggerConfiguration()
            .Enrich.WithMachineName()
            .Enrich.WithProperty("Application", "TradingBot")
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();
        services.AddSerilog(Log.Logger, dispose: true);
        return services;
    }

    public static void ConfigurationExtention(this WebApplicationBuilder builder)
    {
        builder.Configuration
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory()))
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile("platformSettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("aiSettings.json",optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();
    }
}
