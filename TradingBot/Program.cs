using TradingBot.Application.Configuration;
using TradingBot.Percistance.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSpotFuturesInfrastructure(builder.Configuration);
builder.Services.AddSpotFuturesFeature(builder.Configuration);

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    Service = "SpotFuturesCrossMarket",
    Environment = "Binance USD-M Futures Testnet"
}));

app.Run();
