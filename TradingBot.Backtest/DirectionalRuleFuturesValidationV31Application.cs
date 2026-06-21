using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class DirectionalRuleFuturesValidationV31Application(BacktestSettings settings)
{
    public async Task<DirectionalRuleFuturesValidationV31RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var crossSymbols = DirectionalRuleFuturesValidationV31Catalog.ResolveCrossSymbols(settings);
        if (!crossSymbols.Contains(TradingSymbol.BNBUSDT))
            throw new InvalidOperationException("BNBUSDT data required for DirectionalRuleFuturesValidationV31.");

        var bootstrapSymbols = crossSymbols.Append(TradingSymbol.BTCUSDT).Distinct().ToArray();
        var preferredBootstrapDays = settings.BootstrapDays ?? 365;
        // Local-first: always use the on-disk baseline. Network downloads are opt-in
        // (--bootstrap-missing-data) and per-symbol time-bounded so they can never hang the run.
        var bootstrap = await TradingSymbolDataBootstrap.EnsureSymbolsDataAsync(
            settings,
            bootstrapSymbols,
            preferredBootstrapDays,
            attemptDownload: settings.BootstrapMissingData,
            cancellationToken);
        var validated = bootstrap.Validated;
        if (!validated.TryGetValue(TradingSymbol.BTCUSDT, out var btcValidation) || btcValidation.Candles.Count == 0)
            throw new InvalidOperationException("BTCUSDT bootstrap failed.");

        var bootstrapDays = TradingSymbolDataBootstrap.ResolvePracticalBootstrapDays(validated, preferredBootstrapDays);

        var discoveryPath = ResolveDiscoveryJsonPath();
        var rules = await DirectionalRuleFuturesSimulationV1RuleCatalog.LoadHoldoutRulesAsync(discoveryPath, cancellationToken);
        var rule = rules.FirstOrDefault(r => r.RuleName.StartsWith("Rule01", StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException("Rule01 not found.");
        var crossProfiles = DirectionalRuleFuturesValidationV31Catalog.BuildCrossSymbolProfiles(rule, crossSymbols);
        var bestBnbProfile = DirectionalRuleFuturesValidationV31Catalog.BuildBestBnbLongHistoryProfile(rule);
        var allProfiles = new List<DirectionalRuleV31SimulationProfile> { bestBnbProfile };
        allProfiles.AddRange(crossProfiles);

        Directory.CreateDirectory(settings.OutputDirectory);
        var accumulator = new DirectionalRuleFuturesValidationV31RunAccumulator();
        await using var tradesCsv = await DirectionalRuleV31TradesCsvStream.CreateAsync(settings.OutputDirectory, cancellationToken);

        var profilesBySymbol = allProfiles.GroupBy(p => p.Symbol).ToArray();
        foreach (var symbolGroup in profilesBySymbol)
        {
            if (!validated.TryGetValue(symbolGroup.Key, out var symbolValidation) || symbolValidation.Candles.Count == 0)
                continue;

            var symbolProfiles = symbolGroup.ToArray();
            var longHistoryProfiles = symbolProfiles
                .Where(p => p.ValidationTrack == DirectionalRuleV31ValidationTrack.BestBnbLongHistory)
                .ToArray();
            var crossProfilesForSymbol = symbolProfiles
                .Where(p => p.ValidationTrack == DirectionalRuleV31ValidationTrack.CrossSymbol)
                .ToArray();

            if (longHistoryProfiles.Length > 0)
            {
                var (start, end) = RobustnessWindowResolver.ResolveDataBounds(
                    new Dictionary<TradingSymbol, SymbolValidationResult> { [symbolGroup.Key] = symbolValidation });
                var rollingDays = FilterWindows(DirectionalRuleFuturesValidationV31Catalog.LongHistoryWindows(), start, end);
                await ScanProfilesForWindowsAsync(
                    longHistoryProfiles,
                    rollingDays,
                    validated,
                    bootstrapSymbols,
                    accumulator,
                    tradesCsv,
                    cancellationToken);
            }

            if (crossProfilesForSymbol.Length > 0)
            {
                var (start, end) = RobustnessWindowResolver.ResolveDataBounds(
                    new Dictionary<TradingSymbol, SymbolValidationResult> { [symbolGroup.Key] = symbolValidation });
                var rollingDays = FilterWindows(DirectionalRuleFuturesValidationV31Catalog.CrossSymbolWindows(), start, end);
                await ScanProfilesForWindowsAsync(
                    crossProfilesForSymbol,
                    rollingDays,
                    validated,
                    bootstrapSymbols,
                    accumulator,
                    tradesCsv,
                    cancellationToken);
            }
        }

        var summaries = accumulator.BuildSummaries();
        var windowRobustness = DirectionalRuleFuturesValidationV31Aggregator.BuildWindowRobustness(summaries);
        summaries = DirectionalRuleFuturesValidationV31Aggregator.ApplyCrossWindowLabels(summaries, windowRobustness);
        var costSensitivity = DirectionalRuleFuturesValidationV31Aggregator.BuildCostSensitivity(windowRobustness);
        var drawdown = DirectionalRuleFuturesValidationV31Aggregator.ApplyWorstWindowNet(
            accumulator.BuildDrawdownRows(),
            windowRobustness);
        var monthlyWeekly = accumulator.BuildMonthlyWeeklyRows();
        var generalizationAnswers = DirectionalRuleFuturesValidationV31Aggregator.BuildGeneralizationAnswers(
            windowRobustness,
            drawdown,
            monthlyWeekly,
            bootstrapDays,
            bootstrapDays,
            accumulator.ExecutedTradeCount,
            accumulator.SkippedSignalCount);

        var bestBnbSummary = summaries.Where(s => s.ValidationTrack == DirectionalRuleV31ValidationTrack.BestBnbLongHistory).ToArray();
        var crossSymbolSummary = summaries.Where(s => s.ValidationTrack == DirectionalRuleV31ValidationTrack.CrossSymbol).ToArray();

        var writer = new DirectionalRuleFuturesValidationV31ReportWriter(settings.OutputDirectory);
        await writer.WriteAsync(
            bestBnbSummary,
            crossSymbolSummary,
            windowRobustness,
            costSensitivity,
            drawdown,
            monthlyWeekly,
            generalizationAnswers,
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = "directional-rule-futures-validation-v31",
                settings.DataDirectory,
                settings.OutputDirectory,
                bootstrapDays,
                preferredBootstrapDays,
                downloadEnabled = settings.BootstrapMissingData,
                bootstrapOutcomes = bootstrap.Outcomes.Select(o => new
                {
                    symbol = o.Symbol.ToString(),
                    candles = o.Validation.Candles.Count,
                    o.DownloadAttempted,
                    o.DownloadSucceeded,
                    o.Note
                }).ToArray(),
                crossSymbols = crossSymbols.Select(s => s.ToString()).ToArray(),
                profileCount = allProfiles.Count,
                bestBnbProfile = bestBnbProfile.VariantLabel,
                expandedTradeRowCount = accumulator.ExecutedTradeCount,
                skippedSignalCount = accumulator.SkippedSignalCount,
                sameRuleGeneralizesAcrossSymbols = DirectionalRuleFuturesValidationV31Aggregator.SameRuleGeneralizesAcrossSymbols(windowRobustness),
                labelDefinitions = DirectionalRuleFuturesValidationV31Aggregator.LabelDefinitions,
                costScenarios = DirectionalRuleFuturesValidationV3CostModel.BuildValidationScenarios().Select(s => s.Label).ToArray(),
                backtestOnly = true,
                liveFuturesRecommended = false
            }, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        return new DirectionalRuleFuturesValidationV31RunResult(
            bestBnbSummary,
            crossSymbolSummary,
            windowRobustness,
            costSensitivity,
            drawdown,
            monthlyWeekly,
            generalizationAnswers,
            accumulator.ExecutedTradeCount,
            accumulator.SkippedSignalCount);
    }

    private async Task ScanProfilesForWindowsAsync(
        IReadOnlyList<DirectionalRuleV31SimulationProfile> profiles,
        IReadOnlyList<int> rollingDays,
        IReadOnlyDictionary<TradingSymbol, SymbolValidationResult> validated,
        IReadOnlyList<TradingSymbol> bootstrapSymbols,
        DirectionalRuleFuturesValidationV31RunAccumulator accumulator,
        DirectionalRuleV31TradesCsvStream tradesCsv,
        CancellationToken cancellationToken)
    {
        if (profiles.Count == 0 || rollingDays.Count == 0)
            return;

        var symbol = profiles[0].Symbol;
        if (!validated.TryGetValue(symbol, out var symbolValidation))
            return;

        var (dataStartUtc, dataEndUtc) = RobustnessWindowResolver.ResolveDataBounds(
            new Dictionary<TradingSymbol, SymbolValidationResult> { [symbol] = symbolValidation });
        var windows = BuildValidationWindows(dataStartUtc, dataEndUtc, rollingDays);
        var contextSymbols = bootstrapSymbols.Where(validated.ContainsKey).ToArray();
        var contextValidated = contextSymbols.ToDictionary(s => s, s => validated[s]);
        var intervals = profiles.Select(p => p.Interval).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        foreach (var window in windows.Where(w => !w.SkippedInsufficientData))
        {
            var windowDataBySymbol = contextValidated.ToDictionary(
                kv => kv.Key,
                kv => CandleWindowSlicer.Slice(kv.Value.Candles, window.StartUtc, window.EndUtc));

            if (!windowDataBySymbol.TryGetValue(TradingSymbol.BTCUSDT, out var windowBtc) || windowBtc.Count == 0)
                continue;
            if (!windowDataBySymbol.TryGetValue(symbol, out var windowOneMinute) || windowOneMinute.Count == 0)
                continue;

            var btcContext = new BtcContextIndex(windowBtc);
            var marketWideContext = new MarketWideContextIndex(
                windowDataBySymbol.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<KlineCandle>)kv.Value),
                includeBtcInProxy: true);

            var intervalCandlesByInterval = new Dictionary<string, IReadOnlyList<KlineCandle>>(StringComparer.OrdinalIgnoreCase);
            foreach (var interval in intervals)
            {
                var aggregate = CandleAggregator.Aggregate(symbol, windowOneMinute, "1m", interval);
                if (aggregate.Candles.Count > 0)
                    intervalCandlesByInterval[interval] = aggregate.Candles;
            }

            foreach (var profile in profiles)
            {
                if (!intervalCandlesByInterval.TryGetValue(profile.Interval, out var intervalCandles))
                    continue;

                var scan = DirectionalRuleFuturesValidationV31Simulator.ScanProfile(
                    profile,
                    window.Label,
                    intervalCandles,
                    windowOneMinute,
                    btcContext,
                    marketWideContext,
                    cancellationToken);
                accumulator.IngestScanResult(profile, window.Label, scan);
                if (scan.Trades.Count == 0)
                    continue;

                var expanded = DirectionalRuleFuturesValidationV31Simulator.ApplyCostScenarios(scan.Trades);
                accumulator.IngestExpandedBatch(expanded);
                await tradesCsv.WriteBatchAsync(expanded, cancellationToken);
            }
        }
    }

    public static IReadOnlyList<int> FilterWindows(
        IReadOnlyList<int> requested,
        DateTime dataStartUtc,
        DateTime dataEndUtc)
    {
        var spanDays = (int)Math.Max(1, (dataEndUtc - dataStartUtc).TotalDays);
        return requested.Where(d => d <= spanDays).ToArray();
    }

    public static IReadOnlyList<RobustnessWindow> BuildValidationWindows(
        DateTime dataStartUtc,
        DateTime dataEndUtc,
        IReadOnlyList<int> rollingDayWindows)
    {
        var windows = RobustnessWindowResolver.Resolve(
            dataStartUtc,
            dataEndUtc,
            rollingDayWindows,
            null,
            null).ToList();

        var holdoutStart = dataEndUtc.AddDays(-30);
        if (holdoutStart >= dataStartUtc)
        {
            windows.Add(new RobustnessWindow("holdout30d", holdoutStart, dataEndUtc, false, null));
            if (holdoutStart > dataStartUtc)
                windows.Add(new RobustnessWindow("trainReference", dataStartUtc, holdoutStart, false, null));
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
        return Path.Combine(
            repoRoot,
            "TradingBot.Backtest",
            "output",
            "long-short-futures-feasibility-v1-run",
            "long-short-entry-time-rule-discovery.json");
    }
}
