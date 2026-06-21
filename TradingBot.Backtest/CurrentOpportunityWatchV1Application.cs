using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// Diagnostic/shadow-only watcher that refreshes scanner + near-miss audit, records history,
/// and flags exact entry signals. Never places orders or relaxes near-miss conditions.
/// </summary>
public sealed class CurrentOpportunityWatchV1Application(BacktestSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions LoadJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private int _cycleNumber;
    private DateTime? _previousEvaluatedAtUtc;

    public async Task<CurrentOpportunityWatchV1RunResult> RunAsync(CancellationToken cancellationToken)
    {
        if (settings.WatchLoop)
        {
            CurrentOpportunityWatchV1RunResult? last = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                last = await RunCycleAsync(cancellationToken);
                Console.WriteLine(
                    $"[watch cycle {_cycleNumber}] {last.Status.CompactSummaryLine} evalAt={last.Status.EvaluatedAtUtc:o} dataLast={last.Status.DataLastCandleUtc:o} evalAdvanced={last.Status.EvalAdvancedSincePreviousCycle}");
                if (string.Equals(last.Status.WatchStatus, "ExactEntrySignalAppeared", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(last.Status.ExactEntryAppearedNote);
                    break;
                }

                var delayMinutes = Math.Max(1, settings.WatchIntervalMinutes);
                await Task.Delay(TimeSpan.FromMinutes(delayMinutes), cancellationToken);
            }

            return last ?? await RunCycleAsync(cancellationToken);
        }

        return await RunCycleAsync(cancellationToken);
    }

    private async Task<CurrentOpportunityWatchV1RunResult> RunCycleAsync(CancellationToken cancellationToken)
    {
        var runAtUtc = DateTime.UtcNow;
        var paths = ResolvePaths(settings.OutputDirectory);
        var candlesRefreshed = false;

        if (settings.BootstrapFuturesData)
            candlesRefreshed = await RefreshMarketDataAsync(runAtUtc, cancellationToken);

        var loader = new HistoricalKlineDataLoader(settings);
        var dataLastCandleUtc = await ResolveDataLastCandleUtcAsync(loader, cancellationToken);

        var scannerInput = await CurrentOpportunityScannerV1Loader.LoadAsync(
            paths.V1InputDirectory,
            paths.V2InputDirectory,
            paths.OutputRoot,
            cancellationToken);

        var market = await CurrentOpportunityScannerV1MarketContext.BuildAsync(
            settings,
            scannerInput.StudyStartUtc,
            cancellationToken);

        var scannerResult = CurrentOpportunityScannerV1Builder.Build(scannerInput, market, runAtUtc);

        Directory.CreateDirectory(paths.ScannerOutputDirectory);
        await CurrentOpportunityScannerV1ReportWriter.WriteAsync(
            paths.ScannerOutputDirectory,
            scannerResult,
            cancellationToken);

        var auditInput = new EntryNearMissAuditV1InputBundle
        {
            ScannerInputDirectory = paths.ScannerOutputDirectory,
            V1InputDirectory = paths.V1InputDirectory,
            V2InputDirectory = paths.V2InputDirectory,
            StudyStartUtc = scannerInput.StudyStartUtc,
            ScannerCandidates = scannerResult.Candidates,
            LeaderboardByKey = scannerInput.Leaderboard.ToDictionary(
                l => CrossSymbolCandidateEngineV2Catalog.CandidateKey(
                    l.Symbol, l.Interval, l.Direction, l.TargetPercent, l.StopPercent, l.ActivationRule),
                l => l,
                StringComparer.OrdinalIgnoreCase),
            BottleneckAudit = scannerInput.BottleneckAudit
        };

        var auditResult = EntryNearMissAuditV1Builder.Build(auditInput, market, runAtUtc);

        Directory.CreateDirectory(paths.NearMissOutputDirectory);
        await EntryNearMissAuditV1ReportWriter.WriteAsync(
            paths.NearMissOutputDirectory,
            auditResult,
            cancellationToken);

        var (frequencyCandidates, countingBugFixed) =
            await LoadFrequencyStudyAsync(paths.FrequencyStudyDirectory, cancellationToken);

        var historyPath = Path.Combine(settings.OutputDirectory, "current-opportunity-watch-v1-history.json");
        var priorHistory = await CurrentOpportunityWatchV1HistoryStore.LoadAsync(historyPath, cancellationToken);
        _cycleNumber++;
        var evalAdvanced = _previousEvaluatedAtUtc.HasValue
                           && market.EvalUtc > _previousEvaluatedAtUtc.Value;
        var result = CurrentOpportunityWatchV1Builder.Build(
            scannerResult,
            auditResult,
            frequencyCandidates,
            scannerInput.BottleneckAudit,
            countingBugFixed,
            priorHistory,
            runAtUtc,
            dataLastCandleUtc,
            evalAdvanced,
            _cycleNumber);
        _previousEvaluatedAtUtc = market.EvalUtc;

        Directory.CreateDirectory(settings.OutputDirectory);
        await CurrentOpportunityWatchV1ReportWriter.WriteAsync(settings.OutputDirectory, result, cancellationToken);
        await WriteRunMetadataAsync(result, paths, candlesRefreshed, cancellationToken);
        return result;
    }

    private static (string OutputRoot, string V1InputDirectory, string V2InputDirectory, string ScannerOutputDirectory, string NearMissOutputDirectory, string FrequencyStudyDirectory) ResolvePaths(
        string watchOutputDirectory)
    {
        var outputRoot = Path.GetDirectoryName(Path.GetFullPath(watchOutputDirectory)) ?? watchOutputDirectory;
        var (v1Dir, v2Dir, _) = CurrentOpportunityScannerV1Application.ResolveDefaultPaths(watchOutputDirectory);
        var scannerDir = Path.Combine(outputRoot, CurrentOpportunityWatchV1Catalog.DefaultScannerOutputSubdir);
        var nearMissDir = Path.Combine(outputRoot, CurrentOpportunityWatchV1Catalog.DefaultNearMissOutputSubdir);
        var frequencyDir = Path.Combine(outputRoot, CurrentOpportunityWatchV1Catalog.DefaultFrequencyStudySubdir);
        return (outputRoot, v1Dir, v2Dir, scannerDir, nearMissDir, frequencyDir);
    }

    private static async Task<(IReadOnlyList<CrossCandidateExactEntryFrequencyStudyV1CandidateRow> Candidates, bool CountingBugFixed)>
        LoadFrequencyStudyAsync(string frequencyStudyDirectory, CancellationToken cancellationToken)
    {
        var prefix = CurrentOpportunityWatchV1Catalog.FrequencyStudyOutputPrefix;
        var candidates = new List<CrossCandidateExactEntryFrequencyStudyV1CandidateRow>();
        var countingBugFixed = false;

        var candidatesPath = Path.Combine(frequencyStudyDirectory, $"{prefix}-candidates.json");
        if (File.Exists(candidatesPath))
        {
            try
            {
                using var stream = File.OpenRead(candidatesPath);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (doc.RootElement.TryGetProperty("candidates", out var candidatesElement))
                {
                    candidates = JsonSerializer.Deserialize<List<CrossCandidateExactEntryFrequencyStudyV1CandidateRow>>(
                                     candidatesElement.GetRawText(), LoadJsonOptions)
                                 ?? [];
                }
            }
            catch
            {
                candidates = [];
            }
        }

        var summaryPath = Path.Combine(frequencyStudyDirectory, $"{prefix}-summary.json");
        if (File.Exists(summaryPath))
        {
            try
            {
                using var stream = File.OpenRead(summaryPath);
                var summary = await JsonSerializer.DeserializeAsync<JsonElement>(stream, LoadJsonOptions, cancellationToken);
                if (summary.TryGetProperty("CountingBugFixed", out var fixedProp)
                    && fixedProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    countingBugFixed = fixedProp.GetBoolean();
                }
            }
            catch
            {
                countingBugFixed = false;
            }
        }

        return (candidates, countingBugFixed);
    }

    private async Task WriteRunMetadataAsync(
        CurrentOpportunityWatchV1RunResult result,
        (string OutputRoot, string V1InputDirectory, string V2InputDirectory, string ScannerOutputDirectory, string NearMissOutputDirectory, string FrequencyStudyDirectory) paths,
        bool candlesRefreshedThisCycle,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = CurrentOpportunityWatchV1Catalog.ModeName,
                settings.DataDirectory,
                settings.OutputDirectory,
                paths.V1InputDirectory,
                paths.V2InputDirectory,
                paths.ScannerOutputDirectory,
                paths.NearMissOutputDirectory,
                paths.FrequencyStudyDirectory,
                fixedFrequencyPromotedCount = result.Status.FixedFrequencyPromotedCount,
                fixedFrequencyWatchedCount = result.Status.FixedFrequencyWatchedCount,
                fixedFrequencyExactEntryPresent = result.Status.FixedFrequencyExactEntryPresent,
                canEnterTestnetOrderMode = result.Status.CanEnterTestnetOrderMode,
                countingBugFixed = result.Status.CountingBugFixed,
                settings.BootstrapFuturesData,
                settings.WatchLoop,
                settings.WatchIntervalMinutes,
                backtestOnly = true,
                realOrdersPlaced = false,
                liveFuturesRecommended = false,
                wouldPlaceOrderAlwaysFalse = true,
                usesConfirmedClosedCandlesOnly = true,
                nearMissIsWatchOnlyNotForwardProof = true,
                compactSummaryLine = result.Status.CompactSummaryLine,
                watchStatus = result.Status.WatchStatus,
                cycleNumber = result.Status.CycleNumber,
                evaluatedAtUtc = result.Status.EvaluatedAtUtc,
                dataLastCandleUtc = result.Status.DataLastCandleUtc,
                evalAdvancedSincePreviousCycle = result.Status.EvalAdvancedSincePreviousCycle,
                candlesRefreshedThisCycle = candlesRefreshedThisCycle,
                actionableShadowCount = result.Status.ActionableShadowCount,
                topNearMissCount = result.Status.TopNearMissCount,
                topWatchCandidate = result.Status.TopWatchCandidate,
                exactEntrySignalCandidate = result.Status.ExactEntrySignalCandidate,
                statusFile = Path.Combine(settings.OutputDirectory, "current-opportunity-watch-v1-status.json"),
                crossSymbolShadowBridgeMayReadStatus = true
            }, JsonOptions),
            cancellationToken);
    }

    private async Task<bool> RefreshMarketDataAsync(DateTime runAtUtc, CancellationToken cancellationToken)
    {
        var loader = new HistoricalKlineDataLoader(settings);
        var downloader = new BinanceKlineBootstrapDownloader();
        var refreshThreshold = settings.WatchLoop
            ? TimeSpan.FromMinutes(Math.Max(1, settings.WatchIntervalMinutes))
            : TimeSpan.FromMinutes(30);
        var nowUtc = DateTime.UtcNow;
        var anyCandleRefresh = false;

        foreach (var refreshSymbol in NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.Symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = await loader.LoadAndValidateAsync(refreshSymbol, cancellationToken);
            var lastUtc = existing.Candles.Count > 0 ? existing.Candles[^1].OpenTimeUtc : DateTime.UtcNow.AddDays(-35);
            if ((nowUtc - lastUtc) < refreshThreshold)
                continue;

            anyCandleRefresh = true;
            var path = Path.Combine(settings.DataDirectory, $"{refreshSymbol}-1m.json");
            await downloader.DownloadAndMergeToJsonAsync(
                refreshSymbol.ToString(), path, 1000, lastUtc.AddHours(-2), nowUtc, cancellationToken);
        }

        if (anyCandleRefresh || _cycleNumber == 0)
        {
            var flowDownloader = new ShortWindowFlowDataDownloader();
            await flowDownloader.DownloadAllAsync(
                settings.DataDirectory,
                FuturesMarketDataCatalog.Symbols,
                runAtUtc.AddDays(-365),
                runAtUtc,
                cancellationToken);
        }

        return anyCandleRefresh;
    }

    private static async Task<DateTime> ResolveDataLastCandleUtcAsync(
        HistoricalKlineDataLoader loader,
        CancellationToken cancellationToken)
    {
        DateTime? last = null;
        foreach (var symbol in NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.Symbols)
        {
            var data = await loader.LoadAndValidateAsync(symbol, cancellationToken);
            if (data.Candles.Count == 0)
                continue;
            var symbolLast = data.Candles[^1].OpenTimeUtc.AddMinutes(1);
            last = last.HasValue ? (symbolLast > last.Value ? symbolLast : last.Value) : symbolLast;
        }

        return last ?? DateTime.UtcNow;
    }
}
