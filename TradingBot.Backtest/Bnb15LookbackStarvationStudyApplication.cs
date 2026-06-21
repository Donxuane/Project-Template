using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// Diagnostic-only root-cause study for BNB15 lookback starvation. Does not modify frozen profile or strategy.
/// </summary>
public sealed class Bnb15LookbackStarvationStudyApplication(BacktestSettings settings)
{
    public const string ModeName = "bnb15-lookback-starvation-study";
    public const string DefaultOutputSubdir = "bnb15-lookback-starvation-study";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<Bnb15LookbackStarvationStudyResult> RunAsync(CancellationToken cancellationToken)
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

        var frozenKey = NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.FrozenComboKey;
        var frozenConfig = NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.BuildFrozenActivationConfig();
        var frozenPath = NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.FrozenStatePath(settings.DataDirectory);

        FrozenCandidateState state;
        if (File.Exists(frozenPath))
        {
            state = JsonSerializer.Deserialize<FrozenCandidateState>(await File.ReadAllTextAsync(frozenPath, cancellationToken), JsonOptions)
                    ?? NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.BuildDefaultState(runAtUtc);
        }
        else
        {
            state = NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.BuildDefaultState(runAtUtc);
        }

        var symbolData = await loader.LoadAndValidateAsync(TradingSymbol.BNBUSDT, cancellationToken);
        var btc = await loader.LoadAndValidateAsync(TradingSymbol.BTCUSDT, cancellationToken);
        if (symbolData.Candles.Count == 0 || btc.Candles.Count == 0)
            throw new InvalidOperationException("BNBUSDT and BTCUSDT local candle data required for BNB15 lookback study.");

        var validated = new Dictionary<TradingSymbol, SymbolValidationResult>
        {
            [TradingSymbol.BNBUSDT] = symbolData,
            [TradingSymbol.BTCUSDT] = btc
        };
        var (dataStartUtc, dataEndUtc) = RobustnessWindowResolver.ResolveDataBounds(validated);
        var spanDays = (int)Math.Max(1, (dataEndUtc - dataStartUtc).TotalDays);
        var windowStart = dataEndUtc.AddDays(-Math.Min(365, spanDays));
        if (windowStart < dataStartUtc)
            windowStart = dataStartUtc;

        var windowSymbol = CandleWindowSlicer.Slice(symbolData.Candles, windowStart, dataEndUtc);
        var windowBtc = CandleWindowSlicer.Slice(btc.Candles, windowStart, dataEndUtc);
        var intervalCandles = CandleAggregator.Aggregate(TradingSymbol.BNBUSDT, windowSymbol, "1m", "15m").Candles;
        if (intervalCandles.Count == 0)
            throw new InvalidOperationException("No BNBUSDT 15m candles available for lookback study.");

        var btcContext = new BtcContextIndex(windowBtc);
        var marketWideContext = new MarketWideContextIndex(
            new Dictionary<TradingSymbol, IReadOnlyList<KlineCandle>>
            {
                [TradingSymbol.BNBUSDT] = windowSymbol,
                [TradingSymbol.BTCUSDT] = windowBtc
            },
            includeBtcInProxy: true);

        var futuresLoader = new FuturesMarketDataLoader(settings.DataDirectory);
        var flowIndex = new ShortWindowFlowFeatureIndex(futuresLoader, TradingSymbol.BNBUSDT, intervalCandles, windowBtc);

        var scans = NoPaidDataShortWindowFlowResearchV1CrossSymbolSimulator.ScanSymbolInterval(
            TradingSymbol.BNBUSDT,
            "15m",
            intervalCandles,
            windowSymbol,
            flowIndex,
            btcContext,
            marketWideContext,
            windowStart,
            dataEndUtc,
            cancellationToken);

        var scan = scans.FirstOrDefault(s => s.Key == frozenKey)
                   ?? throw new InvalidOperationException($"Frozen combo {frozenKey} not produced by geometry scan.");

        var forwardStart = state.FrozenStartUtc;
        var forwardEnd = dataEndUtc > forwardStart ? dataEndUtc : forwardStart;

        var moderateTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
            scan.BaseTrades,
            NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.PrimaryCostScenario,
            btcContext,
            dataEndUtc);
        var stressTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
            scan.BaseTrades,
            NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.StressPlusScenario,
            btcContext,
            dataEndUtc);

        var frozenForwardSim = NoPaidDataShortWindowFlowResearchV1CrossSymbolEngine.Simulate(
            frozenKey,
            frozenConfig,
            moderateTrades,
            forwardStart,
            forwardEnd,
            flowIndex,
            NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.PrimaryCostScenario,
            collectPeriods: true);

        var result = Bnb15LookbackStarvationStudyBuilder.Build(
            runAtUtc,
            state,
            frozenKey,
            frozenConfig,
            forwardStart,
            forwardEnd,
            moderateTrades,
            stressTrades,
            scan.RawSignalEntryTimesUtc,
            frozenForwardSim);

        Directory.CreateDirectory(settings.OutputDirectory);
        await Bnb15LookbackStarvationStudyReportWriter.WriteAsync(settings.OutputDirectory, result, cancellationToken);
        await WriteRunMetadataAsync(result, cancellationToken);
        return result;
    }

    private async Task WriteRunMetadataAsync(Bnb15LookbackStarvationStudyResult result, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = ModeName,
                settings.DataDirectory,
                settings.OutputDirectory,
                bootstrapFuturesData = settings.BootstrapFuturesData,
                backtestOnly = true,
                realOrdersPlaced = false,
                liveFuturesRecommended = false,
                frozenProfileUnchanged = true,
                diagnosticVariantsAreNotForwardProof = true,
                compactSummaryLine = result.Summary.CompactSummaryLine,
                primaryRootCause = result.Summary.PrimaryRootCause,
                overallRecommendation = result.Summary.PlainEnglish.OverallStudyRecommendation
            }, JsonOptions),
            cancellationToken);
    }

    private async Task RefreshCandlesAsync(HistoricalKlineDataLoader loader, CancellationToken cancellationToken)
    {
        var downloader = new BinanceKlineBootstrapDownloader();
        foreach (var refreshSymbol in new[] { TradingSymbol.BNBUSDT, TradingSymbol.BTCUSDT })
        {
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
