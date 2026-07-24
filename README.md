# SpotFuturesCrossMarket

This branch contains only the synchronized Spot/Futures testnet feature and its adaptive
rolling-profit exit. It uses public Binance Spot testnet candles as signal context and places
orders only on Binance USD-M Futures Testnet.

## Configuration

Non-secret runtime values live in `TradingBot/appsettings.json`. Credentials must be supplied
outside Git:

```powershell
dotnet user-secrets --project TradingBot set "FuturesTestnet:ApiKey" "<testnet-api-key>"
dotnet user-secrets --project TradingBot set "FuturesTestnet:SecretKey" "<testnet-secret-key>"
```

For deployments and the scripts under `tools`, use environment variables:

```powershell
$env:FuturesTestnet__ApiKey = "<testnet-api-key>"
$env:FuturesTestnet__SecretKey = "<testnet-secret-key>"
```

Connection strings and Redis values can also be overridden with the normal .NET configuration
names, for example `ConnectionStrings__MainStorage`, `Redis__Host`, and `Redis__Password`.

Serilog console output and minimum levels are configured under `Serilog` in
`TradingBot/appsettings.json`. Logs are enriched with the machine name and application name,
and ASP.NET request logging is enabled.

The Futures client rejects Binance mainnet hosts before sending a request. The feature also
refuses to start with ordering enabled unless both testnet credentials are available.

## Build and test

```powershell
dotnet build TradingBot.sln
dotnet test TradingBot.Application.Tests/TradingBot.Application.Tests.csproj
dotnet run --project TradingBot
```

Database setup order is documented in `TradingBot.Percistance/DatabaseScripts/README.md`.
