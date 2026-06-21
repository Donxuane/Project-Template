using System.Text.Json;

namespace TradingBot.Backtest;

/// <summary>
/// Cross-symbol candidate engine V2 — reads V1 research outputs and emits shadow-only candidate rankings.
/// Does not place orders, enable live trading, or modify frozen profiles.
/// </summary>
public sealed class CrossSymbolCandidateEngineV2Application(BacktestSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<CrossSymbolCandidateEngineV2RunResult> RunAsync(
        CrossSymbolCandidateEngineV2Settings engineSettings,
        CancellationToken cancellationToken)
    {
        var runAtUtc = DateTime.UtcNow;
        var input = await CrossSymbolCandidateEngineV2Loader.LoadAsync(
            engineSettings.V1InputDirectory,
            engineSettings.BottleneckAuditDirectory,
            cancellationToken);
        var forwardReality = await CrossSymbolCandidateEngineV2ForwardRealityLoader.LoadAsync(
            settings.DataDirectory,
            engineSettings.BottleneckAuditDirectory,
            engineSettings.ShadowRunnerDirectory,
            cancellationToken);

        var result = CrossSymbolCandidateEngineV2Builder.Build(input, engineSettings, forwardReality, runAtUtc);

        Directory.CreateDirectory(settings.OutputDirectory);
        await CrossSymbolCandidateEngineV2ReportWriter.WriteAsync(settings.OutputDirectory, result, cancellationToken);
        await WriteRunMetadataAsync(result, engineSettings, cancellationToken);
        return result;
    }

    public static CrossSymbolCandidateEngineV2Settings ResolveDefaultSettings(string dataDirectory, string outputDirectory)
    {
        var outputRoot = Path.GetDirectoryName(Path.GetFullPath(outputDirectory)) ?? outputDirectory;
        var v1Dir = Path.Combine(outputRoot, CrossSymbolCandidateEngineV2Catalog.DefaultV1InputSubdir);
        if (!Directory.Exists(v1Dir))
        {
            v1Dir = Path.Combine(
                Directory.GetCurrentDirectory(),
                "TradingBot.Backtest",
                "output",
                CrossSymbolCandidateEngineV2Catalog.DefaultV1InputSubdir);
        }

        var bottleneckDir = Path.Combine(outputRoot, CrossSymbolCandidateEngineV2Catalog.DefaultBottleneckAuditSubdir);
        if (!File.Exists(Path.Combine(bottleneckDir, "frozen-profile-bottleneck-audit.json")))
            bottleneckDir = null;

        var shadowDir = Path.Combine(outputRoot, CrossSymbolCandidateEngineV2Catalog.DefaultShadowRunnerSubdir);
        if (!File.Exists(Path.Combine(shadowDir, "futures-testnet-shadow-decisions.json")))
            shadowDir = null;

        return new CrossSymbolCandidateEngineV2Settings
        {
            V1InputDirectory = v1Dir,
            BottleneckAuditDirectory = bottleneckDir,
            ShadowRunnerDirectory = shadowDir,
            OneCandidatePerSymbol = CrossSymbolCandidateEngineV2Catalog.DefaultOneCandidatePerSymbol,
            MaxShadowCandidates = CrossSymbolCandidateEngineV2Catalog.DefaultMaxShadowCandidates,
            MaxTotalShadowNotionalUsdt = CrossSymbolCandidateEngineV2Catalog.DefaultMaxTotalShadowNotionalUsdt,
            MaxPerCandidateNotionalUsdt = CrossSymbolCandidateEngineV2Catalog.DefaultMaxPerCandidateNotionalUsdt
        };
    }

    private async Task WriteRunMetadataAsync(
        CrossSymbolCandidateEngineV2RunResult result,
        CrossSymbolCandidateEngineV2Settings engineSettings,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = CrossSymbolCandidateEngineV2Catalog.ModeName,
                settings.DataDirectory,
                settings.OutputDirectory,
                engineSettings.V1InputDirectory,
                engineSettings.BottleneckAuditDirectory,
                engineSettings.ShadowRunnerDirectory,
                engineSettings.OneCandidatePerSymbol,
                engineSettings.MaxShadowCandidates,
                engineSettings.MaxTotalShadowNotionalUsdt,
                engineSettings.MaxPerCandidateNotionalUsdt,
                backtestOnly = true,
                shadowDryRunOnly = true,
                realOrdersPlaced = false,
                liveFuturesRecommended = false,
                discoveryCountedAsForwardProof = false,
                portfolioUsesFixedUsdtNotional = true,
                portfolioDoesNotSumRawBaseCoinPnl = true,
                compactSummaryLine = result.Summary.CompactSummaryLine,
                researchPromotedCount = result.Summary.ResearchPromotedCount,
                executableShadowCandidateCount = result.Summary.ExecutableShadowCandidateCount,
                canEnterTestnetOrderModeCount = result.Summary.CanEnterTestnetOrderModeCount,
                blockedByLookbackStarvationCount = result.Summary.BlockedByLookbackStarvationCount,
                promoteToShadowCount = result.Summary.PromoteToShadowCount,
                shadowPortfolioCandidateCount = result.Summary.ShadowPortfolioCandidateCount,
                executionReadyPortfolioCandidateCount = result.Summary.ExecutionReadyPortfolioCandidateCount
            }, JsonOptions),
            cancellationToken);
    }
}
