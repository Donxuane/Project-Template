using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class DirectionalRuleFuturesValidationV3Application(BacktestSettings settings)
{
    public async Task<DirectionalRuleFuturesValidationV3RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var robustnessWindows = settings.RobustnessWindows.Count > 0
            ? settings.RobustnessWindows
            : settings.RunDirectionalRuleFuturesValidationV3Focused
                ? DirectionalRuleFuturesValidationV3Catalog.FocusedRobustnessWindows()
                : DirectionalRuleFuturesValidationV3Catalog.DefaultRobustnessWindows();
        var bootstrapDays = settings.BootstrapDays ?? Math.Max(180, robustnessWindows.Max());
        var bootstrap = await BtcContextDataBootstrap.EnsureBtcUsdtDataAsync(settings, bootstrapDays, cancellationToken);
        if (bootstrap.Validation.Candles.Count == 0)
            throw new InvalidOperationException($"BTCUSDT candle bootstrap failed under '{settings.DataDirectory}'.");

        var symbols = BroadReachabilitySymbolResolver.ResolveAvailableSymbols(settings)
            .Where(s => s is TradingSymbol.BNBUSDT or TradingSymbol.BTCUSDT)
            .ToArray();
        if (!symbols.Contains(TradingSymbol.BTCUSDT) || !symbols.Contains(TradingSymbol.BNBUSDT))
            throw new InvalidOperationException("BNBUSDT and BTCUSDT data required for DirectionalRuleFuturesValidationV3.");

        var discoveryPath = ResolveDiscoveryJsonPath();
        var rules = await DirectionalRuleFuturesSimulationV1RuleCatalog.LoadHoldoutRulesAsync(discoveryPath, cancellationToken);
        var profiles = settings.RunDirectionalRuleFuturesValidationV3Focused
            ? DirectionalRuleFuturesValidationV3Catalog.BuildFocusedProfiles(rules)
            : DirectionalRuleFuturesValidationV3Catalog.BuildProfiles(rules);
        if (profiles.Count == 0)
            throw new InvalidOperationException("Rule01 not found for DirectionalRuleFuturesValidationV3.");

        var dataLoader = new HistoricalKlineDataLoader(settings);
        var validatedDataBySymbol = new Dictionary<TradingSymbol, SymbolValidationResult>
        {
            [TradingSymbol.BTCUSDT] = await dataLoader.LoadAndValidateAsync(TradingSymbol.BTCUSDT, cancellationToken),
            [TradingSymbol.BNBUSDT] = await dataLoader.LoadAndValidateAsync(TradingSymbol.BNBUSDT, cancellationToken)
        };

        var (dataStartUtc, dataEndUtc) = RobustnessWindowResolver.ResolveDataBounds(validatedDataBySymbol);
        var historyDaysAvailable = (int)Math.Max(1, (dataEndUtc - dataStartUtc).TotalDays);
        var windows = BuildValidationWindows(
            dataStartUtc,
            dataEndUtc,
            robustnessWindows,
            settings.RobustnessWindowStartUtc,
            settings.RobustnessWindowEndUtc);

        Directory.CreateDirectory(settings.OutputDirectory);
        var accumulator = new DirectionalRuleFuturesValidationV3RunAccumulator();
        var allExpandedTrades = new List<DirectionalRuleV3TradeRecord>();

        foreach (var window in windows.Where(w => !w.SkippedInsufficientData))
        {
            var windowDataBySymbol = validatedDataBySymbol.ToDictionary(
                kv => kv.Key,
                kv => CandleWindowSlicer.Slice(kv.Value.Candles, window.StartUtc, window.EndUtc));

            if (!windowDataBySymbol.TryGetValue(TradingSymbol.BTCUSDT, out var windowBtcCandles) || windowBtcCandles.Count == 0)
                continue;
            if (!windowDataBySymbol.TryGetValue(TradingSymbol.BNBUSDT, out var windowBnbOneMinute) || windowBnbOneMinute.Count == 0)
                continue;

            var btcContext = new BtcContextIndex(windowBtcCandles);
            var marketWideContext = new MarketWideContextIndex(
                windowDataBySymbol.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<KlineCandle>)kv.Value),
                includeBtcInProxy: true);
            var intervalCandles = CandleAggregator.Aggregate(TradingSymbol.BNBUSDT, windowBnbOneMinute, "1m", "5m").Candles;
            if (intervalCandles.Count == 0)
                continue;

            foreach (var profile in profiles)
            {
                var scan = DirectionalRuleFuturesValidationV3Simulator.ScanProfile(
                    profile,
                    window.Label,
                    intervalCandles,
                    windowBnbOneMinute,
                    btcContext,
                    marketWideContext,
                    cancellationToken);
                accumulator.IngestScanResult(profile, window.Label, scan);
                if (scan.Trades.Count == 0)
                    continue;

                var expanded = DirectionalRuleFuturesValidationV3Simulator.ApplyCostScenarios(scan.Trades);
                accumulator.IngestExpandedBatch(expanded);
                allExpandedTrades.AddRange(expanded);
            }
        }

        var summaries = accumulator.BuildSummaries();
        var windowRobustness = DirectionalRuleFuturesValidationV3Aggregator.BuildWindowRobustness(summaries);
        summaries = DirectionalRuleFuturesValidationV3Aggregator.ApplyCrossWindowLabels(summaries, windowRobustness);
        var costSensitivity = DirectionalRuleFuturesValidationV3Aggregator.BuildCostSensitivity(windowRobustness);
        var drawdown = DirectionalRuleFuturesValidationV3Aggregator.ApplyWorstWindowNet(
            accumulator.BuildDrawdownRows(),
            windowRobustness);
        var variantComparison = DirectionalRuleFuturesValidationV3Aggregator.BuildVariantComparison(windowRobustness, drawdown);
        var reportConsistency = DirectionalRuleFuturesValidationV3Aggregator.BuildReportConsistency(
            summaries,
            allExpandedTrades,
            robustnessWindows);
        var researchAnswers = DirectionalRuleFuturesValidationV3Aggregator.BuildResearchAnswers(
            variantComparison,
            windowRobustness,
            drawdown,
            reportConsistency,
            accumulator.ExecutedTradeCount,
            accumulator.SkippedSignalCount,
            historyDaysAvailable);

        var writer = new DirectionalRuleFuturesValidationV3ReportWriter(settings.OutputDirectory);
        await writer.WriteAsync(
            summaries,
            allExpandedTrades,
            windowRobustness,
            costSensitivity,
            drawdown,
            variantComparison,
            reportConsistency,
            researchAnswers,
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = "directional-rule-futures-validation-v3",
                focusedProfiles = settings.RunDirectionalRuleFuturesValidationV3Focused,
                settings.DataDirectory,
                settings.OutputDirectory,
                robustnessWindows,
                bootstrapDays,
                historyDaysAvailable,
                dataStartUtc,
                dataEndUtc,
                discoveryJsonPath = discoveryPath,
                profileCount = profiles.Count,
                primaryCandidateCount = profiles.Count(p => p.IsPrimaryCandidate),
                windowCount = windows.Count(w => !w.SkippedInsufficientData),
                expandedTradeRowCount = accumulator.ExecutedTradeCount,
                skippedSignalCount = accumulator.SkippedSignalCount,
                reportConsistencyMismatchCount = reportConsistency.Count(r => r.CountMismatch),
                labelDefinitions = DirectionalRuleFuturesValidationV3Aggregator.LabelDefinitions,
                costScenarios = DirectionalRuleFuturesValidationV3CostModel.BuildValidationScenarios()
                    .Select(s => s.Label).ToArray(),
                backtestOnly = true,
                liveFuturesRecommended = false
            }, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        return new DirectionalRuleFuturesValidationV3RunResult(
            summaries,
            windowRobustness,
            costSensitivity,
            drawdown,
            variantComparison,
            reportConsistency,
            researchAnswers,
            accumulator.ExecutedTradeCount,
            accumulator.SkippedSignalCount);
    }

    public static IReadOnlyList<RobustnessWindow> BuildValidationWindows(
        DateTime dataStartUtc,
        DateTime dataEndUtc,
        IReadOnlyList<int> rollingDayWindows,
        DateTime? fixedWindowStartUtc,
        DateTime? fixedWindowEndUtc)
    {
        var windows = RobustnessWindowResolver.Resolve(
            dataStartUtc,
            dataEndUtc,
            rollingDayWindows,
            fixedWindowStartUtc,
            fixedWindowEndUtc).ToList();

        var holdoutStart = dataEndUtc.AddDays(-30);
        if (holdoutStart >= dataStartUtc)
        {
            windows.Add(new RobustnessWindow("holdout30d", holdoutStart, dataEndUtc, false, null));
            if (holdoutStart > dataStartUtc)
            {
                windows.Add(new RobustnessWindow(
                    "trainReference",
                    dataStartUtc,
                    holdoutStart,
                    false,
                    null));
            }
        }

        return windows;
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
