using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TradingBot.Application.TestnetExecution;

/// <summary>
/// Fail-fast startup guard for the ETH15 testnet execution path. The application refuses to
/// start when the configuration could possibly reach a production/mainnet endpoint, reuse the
/// live (production) Binance keys, or enable real orders.
/// </summary>
public sealed class Eth15TestnetExecutionStartupValidator(
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    ILogger<Eth15TestnetExecutionStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var section = configuration.GetSection(Eth15TestnetExecutionSettings.SectionName);
        if (!section.Exists())
        {
            logger.LogInformation("Eth15TestnetExecution section not present; testnet execution path inactive.");
            return Task.CompletedTask;
        }

        // Load throws immediately if AllowRealOrders is set true anywhere.
        var settings = Eth15TestnetExecutionSettings.Load(configuration, hostEnvironment.ContentRootPath);

        var liveApiKey = configuration.GetValue<string>("ApiKey");
        var liveSecretKey = configuration.GetValue<string>("SecretKey");

        // Throws on mainnet host, unrecognized host, missing credentials (when ordering), or production-key reuse.
        settings.ValidateTestnetSafety(liveApiKey, liveSecretKey);

        if (!settings.Enabled)
        {
            logger.LogInformation("Eth15TestnetExecution disabled by config. Startup safety checks passed (no orders will be placed).");
            return Task.CompletedTask;
        }

        logger.LogInformation(
            "Eth15TestnetExecution startup validation passed. Enabled={Enabled} AllowTestnetOrders={AllowTestnetOrders} AllowRealOrders={AllowRealOrders} TestnetBaseUrl={TestnetBaseUrl} ExecutionEnvironment={ExecutionEnvironment} NotionalUsdt={NotionalUsdt} Leverage={Leverage} MaxOpenPositions={MaxOpenPositions} DailyMaxTrades={DailyMaxTrades} MaxConsecutiveLosses={MaxConsecutiveLosses} IncubationOutputDirectory={IncubationOutputDirectory} ReportOutputDirectory={ReportOutputDirectory}",
            settings.Enabled,
            settings.AllowTestnetOrders,
            settings.AllowRealOrders,
            settings.TestnetBaseUrl,
            Eth15TestnetExecutionSettings.ExecutionEnvironment,
            settings.NotionalUsdt,
            settings.Leverage,
            settings.MaxOpenPositions,
            settings.DailyMaxTrades,
            settings.MaxConsecutiveLosses,
            settings.IncubationOutputDirectory,
            settings.ReportOutputDirectory);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
