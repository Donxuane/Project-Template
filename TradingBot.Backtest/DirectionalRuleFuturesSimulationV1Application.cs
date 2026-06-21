using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class DirectionalRuleFuturesSimulationV1Application(BacktestSettings settings)
{
    public async Task<DirectionalRuleFuturesSimulationRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var bootstrapDays = settings.BootstrapDays ?? settings.RobustnessWindows.DefaultIfEmpty(90).Max();
        var bootstrap = await BtcContextDataBootstrap.EnsureBtcUsdtDataAsync(settings, bootstrapDays, cancellationToken);
        if (bootstrap.Validation.Candles.Count == 0)
            throw new InvalidOperationException($"BTCUSDT candle bootstrap failed under '{settings.DataDirectory}'.");

        var symbols = BroadReachabilitySymbolResolver.ResolveAvailableSymbols(settings)
            .Where(s => s is TradingSymbol.ETHUSDT or TradingSymbol.BNBUSDT or TradingSymbol.SOLUSDT or TradingSymbol.BTCUSDT)
            .ToArray();
        if (!symbols.Contains(TradingSymbol.BTCUSDT))
            throw new InvalidOperationException("BTCUSDT data required for DirectionalRuleFuturesSimulationV1.");

        var tradeSymbols = symbols.Where(s => s is not TradingSymbol.BTCUSDT).ToArray();
        var intervals = settings.Intervals;
        var discoveryPath = ResolveDiscoveryJsonPath();
        var rules = await DirectionalRuleFuturesSimulationV1RuleCatalog.LoadHoldoutRulesAsync(discoveryPath, cancellationToken);

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

        Directory.CreateDirectory(settings.OutputDirectory);
        var writer = new DirectionalRuleFuturesSimulationV1ReportWriter(settings.OutputDirectory);
        await writer.InitializeStreamingTradesAsync(cancellationToken);

        var accumulator = new DirectionalRuleFuturesSimulationV1RunAccumulator();

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

                    var batch = DirectionalRuleFuturesSimulationV1Simulator.ScanSymbolInterval(
                        symbol,
                        interval,
                        window.Label,
                        aggregate.Candles,
                        windowCandles,
                        rules,
                        btcContext,
                        marketWideContext,
                        cancellationToken);

                    if (batch.Count == 0)
                        continue;

                    var expandedBatch = DirectionalRuleFuturesSimulationV1Simulator.ExpandCostScenarios(batch);
                    accumulator.AddBaseTrades(batch.Count);
                    accumulator.IngestExpandedBatch(expandedBatch);
                    await writer.AppendStreamingTradesAsync(expandedBatch, cancellationToken);
                }
            }
        }

        // Fix base trade count - track separately
        await writer.FinalizeStreamingTradesAsync(cancellationToken);

        var summaries = accumulator.BuildSummaries();
        var rulePerformance = accumulator.BuildRulePerformance();
        var windowRobustness = DirectionalRuleFuturesSimulationV1Aggregator.BuildWindowRobustness(summaries);
        var costSensitivity = accumulator.BuildCostSensitivity();
        var researchAnswers = DirectionalRuleFuturesSimulationV1Aggregator.BuildResearchAnswers(
            summaries,
            rulePerformance,
            windowRobustness,
            costSensitivity,
            rules,
            accumulator.ModerateTradeCount,
            accumulator.ModerateNetPnlQuote,
            accumulator.EntryModeModerateStats());

        await writer.WriteAsync(
            summaries,
            [],
            rulePerformance,
            windowRobustness,
            costSensitivity,
            researchAnswers,
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = "directional-rule-futures-simulation-v1",
                settings.DataDirectory,
                settings.OutputDirectory,
                settings.Intervals,
                settings.RobustnessWindows,
                btcContextEnabled = true,
                discoveryJsonPath = discoveryPath,
                bootstrapDays,
                bootstrap.HadLocalDataBeforeBootstrap,
                symbols = symbols.Select(s => s.ToString()).ToArray(),
                tradeSymbols = tradeSymbols.Select(s => s.ToString()).ToArray(),
                ruleCount = rules.Count,
                rules = rules.Select(r => new { r.RuleName, r.Direction, r.RuleDescription }).ToArray(),
                baseTradeCount = accumulator.BaseTradeCount,
                tradeCount = accumulator.ExpandedTradeCount,
                moderateTradeCount = accumulator.ModerateTradeCount,
                moderateNetPnlQuote = accumulator.ModerateNetPnlQuote,
                positiveModerateConfigs = rulePerformance.Count(r =>
                    string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase)
                    && r.TradeCount >= DirectionalRuleFuturesSimulationV1Aggregator.MinimumMeaningfulTrades
                    && r.NetPnlQuote >= 0m),
                costScenarios = DirectionalRuleFuturesSimulationV1Simulator.SimulationCostScenarioLabels,
                entryModes = DirectionalRuleFuturesSimulationV1Simulator.EntryModes.Select(m => m.ToString()).ToArray(),
                targetStopMatrix = DirectionalRuleFuturesSimulationV1Simulator.TargetStopMatrix
                    .Select(p => new { target = p.Target, stop = p.Stop }).ToArray(),
                maxHoldMinutes = DirectionalRuleFuturesSimulationV1Simulator.MaxHoldMinutesOptions,
                streamingTradesOutput = true,
                backtestOnly = true,
                liveFuturesRecommended = false
            }, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        var positiveModerateConfigs = rulePerformance.Count(r =>
            string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase)
            && r.TradeCount >= DirectionalRuleFuturesSimulationV1Aggregator.MinimumMeaningfulTrades
            && r.NetPnlQuote >= 0m);

        return new DirectionalRuleFuturesSimulationRunResult(
            [],
            summaries,
            rulePerformance,
            windowRobustness,
            costSensitivity,
            researchAnswers,
            rules,
            tradeSymbols,
            intervals,
            accumulator.BaseTradeCount,
            accumulator.ExpandedTradeCount,
            accumulator.ModerateTradeCount,
            accumulator.ModerateNetPnlQuote,
            positiveModerateConfigs);
    }

    private string ResolveDiscoveryJsonPath()
    {
        if (!string.IsNullOrWhiteSpace(settings.DirectionalRuleDiscoveryJsonPath)
            && File.Exists(settings.DirectionalRuleDiscoveryJsonPath))
        {
            return settings.DirectionalRuleDiscoveryJsonPath;
        }

        var repoRoot = Directory.GetCurrentDirectory();
        var defaultPath = Path.Combine(
            repoRoot,
            "TradingBot.Backtest",
            "output",
            "long-short-futures-feasibility-v1-run",
            "long-short-entry-time-rule-discovery.json");
        return File.Exists(defaultPath) ? defaultPath : defaultPath;
    }
}
