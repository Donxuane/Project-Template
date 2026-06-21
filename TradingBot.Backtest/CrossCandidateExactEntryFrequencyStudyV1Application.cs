using System.Text.Json;

namespace TradingBot.Backtest;

/// <summary>
/// Diagnostic-only study ranking cross-symbol candidates by historical exact-entry frequency under activation.
/// Never places orders, enables testnet orders, or modifies strategy logic.
/// </summary>
public sealed class CrossCandidateExactEntryFrequencyStudyV1Application(BacktestSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<CrossCandidateExactEntryFrequencyStudyV1RunResult> RunAsync(
        string? v1InputDirectory,
        string? v2InputDirectory,
        string? scannerInputDirectory,
        CancellationToken cancellationToken)
    {
        var runAtUtc = DateTime.UtcNow;
        var (defaultV1, defaultV2, defaultScanner, outputRoot) =
            CrossCandidateExactEntryFrequencyStudyV1Loader.ResolveDefaultPaths(
                settings.OutputDirectory,
                settings.CrossSymbolV1InputDirectory,
                settings.OpportunityScannerInputDirectory);

        var v1Dir = string.IsNullOrWhiteSpace(v1InputDirectory) ? defaultV1 : v1InputDirectory;
        var v2Dir = string.IsNullOrWhiteSpace(v2InputDirectory) ? defaultV2 : v2InputDirectory;
        var scannerDir = string.IsNullOrWhiteSpace(scannerInputDirectory)
            ? defaultScanner
            : scannerInputDirectory;

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

        var input = await CrossCandidateExactEntryFrequencyStudyV1Loader.LoadAsync(
            v1Dir,
            v2Dir,
            scannerDir,
            outputRoot,
            cancellationToken);

        var market = await CurrentOpportunityScannerV1MarketContext.BuildAsync(
            settings,
            input.StudyStartUtc,
            cancellationToken);

        var studyStartUtc = input.StudyStartUtc ?? market.StudyStartUtc;
        if (studyStartUtc >= market.EvalUtc)
            studyStartUtc = market.EvalUtc.AddDays(-1);

        var result = CrossCandidateExactEntryFrequencyStudyV1Builder.Build(
            input,
            market,
            runAtUtc,
            studyStartUtc,
            market.EvalUtc);

        Directory.CreateDirectory(settings.OutputDirectory);
        await CrossCandidateExactEntryFrequencyStudyV1ReportWriter.WriteAsync(
            settings.OutputDirectory,
            result,
            cancellationToken);

        await WriteRunMetadataAsync(result, v1Dir, v2Dir, scannerDir, cancellationToken);
        return result;
    }

    private async Task WriteRunMetadataAsync(
        CrossCandidateExactEntryFrequencyStudyV1RunResult result,
        string v1InputDirectory,
        string v2InputDirectory,
        string scannerInputDirectory,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = CrossCandidateExactEntryFrequencyStudyV1Catalog.ModeName,
                settings.DataDirectory,
                settings.OutputDirectory,
                v1InputDirectory,
                v2InputDirectory,
                scannerInputDirectory,
                bootstrapFuturesData = settings.BootstrapFuturesData,
                backtestOnly = true,
                realOrdersPlaced = false,
                liveFuturesRecommended = false,
                nearMissNotUsed = true,
                usesConfirmedClosedCandlesOnly = true,
                compactSummaryLine = result.Summary.CompactSummaryLine,
                evaluatedCandidateCount = result.Summary.EvaluatedCandidateCount,
                promoteToExactEntryWatcherCount = result.Summary.PromoteToExactEntryWatcherCount
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
