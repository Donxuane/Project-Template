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
        var columnWriters = new Dictionary<string, ColumnWriterBase>
        {
            { "message", new RenderedMessageColumnWriter() },
            { "message_template", new MessageTemplateColumnWriter() },
            { "level", new LevelColumnWriter(true, NpgsqlDbType.Varchar) },
            { "timestamp", new TimestampColumnWriter(NpgsqlDbType.TimestampTz) },
            { "exception", new ExceptionColumnWriter() },
            { "properties", new LogEventSerializedColumnWriter() },
            { "machine_name", new SinglePropertyColumnWriter("MachineName", PropertyWriteMethod.ToString, NpgsqlDbType.Varchar) },
            { "application", new SinglePropertyColumnWriter("Application", PropertyWriteMethod.ToString, NpgsqlDbType.Varchar) }
        };

        var connectionString = configuration.GetConnectionString("MainStorage");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Connection string 'MainStorage' is missing. Serilog PostgreSQL sink cannot start.");

        // If sink fails, write internal Serilog diagnostics to stderr.
        SelfLog.Enable(Console.Error);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.WithMachineName()
            .Enrich.WithProperty("Application", "TradingBot")
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.PostgreSQL(
                connectionString: connectionString,
                tableName: "app_execution_logs",
                columnOptions: columnWriters,
                needAutoCreateTable: false
            )
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
