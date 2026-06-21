using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TradingBot.Application.BackgroundHostService;

/// <summary>
/// Loads Cross-Symbol Candidate Engine V2 outputs into the actual bot as a shadow-only bridge.
/// Never places real or testnet orders.
/// </summary>
public sealed class CrossSymbolShadowBridgeWorker(
    CrossSymbolShadowBridge.CrossSymbolShadowBridgeService bridgeService,
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    ILogger<CrossSymbolShadowBridgeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CrossSymbolShadowBridge.CrossSymbolShadowBridgeSettings settings;
        try
        {
            settings = CrossSymbolShadowBridge.CrossSymbolShadowBridgeSettings.Load(
                configuration,
                hostEnvironment.ContentRootPath);
        }
        catch (InvalidOperationException ex) when (
            ex.Message == CrossSymbolShadowBridge.CrossSymbolShadowBridgeSettings.RealOrdersForbiddenError)
        {
            logger.LogCritical(ex, "CrossSymbolShadowBridge forbidden configuration detected. Stopping worker.");
            throw;
        }

        if (!settings.Enabled)
        {
            logger.LogInformation("CrossSymbolShadowBridge disabled by config.");
            return;
        }

        var outputDirectory = settings.ResolveOutputDirectory(hostEnvironment.ContentRootPath);
        var probe = CrossSymbolShadowBridge.CrossSymbolShadowBridgeLoader.Probe(settings);

        logger.LogInformation(
            "CrossSymbolShadowBridge starting. Enabled={Enabled} DryRunOnly={DryRunOnly} CandidateInputDirectory={CandidateInputDirectory} OutputDirectory={OutputDirectory} CandidatesFileExists={CandidatesFileExists} SummaryFileExists={SummaryFileExists} ExecutionPortfolioFileExists={ExecutionPortfolioFileExists} IntervalSeconds={IntervalSeconds}",
            settings.Enabled,
            settings.DryRunOnly,
            probe.CandidateInputDirectory,
            outputDirectory,
            probe.CandidatesFileExists,
            probe.SummaryFileExists,
            probe.ExecutionPortfolioFileExists,
            settings.IntervalSeconds);

        var intervalSeconds = settings.IntervalSeconds;
        var isFirstCycle = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await bridgeService.RunCycleAsync(stoppingToken);

                if (isFirstCycle)
                {
                    logger.LogInformation(
                        "CrossSymbolShadowBridge initial evaluation completed. OutputDirectory={OutputDirectory}",
                        outputDirectory);
                    isFirstCycle = false;
                }

                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (InvalidOperationException ex) when (
                ex.Message == CrossSymbolShadowBridge.CrossSymbolShadowBridgeSettings.RealOrdersForbiddenError)
            {
                logger.LogCritical(ex, "CrossSymbolShadowBridge forbidden configuration detected. Stopping worker.");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CrossSymbolShadowBridgeWorker cycle failed at {Time}", DateTime.UtcNow);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        logger.LogInformation("CrossSymbolShadowBridgeWorker stopped.");
    }
}
