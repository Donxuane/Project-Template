using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TradingBot.Application.CrossSymbolShadowBridge;

public sealed class CrossSymbolShadowBridgeStartupValidator(
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    ILogger<CrossSymbolShadowBridgeStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var section = configuration.GetSection(CrossSymbolShadowBridgeSettings.SectionName);
        if (!section.Exists())
            return Task.CompletedTask;

        var enabled = section.GetValue("Enabled", false);
        var dryRunOnly = section.GetValue("DryRunOnly", true);

        if (section.GetValue("AllowRealOrders", false))
            throw new InvalidOperationException(CrossSymbolShadowBridgeSettings.RealOrdersForbiddenError);

        if (section.GetValue("AllowOrders", false))
        {
            throw new InvalidOperationException(
                "CrossSymbolShadowBridgeAllowOrdersForbidden: AllowOrders must remain false for shadow-only bridge.");
        }

        if (!enabled)
        {
            logger.LogInformation("CrossSymbolShadowBridge disabled by config.");
            return Task.CompletedTask;
        }

        var settings = CrossSymbolShadowBridgeSettings.Load(configuration, hostEnvironment.ContentRootPath);
        var outputDirectory = settings.ResolveOutputDirectory(hostEnvironment.ContentRootPath);
        var probe = CrossSymbolShadowBridgeLoader.Probe(settings);

        logger.LogInformation(
            "CrossSymbolShadowBridge startup validation passed. Enabled={Enabled} DryRunOnly={DryRunOnly} CandidateInputDirectory={CandidateInputDirectory} OutputDirectory={OutputDirectory} CandidatesFileExists={CandidatesFileExists} SummaryFileExists={SummaryFileExists} ExecutionPortfolioFileExists={ExecutionPortfolioFileExists}",
            settings.Enabled,
            dryRunOnly,
            probe.CandidateInputDirectory,
            outputDirectory,
            probe.CandidatesFileExists,
            probe.SummaryFileExists,
            probe.ExecutionPortfolioFileExists);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
