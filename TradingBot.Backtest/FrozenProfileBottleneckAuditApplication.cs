using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// Diagnostic-only audit across all four frozen forward-incubation profiles.
/// Does not change strategy logic, activation thresholds, frozen profiles, or health gates.
/// </summary>
public sealed class FrozenProfileBottleneckAuditApplication(BacktestSettings settings)
{
    public const string ModeName = "frozen-profile-bottleneck-audit";
    public const string DefaultOutputSubdir = "frozen-profile-bottleneck-audit";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<FrozenProfileBottleneckAuditSummary> RunAsync(CancellationToken cancellationToken)
    {
        var runAtUtc = DateTime.UtcNow;
        var loader = new HistoricalKlineDataLoader(settings);

        if (settings.BootstrapFuturesData)
        {
            await RefreshCandlesAsync(loader, cancellationToken);
            var flowDownloader = new ShortWindowFlowDataDownloader();
            await flowDownloader.DownloadAllAsync(
                settings.DataDirectory, FuturesMarketDataCatalog.Symbols, runAtUtc.AddDays(-365), runAtUtc, cancellationToken);
        }

        var rows = new List<FrozenProfileBottleneckAuditRow>();
        foreach (var profile in FuturesTestnetShadowCatalog.Profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(await EvaluateProfileAsync(profile, loader, cancellationToken));
        }

        var summary = FrozenProfileBottleneckAuditBuilder.BuildSummary(runAtUtc, rows);
        Directory.CreateDirectory(settings.OutputDirectory);
        await FrozenProfileBottleneckAuditReportWriter.WriteAsync(settings.OutputDirectory, summary, cancellationToken);
        await WriteRunMetadataAsync(summary, cancellationToken);
        return summary;
    }

    private async Task<FrozenProfileBottleneckAuditRow> EvaluateProfileAsync(
        FuturesTestnetShadowCatalog.ProfileRef profile,
        HistoricalKlineDataLoader loader,
        CancellationToken cancellationToken)
    {
        var frozenPath = profile.FrozenStatePath(settings.DataDirectory);
        if (!File.Exists(frozenPath))
        {
            return MissingProfileRow(profile, $"Frozen state not found: {frozenPath}");
        }

        var state = JsonSerializer.Deserialize<FrozenCandidateState>(await File.ReadAllTextAsync(frozenPath, cancellationToken))
                    ?? throw new InvalidOperationException($"Could not deserialize frozen state: {frozenPath}");

        var symbolData = await loader.LoadAndValidateAsync(profile.Symbol, cancellationToken);
        var btc = await loader.LoadAndValidateAsync(TradingSymbol.BTCUSDT, cancellationToken);
        if (symbolData.Candles.Count == 0 || btc.Candles.Count == 0)
            return MissingProfileRow(profile, "Symbol or BTCUSDT local candle data missing.");

        var validated = new Dictionary<TradingSymbol, SymbolValidationResult>
        {
            [profile.Symbol] = symbolData,
            [TradingSymbol.BTCUSDT] = btc
        };
        var (dataStartUtc, dataEndUtc) = RobustnessWindowResolver.ResolveDataBounds(validated);
        var spanDays = (int)Math.Max(1, (dataEndUtc - dataStartUtc).TotalDays);
        var windowStart = dataEndUtc.AddDays(-Math.Min(365, spanDays));
        if (windowStart < dataStartUtc)
            windowStart = dataStartUtc;

        var windowSymbol = CandleWindowSlicer.Slice(symbolData.Candles, windowStart, dataEndUtc);
        var windowBtc = CandleWindowSlicer.Slice(btc.Candles, windowStart, dataEndUtc);
        var intervalCandles = CandleAggregator.Aggregate(profile.Symbol, windowSymbol, "1m", profile.Interval).Candles;
        var btcContext = new BtcContextIndex(windowBtc);
        var marketWideContext = new MarketWideContextIndex(
            new Dictionary<TradingSymbol, IReadOnlyList<KlineCandle>>
            {
                [profile.Symbol] = windowSymbol,
                [TradingSymbol.BTCUSDT] = windowBtc
            },
            includeBtcInProxy: true);
        var futuresLoader = new FuturesMarketDataLoader(settings.DataDirectory);
        var flowIndex = new ShortWindowFlowFeatureIndex(futuresLoader, profile.Symbol, intervalCandles, windowBtc);

        var frozenStart = state.FrozenStartUtc;
        var forwardEnd = dataEndUtc > frozenStart ? dataEndUtc : frozenStart;
        var forwardSpanDays = Math.Round((decimal)(forwardEnd - frozenStart).TotalDays, 4);
        var evalUtc = dataEndUtc;

        if (profile.IsBnbRule01)
        {
            return await EvaluateBnbRule01Async(
                profile, state, frozenStart, forwardEnd, forwardSpanDays, evalUtc,
                intervalCandles, windowSymbol, btcContext, marketWideContext, flowIndex,
                dataEndUtc, cancellationToken);
        }

        return await EvaluateCrossSymbolAsync(
            profile, state, frozenStart, forwardEnd, forwardSpanDays, evalUtc,
            intervalCandles, windowSymbol, btcContext, marketWideContext, flowIndex,
            dataEndUtc, windowStart, cancellationToken);
    }

