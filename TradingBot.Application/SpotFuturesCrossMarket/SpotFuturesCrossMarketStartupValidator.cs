using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TradingBot.Application.SpotFuturesCrossMarket;

/// <summary>
/// Fail-fast startup guard for the SpotFuturesCrossMarketTestnetV1 strategy. The application
/// refuses to start when the configuration could enable real orders, reach a mainnet host,
/// reuse production keys, or trade the same testnet symbol as the ETH15 worker.
/// </summary>
public sealed class SpotFuturesCrossMarketStartupValidator(
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    ILogger<SpotFuturesCrossMarketStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var section = configuration.GetSection(SpotFuturesCrossMarketSettings.SectionName);
        if (!section.Exists())
        {
            logger.LogInformation("SpotFuturesCrossMarketTestnetV1 section not present; strategy inactive.");
            return Task.CompletedTask;
        }

        var settings = SpotFuturesCrossMarketSettings.Load(configuration, hostEnvironment.ContentRootPath);
        settings.ValidateTestnetSafety(configuration);

        if (!settings.Enabled)
        {
            logger.LogInformation("SpotFuturesCrossMarketTestnetV1 disabled by config. Startup safety checks passed (no orders will be placed).");
            return Task.CompletedTask;
        }

        logger.LogInformation(
            "SpotFuturesCrossMarketTestnetV1 startup validation passed. Enabled={Enabled} AllowTestnetOrders={AllowTestnetOrders} AllowRealOrders={AllowRealOrders} Symbols={Symbols} Interval={Interval} ExecutionEnvironment={ExecutionEnvironment} BalanceSizing={BalanceSizing} AllocationPercent={AllocationPercent} FallbackNotional={Notional} Leverage={Leverage} MaxOpenPositions={MaxOpenPositions} DailyMaxTrades={DailyMaxTrades} MaxConsecutiveLosses={MaxConsecutiveLosses} ReportOutputDirectory={ReportOutputDirectory}",
            settings.Enabled,
            settings.AllowTestnetOrders,
            settings.AllowRealOrders,
            string.Join(",", settings.Symbols),
            settings.Interval,
            SpotFuturesCrossMarketSettings.ExecutionEnvironment,
            settings.UseBalanceBasedSizing,
            settings.BalanceAllocationPercent,
            settings.NotionalUsdt,
            settings.Leverage,
            settings.MaxOpenPositions,
            settings.DailyMaxTrades,
            settings.MaxConsecutiveLosses,
            settings.ReportOutputDirectory);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
