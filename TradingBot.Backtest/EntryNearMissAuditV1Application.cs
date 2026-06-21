using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// Diagnostic/shadow-only audit measuring how close activation-passed candidates are to base entry signals.
/// Never places orders, enables testnet orders, or modifies frozen profiles or strategy logic.
/// </summary>
public sealed class EntryNearMissAuditV1Application(BacktestSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<EntryNearMissAuditV1RunResult> RunAsync(
        string? scannerInputDirectory,
        string? v1InputDirectory,
        string? v2InputDirectory,
        CancellationToken cancellationToken)
    {
        var runAtUtc = DateTime.UtcNow;
        var (defaultScanner, defaultV1, defaultV2, outputRoot) = ResolveDefaultPaths(settings.OutputDirectory);
        var scannerDir = string.IsNullOrWhiteSpace(scannerInputDirectory) ? defaultScanner : scannerInputDirectory;
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

        var input = await EntryNearMissAuditV1Loader.LoadAsync(
            scannerDir,
            v1Dir,
            v2Dir,
            outputRoot,
            cancellationToken);

        var market = await CurrentOpportunityScannerV1MarketContext.BuildAsync(
            settings,
            input.StudyStartUtc,
            cancellationToken);

        var result = EntryNearMissAuditV1Builder.Build(input, market, runAtUtc);

        Directory.CreateDirectory(settings.OutputDirectory);
        await EntryNearMissAuditV1ReportWriter.WriteAsync(settings.OutputDirectory, result, cancellationToken);
        await WriteRunMetadataAsync(result, scannerDir, v1Dir, v2Dir, cancellationToken);
        return result;
    }

    public static (string ScannerInputDirectory, string V1InputDirectory, string V2InputDirectory, string OutputRoot) ResolveDefaultPaths(string outputDirectory)
    {
        var outputRoot = Path.GetDirectoryName(Path.GetFullPath(outputDirectory)) ?? outputDirectory;
        var scannerDir = Path.Combine(outputRoot, EntryNearMissAuditV1Catalog.DefaultScannerInputSubdir);
        if (!File.Exists(Path.Combine(scannerDir, "current-opportunity-scanner-v1-candidates.json")))
        {
            var fallback = Path.Combine(
                Directory.GetCurrentDirectory(),
                "TradingBot.Backtest",
                "output",
                EntryNearMissAuditV1Catalog.DefaultScannerInputSubdir);
            if (File.Exists(Path.Combine(fallback, "current-opportunity-scanner-v1-candidates.json")))
                scannerDir = fallback;
        }

        var (v1Dir, v2Dir, _) = CurrentOpportunityScannerV1Application.ResolveDefaultPaths(outputDirectory);
        return (scannerDir, v1Dir, v2Dir, outputRoot);
    }

    private async Task WriteRunMetadataAsync(
        EntryNearMissAuditV1RunResult result,
        string scannerInputDirectory,
        string v1InputDirectory,
        string v2InputDirectory,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = EntryNearMissAuditV1Catalog.ModeName,
                settings.DataDirectory,
                settings.OutputDirectory,
                scannerInputDirectory,
                v1InputDirectory,
                v2InputDirectory,
                bootstrapFuturesData = settings.BootstrapFuturesData,
                backtestOnly = true,
                realOrdersPlaced = false,
                liveFuturesRecommended = false,
                wouldPlaceOrderAlwaysFalse = true,
                nearMissResultsAreNotForwardProof = true,
                compactSummaryLine = result.Summary.CompactSummaryLine,
                evaluatedActivationPassedCount = result.Summary.EvaluatedActivationPassedCount,
                topNearMissCount = result.Summary.TopNearMissCount,
                entryRarityVerdict = result.Summary.EntryRarityVerdict,
                topNearMissCandidate = result.Summary.TopNearMissCandidate
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