    private async Task<FrozenProfileBottleneckAuditRow> EvaluateBnbRule01Async(
        FuturesTestnetShadowCatalog.ProfileRef profile,
        FrozenCandidateState state,
        DateTime frozenStart,
        DateTime forwardEnd,
        decimal forwardSpanDays,
        DateTime evalUtc,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<KlineCandle> windowSymbol,
        BtcContextIndex btcContext,
        MarketWideContextIndex marketWideContext,
        ShortWindowFlowFeatureIndex flowIndex,
        DateTime dataEndUtc,
        CancellationToken cancellationToken)
    {
        var discoveryPath = ResolveDiscoveryJsonPath();
        var rules = await DirectionalRuleFuturesSimulationV1RuleCatalog.LoadHoldoutRulesAsync(discoveryPath, cancellationToken);
        var rule = rules.FirstOrDefault(r => r.RuleName.StartsWith("Rule01", StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException("Rule01 not found.");
        var simProfile = NoPaidDataShortWindowFlowResearchV1Catalog.BuildProfile(rule);
        var v2Profile = DirectionalRuleFuturesValidationV31Catalog.ToV2Profile(simProfile);
        var scan = DirectionalRuleFuturesValidationV2Simulator.ScanProfile(
            v2Profile, "365d", intervalCandles, windowSymbol, btcContext, marketWideContext, cancellationToken);

        var frozenConfig = profile.BuildShortWindowActivationConfig();
        var moderateTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
            scan.Trades, profile.PrimaryCostScenario, btcContext, dataEndUtc);
        var forwardSim = NoPaidDataShortWindowFlowResearchV1Engine.Simulate(
            frozenConfig, moderateTrades, frozenStart, forwardEnd, flowIndex, profile.PrimaryCostScenario);

        var costSensitivity = NoPaidDataShortWindowFlowResearchV1Aggregator.BuildCostSensitivity(
            frozenConfig, scan.Trades, btcContext, frozenStart, forwardEnd, dataEndUtc, flowIndex);
        var netStressPlus = costSensitivity
            .FirstOrDefault(c => string.Equals(c.CostScenario, NoPaidDataShortWindowForwardIncubationV1Catalog.StressPlusScenario, StringComparison.OrdinalIgnoreCase))
            ?.NetPnlQuote ?? 0m;

        var activation = FuturesTestnetShadowEvaluator.EvaluateShortWindowActivation(
            frozenConfig, moderateTrades, evalUtc, frozenStart, flowIndex);
        var entry = FuturesTestnetShadowEvaluator.EvaluateBnbRule01EntryNow(
            scan.Trades, evalUtc, frozenStart, profile.Interval == "15m" ? 15 : 5);

        return FrozenProfileBottleneckAuditBuilder.BuildForBnbRule01(
            state,
            frozenStart,
            forwardEnd,
            forwardSpanDays,
            forwardSim.Summary.TotalTrades,
            forwardSim.Summary.NetPnlQuote,
            netStressPlus,
            forwardSim.Periods,
            forwardSim.Trades,
            moderateTrades,
            scan.Skipped,
            activation.Passed,
            entry.Present);
    }

    private async Task<FrozenProfileBottleneckAuditRow> EvaluateCrossSymbolAsync(
        FuturesTestnetShadowCatalog.ProfileRef profile,
        FrozenCandidateState state,
        DateTime frozenStart,
        DateTime forwardEnd,
        decimal forwardSpanDays,
        DateTime evalUtc,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<KlineCandle> windowSymbol,
        BtcContextIndex btcContext,
        MarketWideContextIndex marketWideContext,
        ShortWindowFlowFeatureIndex flowIndex,
        DateTime dataEndUtc,
        DateTime windowStart,
        CancellationToken cancellationToken)
    {
        var frozenKey = profile.ComboKey
                        ?? throw new InvalidOperationException($"Cross-symbol combo key missing for {profile.ProfileName}");
        var frozenConfig = profile.BuildCrossSymbolActivationConfig();

        var scans = NoPaidDataShortWindowFlowResearchV1CrossSymbolSimulator.ScanSymbolInterval(
            profile.Symbol, profile.Interval, intervalCandles, windowSymbol, flowIndex, btcContext,
            marketWideContext, windowStart, dataEndUtc, cancellationToken);
        var scan = scans.FirstOrDefault(s => s.Key == frozenKey)
                   ?? throw new InvalidOperationException($"Frozen combo {frozenKey} not produced by scan.");

        var moderateTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
            scan.BaseTrades, profile.PrimaryCostScenario, btcContext, dataEndUtc);
        var forwardSim = NoPaidDataShortWindowFlowResearchV1CrossSymbolEngine.Simulate(
            frozenKey, frozenConfig, moderateTrades, frozenStart, forwardEnd, flowIndex,
            profile.PrimaryCostScenario, collectPeriods: true);
        var forwardStats = forwardSim.TakenTrades;
        var netModerate = forwardStats.Sum(t => t.NetPnlQuote);

        var stressTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
            scan.BaseTrades, NoPaidDataShortWindowSolForwardIncubationV1Catalog.StressPlusScenario, btcContext, dataEndUtc);
        var stressSim = NoPaidDataShortWindowFlowResearchV1CrossSymbolEngine.Simulate(
            frozenKey, frozenConfig, stressTrades, frozenStart, forwardEnd, flowIndex,
            NoPaidDataShortWindowSolForwardIncubationV1Catalog.StressPlusScenario, collectPeriods: false);
        var netStressPlus = stressSim.TakenTrades.Sum(t => t.NetPnlQuote);

