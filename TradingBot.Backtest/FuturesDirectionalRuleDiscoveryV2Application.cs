using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class FuturesDirectionalRuleDiscoveryV2Application(BacktestSettings settings)
{
    private const int MaxTradeRows = 6000;

    public async Task<FuturesDirectionalRuleDiscoveryV2RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var loader = new HistoricalKlineDataLoader(settings);
        var loaded = new Dictionary<TradingSymbol, SymbolValidationResult>();
        foreach (var symbol in FuturesDirectionalRuleDiscoveryV2Catalog.Symbols)
        {
            var data = await loader.LoadAndValidateAsync(symbol, cancellationToken);
            if (data.Candles.Count > 0)
                loaded[symbol] = data;
        }

        if (loaded.Count == 0)
            throw new InvalidOperationException("No local candle data found for any requested symbol.");

        var (dataStartUtc, dataEndUtc) = RobustnessWindowResolver.ResolveDataBounds(loaded);
        var spanDays = (int)Math.Max(1, (dataEndUtc - dataStartUtc).TotalDays);
        var windowStart = dataEndUtc.AddDays(-Math.Min(365, spanDays));
        if (windowStart < dataStartUtc)
            windowStart = dataStartUtc;

        var windowOneMinute = new Dictionary<TradingSymbol, IReadOnlyList<KlineCandle>>();
        foreach (var (symbol, data) in loaded)
            windowOneMinute[symbol] = CandleWindowSlicer.Slice(data.Candles, windowStart, dataEndUtc);

        BtcContextIndex? btcContext = windowOneMinute.TryGetValue(TradingSymbol.BTCUSDT, out var btcWindow)
            ? new BtcContextIndex(btcWindow)
            : null;
        var marketWideContext = new MarketWideContextIndex(windowOneMinute, includeBtcInProxy: true);

        var candidates = new List<DiscoveryRuleCandidateRow>();
        var trades = new List<DiscoveryTradeRow>();
        var splitPerformance = new List<DiscoverySplitPerformanceRow>();
        var windowRobustness = new List<DiscoveryWindowRobustnessRow>();
        var monthlyPerformance = new List<DiscoveryMonthlyPerformanceRow>();
        var costSensitivity = new List<DiscoveryCostSensitivityRow>();
        var drawdown = new List<DiscoveryDrawdownRow>();
        var contributions = new List<DiscoveryFeatureContribution>();
        var combosScanned = 0;
        var candidateCount = 0;
        var validationSurvivors = 0;
        var holdoutSurvivors = 0;

        foreach (var (symbol, oneMinute) in windowOneMinute)
        {
            foreach (var interval in FuturesDirectionalRuleDiscoveryV2Catalog.Intervals)
            {
                var intervalCandles = CandleAggregator.Aggregate(symbol, oneMinute, "1m", interval).Candles;
                if (intervalCandles.Count <= MarketRegimeForwardEdgeScanner.MinimumWarmupCandles + 2)
                    continue;
                var basePoints = FuturesDirectionalRuleDiscoveryV2Engine.BuildBasePoints(
                    interval, intervalCandles, btcContext, marketWideContext, cancellationToken);
                if (basePoints.Count < FuturesDirectionalRuleDiscoveryV2Catalog.MinimumTotalTrades)
                    continue;

                foreach (var direction in new[] { LongShortDirection.Long, LongShortDirection.Short })
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var combo = FuturesDirectionalRuleDiscoveryV2Engine.ScanCombo(
                        symbol, interval, direction, basePoints, intervalCandles, oneMinute, dataEndUtc, cancellationToken);
                    combosScanned++;
                    candidateCount += combo.CandidateCount;
                    validationSurvivors += combo.ValidationSurvivorCount;
                    holdoutSurvivors += combo.HoldoutSurvivorCount;

                    candidates.AddRange(combo.Candidates);
                    trades.AddRange(combo.Trades);
                    splitPerformance.AddRange(combo.SplitPerformance);
                    windowRobustness.AddRange(combo.WindowRobustness);
                    monthlyPerformance.AddRange(combo.MonthlyPerformance);
                    costSensitivity.AddRange(combo.CostSensitivity);
                    drawdown.AddRange(combo.Drawdown);
                    contributions.AddRange(combo.FeatureContributions);
                }
            }
        }

        var cappedTrades = trades.Take(MaxTradeRows).ToArray();
        var featureImportance = BuildFeatureImportance(contributions);
        var answers = BuildAnswers(candidates, candidateCount, validationSurvivors, holdoutSurvivors, loaded.Count, windowStart, dataEndUtc);

        var orderedCandidates = candidates
            .OrderByDescending(c => c.AllSplitsPositive)
            .ThenByDescending(c => c.FullHistoryPositive)
            .ThenByDescending(c => c.FullHistoryNet)
            .ToArray();

        var result = new FuturesDirectionalRuleDiscoveryV2RunResult(
            orderedCandidates,
            cappedTrades,
            splitPerformance,
            windowRobustness,
            monthlyPerformance,
            costSensitivity,
            drawdown,
            featureImportance,
            answers,
            loaded.Count,
            combosScanned,
            candidateCount,
            validationSurvivors,
            holdoutSurvivors,
            windowStart,
            dataEndUtc);

        Directory.CreateDirectory(settings.OutputDirectory);
        var writer = new FuturesDirectionalRuleDiscoveryV2ReportWriter(settings.OutputDirectory);
        await writer.WriteAsync(result, cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = "futures-directional-rule-discovery-v2",
                settings.DataDirectory,
                settings.OutputDirectory,
                symbolsScanned = loaded.Keys.Select(k => k.ToString()).ToArray(),
                intervals = FuturesDirectionalRuleDiscoveryV2Catalog.Intervals,
                combosScanned,
                candidateCount,
                validationSurvivors,
                holdoutSurvivors,
                primaryConfig = FuturesDirectionalRuleDiscoveryV2Catalog.PrimaryConfig.Label,
                dataStartUtc = windowStart,
                dataEndUtc,
                backtestOnly = true,
                liveFuturesRecommended = false
            }, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        return result;
    }

    private static IReadOnlyList<DiscoveryFeatureImportanceRow> BuildFeatureImportance(
        IReadOnlyList<DiscoveryFeatureContribution> contributions)
        => contributions
            .GroupBy(c => (c.Feature, c.Direction))
            .Select(g => new DiscoveryFeatureImportanceRow
            {
                Feature = g.Key.Feature,
                Direction = g.Key.Direction,
                CandidateCount = g.Count(),
                TrainQualifiedCount = g.Count(c => c.TrainQualified),
                ValidationSurvivorCount = g.Count(c => c.ValidationSurvivor),
                HoldoutPositiveCount = g.Count(c => c.HoldoutPositive),
                AvgTrainNet = Math.Round(g.Average(c => c.TrainNet), 6),
                AvgValidationNet = Math.Round(g.Average(c => c.ValidationNet), 6),
                AvgHoldoutNet = Math.Round(g.Average(c => c.HoldoutNet), 6)
            })
            .OrderByDescending(r => r.ValidationSurvivorCount)
            .ThenByDescending(r => r.TrainQualifiedCount)
            .ToArray();

    private static IReadOnlyList<ReachabilityResearchAnswer> BuildAnswers(
        IReadOnlyList<DiscoveryRuleCandidateRow> candidates,
        int candidateCount,
        int validationSurvivors,
        int holdoutSurvivors,
        int symbolsScanned,
        DateTime dataStartUtc,
        DateTime dataEndUtc)
    {
        var allSplitSurvivors = candidates.Where(c => c.AllSplitsPositive && c.FullHistoryPositive).ToArray();
        var answers = new List<ReachabilityResearchAnswer>();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are there any long or short directional rules that survive train, validation, and holdout?",
            Answer = allSplitSurvivors.Length > 0
                ? $"{allSplitSurvivors.Length} rule(s) positive across all splits and full history. Example: {allSplitSurvivors[0].RuleName} ({allSplitSurvivors[0].RuleDescription})."
                : $"No rule is positive across train, validation, and holdout. {candidateCount} train-qualified candidates, {candidates.Count} best-effort candidates reported, {validationSurvivors} validation survivors, {holdoutSurvivors} also holdout-positive.",
            Verdict = allSplitSurvivors.Length > 0 ? "SurvivorsFound" : "NoSurvivors",
            Details = new Dictionary<string, object?> { ["allSplitSurvivors"] = allSplitSurvivors.Take(10).ToArray() }
        });

        var persistent = allSplitSurvivors
            .GroupBy(c => $"{c.Symbol} {c.Interval} {c.Direction}")
            .Select(g => g.Key)
            .ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which symbols/intervals show persistent edge across long history?",
            Answer = persistent.Length > 0
                ? $"Persistent all-split edge in: {string.Join(", ", persistent)}."
                : "No symbol/interval/direction showed a persistent all-split edge.",
            Verdict = persistent.Length > 0 ? "PersistentEdgePresent" : "NoPersistentEdge",
            Details = null
        });

        var shortSurvivors = candidates.Count(c => c.Direction == "Short" && c.ValidationPositive);
        var longSurvivors = candidates.Count(c => c.Direction == "Long" && c.ValidationPositive);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is short side consistently better than long side?",
            Answer = $"Validation-positive candidates: Short={shortSurvivors}, Long={longSurvivors}. All-split: Short={allSplitSurvivors.Count(c => c.Direction == "Short")}, Long={allSplitSurvivors.Count(c => c.Direction == "Long")}.",
            Verdict = shortSurvivors > longSurvivors ? "ShortStronger" : longSurvivors > shortSurvivors ? "LongStronger" : "NoClearSideEdge",
            Details = null
        });

        var btcSurvivors = allSplitSurvivors.Count(c => c.FeaturesUsed.Contains("Btc", StringComparison.OrdinalIgnoreCase));
        var nonBtcSurvivors = allSplitSurvivors.Length - btcSurvivors;
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does BTC context materially improve rule robustness?",
            Answer = allSplitSurvivors.Length == 0
                ? "No all-split survivors to assess BTC-context contribution."
                : $"All-split survivors using BTC features: {btcSurvivors}; without BTC features: {nonBtcSurvivors}.",
            Verdict = allSplitSurvivors.Length == 0 ? "Inconclusive" : btcSurvivors > nonBtcSurvivors ? "BtcContextHelps" : "BtcContextNeutral",
            Details = null
        });

        var stressSurvivors = allSplitSurvivors.Count(c => c.StressPositive);
        var stressPlusSurvivors = allSplitSurvivors.Count(c => c.StressPlusPositive);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Do any rules survive futures-stress or stress-plus?",
            Answer = $"All-split survivors also positive under futures-stress: {stressSurvivors}; under stress-plus: {stressPlusSurvivors}.",
            Verdict = stressSurvivors > 0 ? "SurvivesStress" : "FailsStress",
            Details = null
        });

        var monthlyPass = allSplitSurvivors.Count(c => c.MonthlyConsistencyPass);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are profits spread across months or concentrated in recent clusters?",
            Answer = allSplitSurvivors.Length == 0
                ? "No all-split survivors; profit-spread question not applicable to a robust rule."
                : $"{monthlyPass}/{allSplitSurvivors.Length} all-split survivors pass monthly consistency (>=50% positive months, >=6 active months).",
            Verdict = monthlyPass > 0 ? "ProfitsSpread" : "ProfitsConcentrated",
            Details = null
        });

        var paperReady = allSplitSurvivors
            .Where(c => c.StressPositive && c.MonthlyConsistencyPass && c.TradeCountSufficient)
            .ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is there any candidate strong enough for future paper/sandbox planning?",
            Answer = paperReady.Length > 0
                ? $"{paperReady.Length} candidate(s) pass all-split + stress + monthly + sample criteria: {string.Join(", ", paperReady.Select(c => c.RuleName).Take(8))}. Still no live recommendation from this branch alone."
                : "No candidate meets all-split + stress + monthly-consistency + sample criteria. None ready for paper/sandbox planning.",
            Verdict = paperReady.Length > 0 ? "PaperCandidateExists" : "NoPaperCandidate",
            Details = new Dictionary<string, object?> { ["paperReady"] = paperReady.Take(10).ToArray() }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "If no rules survive, should we pause candle-rule futures research and require richer data (order book, funding, open interest, liquidation, broader history)?",
            Answer = allSplitSurvivors.Length > 0
                ? "Survivors exist; continue research-only validation before considering richer data."
                : "No candle-only rule survived train/validation/holdout across symbols and intervals. Recommend pausing candle-rule futures discovery and acquiring richer data (order book depth, funding rate, open interest, liquidation feeds) and/or broader history.",
            Verdict = allSplitSurvivors.Length > 0 ? "ContinueResearch" : "PauseAndRequireRicherData",
            Details = new Dictionary<string, object?>
            {
                ["candidateCount"] = candidateCount,
                ["validationSurvivors"] = validationSurvivors,
                ["holdoutSurvivors"] = holdoutSurvivors,
                ["symbolsScanned"] = symbolsScanned,
                ["dataStartUtc"] = dataStartUtc,
                ["dataEndUtc"] = dataEndUtc
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Overall verdict: recommend live Futures from this discovery branch?",
            Answer = "Do not recommend live Futures. Backtest-only discovery; any survivor is at most a research/paper candidate pending further validation.",
            Verdict = "DoNotRecommendLiveFutures",
            Details = new Dictionary<string, object?> { ["backtestOnly"] = true, ["liveFuturesRecommended"] = false }
        });

        return answers;
    }
}
