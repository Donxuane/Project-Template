using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class NoPaidDataAdaptiveActivationV1Application(BacktestSettings settings)
{
    public async Task<NoPaidDataAdaptiveActivationV1RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var loader = new HistoricalKlineDataLoader(settings);
        var bnb = await loader.LoadAndValidateAsync(TradingSymbol.BNBUSDT, cancellationToken);
        var btc = await loader.LoadAndValidateAsync(TradingSymbol.BTCUSDT, cancellationToken);
        if (bnb.Candles.Count == 0 || btc.Candles.Count == 0)
            throw new InvalidOperationException("BNBUSDT and BTCUSDT local data required for NoPaidDataAdaptiveActivationV1.");

        var discoveryPath = ResolveDiscoveryJsonPath();
        var rules = await DirectionalRuleFuturesSimulationV1RuleCatalog.LoadHoldoutRulesAsync(discoveryPath, cancellationToken);
        var rule = rules.FirstOrDefault(r => r.RuleName.StartsWith("Rule01", StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException("Rule01 not found.");
        var profile = NoPaidDataAdaptiveActivationV1Catalog.BuildProfile(rule);

        var validated = new Dictionary<TradingSymbol, SymbolValidationResult>
        {
            [TradingSymbol.BNBUSDT] = bnb,
            [TradingSymbol.BTCUSDT] = btc
        };
        var (dataStartUtc, dataEndUtc) = RobustnessWindowResolver.ResolveDataBounds(validated);
        var spanDays = (int)Math.Max(1, (dataEndUtc - dataStartUtc).TotalDays);
        var windowStart = dataEndUtc.AddDays(-Math.Min(365, spanDays));
        if (windowStart < dataStartUtc)
            windowStart = dataStartUtc;

        var windowBnb = CandleWindowSlicer.Slice(bnb.Candles, windowStart, dataEndUtc);
        var windowBtc = CandleWindowSlicer.Slice(btc.Candles, windowStart, dataEndUtc);
        var intervalCandles = CandleAggregator.Aggregate(TradingSymbol.BNBUSDT, windowBnb, "1m", profile.Interval).Candles;
        if (intervalCandles.Count == 0)
            throw new InvalidOperationException("No interval candles for BNB 5m profile.");

        var btcContext = new BtcContextIndex(windowBtc);
        var marketWideContext = new MarketWideContextIndex(
            new Dictionary<TradingSymbol, IReadOnlyList<KlineCandle>>
            {
                [TradingSymbol.BNBUSDT] = windowBnb,
                [TradingSymbol.BTCUSDT] = windowBtc
            },
            includeBtcInProxy: true);

        var v2Profile = DirectionalRuleFuturesValidationV31Catalog.ToV2Profile(profile);
        var scan = DirectionalRuleFuturesValidationV2Simulator.ScanProfile(
            v2Profile, "365d", intervalCandles, windowBnb, btcContext, marketWideContext, cancellationToken);

        var moderateTrades = MapCostScenario(scan.Trades, NoPaidDataAdaptiveActivationV1Catalog.PrimaryCostScenario, btcContext, dataEndUtc);
        var btc30Q3Lower = NoPaidDataAdaptiveActivationV1Catalog.ComputeBtc30Q3Lower(moderateTrades);
        var activationRules = NoPaidDataAdaptiveActivationV1Catalog.BuildActivationRules(btc30Q3Lower);

        var summary = new List<AdaptiveActivationSummaryRow>();
        var periods = new List<AdaptiveActivationPeriodRow>();
        var trades = new List<AdaptiveActivationTradeRow>();
        var drawdown = new List<AdaptiveActivationDrawdownRow>();
        var simByRule = new Dictionary<string, AdaptiveActivationSimResult>(StringComparer.OrdinalIgnoreCase);

        foreach (var activationRule in activationRules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sim = NoPaidDataAdaptiveActivationV1Engine.Simulate(
                activationRule, moderateTrades, moderateTrades, windowStart, dataEndUtc,
                btcContext, intervalCandles, btc30Q3Lower, NoPaidDataAdaptiveActivationV1Catalog.PrimaryCostScenario);
            simByRule[activationRule.ActivationRuleName] = sim;
            summary.Add(sim.Summary);
            periods.AddRange(sim.Periods);
            drawdown.Add(NoPaidDataAdaptiveActivationV1Aggregator.BuildDrawdown(
                activationRule.ActivationRuleName,
                FilterTaken(moderateTrades, sim.Trades),
                NoPaidDataAdaptiveActivationV1Catalog.PrimaryCostScenario));
        }

        var orderedSummary = summary
            .OrderByDescending(s => s.PassesSuccessCriteria)
            .ThenByDescending(s => s.Full365NetPnl)
            .ToArray();

        var tradeReportRules = orderedSummary
            .Where(s => s.PassesSuccessCriteria
                        || s.ConditionType == nameof(AdaptiveActivationConditionType.AlwaysOn)
                        || s.Full365Delta > orderedSummary.First(x => x.ConditionType == nameof(AdaptiveActivationConditionType.AlwaysOn)).Full365Delta)
            .Take(30)
            .Select(s => s.ActivationRuleName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in tradeReportRules)
        {
            if (simByRule.TryGetValue(name, out var sim))
                trades.AddRange(sim.Trades);
        }

        var windowPerformance = new List<AdaptiveActivationWindowPerformanceRow>();
        var costRules = orderedSummary
            .Where(s => s.PassesSuccessCriteria || s.ConditionType == nameof(AdaptiveActivationConditionType.AlwaysOn))
            .Concat(orderedSummary.Where(s => s.ConditionType != nameof(AdaptiveActivationConditionType.AlwaysOn)).Take(10))
            .DistinctBy(s => s.ActivationRuleName)
            .ToArray();

        foreach (var row in costRules)
        {
            var config = activationRules.First(r => r.ActivationRuleName == row.ActivationRuleName);
            var taken = FilterTaken(moderateTrades, simByRule[row.ActivationRuleName].Trades);
            windowPerformance.AddRange(NoPaidDataAdaptiveActivationV1Aggregator.BuildWindowPerformance(
                row.ActivationRuleName, taken, moderateTrades, NoPaidDataAdaptiveActivationV1Catalog.PrimaryCostScenario, dataEndUtc));
        }

        var costSensitivity = new List<AdaptiveActivationCostSensitivityRow>();
        foreach (var config in costRules.Select(r => activationRules.First(a => a.ActivationRuleName == r.ActivationRuleName)))
        {
            costSensitivity.AddRange(NoPaidDataAdaptiveActivationV1Aggregator.BuildCostSensitivity(
                config, scan.Trades, btcContext, windowStart, dataEndUtc, intervalCandles, btc30Q3Lower));
        }

        var baseline = orderedSummary.First(s => s.ConditionType == nameof(AdaptiveActivationConditionType.AlwaysOn));
        var answers = NoPaidDataAdaptiveActivationV1Aggregator.BuildAnswers(
            orderedSummary, costSensitivity, baseline.TotalTrades, baseline.Full365NetPnl,
            baseline.OlderNetPnl, baseline.Recent90dNetPnl, windowStart, dataEndUtc);

        var result = new NoPaidDataAdaptiveActivationV1RunResult(
            orderedSummary, trades, periods, windowPerformance, costSensitivity, drawdown, answers,
            baseline.TotalTrades, baseline.Full365NetPnl, windowStart, dataEndUtc);

        Directory.CreateDirectory(settings.OutputDirectory);
        var writer = new NoPaidDataAdaptiveActivationV1ReportWriter(settings.OutputDirectory);
        await writer.WriteAsync(result, cancellationToken);

        var qualifying = orderedSummary.Count(s => s.PassesSuccessCriteria);
        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = "no-paid-data-adaptive-activation-v1",
                settings.DataDirectory,
                settings.OutputDirectory,
                rule = rule.RuleName,
                profile = profile.VariantLabel,
                costScenarioPrimary = NoPaidDataAdaptiveActivationV1Catalog.PrimaryCostScenario,
                activationRuleCount = activationRules.Count,
                qualifyingRuleCount = qualifying,
                baselineTrades = baseline.TotalTrades,
                baselineFull365NetPnl = baseline.Full365NetPnl,
                dataStartUtc = windowStart,
                dataEndUtc,
                backtestOnly = true,
                liveFuturesRecommended = false,
                paidDataRequired = false
            }, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        return result;
    }

    private static RegimeDriftDiagnosticTrade[] MapCostScenario(
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        string scenarioLabel,
        BtcContextIndex btcContext,
        DateTime dataEndUtc)
        => DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog
            .ApplyCostScenario(baseTrades, scenarioLabel)
            .Select(t => DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.MapTrade(t, btcContext, dataEndUtc))
            .Where(t => !string.Equals(t.ExitReason, "InvalidEntry", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static RegimeDriftDiagnosticTrade[] FilterTaken(
        IReadOnlyList<RegimeDriftDiagnosticTrade> moderateTrades,
        IReadOnlyList<AdaptiveActivationTradeRow> simTrades)
    {
        var keys = simTrades.Select(t => (t.EntryTimeUtc, t.ExitTimeUtc)).ToHashSet();
        return moderateTrades.Where(t => keys.Contains((t.EntryTimeUtc, t.ExitTimeUtc))).ToArray();
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
