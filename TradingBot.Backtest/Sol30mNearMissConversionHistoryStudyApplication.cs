using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// Diagnostic-only historical study of SOLUSDT 30m Short near-miss conversion into exact entry signals.
/// Never places orders, enables testnet orders, or modifies strategy logic.
/// </summary>
public sealed class Sol30mNearMissConversionHistoryStudyApplication(BacktestSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<Sol30mNearMissConversionHistoryStudyResult> RunAsync(CancellationToken cancellationToken)
    {
        var runAtUtc = DateTime.UtcNow;
        var loader = new HistoricalKlineDataLoader(settings);

        if (settings.BootstrapFuturesData)
        {
            await RefreshCandlesAsync(loader, cancellationToken);
            var flowDownloader = new ShortWindowFlowDataDownloader();
            await flowDownloader.DownloadAllAsync(
                settings.DataDirectory,
                FuturesMarketDataCatalog.Symbols,
                runAtUtc.AddDays(-365),
                runAtUtc,
                cancellationToken);
        }

        var key = Sol30mNearMissConversionHistoryStudyCatalog.TargetKey;
        var activationConfig = Sol30mNearMissConversionHistoryStudyCatalog.ResolveActivationConfig();
        var cooldown = NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.CooldownFor(key.Interval);

        var solData = await loader.LoadAndValidateAsync(TradingSymbol.SOLUSDT, cancellationToken);
        var btcData = await loader.LoadAndValidateAsync(TradingSymbol.BTCUSDT, cancellationToken);
        if (solData.Candles.Count == 0 || btcData.Candles.Count == 0)
            throw new InvalidOperationException("SOLUSDT and BTCUSDT local candle data required for near-miss conversion study.");

        var validated = new Dictionary<TradingSymbol, SymbolValidationResult>
        {
            [TradingSymbol.SOLUSDT] = solData,
            [TradingSymbol.BTCUSDT] = btcData
        };
        var (dataStartUtc, dataEndUtc) = RobustnessWindowResolver.ResolveDataBounds(validated);
        var evalUtc = ConfirmedClosedEvalUtcResolver.Resolve(validated, runAtUtc);
        var spanDays = (int)Math.Max(1, (evalUtc - dataStartUtc).TotalDays);
        var windowStart = evalUtc.AddDays(-Math.Min(365, spanDays));
        if (windowStart < dataStartUtc)
            windowStart = dataStartUtc;

        var studyStartUtc = evalUtc.AddDays(-38);
        if (studyStartUtc < windowStart)
            studyStartUtc = windowStart;
        if (studyStartUtc >= evalUtc)
            studyStartUtc = evalUtc.AddDays(-1);

        var windowSymbol = CandleWindowSlicer.Slice(solData.Candles, windowStart, evalUtc);
        var windowBtc = CandleWindowSlicer.Slice(btcData.Candles, windowStart, evalUtc);
        var intervalCandles = CandleAggregator.Aggregate(TradingSymbol.SOLUSDT, windowSymbol, "1m", key.Interval).Candles;
        if (intervalCandles.Count == 0)
            throw new InvalidOperationException("No SOLUSDT 30m candles available for near-miss conversion study.");

        var btcContext = new BtcContextIndex(windowBtc);
        var marketWideContext = new MarketWideContextIndex(
            new Dictionary<TradingSymbol, IReadOnlyList<KlineCandle>>
            {
                [TradingSymbol.SOLUSDT] = windowSymbol,
                [TradingSymbol.BTCUSDT] = windowBtc
            },
            includeBtcInProxy: true);

        var futuresLoader = new FuturesMarketDataLoader(settings.DataDirectory);
        var flowIndex = new ShortWindowFlowFeatureIndex(futuresLoader, TradingSymbol.SOLUSDT, intervalCandles, windowBtc);

        var scans = NoPaidDataShortWindowFlowResearchV1CrossSymbolSimulator.ScanSymbolInterval(
            TradingSymbol.SOLUSDT,
            key.Interval,
            intervalCandles,
            windowSymbol,
            flowIndex,
            btcContext,
            marketWideContext,
            windowStart,
            evalUtc,
            cancellationToken);

        var scan = scans.FirstOrDefault(s => s.Key == key)
                   ?? throw new InvalidOperationException($"Target combo {key} not produced by geometry scan.");

        var moderateTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
            scan.BaseTrades,
            NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.PrimaryCostScenario,
            btcContext,
            evalUtc);
        var stressPlusTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
            scan.BaseTrades,
            NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.StressPlusScenario,
            btcContext,
            evalUtc);

        var currentDistance = Sol30mNearMissConversionHistoryStudyBuilder.EvaluateCurrentNearMissDistance(
            key,
            intervalCandles,
            scan.BaseTrades,
            moderateTrades,
            flowIndex,
            btcContext,
            marketWideContext,
            studyStartUtc,
            cooldown,
            evalUtc);

        var result = Sol30mNearMissConversionHistoryStudyBuilder.Build(
            runAtUtc,
            studyStartUtc,
            evalUtc,
            key,
            activationConfig,
            intervalCandles,
            scan.BaseTrades,
            moderateTrades,
            stressPlusTrades,
            flowIndex,
            btcContext,
            marketWideContext,
            cooldown,
            currentDistance);

        Directory.CreateDirectory(settings.OutputDirectory);
        await Sol30mNearMissConversionHistoryStudyReportWriter.WriteAsync(settings.OutputDirectory, result, cancellationToken);
        await WriteRunMetadataAsync(result, evalUtc, cancellationToken);
        return result;
    }

    private async Task WriteRunMetadataAsync(
        Sol30mNearMissConversionHistoryStudyResult result,
        DateTime evalUtc,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = Sol30mNearMissConversionHistoryStudyCatalog.ModeName,
                settings.DataDirectory,
                settings.OutputDirectory,
                bootstrapFuturesData = settings.BootstrapFuturesData,
                backtestOnly = true,
                realOrdersPlaced = false,
                liveFuturesRecommended = false,
                nearMissConversionIsNotForwardProof = true,
                usesConfirmedClosedCandlesOnly = true,
                evaluatedAtUtc = evalUtc,
                candidateKey = Sol30mNearMissConversionHistoryStudyCatalog.CandidateKey,
                compactSummaryLine = result.Summary.CompactSummaryLine,
                recommendation = result.Summary.Recommendation,
                totalNearMissEvents = result.Summary.TotalNearMissEvents,
                conversionRateWithin24h = result.Summary.ConversionRateWithin24h
            }, JsonOptions),
            cancellationToken);
    }

    private async Task RefreshCandlesAsync(HistoricalKlineDataLoader loader, CancellationToken cancellationToken)
    {
        var downloader = new BinanceKlineBootstrapDownloader();
        foreach (var refreshSymbol in new[] { TradingSymbol.SOLUSDT, TradingSymbol.BTCUSDT })
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
