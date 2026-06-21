using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class DirectionalRuleFuturesRegimeDriftAnalysisV1Application(BacktestSettings settings)
{
    public async Task<DirectionalRuleFuturesRegimeDriftAnalysisV1RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var loader = new HistoricalKlineDataLoader(settings);
        var bnb = await loader.LoadAndValidateAsync(TradingSymbol.BNBUSDT, cancellationToken);
        var btc = await loader.LoadAndValidateAsync(TradingSymbol.BTCUSDT, cancellationToken);
        if (bnb.Candles.Count == 0 || btc.Candles.Count == 0)
            throw new InvalidOperationException("BNBUSDT and BTCUSDT local data required for RegimeDriftAnalysisV1.");

        var discoveryPath = ResolveDiscoveryJsonPath();
        var rules = await DirectionalRuleFuturesSimulationV1RuleCatalog.LoadHoldoutRulesAsync(discoveryPath, cancellationToken);
        var rule = rules.FirstOrDefault(r => r.RuleName.StartsWith("Rule01", StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException("Rule01 not found.");
        var profile = DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.BuildProfile(rule);

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
            v2Profile,
            "365d",
            intervalCandles,
            windowBnb,
            btcContext,
            marketWideContext,
            cancellationToken);

        var moderateTrades = DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog
            .ApplyCostScenario(scan.Trades, DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.PrimaryCostScenario);
        var diagnosticTrades = moderateTrades
            .Select(t => DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.MapTrade(t, btcContext, dataEndUtc))
            .Where(t => !string.Equals(t.ExitReason, "InvalidEntry", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var summary = DirectionalRuleFuturesRegimeDriftAnalysisV1Aggregator.BuildSummary(
            diagnosticTrades,
            DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.PrimaryCostScenario);
        var featureComparison = DirectionalRuleFuturesRegimeDriftAnalysisV1Aggregator.BuildFeatureComparison(
            diagnosticTrades,
            DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.PrimaryCostScenario);
        var monthly = DirectionalRuleFuturesRegimeDriftAnalysisV1Aggregator.BuildMonthlyPerformance(
            diagnosticTrades,
            DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.PrimaryCostScenario);
        var entryTimeRules = DirectionalRuleFuturesRegimeDriftAnalysisV1Aggregator.BuildEntryTimeRules(
            diagnosticTrades,
            DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.PrimaryCostScenario);
        var outcomeRules = DirectionalRuleFuturesRegimeDriftAnalysisV1Aggregator.BuildOutcomeRules(
            diagnosticTrades,
            entryTimeRules,
            DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.PrimaryCostScenario);

        // Stress scenarios only for rules that show any dual-period or recent-only promise.
        var stressCandidates = outcomeRules
            .Where(r => r.SurvivesBothPeriods || r.KeepsRecentWinners)
            .Take(4)
            .Select(r => r.RuleDescription)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (stressCandidates.Length > 0)
        {
            foreach (var stressLabel in DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.StressCostScenarios)
            {
                var stressCosted = DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog
                    .ApplyCostScenario(scan.Trades, stressLabel)
                    .Select(t => DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.MapTrade(t, btcContext, dataEndUtc))
                    .Where(t => !string.Equals(t.ExitReason, "InvalidEntry", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                summary = summary.Concat(DirectionalRuleFuturesRegimeDriftAnalysisV1Aggregator.BuildSummary(stressCosted, stressLabel)).ToArray();
            }
        }

        var answers = DirectionalRuleFuturesRegimeDriftAnalysisV1Aggregator.BuildAnswers(
            summary.Where(s => s.CostScenarioLabel == DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.PrimaryCostScenario).ToArray(),
            featureComparison,
            monthly,
            entryTimeRules,
            outcomeRules,
            diagnosticTrades.Length,
            windowStart,
            dataEndUtc);

        Directory.CreateDirectory(settings.OutputDirectory);
        var writer = new DirectionalRuleFuturesRegimeDriftAnalysisV1ReportWriter(settings.OutputDirectory);
        await writer.WriteAsync(summary, featureComparison, monthly, entryTimeRules, outcomeRules, answers, cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = "directional-rule-futures-regime-drift-v1",
                settings.DataDirectory,
                settings.OutputDirectory,
                profile = profile.VariantLabel,
                rule = rule.RuleName,
                costScenarioPrimary = DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.PrimaryCostScenario,
                dataStartUtc = windowStart,
                dataEndUtc,
                tradeCount = diagnosticTrades.Length,
                signalCount = scan.SignalCount,
                stressScenariosEvaluated = stressCandidates.Length > 0,
                backtestOnly = true,
                liveFuturesRecommended = false
            }, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        return new DirectionalRuleFuturesRegimeDriftAnalysisV1RunResult(
            summary,
            featureComparison,
            monthly,
            entryTimeRules,
            outcomeRules,
            answers,
            diagnosticTrades.Length,
            windowStart,
            dataEndUtc);
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