        var activation = FuturesTestnetShadowEvaluator.EvaluateCrossSymbolActivation(
            frozenConfig, frozenKey, moderateTrades, evalUtc, frozenStart, flowIndex);
        var cooldown = NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.CooldownFor(profile.Interval);
        var entry = FuturesTestnetShadowEvaluator.EvaluateCrossSymbolEntryNow(
            frozenKey, intervalCandles, scan.BaseTrades, evalUtc, frozenStart, cooldown);

        return FrozenProfileBottleneckAuditBuilder.BuildForCrossSymbol(
            state,
            frozenKey,
            frozenConfig,
            frozenStart,
            forwardEnd,
            forwardSpanDays,
            forwardStats.Count,
            netModerate,
            netStressPlus,
            forwardSim.Periods,
            moderateTrades,
            scan.BaseTrades,
            scan.RawSignalEntryTimesUtc,
            intervalCandles,
            btcContext,
            flowIndex,
            profile.PrimaryCostScenario,
            ResolveModerateSlippageScenario(profile),
            ResolveStressPlusScenario(profile),
            activation.Passed,
            entry.Present);
    }

    private static string ResolveModerateSlippageScenario(FuturesTestnetShadowCatalog.ProfileRef profile)
        => profile.Symbol == TradingSymbol.BNBUSDT && profile.Interval == "15m"
            ? NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.ModerateSlippageScenario
            : profile.Interval == "15m"
                ? NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.ModerateSlippageScenario
                : NoPaidDataShortWindowSolForwardIncubationV1Catalog.ModerateSlippageScenario;

    private static string ResolveStressPlusScenario(FuturesTestnetShadowCatalog.ProfileRef profile)
        => profile.Symbol == TradingSymbol.BNBUSDT && profile.Interval == "15m"
            ? NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.StressPlusScenario
            : profile.Interval == "15m"
                ? NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.StressPlusScenario
                : NoPaidDataShortWindowSolForwardIncubationV1Catalog.StressPlusScenario;

    private static FrozenProfileBottleneckAuditRow MissingProfileRow(
        FuturesTestnetShadowCatalog.ProfileRef profile,
        string reason)
    {
        var draft = new FrozenProfileBottleneckAuditRow
        {
            ProfileName = profile.ProfileName,
            Symbol = profile.Symbol.ToString(),
            Interval = profile.Interval,
            Direction = profile.IsBnbRule01
                ? LongShortDirection.Short.ToString()
                : profile.ComboKey?.Direction.ToString() ?? string.Empty,
            BottleneckExplanation = reason
        };
        return draft with
        {
            BottleneckClassification = "CandidateWeakOrPark",
            Recommendation = "Park"
        };
    }

    private async Task RefreshCandlesAsync(HistoricalKlineDataLoader loader, CancellationToken cancellationToken)
    {
        var downloader = new BinanceKlineBootstrapDownloader();
        foreach (var symbol in new[] { TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT, TradingSymbol.BTCUSDT })
        {
            var existing = await loader.LoadAndValidateAsync(symbol, cancellationToken);
            var lastUtc = existing.Candles.Count > 0 ? existing.Candles[^1].OpenTimeUtc : DateTime.UtcNow.AddDays(-35);
            var nowUtc = DateTime.UtcNow;
            if ((nowUtc - lastUtc) < TimeSpan.FromMinutes(30))
                continue;
            var path = Path.Combine(settings.DataDirectory, $"{symbol}-1m.json");
            await downloader.DownloadAndMergeToJsonAsync(
                symbol.ToString(), path, 1000, lastUtc.AddHours(-2), nowUtc, cancellationToken);
        }
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
            "futures-directional-rule-discovery-v2",
            "directional-rule-discovery-holdout-rules.json");
    }

    private async Task WriteRunMetadataAsync(FrozenProfileBottleneckAuditSummary summary, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = ModeName,
                settings.DataDirectory,
                settings.OutputDirectory,
                frozenProfiles = FuturesTestnetShadowCatalog.FrozenProfileNames,
                diagnosticOnly = true,
                strategyLogicChanged = false,
                activationThresholdsChanged = false,
                frozenProfilesChanged = false,
                testnetOrdersEnabled = false,
                realOrderExecutionAdded = false,
                healthGatesAffected = false,
                verdictsAffected = false,
                compactSummaryLine = summary.CompactSummaryLine,
                profiles = summary.Profiles.Select(p => new
                {
                    p.ProfileName,
                    p.BottleneckClassification,
                    p.Recommendation,
                    p.ForwardTrades,
                    p.NetModerate,
                    p.NetStressPlus
                })
            }, JsonOptions),
            cancellationToken);
    }
}
