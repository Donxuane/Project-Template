using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class MarketRegimeForwardEdgeStudyWithBtcContextApplication(BacktestSettings settings)
{
    public async Task<MarketRegimeForwardEdgeStudyResult> RunAsync(CancellationToken cancellationToken)
    {
        var bootstrapDays = settings.BootstrapDays ?? settings.RobustnessWindows.DefaultIfEmpty(90).Max();
        var bootstrap = await BtcContextDataBootstrap.EnsureBtcUsdtDataAsync(settings, bootstrapDays, cancellationToken);
        if (bootstrap.Validation.Candles.Count == 0)
            throw new InvalidOperationException($"BTCUSDT candle bootstrap failed under '{settings.DataDirectory}'.");

        var symbols = BroadReachabilitySymbolResolver.ResolveAvailableSymbols(settings)
            .Where(s => s is TradingSymbol.ETHUSDT or TradingSymbol.BNBUSDT or TradingSymbol.SOLUSDT or TradingSymbol.BTCUSDT)
            .ToArray();
        if (!symbols.Contains(TradingSymbol.BTCUSDT))
            throw new InvalidOperationException("BTCUSDT data required for MarketRegimeForwardEdgeStudyWithBtcContext.");

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

        var roundTripCostPercent = RangeExpansionCostModel.ComputeRoundTripCostPercent(
            new ExecutionCostSettings(settings.FeeRatePercent, settings.EstimatedSpreadPercent, settings.SlippagePercent));

        var allObservations = new List<MarketRegimeForwardEdgeObservation>();
        var allMatrixRows = new List<TargetBeforeStopMatrixRow>();

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

                    var observations = MarketRegimeForwardEdgeScanner.ScanSymbolInterval(
                        symbol,
                        interval,
                        window.Label,
                        aggregate.Candles,
                        windowCandles,
                        roundTripCostPercent,
                        btcContext,
                        marketWideContext,
                        cancellationToken);

                    allObservations.AddRange(observations);
                    allMatrixRows.AddRange(MarketRegimeForwardEdgeScanner.BuildTargetBeforeStopMatrix(
                        window.Label, symbol, interval, observations));
                }
            }
        }

        var summary = MarketRegimeForwardEdgeAggregator.BuildSummary(allObservations);
        var symbolIntervalRanking = MarketRegimeForwardEdgeAggregator.BuildSymbolIntervalRanking(summary);
        var regimeBucketRanking = MarketRegimeForwardEdgeAggregator.BuildRegimeBucketRanking(allObservations);
        var sessionRanking = MarketRegimeForwardEdgeAggregator.BuildSessionRanking(allObservations);
        var btcContextRanking = MarketRegimeForwardEdgeAggregator.BuildBtcContextRanking(allObservations);
        var entryTimeRules = MarketRegimeForwardEdgeAggregator.BuildEntryTimeRules(allObservations, includeBtcContextRules: true);
        var researchAnswers = MarketRegimeForwardEdgeAggregator.BuildResearchAnswers(
            allObservations,
            symbolIntervalRanking,
            regimeBucketRanking,
            sessionRanking,
            allMatrixRows,
            entryTimeRules,
            roundTripCostPercent,
            btcContextRanking,
            btcContextEnabled: true);

        Directory.CreateDirectory(settings.OutputDirectory);
        var writer = new MarketRegimeForwardEdgeReportWriter(settings.OutputDirectory);
        await writer.WriteAsync(
            summary,
            symbolIntervalRanking,
            regimeBucketRanking,
            sessionRanking,
            allMatrixRows,
            researchAnswers,
            btcContextRanking,
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "market-regime-entry-time-rule-discovery.json"),
            JsonSerializer.Serialize(entryTimeRules, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = "market-regime-forward-edge-study-with-btc-context",
                settings.DataDirectory,
                settings.OutputDirectory,
                settings.Intervals,
                settings.RobustnessWindows,
                roundTripCostPercent,
                btcContextEnabled = true,
                marketWideIncludesBtc = true,
                bootstrapDays,
                bootstrap.HadLocalDataBeforeBootstrap,
                bootstrap.MergeResult,
                btcDataQuality = bootstrap.Validation.Issues,
                symbols = symbols.Select(s => s.ToString()).ToArray(),
                tradeSymbols = tradeSymbols.Select(s => s.ToString()).ToArray(),
                observationCount = allObservations.Count,
                entryTimeRuleCount = entryTimeRules.Count,
                holdoutPositiveRules = entryTimeRules.Count(r => r.HoldoutMedianExpectedNetPercent >= 0m)
            }, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        return new MarketRegimeForwardEdgeStudyResult(
            allObservations,
            summary,
            symbolIntervalRanking,
            regimeBucketRanking,
            sessionRanking,
            allMatrixRows,
            entryTimeRules,
            btcContextRanking,
            researchAnswers,
            symbols,
            intervals,
            BtcContextEnabled: true);
    }
}
