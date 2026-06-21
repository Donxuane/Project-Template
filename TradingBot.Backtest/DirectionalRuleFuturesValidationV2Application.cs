using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class DirectionalRuleFuturesValidationV2Application(BacktestSettings settings)
{
    private static readonly string[] RequiredIntervals = ["5m", "15m", "30m"];

    public async Task<DirectionalRuleFuturesValidationV2RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var bootstrapDays = settings.BootstrapDays ?? settings.RobustnessWindows.DefaultIfEmpty(90).Max();
        var bootstrap = await BtcContextDataBootstrap.EnsureBtcUsdtDataAsync(settings, bootstrapDays, cancellationToken);
        if (bootstrap.Validation.Candles.Count == 0)
            throw new InvalidOperationException($"BTCUSDT candle bootstrap failed under '{settings.DataDirectory}'.");

        var symbols = BroadReachabilitySymbolResolver.ResolveAvailableSymbols(settings)
            .Where(s => s is TradingSymbol.ETHUSDT or TradingSymbol.BNBUSDT or TradingSymbol.BTCUSDT)
            .ToArray();
        if (!symbols.Contains(TradingSymbol.BTCUSDT))
            throw new InvalidOperationException("BTCUSDT data required for DirectionalRuleFuturesValidationV2.");

        var discoveryPath = ResolveDiscoveryJsonPath();
        var rules = await DirectionalRuleFuturesSimulationV1RuleCatalog.LoadHoldoutRulesAsync(discoveryPath, cancellationToken);
        var profiles = DirectionalRuleFuturesValidationV2Catalog.BuildProfiles(rules);
        if (profiles.Count == 0)
            throw new InvalidOperationException("No validation profiles resolved from discovery rules.");

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
        var writer = new DirectionalRuleFuturesValidationV2ReportWriter(settings.OutputDirectory);
        await writer.InitializeStreamingTradesAsync(cancellationToken);
        var accumulator = new DirectionalRuleFuturesValidationV2RunAccumulator();

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

            var intervalCandlesBySymbol = new Dictionary<(TradingSymbol Symbol, string Interval), IReadOnlyList<KlineCandle>>();
            foreach (var symbol in new[] { TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT })
            {
                if (!windowDataBySymbol.TryGetValue(symbol, out var oneMinute) || oneMinute.Count == 0)
                    continue;
                foreach (var interval in RequiredIntervals)
                {
                    var aggregate = CandleAggregator.Aggregate(symbol, oneMinute, "1m", interval);
                    if (aggregate.Candles.Count > 0)
                        intervalCandlesBySymbol[(symbol, interval)] = aggregate.Candles;
                }
            }

            foreach (var profile in profiles)
            {
                if (!windowDataBySymbol.TryGetValue(profile.Symbol, out var oneMinuteCandles) || oneMinuteCandles.Count == 0)
                    continue;
                if (!intervalCandlesBySymbol.TryGetValue((profile.Symbol, profile.Interval), out var intervalCandles))
                    continue;

                var scan = DirectionalRuleFuturesValidationV2Simulator.ScanProfile(
                    profile,
                    window.Label,
                    intervalCandles,
                    oneMinuteCandles,
                    btcContext,
                    marketWideContext,
                    cancellationToken);
                accumulator.IngestScanResult(profile, window.Label, scan);
                if (scan.Trades.Count == 0)
                    continue;

                var expanded = DirectionalRuleFuturesValidationV2Simulator.ApplyCostScenarios(scan.Trades);
                accumulator.IngestExpandedBatch(expanded);
                await writer.AppendStreamingTradesAsync(expanded, cancellationToken);
            }

            foreach (var symbol in new[] { TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT })
            {
                if (!windowDataBySymbol.TryGetValue(symbol, out var oneMinute) || oneMinute.Count == 0)
                    continue;

                var rule01Interval = symbol == TradingSymbol.ETHUSDT ? "30m" : "5m";
                var rule05Interval = "15m";
                if (!intervalCandlesBySymbol.TryGetValue((symbol, rule01Interval), out var rule01Candles)
                    || !intervalCandlesBySymbol.TryGetValue((symbol, rule05Interval), out var rule05Candles))
                    continue;

                var overlapRows = DirectionalRuleFuturesValidationV2Simulator.AnalyzeRuleOverlap(
                    rules,
                    window.Label,
                    symbol,
                    rule01Interval,
                    rule05Interval,
                    rule01Candles,
                    rule05Candles,
                    btcContext,
                    marketWideContext,
                    cancellationToken);
                accumulator.AddOverlapRows(overlapRows);

                var priorityProfiles = DirectionalRuleFuturesValidationV2Catalog.BuildPriorityProfiles(rules, symbol);
                foreach (var priorityProfile in priorityProfiles)
                {
                    var scan = DirectionalRuleFuturesValidationV2Simulator.ScanPriorityEth(
                        priorityProfile,
                        DirectionalRuleFuturesValidationV2Catalog.ResolveRule(rules, "Rule01")!,
                        DirectionalRuleFuturesValidationV2Catalog.ResolveRule(rules, "Rule05")!,
                        window.Label,
                        rule01Candles,
                        rule05Candles,
                        oneMinute,
                        btcContext,
                        marketWideContext,
                        cancellationToken);
                    accumulator.IngestScanResult(priorityProfile, window.Label, scan);
                    if (scan.Trades.Count == 0)
                        continue;

                    var expanded = DirectionalRuleFuturesValidationV2Simulator.ApplyCostScenarios(scan.Trades);
                    accumulator.IngestExpandedBatch(expanded);
                    await writer.AppendStreamingTradesAsync(expanded, cancellationToken);
                }
            }
        }

        await writer.FinalizeStreamingTradesAsync(cancellationToken);

        var summaries = DirectionalRuleFuturesValidationV2Aggregator.ApplyRobustnessLabels(accumulator.BuildSummaries());
        var windowRobustness = DirectionalRuleFuturesValidationV2Aggregator.BuildWindowRobustness(summaries);
        var costSensitivity = DirectionalRuleFuturesValidationV2Aggregator.BuildCostSensitivity(summaries, windowRobustness);
        var drawdown = DirectionalRuleFuturesValidationV2Aggregator.ApplyWorstWindowNet(
            accumulator.BuildDrawdownRows(),
            windowRobustness);
        var researchAnswers = DirectionalRuleFuturesValidationV2Aggregator.BuildResearchAnswers(
            summaries,
            windowRobustness,
            costSensitivity,
            drawdown,
            accumulator.OverlapRows,
            accumulator.ExecutedTradeCount,
            accumulator.SkippedSignalCount);

        await writer.WriteAsync(
            summaries,
            windowRobustness,
            costSensitivity,
            drawdown,
            accumulator.OverlapRows,
            researchAnswers,
            cancellationToken);

        var moderatePositiveProfiles = windowRobustness
            .Count(r => string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase)
                        && r.AggregateNetPositive
                        && r.Window30dTrades + r.Window60dTrades + r.Window90dTrades >= DirectionalRuleFuturesValidationV2Aggregator.MinimumMeaningfulTrades);

        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = "directional-rule-futures-validation-v2",
                settings.DataDirectory,
                settings.OutputDirectory,
                settings.RobustnessWindows,
                btcContextEnabled = true,
                discoveryJsonPath = discoveryPath,
                bootstrapDays,
                bootstrap.HadLocalDataBeforeBootstrap,
                symbols = symbols.Select(s => s.ToString()).ToArray(),
                candidateCount = DirectionalRuleFuturesValidationV2Catalog.BuildCandidates().Count,
                profileCount = profiles.Count,
                executedTradeCount = accumulator.ExecutedTradeCount,
                skippedSignalCount = accumulator.SkippedSignalCount,
                moderatePositiveProfiles,
                costScenarios = DirectionalRuleFuturesValidationV2CostModel.BuildValidationScenarios()
                    .Select(s => s.Label).ToArray(),
                overlapPolicies = DirectionalRuleFuturesValidationV2Catalog.OverlapPolicies
                    .Select(p => p.ToString()).ToArray(),
                cooldownOptions = DirectionalRuleFuturesValidationV2Catalog.CooldownCandleOptions,
                holdMinutes = DirectionalRuleFuturesValidationV2Catalog.HoldMinutesOptions,
                targetPercent = DirectionalRuleFuturesValidationV2Catalog.PrimaryTargetPercent,
                stopPercent = DirectionalRuleFuturesValidationV2Catalog.PrimaryStopPercent,
                streamingTradesOutput = true,
                backtestOnly = true,
                liveFuturesRecommended = false
            }, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        return new DirectionalRuleFuturesValidationV2RunResult(
            summaries,
            windowRobustness,
            costSensitivity,
            drawdown,
            accumulator.OverlapRows,
            researchAnswers,
            accumulator.ExecutedTradeCount,
            accumulator.SkippedSignalCount);
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
