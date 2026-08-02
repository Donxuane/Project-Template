using Serilog;
using TradingBot;
using TradingBot.Application.Configuration;
using TradingBot.Percistance.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.ConfigurationExtention();

builder.Services.AddSpotFuturesInfrastructure(builder.Configuration);
builder.Services.AddSpotFuturesFeature(builder.Configuration);
builder.Services.LoggerConfigure(builder.Configuration);


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
