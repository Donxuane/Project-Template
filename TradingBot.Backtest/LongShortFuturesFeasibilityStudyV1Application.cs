using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class LongShortFuturesFeasibilityStudyV1Application(BacktestSettings settings)
{
    public async Task<LongShortFuturesFeasibilityStudyResult> RunAsync(CancellationToken cancellationToken)
    {
        var bootstrapDays = settings.BootstrapDays ?? settings.RobustnessWindows.DefaultIfEmpty(90).Max();
        var bootstrap = await BtcContextDataBootstrap.EnsureBtcUsdtDataAsync(settings, bootstrapDays, cancellationToken);
        if (bootstrap.Validation.Candles.Count == 0)
            throw new InvalidOperationException($"BTCUSDT candle bootstrap failed under '{settings.DataDirectory}'.");

        var symbols = BroadReachabilitySymbolResolver.ResolveAvailableSymbols(settings)
            .Where(s => s is TradingSymbol.ETHUSDT or TradingSymbol.BNBUSDT or TradingSymbol.SOLUSDT or TradingSymbol.BTCUSDT)
            .ToArray();
        if (!symbols.Contains(TradingSymbol.BTCUSDT))
            throw new InvalidOperationException("BTCUSDT data required for LongShortFuturesFeasibilityStudyV1.");

        var tradeSymbols = symbols.Where(s => s is not TradingSymbol.BTCUSDT).ToArray();
        var intervals = settings.Intervals;
        var dataLoader = new HistoricalKlineDataLoader(settings);
        var validatedDataBySymbol = new Dictionary<TradingSymbol, SymbolValidationResult>();
        foreach (var symbol in symbols)
            validatedDataBySymbol[symbol] = await dataLoader.LoadAndValidateAsync(symbol, cancellationToken);

        var (dataStartUtc, dataEndUtc) = RobustnessWindowResolver.ResolveDataBounds(validatedDataBySymbol);
        var windows = RobustnessWindowResolver.Resolve(
            dataStartUtc,
            dataEndUtc,
            settings.RobustnessWindows,
            settings.RobustnessWindowStartUtc,
            settings.RobustnessWindowEndUtc)
            .Where(w => !w.SkippedInsufficientData)
            .ToArray();

        var allObservations = new List<LongShortFuturesFeasibilityObservation>();
        var allMatrixRows = new List<LongShortTargetStopMatrixRow>();
        var costScenarioLabels = LongShortFuturesFeasibilityStudyV1CostModel.BuildStudyScenarios()
            .Select(s => s.Label)
            .ToArray();

        foreach (var window in windows)
        {
            var windowDataBySymbol = validatedDataBySymbol.ToDictionary(
                kv => kv.Key,
                kv => CandleWindowSlicer.Slice(kv.Value.Candles, window.StartUtc, window.EndUtc));

            if (!windowDataBySymbol.TryGetValue(TradingSymbol.BTCUSDT, out var windowBtcCandles) || windowBtcCandles.Count == 0)
                continue;

            var btcContext = new BtcContextIndex(windowBtcCandles);
            var marketWideContext = new MarketWideContextIndex(
                windowDataBySymbol.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<KlineCandle>)kv.Value),
                includeBtcInProxy: true);

            foreach (var interval in intervals)
            {
                foreach (var symbol in tradeSymbols)
                {
                    if (!windowDataBySymbol.TryGetValue(symbol, out var windowCandles) || windowCandles.Count == 0)
                        continue;

                    var aggregate = CandleAggregator.Aggregate(symbol, windowCandles, "1m", interval);
                    if (aggregate.Candles.Count == 0)
                        continue;

                    var batch = LongShortFuturesFeasibilityStudyV1Scanner.ScanSymbolInterval(
                        symbol,
                        interval,
                        window.Label,
                        aggregate.Candles,
                        windowCandles,
                        btcContext,
                        marketWideContext,
                        cancellationToken);

                    allObservations.AddRange(batch.Observations);
                    allMatrixRows.AddRange(batch.TargetStopMatrix);
                }
            }
        }

        var summary = LongShortFuturesFeasibilityStudyV1Aggregator.BuildSummary(allObservations);
        var symbolIntervalRanking = LongShortFuturesFeasibilityStudyV1Aggregator.BuildSymbolIntervalRanking(allObservations);
        var regimeRanking = LongShortFuturesFeasibilityStudyV1Aggregator.BuildRegimeRanking(allObservations);
        var costSensitivity = LongShortFuturesFeasibilityStudyV1Aggregator.BuildCostSensitivity(allObservations);
        var entryTimeRules = LongShortFuturesFeasibilityStudyV1Aggregator.BuildEntryTimeRules(allObservations, "futures-moderate")
            .Concat(LongShortFuturesFeasibilityStudyV1Aggregator.BuildEntryTimeRules(allObservations, "futures-low"))
            .ToArray();
        var researchAnswers = LongShortFuturesFeasibilityStudyV1Aggregator.BuildResearchAnswers(
            allObservations,
            summary,
            symbolIntervalRanking,
            regimeRanking,
            allMatrixRows,
            costSensitivity,
            entryTimeRules);

        Directory.CreateDirectory(settings.OutputDirectory);
        var writer = new LongShortFuturesFeasibilityStudyV1ReportWriter(settings.OutputDirectory);
        await writer.WriteAsync(
            summary,
            symbolIntervalRanking,
            regimeRanking,
            allMatrixRows,
            costSensitivity,
            entryTimeRules,
            researchAnswers,
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = "long-short-futures-feasibility-v1",
                settings.DataDirectory,
                settings.OutputDirectory,
                settings.Intervals,
                settings.RobustnessWindows,
                btcContextEnabled = true,
                marketWideIncludesBtc = true,
                bootstrapDays,
                bootstrap.HadLocalDataBeforeBootstrap,
                bootstrap.MergeResult,
                btcDataQuality = bootstrap.Validation.Issues,
                symbols = symbols.Select(s => s.ToString()).ToArray(),
                tradeSymbols = tradeSymbols.Select(s => s.ToString()).ToArray(),
                observationCount = allObservations.Count,
                entryTimeRuleCount = entryTimeRules.Length,
                holdoutNonNegativeRules = entryTimeRules.Count(r => r.HoldoutMedianExpectedNetPercent >= 0m),
                costScenarios = costScenarioLabels,
                tradeModes = Enum.GetNames<LongShortTradeMode>()
            }, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        return new LongShortFuturesFeasibilityStudyResult(
            allObservations,
            summary,
            symbolIntervalRanking,
            regimeRanking,
            allMatrixRows,
            costSensitivity,
            entryTimeRules,
            researchAnswers,
            symbols,
            intervals);
    }
}
