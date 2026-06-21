using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class DirectionalRuleFuturesRegimeConditionalV2Application(BacktestSettings settings)
{
    public async Task<DirectionalRuleFuturesRegimeConditionalV2RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var loader = new HistoricalKlineDataLoader(settings);
        var bnb = await loader.LoadAndValidateAsync(TradingSymbol.BNBUSDT, cancellationToken);
        var btc = await loader.LoadAndValidateAsync(TradingSymbol.BTCUSDT, cancellationToken);
        if (bnb.Candles.Count == 0 || btc.Candles.Count == 0)
            throw new InvalidOperationException("BNBUSDT and BTCUSDT local data required for RegimeConditionalV2.");

        var discoveryPath = ResolveDiscoveryJsonPath();
        var rules = await DirectionalRuleFuturesSimulationV1RuleCatalog.LoadHoldoutRulesAsync(discoveryPath, cancellationToken);
        var rule = rules.FirstOrDefault(r => r.RuleName.StartsWith("Rule01", StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException("Rule01 not found.");
        var profile = DirectionalRuleFuturesRegimeConditionalV2Catalog.BuildProfile(rule);

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

        var moderateTrades = MapCostScenario(scan.Trades, DirectionalRuleFuturesRegimeConditionalV2Catalog.PrimaryCostScenario, btcContext, dataEndUtc);
        var filters = DirectionalRuleFuturesRegimeConditionalV2Catalog.BuildFilters(moderateTrades);

        var summary = DirectionalRuleFuturesRegimeConditionalV2Aggregator.BuildSummary(
            moderateTrades, filters, DirectionalRuleFuturesRegimeConditionalV2Catalog.PrimaryCostScenario);
        var baselineMonthly = DirectionalRuleFuturesRegimeConditionalV2Aggregator.BuildMonthly(
            moderateTrades.Where(filters.First(f => f.Name == "Baseline").Predicate).ToArray());
        var filterImpact = DirectionalRuleFuturesRegimeConditionalV2Aggregator.BuildFilterImpact(moderateTrades, filters);

        // Cost/stress: only for filters that pass split validation under futures-moderate.
        // Always include Baseline as reference so the cost report is never empty.
        var qualifyingNames = summary.Where(s => s.PassesAllCriteria).Select(s => s.FilterName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stressFilters = filters
            .Where(f => qualifyingNames.Contains(f.Name) || string.Equals(f.Name, "Baseline", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var costSensitivity = new List<RegimeConditionalCostSensitivityRow>();
        foreach (var scenarioLabel in DirectionalRuleFuturesRegimeConditionalV2Catalog.CostStressScenarios)
        {
            var costedTrades = MapCostScenario(scan.Trades, scenarioLabel, btcContext, dataEndUtc);
            costSensitivity.AddRange(DirectionalRuleFuturesRegimeConditionalV2Aggregator.BuildCostSensitivity(
                costedTrades, stressFilters, scenarioLabel));
        }

        var answers = DirectionalRuleFuturesRegimeConditionalV2Aggregator.BuildAnswers(
            summary, costSensitivity, moderateTrades.Length, windowStart, dataEndUtc);

        var tradeRows = moderateTrades.Select(MapTradeRow).ToArray();

        Directory.CreateDirectory(settings.OutputDirectory);
        var writer = new DirectionalRuleFuturesRegimeConditionalV2ReportWriter(settings.OutputDirectory);
        await writer.WriteAsync(summary, costSensitivity, filterImpact, tradeRows, summary, answers, cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = "directional-rule-futures-regime-conditional-v2",
                settings.DataDirectory,
                settings.OutputDirectory,
                profile = profile.VariantLabel,
                rule = rule.RuleName,
                costScenarioPrimary = DirectionalRuleFuturesRegimeConditionalV2Catalog.PrimaryCostScenario,
                dataStartUtc = windowStart,
                dataEndUtc,
                tradeCount = moderateTrades.Length,
                signalCount = scan.SignalCount,
                filterCount = filters.Count,
                qualifyingFilterCount = qualifyingNames.Count,
                backtestOnly = true,
                liveFuturesRecommended = false
            }, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        return new DirectionalRuleFuturesRegimeConditionalV2RunResult(
            summary,
            baselineMonthly,
            costSensitivity,
            filterImpact,
            tradeRows,
            answers,
            moderateTrades.Length,
            qualifyingNames.Count,
            windowStart,
            dataEndUtc);
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

    private static RegimeConditionalTradeRow MapTradeRow(RegimeDriftDiagnosticTrade t)
        => new()
        {
            EntryTimeUtc = t.EntryTimeUtc,
            ExitTimeUtc = t.ExitTimeUtc,
            NetPnlQuote = t.NetPnlQuote,
            IsWinner = t.IsWinner,
            ExitReason = t.ExitReason,
            BtcReturn30mPercent = t.BtcReturn30mPercent,
            BtcReturn60mPercent = t.BtcReturn60mPercent,
            AtrPercent = t.AtrPercent,
            TrendSlopePercent = t.TrendSlopePercent,
            DistanceFromRecentHighPercent = t.DistanceFromRecentHighPercent,
            DistanceFromRecentLowPercent = t.DistanceFromRecentLowPercent,
            RangeWidthPercent = t.RangeWidthPercent,
            VolatilityRegime = t.VolatilityRegime,
            BtcTrendRegime = t.BtcTrendRegime ?? "Unknown",
            SessionBucket = t.SessionBucket,
            HourOfDayUtc = t.HourOfDayUtc,
            DayOfWeek = t.DayOfWeek,
            MonthKey = t.MonthKey,
            InRecent30d = t.InRecent30d,
            InRecent60d = t.InRecent60d,
            InRecent90d = t.InRecent90d,
            InOlder = t.InOlder,
            InTrainReference = t.InTrainReference,
            InHoldout30d = t.InHoldout30d
        };

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
