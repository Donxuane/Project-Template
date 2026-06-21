using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TradingBot.Application.CrossSymbolShadowBridge;

public sealed class CrossSymbolShadowBridgeService(
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    ILogger<CrossSymbolShadowBridgeService> logger)
{
    public async Task<CrossSymbolShadowBridgeRunResult?> RunCycleAsync(CancellationToken cancellationToken)
    {
        CrossSymbolShadowBridgeSettings settings;
        try
        {
            settings = CrossSymbolShadowBridgeSettings.Load(configuration, hostEnvironment.ContentRootPath);
        }
        catch (InvalidOperationException ex) when (ex.Message == CrossSymbolShadowBridgeSettings.RealOrdersForbiddenError)
        {
            throw;
        }

        if (!settings.Enabled)
            return null;

        var outputDirectory = settings.ResolveOutputDirectory(hostEnvironment.ContentRootPath);
        Directory.CreateDirectory(outputDirectory);

        var probe = CrossSymbolShadowBridgeLoader.Probe(settings);
        var evaluatedAtUtc = DateTime.UtcNow;

        CrossSymbolShadowBridgeRunResult result;
        if (!probe.RequiredInputAvailable)
        {
            logger.LogWarning(
                "CrossSymbolShadowBridge input files missing. Status=InputFilesMissing OutputDirectory={OutputDirectory} Missing={MissingFiles}",
                outputDirectory,
                string.Join(", ", probe.MissingFiles));

            result = CrossSymbolShadowBridgeEvaluator.BuildInputFilesMissingResult(
                settings, probe, outputDirectory, evaluatedAtUtc);
        }
        else
        {
            var input = await CrossSymbolShadowBridgeLoader.LoadAsync(settings, cancellationToken);
            result = CrossSymbolShadowBridgeEvaluator.Evaluate(settings, input, evaluatedAtUtc);
            result = result with
            {
                Status = result.Status with
                {
                    OutputDirectory = outputDirectory,
                    CandidatesFileExists = probe.CandidatesFileExists,
                    SummaryFileExists = probe.SummaryFileExists,
                    ExecutionPortfolioFileExists = probe.ExecutionPortfolioFileExists
                }
            };
        }

        await CrossSymbolShadowBridgeReportWriter.WriteAsync(outputDirectory, result, cancellationToken);

        if (result.Status.Status == "InputFilesMissing")
        {
            logger.LogWarning(
                "CrossSymbolShadowBridge wrote InputFilesMissing status to {StatusFile}",
                Path.Combine(outputDirectory, "cross-symbol-shadow-bridge-status.json"));
        }
        else if (result.Status.ExecutionReadyCandidateCount == 0)
        {
            logger.LogWarning(
                "No execution-ready candidates. Shadow only. Orders blocked. Status={Status} ResearchShadowOnly={ResearchShadowOnly} RejectedOrParked={Rejected}",
                result.Status.Status,
                result.Status.ResearchPromotedShadowOnlyCount,
                result.Status.RejectedOrParkedCount);
        }
        else
        {
            logger.LogInformation(
                "CrossSymbolShadowBridge evaluated. Status={Status} ExecutionReady={ExecutionReady} ResearchShadowOnly={ResearchShadowOnly} Output={OutputDirectory}",
                result.Status.Status,
                result.Status.ExecutionReadyCandidateCount,
                result.Status.ResearchPromotedShadowOnlyCount,
                outputDirectory);
        }

        foreach (var decision in result.Decisions.Where(d =>
                     d.Category == nameof(CrossSymbolShadowBridgeCandidateCategory.ResearchPromotedShadowOnly)))
        {
            logger.LogInformation(
                "Research shadow only (not tradable): {Symbol} {Interval} {Direction} readiness={Readiness} reason={Reason}",
                decision.Symbol,
                decision.Interval,
                decision.Direction,
                decision.CurrentExecutionReadiness,
                decision.ReasonIfBlocked);
        }

        return result;
    }
}
