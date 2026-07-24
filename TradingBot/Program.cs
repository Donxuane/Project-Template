using Serilog;
using Serilog.Debugging;
using TradingBot.Application.Configuration;
using TradingBot.Percistance.Configuration;

var builder = WebApplication.CreateBuilder(args);

SelfLog.Enable(Console.Error);
builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.WithMachineName()
    .Enrich.WithProperty("Application", "TradingBot")
    .Enrich.FromLogContext());

builder.Services.AddSpotFuturesInfrastructure(builder.Configuration);
builder.Services.AddSpotFuturesFeature(builder.Configuration);

var app = builder.Build();

app.UseSerilogRequestLogging();

app.MapGet("/", () => Results.Ok(new
{
    Service = "SpotFuturesCrossMarket",
    Environment = "Binance USD-M Futures Testnet"
}));

try
{
    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "SpotFuturesCrossMarket terminated unexpectedly.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
