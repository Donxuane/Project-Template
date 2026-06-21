using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// Diagnostic/shadow-only scanner for current cross-symbol V1/V2 candidate opportunities.
/// Never places orders, enables testnet orders, or modifies frozen profiles.
/// </summary>
public sealed class CurrentOpportunityScannerV1Application(BacktestSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<CurrentOpportunityScannerV1RunResult> RunAsync(
        string? v1InputDirectory,
        string? v2InputDirectory,
        CancellationToken cancellationToken)
    {
        var runAtUtc = DateTime.UtcNow;
        var (defaultV1, defaultV2, outputRoot) = ResolveDefaultPaths(settings.OutputDirectory);
        var v1Dir = string.IsNullOrWhiteSpace(v1InputDirectory) ? defaultV1 : v1InputDirectory;
        var v2Dir = string.IsNullOrWhiteSpace(v2InputDirectory) ? defaultV2 : v2InputDirectory;

        if (settings.BootstrapFuturesData)
        {
            var loader = new HistoricalKlineDataLoader(settings);
            await RefreshCandlesAsync(loader, cancellationToken);
            var flowDownloader = new ShortWindowFlowDataDownloader();
            await flowDownloader.DownloadAllAsync(
                settings.DataDirectory,
                FuturesMarketDataCatalog.Symbols,
                runAtUtc.AddDays(-365),
                runAtUtc,
                cancellationToken);
        }

        var input = await CurrentOpportunityScannerV1Loader.LoadAsync(
            v1Dir,
            v2Dir,
            outputRoot,
            cancellationToken);

        var market = await CurrentOpportunityScannerV1MarketContext.BuildAsync(
            settings,
            input.StudyStartUtc,
            cancellationToken);

        var result = CurrentOpportunityScannerV1Builder.Build(input, market, runAtUtc);

        Directory.CreateDirectory(settings.OutputDirectory);
        await CurrentOpportunityScannerV1ReportWriter.WriteAsync(settings.OutputDirectory, result, cancellationToken);
        await WriteRunMetadataAsync(result, v1Dir, v2Dir, cancellationToken);
        return result;
    }

    public static (string V1InputDirectory, string V2InputDirectory, string OutputRoot) ResolveDefaultPaths(string outputDirectory)
    {
        var outputRoot = Path.GetDirectoryName(Path.GetFullPath(outputDirectory)) ?? outputDirectory;
        var v1Dir = Path.Combine(outputRoot, CurrentOpportunityScannerV1Catalog.DefaultV1InputSubdir);
        if (!Directory.Exists(v1Dir))
        {
            v1Dir = Path.Combine(
                Directory.GetCurrentDirectory(),
                "TradingBot.Backtest",
                "output",
                CurrentOpportunityScannerV1Catalog.DefaultV1InputSubdir);
        }

        var v2Dir = Path.Combine(outputRoot, CurrentOpportunityScannerV1Catalog.DefaultV2InputSubdir);
        if (!File.Exists(Path.Combine(v2Dir, "cross-symbol-candidate-engine-v2-candidates.json")))
        {
            var fallback = Path.Combine(
                Directory.GetCurrentDirectory(),
                "TradingBot.Backtest",
                "output",
                CurrentOpportunityScannerV1Catalog.DefaultV2InputSubdir);
            if (File.Exists(Path.Combine(fallback, "cross-symbol-candidate-engine-v2-candidates.json")))
                v2Dir = fallback;
        }

        return (v1Dir, v2Dir, outputRoot);
    }

    private async Task WriteRunMetadataAsync(
        CurrentOpportunityScannerV1RunResult result,
        string v1InputDirectory,
        string v2InputDirectory,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = CurrentOpportunityScannerV1Catalog.ModeName,
                settings.DataDirectory,
                settings.OutputDirectory,
                v1InputDirectory,
                v2InputDirectory,
                bootstrapFuturesData = settings.BootstrapFuturesData,
                backtestOnly = true,
                realOrdersPlaced = false,
                liveFuturesRecommended = false,
                wouldPlaceOrderAlwaysFalse = true,
                scannerResultsAreNotForwardProof = true,
                compactSummaryLine = result.Summary.CompactSummaryLine,
                headline = result.Summary.ActionableShadowCount == 0
                    ? "No current opportunity"
                    : "Shadow opportunity exists",
                evaluatedCandidateCount = result.Summary.EvaluatedCandidateCount,
                actionableShadowCount = result.Summary.ActionableShadowCount,
                activationPassedCount = result.Summary.ActivationPassedCount,
                baseEntrySignalPresentCount = result.Summary.BaseEntrySignalPresentCount
            }, JsonOptions),
            cancellationToken);
    }

    private async Task RefreshCandlesAsync(HistoricalKlineDataLoader loader, CancellationToken cancellationToken)
    {
        var downloader = new BinanceKlineBootstrapDownloader();
        foreach (var refreshSymbol in NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.Symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = await loader.LoadAndValidateAsync(refreshSymbol, cancellationToken);
            var lastUtc = existing.Candles.Count > 0 ? existing.Candles[^1].OpenTimeUtc : DateTime.UtcNow.AddDays(-35);
            var nowUtc = DateTime.UtcNow;
            if ((nowUtc - lastUtc) < TimeSpan.FromMinutes(30))
                continue;

            var path = Path.Combine(settings.DataDirectory, $"{refreshSymbol}-1m.json");
            await downloader.DownloadAndMergeToJsonAsync(
                refreshSymbol.ToString(), path, 1000, lastUtc.AddHours(-2), nowUtc, cancellationToken);
        }
    }
}
