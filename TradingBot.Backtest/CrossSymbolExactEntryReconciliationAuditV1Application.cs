using System.Text.Json;

namespace TradingBot.Backtest;

/// <summary>
/// Diagnostic-only reconciliation of V1 discovery trades vs exact-entry frequency study output.
/// Never places orders, enables testnet orders, or modifies strategy logic.
/// </summary>
public sealed class CrossSymbolExactEntryReconciliationAuditV1Application(BacktestSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<CrossSymbolExactEntryReconciliationAuditV1RunResult> RunAsync(
        string? v1InputDirectory,
        string? v2InputDirectory,
        string? frequencyInputDirectory,
        CancellationToken cancellationToken)
    {
        var runAtUtc = DateTime.UtcNow;
        var (defaultV1, defaultV2, defaultFrequency, outputRoot) =
            CrossSymbolExactEntryReconciliationAuditV1Loader.ResolveDefaultPaths(
                settings.OutputDirectory,
                settings.CrossSymbolV1InputDirectory,
                settings.FrequencyStudyInputDirectory);

        var v1Dir = string.IsNullOrWhiteSpace(v1InputDirectory) ? defaultV1 : v1InputDirectory;
        var v2Dir = string.IsNullOrWhiteSpace(v2InputDirectory) ? defaultV2 : v2InputDirectory;
        var frequencyDir = string.IsNullOrWhiteSpace(frequencyInputDirectory) ? defaultFrequency : frequencyInputDirectory;

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

        var input = await CrossSymbolExactEntryReconciliationAuditV1Loader.LoadAsync(
            v1Dir,
            v2Dir,
            frequencyDir,
            outputRoot,
            cancellationToken);

        var market = await CurrentOpportunityScannerV1MarketContext.BuildAsync(
            settings,
            input.StudyStartUtc,
            cancellationToken);

        var studyStartUtc = input.StudyStartUtc ?? market.StudyStartUtc;
        if (studyStartUtc >= market.EvalUtc)
            studyStartUtc = market.EvalUtc.AddDays(-1);

        var studyEndUtc = input.StudyEndUtc ?? market.EvalUtc;

        var result = CrossSymbolExactEntryReconciliationAuditV1Builder.Build(
            input,
            market,
            runAtUtc,
            studyStartUtc,
            studyEndUtc);

        Directory.CreateDirectory(settings.OutputDirectory);
        await CrossSymbolExactEntryReconciliationAuditV1ReportWriter.WriteAsync(
            settings.OutputDirectory,
            result,
            cancellationToken);

        await WriteRunMetadataAsync(result, v1Dir, v2Dir, frequencyDir, cancellationToken);
        return result;
    }

    private async Task WriteRunMetadataAsync(
        CrossSymbolExactEntryReconciliationAuditV1RunResult result,
        string v1InputDirectory,
        string v2InputDirectory,
        string frequencyInputDirectory,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = CrossSymbolExactEntryReconciliationAuditV1Catalog.ModeName,
                settings.DataDirectory,
                settings.OutputDirectory,
                v1InputDirectory,
                v2InputDirectory,
                frequencyInputDirectory,
                bootstrapFuturesData = settings.BootstrapFuturesData,
                backtestOnly = true,
                realOrdersPlaced = false,
                liveFuturesRecommended = false,
                compactSummaryLine = result.Summary.CompactSummaryLine,
                evaluatedCandidateCount = result.Summary.EvaluatedCandidateCount,
                primaryRootCause = result.Summary.PrimaryRootCause
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
