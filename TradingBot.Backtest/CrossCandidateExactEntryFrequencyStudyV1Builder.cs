using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class CrossCandidateExactEntryFrequencyStudyV1Builder
{
    private static readonly Dictionary<string, CrossSymbolActivationConfig> ActivationLookup =
        NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.BuildActivationConfigs()
            .ToDictionary(c => c.ActivationRuleName, StringComparer.OrdinalIgnoreCase);

    public static CrossCandidateExactEntryFrequencyStudyV1RunResult Build(
        CrossCandidateExactEntryFrequencyStudyV1InputBundle input,
        CurrentOpportunityScannerV1MarketContext market,
        DateTime runAtUtc,
        DateTime studyStartUtc,
        DateTime studyEndUtc)
    {
        var studySpanDays = Math.Max(1m, (decimal)(studyEndUtc - studyStartUtc).TotalDays);
        var costLookup = BuildCostLookup(input.CostSensitivity);
        var periodsByKey = GroupPeriods(input.Periods);
        var tradesByKey = GroupTrades(input.Trades);

        var draftRows = new List<CrossCandidateExactEntryFrequencyStudyV1CandidateRow>();

        foreach (var leader in input.Leaderboard)
        {
            if (!ActivationLookup.TryGetValue(leader.ActivationRule, out var activationConfig))
                continue;
            if (!Enum.TryParse<TradingSymbol>(leader.Symbol, true, out var symbol))
                continue;
            if (!Enum.TryParse<LongShortDirection>(leader.Direction, true, out var direction))
                continue;

            var candidateKey = CrossSymbolCandidateEngineV2Catalog.CandidateKey(
                leader.Symbol, leader.Interval, leader.Direction,
                leader.TargetPercent, leader.StopPercent, leader.ActivationRule);

            var comboKey = new CrossSymbolComboKey(
                symbol, leader.Interval, direction,
                leader.TargetPercent, leader.StopPercent,
                NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.HoldMinutes);

            if (!market.TryGetScan(comboKey, out var scan))
                continue;

            var cooldown = NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.CooldownFor(leader.Interval);
            var intervalCandles = market.GetIntervalCandles(symbol, leader.Interval);
            var flowIndex = market.GetFlowIndex(symbol, leader.Interval);

            var moderateTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
                scan.BaseTrades,
                NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.PrimaryCostScenario,
                market.BtcContext,
                studyEndUtc);

            periodsByKey.TryGetValue(candidateKey, out var candidatePeriods);
            candidatePeriods ??= [];

            tradesByKey.TryGetValue(candidateKey, out var candidateTrades);
            candidateTrades ??= [];

            // V1 trades are the activation truth source for historical exact-entry frequency.
            // Primary-scenario, correct-key trades whose EntryTimeUtc lands in-window.
            var v1TradesInWindow = candidateTrades
                .Where(t => t.EntryTimeUtc >= studyStartUtc && t.EntryTimeUtc <= studyEndUtc)
                .OrderBy(t => t.EntryTimeUtc)
                .ToArray();

            // Activated periods (Activated=true) overlapping the study window, used as the
            // [ActivationStartUtc, ActivationEndUtc) truth source from cross-symbol-v1-periods.json.
            var activatedPeriods = candidatePeriods
                .Where(p => p.Activated)
                .Where(p => p.ActivationEndUtc > studyStartUtc && p.ActivationStartUtc <= studyEndUtc)
                .ToArray();

            // Fixed exact-entry count: distinct V1 trade EntryTimeUtc values that fall inside an
            // activated period. Does NOT require EvaluateCrossSymbolEntryNow.Present at the same time,
            // because at a V1 EntryTimeUtc the shadow evaluator usually returns OpenTradeOverlap.
            var exactEntryTimes = CountV1ExactEntriesInsideActivatedPeriods(v1TradesInWindow, activatedPeriods);
            var v1TradesInsideActivated = v1TradesInWindow.Count(t => FindActivatedPeriod(t.EntryTimeUtc, activatedPeriods) is not null);

            // Diagnostic-only: keep the old evaluator-replay walk and overlap probe for transparency.
            var evaluatorReplayTimes = CountEvaluatorReplayExactEntries(
                activationConfig,
                comboKey,
                intervalCandles,
                scan.BaseTrades,
                moderateTrades,
                flowIndex,
                studyStartUtc,
                studyEndUtc,
                cooldown);
            var evaluatorOpenTradeOverlapCount = CountEvaluatorOpenTradeOverlap(
                comboKey,
                intervalCandles,
                scan.BaseTrades,
                v1TradesInWindow,
                studyStartUtc,
                cooldown);

            costLookup.TryGetValue(candidateKey, out var costs);
            input.V2ByKey.TryGetValue(candidateKey, out var v2);
            input.ScannerByKey.TryGetValue(candidateKey, out var scanner);

            var netModerate = costs?.Moderate ?? v2?.NetModerate ?? leader.NetPnl;
            var netStress = costs?.StressPlus ?? v2?.NetStressPlus ?? leader.StressPlusNet;

            var activatedCheckpointCount = candidatePeriods.Count(p =>
                p.Activated
                && p.CheckpointUtc >= studyStartUtc
                && p.CheckpointUtc <= studyEndUtc);

            var exactCount = exactEntryTimes.Count;
            var exactPerDay = Math.Round(exactCount / studySpanDays, 6);
            var lastExact = exactCount > 0 ? exactEntryTimes[^1] : (DateTime?)null;
            var daysSinceLast = lastExact.HasValue
                ? Math.Round((decimal)(studyEndUtc - lastExact.Value).TotalDays, 4)
                : (decimal?)null;

            var (medianHours, maxHours) = ComputeEntryGaps(exactEntryTimes);

            draftRows.Add(new CrossCandidateExactEntryFrequencyStudyV1CandidateRow
            {
                CandidateKey = candidateKey,
                Symbol = leader.Symbol,
                Interval = leader.Interval,
                Direction = leader.Direction,
                TargetPercent = leader.TargetPercent,
                StopPercent = leader.StopPercent,
                ActivationRule = leader.ActivationRule,
                TradeCount = leader.TradeCount,
                V1TradeCount = v1TradesInWindow.Length,
                V1TradesInsideActivatedPeriods = v1TradesInsideActivated,
                ActivatedCheckpointCount = activatedCheckpointCount,
                ExactEntryCountInsideActivatedWindows = exactCount,
                ExactEntriesPerDay = exactPerDay,
                DaysSinceLastExactEntry = daysSinceLast,
                LastExactEntryUtc = lastExact,
                MedianHoursBetweenExactEntries = medianHours,
                MaxHoursBetweenExactEntries = maxHours,
                EvaluatorReplayPresentCount = evaluatorReplayTimes.Count,
                EvaluatorOpenTradeOverlapCount = evaluatorOpenTradeOverlapCount,
                FrequencyCountingMethod = "V1TradesInsideActivatedPeriods",
                NetModerate = Math.Round(netModerate, 8),
                NetStressPlus = Math.Round(netStress, 8),
                WinRate = leader.WinRate,
                ProfitFactor = leader.ProfitFactor,
                MaxDrawdown = leader.MaxDrawdown,
                MaxConsecutiveLosses = leader.MaxConsecutiveLosses,
                SparseWarning = leader.SparseWarning,
                OverfitWarning = leader.OverfitWarning,
                SingleClusterWarning = leader.SingleClusterWarning,
                PositiveActivatedPeriodsPercent = leader.PositiveActivatedPeriodsPercent,
                ActivationCurrentlyPassed = scanner?.ActivationCurrentlyPassed,
                BaseEntrySignalPresentNow = scanner?.BaseEntrySignalPresentNow
            });
        }

        ApplyScoresAndClassifications(draftRows, studyEndUtc);

        var candidates = draftRows
            .OrderByDescending(c => c.CombinedCandidateScore)
            .ThenByDescending(c => c.ExactEntriesPerDay)
            .ThenByDescending(c => c.NetStressPlus)
            .ToArray();

        var topFrequency = candidates
            .OrderByDescending(c => c.EntryFrequencyScore)
            .ThenByDescending(c => c.ExactEntriesPerDay)
            .Take(CrossCandidateExactEntryFrequencyStudyV1Catalog.TopFrequencyLimit)
            .ToArray();

        var topStressPositive = candidates
            .Where(c => c.NetStressPlus > 0m)
            .Where(c => c.EntryFrequencyClassification is not ("TooRare" or "NoRecentEntries" or "StressNegative"))
            .OrderByDescending(c => c.CombinedCandidateScore)
            .ThenByDescending(c => c.NetStressPlus)
            .Take(CrossCandidateExactEntryFrequencyStudyV1Catalog.TopStressPositiveLimit)
            .ToArray();

        var tooRare = candidates
            .Where(c => c.EntryFrequencyClassification is "TooRare" or "RareButProfitable" or "NoRecentEntries"
                        || c.Recommendation == "ParkTooRare")
            .OrderByDescending(c => c.NetStressPlus)
            .ThenBy(c => c.ExactEntriesPerDay)
            .ToArray();

        var promoteCount = candidates.Count(c => c.Recommendation == "PromoteToExactEntryWatcher");

        var totalV1Trades = candidates.Sum(c => c.V1TradeCount);
        var totalV1Inside = candidates.Sum(c => c.V1TradesInsideActivatedPeriods);
        var totalExactAfterFix = candidates.Sum(c => c.ExactEntryCountInsideActivatedWindows);
        var withExactAfterFix = candidates.Count(c => c.ExactEntryCountInsideActivatedWindows > 0);
        var stillTooRare = candidates.Count(c =>
            c.EntryFrequencyClassification is "TooRare" or "RareButProfitable" or "NoRecentEntries");
        var stressPositiveAndFrequent = candidates.Count(c =>
            c.NetStressPlus > 0m && c.EntryFrequencyClassification == "FrequentEnough");

        var plainEnglish = BuildPlainEnglish(
            candidates, topFrequency, topStressPositive, tooRare, promoteCount,
            totalV1Trades, totalV1Inside, totalExactAfterFix, withExactAfterFix);

        var summary = new CrossCandidateExactEntryFrequencyStudyV1SummaryRow
        {
            RunAtUtc = runAtUtc,
            StudyStartUtc = studyStartUtc,
            StudyEndUtc = studyEndUtc,
            StudySpanDays = studySpanDays,
            EvaluatedCandidateCount = candidates.Length,
            CandidatesWithExactEntries = withExactAfterFix,
            PromoteToExactEntryWatcherCount = promoteCount,
            TooRareCount = tooRare.Length,
            StressNegativeCount = candidates.Count(c => c.EntryFrequencyClassification == "StressNegative"),
            TotalV1Trades = totalV1Trades,
            TotalV1TradesInsideActivatedPeriods = totalV1Inside,
            TotalExactEntriesAfterFix = totalExactAfterFix,
            CandidatesWithExactEntriesAfterFix = withExactAfterFix,
            CandidatesStillTooRare = stillTooRare,
            CandidatesStressPositiveAndFrequentEnough = stressPositiveAndFrequent,
            CountingBugFixed = true,
            PlainEnglish = plainEnglish,
            CompactSummaryLine =
                $"Cross-candidate exact entry frequency | evaluated={candidates.Length} v1Trades={totalV1Trades} v1Inside={totalV1Inside} exactAfterFix={totalExactAfterFix} withExactEntries={withExactAfterFix} promote={promoteCount} tooRare={tooRare.Length} stressNegative={candidates.Count(c => c.EntryFrequencyClassification == "StressNegative")} countingBugFixed=true"
        };

        return new CrossCandidateExactEntryFrequencyStudyV1RunResult(
            summary, candidates, topFrequency, topStressPositive, tooRare);
    }

    // Fixed exact-entry counting (reconciliation audit semantics): count distinct V1 trade
    // EntryTimeUtc values that fall inside an activated period [ActivationStartUtc, ActivationEndUtc).
    // The activation truth source is cross-symbol-v1-periods.json (Activated=true). The shadow
    // evaluator's EvaluateCrossSymbolEntryNow is intentionally NOT required here because it returns
    // OpenTradeOverlap at a V1 EntryTimeUtc (the base trade is already open at that instant).
    private static List<DateTime> CountV1ExactEntriesInsideActivatedPeriods(
        IReadOnlyList<CrossSymbolTradeRow> v1TradesInWindow,
        IReadOnlyList<CrossSymbolPeriodRow> activatedPeriods)
    {
        var seenEntryTimes = new HashSet<DateTime>();
        var exactEntryTimes = new List<DateTime>();

        foreach (var trade in v1TradesInWindow)
        {
            if (FindActivatedPeriod(trade.EntryTimeUtc, activatedPeriods) is null)
                continue;

            // Deduplicate by CandidateKey + EntryTimeUtc (CandidateKey is fixed per candidate loop).
            if (seenEntryTimes.Add(trade.EntryTimeUtc))
                exactEntryTimes.Add(trade.EntryTimeUtc);
        }

        exactEntryTimes.Sort();
        return exactEntryTimes;
    }

    private static CrossSymbolPeriodRow? FindActivatedPeriod(
        DateTime entryUtc,
        IReadOnlyList<CrossSymbolPeriodRow> activatedPeriods)
        => activatedPeriods.FirstOrDefault(p =>
            p.Activated
            && entryUtc >= p.ActivationStartUtc
            && entryUtc < p.ActivationEndUtc);

    // Diagnostic-only: original evaluator-replay walk. Retained so EvaluatorReplayPresentCount stays
    // visible and the historical undercount is transparent. Not used as the exact-entry count.
    private static List<DateTime> CountEvaluatorReplayExactEntries(
        CrossSymbolActivationConfig activationConfig,
        CrossSymbolComboKey comboKey,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        IReadOnlyList<RegimeDriftDiagnosticTrade> moderateTrades,
        ShortWindowFlowFeatureIndex flowIndex,
        DateTime studyStartUtc,
        DateTime studyEndUtc,
        int cooldownCandles)
    {
        var seenEntryTimes = new HashSet<DateTime>();
        var exactEntryTimes = new List<DateTime>();

        if (intervalCandles.Count < MarketRegimeForwardEdgeScanner.MinimumWarmupCandles + 2)
            return exactEntryTimes;

        for (var i = MarketRegimeForwardEdgeScanner.MinimumWarmupCandles; i < intervalCandles.Count - 1; i++)
        {
            var evalUtc = intervalCandles[i + 1].OpenTimeUtc;
            if (evalUtc < studyStartUtc || evalUtc > studyEndUtc)
                continue;

            var activation = FuturesTestnetShadowEvaluator.EvaluateCrossSymbolActivation(
                activationConfig,
                comboKey,
                moderateTrades,
                evalUtc,
                studyStartUtc,
                flowIndex);

            if (!activation.Passed)
                continue;

            var entry = FuturesTestnetShadowEvaluator.EvaluateCrossSymbolEntryNow(
                comboKey,
                intervalCandles,
                baseTrades,
                evalUtc,
                studyStartUtc,
                cooldownCandles);

            if (!entry.Present || entry.EntryTimeUtc is null)
                continue;

            if (seenEntryTimes.Add(entry.EntryTimeUtc.Value))
                exactEntryTimes.Add(entry.EntryTimeUtc.Value);
        }

        exactEntryTimes.Sort();
        return exactEntryTimes;
    }

    // Diagnostic-only: how many V1 trades have the evaluator return OpenTradeOverlap at their exact
    // EntryTimeUtc. This is the documented root cause of the old zero exact-entry count.
    private static int CountEvaluatorOpenTradeOverlap(
        CrossSymbolComboKey comboKey,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        IReadOnlyList<CrossSymbolTradeRow> v1TradesInWindow,
        DateTime studyStartUtc,
        int cooldownCandles)
    {
        var count = 0;
        foreach (var trade in v1TradesInWindow)
        {
            var entry = FuturesTestnetShadowEvaluator.EvaluateCrossSymbolEntryNow(
                comboKey,
                intervalCandles,
                baseTrades,
                trade.EntryTimeUtc,
                studyStartUtc,
                cooldownCandles);

            if (string.Equals(entry.Reason, "OpenTradeOverlap", StringComparison.OrdinalIgnoreCase))
                count++;
        }

        return count;
    }

    private static void ApplyScoresAndClassifications(
        List<CrossCandidateExactEntryFrequencyStudyV1CandidateRow> rows,
        DateTime studyEndUtc)
    {
        if (rows.Count == 0)
            return;

        var maxPerDay = rows.Max(r => r.ExactEntriesPerDay);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var entryFreqScore = maxPerDay <= 0m || row.ExactEntriesPerDay <= 0m
                ? 0m
                : Math.Round(row.ExactEntriesPerDay / maxPerDay * 100m, 4);

            var stressScore = ComputeStressQualityScore(row);
            var combined = Math.Round(
                entryFreqScore * CrossCandidateExactEntryFrequencyStudyV1Catalog.EntryFrequencyScoreWeight
                + stressScore * CrossCandidateExactEntryFrequencyStudyV1Catalog.StressQualityScoreWeight,
                4);

            var classification = ClassifyEntryFrequency(row, studyEndUtc);
            var recommendation = ResolveRecommendation(row, classification);

            rows[i] = row with
            {
                EntryFrequencyScore = entryFreqScore,
                StressQualityScore = stressScore,
                CombinedCandidateScore = combined,
                EntryFrequencyClassification = classification,
                Recommendation = recommendation
            };
        }
    }

    private static decimal ComputeStressQualityScore(CrossCandidateExactEntryFrequencyStudyV1CandidateRow row)
    {
        if (row.NetStressPlus <= 0m)
            return 0m;

        var netComponent = Math.Min(100m, Math.Max(0m, row.NetStressPlus * 10m));
        var winComponent = Math.Min(100m, row.WinRate * 100m);
        var pfComponent = Math.Min(100m, row.ProfitFactor * 25m);
        var periodComponent = Math.Min(100m, row.PositiveActivatedPeriodsPercent);

        return Math.Round(netComponent * 0.35m + winComponent * 0.25m + pfComponent * 0.20m + periodComponent * 0.20m, 4);
    }

    private static string ClassifyEntryFrequency(
        CrossCandidateExactEntryFrequencyStudyV1CandidateRow row,
        DateTime studyEndUtc)
    {
        if (row.NetStressPlus <= 0m)
            return "StressNegative";

        var stale = row.ExactEntryCountInsideActivatedWindows == 0
                    || (row.LastExactEntryUtc.HasValue
                        && (studyEndUtc - row.LastExactEntryUtc.Value).TotalDays
                        > CrossCandidateExactEntryFrequencyStudyV1Catalog.StaleLastEntryDays);

        if (stale)
            return "NoRecentEntries";

        var tooRare = row.ExactEntryCountInsideActivatedWindows
                      <= CrossCandidateExactEntryFrequencyStudyV1Catalog.TooRareMaxExactEntries
                      || row.ExactEntriesPerDay
                      <= CrossCandidateExactEntryFrequencyStudyV1Catalog.TooRareMaxEntriesPerDay;

        if (tooRare)
            return row.NetStressPlus > 0m ? "RareButProfitable" : "TooRare";

        if (row.ExactEntriesPerDay >= CrossCandidateExactEntryFrequencyStudyV1Catalog.FrequentEnoughMinEntriesPerDay)
            return "FrequentEnough";

        if (row.ExactEntriesPerDay >= CrossCandidateExactEntryFrequencyStudyV1Catalog.ModerateFrequencyMinEntriesPerDay)
            return "ModerateFrequency";

        return "TooRare";
    }

    private static string ResolveRecommendation(
        CrossCandidateExactEntryFrequencyStudyV1CandidateRow row,
        string classification)
    {
        if (row.NetStressPlus <= 0m)
            return "RejectStressNegative";

        if (row.SingleClusterWarning)
            return classification is "TooRare" or "RareButProfitable" or "NoRecentEntries"
                ? "ParkTooRare"
                : "KeepIncubating";

        var promoteEligible = row.NetModerate > 0m
                              && row.NetStressPlus > 0m
                              && !row.SparseWarning
                              && !row.OverfitWarning
                              && !row.SingleClusterWarning
                              && row.ExactEntriesPerDay
                              >= CrossCandidateExactEntryFrequencyStudyV1Catalog.FrequentEnoughMinEntriesPerDay
                              && row.ExactEntryCountInsideActivatedWindows > 0
                              && row.DaysSinceLastExactEntry.HasValue
                              && row.DaysSinceLastExactEntry.Value
                              <= CrossCandidateExactEntryFrequencyStudyV1Catalog.StaleLastEntryDays
                              && classification == "FrequentEnough";

        if (promoteEligible)
            return "PromoteToExactEntryWatcher";

        if (classification is "TooRare" or "RareButProfitable" or "NoRecentEntries")
            return "ParkTooRare";

        if ((row.SparseWarning || row.TradeCount < CrossCandidateExactEntryFrequencyStudyV1Catalog.MinTradeCountForShadow)
            && row.ExactEntryCountInsideActivatedWindows > 0)
            return "NeedsMoreForwardData";

        return "KeepIncubating";
    }

    private static (decimal? MedianHours, decimal? MaxHours) ComputeEntryGaps(IReadOnlyList<DateTime> exactEntryTimes)
    {
        if (exactEntryTimes.Count < 2)
            return (null, null);

        var gaps = new List<decimal>();
        for (var i = 1; i < exactEntryTimes.Count; i++)
        {
            gaps.Add((decimal)(exactEntryTimes[i] - exactEntryTimes[i - 1]).TotalHours);
        }

        gaps.Sort();
        var median = gaps.Count % 2 == 0
            ? (gaps[gaps.Count / 2 - 1] + gaps[gaps.Count / 2]) / 2m
            : gaps[gaps.Count / 2];

        return (Math.Round(median, 4), Math.Round(gaps[^1], 4));
    }

    private static Dictionary<string, List<CrossSymbolPeriodRow>> GroupPeriods(IReadOnlyList<CrossSymbolPeriodRow> periods)
    {
        var dict = new Dictionary<string, List<CrossSymbolPeriodRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in periods)
        {
            var key = CrossSymbolCandidateEngineV2Catalog.CandidateKey(
                p.Symbol, p.Interval, p.Direction, p.TargetPercent, p.StopPercent, p.ActivationRule);
            if (!dict.TryGetValue(key, out var list))
            {
                list = [];
                dict[key] = list;
            }

            list.Add(p);
        }

        return dict;
    }

    private static Dictionary<string, List<CrossSymbolTradeRow>> GroupTrades(IReadOnlyList<CrossSymbolTradeRow> trades)
    {
        var dict = new Dictionary<string, List<CrossSymbolTradeRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in trades)
        {
            // Primary scenario / correct symbol / interval / direction / target / stop / activation rule.
            if (!string.Equals(
                    t.CostScenario,
                    NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.PrimaryCostScenario,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var key = CrossSymbolCandidateEngineV2Catalog.CandidateKey(
                t.Symbol, t.Interval, t.Direction, t.TargetPercent, t.StopPercent, t.ActivationRule);
            if (!dict.TryGetValue(key, out var list))
            {
                list = [];
                dict[key] = list;
            }

            list.Add(t);
        }

        return dict;
    }

    private static Dictionary<string, CostTriplet> BuildCostLookup(IReadOnlyList<CrossSymbolCostSensitivityRow> rows)
    {
        var dict = new Dictionary<string, CostTriplet>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var key = CrossSymbolCandidateEngineV2Catalog.CandidateKey(
                row.Symbol, row.Interval, row.Direction, row.TargetPercent, row.StopPercent, row.ActivationRule);
            if (!dict.TryGetValue(key, out var triplet))
            {
                triplet = new CostTriplet();
                dict[key] = triplet;
            }

            if (string.Equals(row.CostScenario, CrossSymbolCandidateEngineV2Catalog.PrimaryCostScenario, StringComparison.OrdinalIgnoreCase))
                triplet.Moderate = row.NetPnlQuote;
            else if (string.Equals(row.CostScenario, CrossSymbolCandidateEngineV2Catalog.ModerateLatencyScenario, StringComparison.OrdinalIgnoreCase))
                triplet.Latency = row.NetPnlQuote;
            else if (string.Equals(row.CostScenario, CrossSymbolCandidateEngineV2Catalog.StressPlusScenario, StringComparison.OrdinalIgnoreCase))
                triplet.StressPlus = row.NetPnlQuote;
        }

        return dict;
    }

    private sealed class CostTriplet
    {
        public decimal Moderate { get; set; }
        public decimal Latency { get; set; }
        public decimal StressPlus { get; set; }
    }

    private static CrossCandidateExactEntryFrequencyStudyV1PlainEnglish BuildPlainEnglish(
        IReadOnlyList<CrossCandidateExactEntryFrequencyStudyV1CandidateRow> candidates,
        IReadOnlyList<CrossCandidateExactEntryFrequencyStudyV1CandidateRow> topFrequency,
        IReadOnlyList<CrossCandidateExactEntryFrequencyStudyV1CandidateRow> topStressPositive,
        IReadOnlyList<CrossCandidateExactEntryFrequencyStudyV1CandidateRow> tooRare,
        int promoteCount,
        int totalV1Trades,
        int totalV1Inside,
        int totalExactAfterFix,
        int withExactAfterFix)
    {
        string FormatTop(IReadOnlyList<CrossCandidateExactEntryFrequencyStudyV1CandidateRow> list, int take = 5)
        {
            if (list.Count == 0)
                return "(none)";
            return string.Join("; ", list.Take(take).Select(c =>
                $"{c.CandidateKey} exact={c.ExactEntryCountInsideActivatedWindows} perDay={c.ExactEntriesPerDay:F4}"));
        }

        var mostOften = topFrequency.Count == 0
            ? "No candidates produced exact entries inside activated windows in the study window."
            : $"Top by frequency: {FormatTop(topFrequency)}.";

        var stressNotRare = topStressPositive.Count == 0
            ? "No candidates are both stress-positive and not classified too-rare/no-recent."
            : $"Top stress-positive and not too rare: {FormatTop(topStressPositive)}.";

        var profitableTooRare = tooRare.Count(c => c.NetStressPlus > 0m) == 0
            ? "No stress-positive candidates were classified too-rare."
            : $"Profitable but too rare: {FormatTop(tooRare.Where(c => c.NetStressPlus > 0m).OrderByDescending(c => c.NetStressPlus).ToList())}.";

        var watcherFocus = promoteCount == 0
            ? "No candidates meet PromoteToExactEntryWatcher gates. Watcher should remain exact-entry-only, not near-miss focused."
            : $"Watcher should focus on: {FormatTop(candidates.Where(c => c.Recommendation == "PromoteToExactEntryWatcher").ToList())}.";

        var testnetPrep = promoteCount > 0
                         && candidates.Any(c =>
                             c.Recommendation == "PromoteToExactEntryWatcher"
                             && c.EntryFrequencyClassification == "FrequentEnough")
            ? $"Yes — {promoteCount} candidate(s) pass exact-entry frequency and stress gates for testnet-order preparation review (still diagnostic only; no orders placed by this study)."
            : "No — no candidate passes both FrequentEnough exact-entry frequency and all stress/quality promote gates.";

        var countingBugFixNote =
            "COUNTING BUG FIXED. The earlier result (EvaluatedCandidates=120, ExactEntryCountInsideActivatedWindows=0 for every candidate) was INVALID as a market conclusion. "
            + "Cross-Symbol Exact Entry Reconciliation Audit V1 proved V1 discovery recorded real primary-scenario trades inside activated periods, but the old walk required EvaluateCrossSymbolEntryNow.Present=true at the same candle as activation. At a V1 EntryTimeUtc the evaluator returns OpenTradeOverlap (the base trade is already open), so the old count was structurally forced to zero.";

        var fixedFrequencyMethodNote =
            $"FixedFrequencyMethod=V1TradesInsideActivatedPeriods. Exact entries are now counted as distinct V1 trade EntryTimeUtc values that fall inside an activated period [ActivationStartUtc, ActivationEndUtc) from cross-symbol-v1-periods.json (Activated=true), deduplicated by CandidateKey + EntryTimeUtc. "
            + $"After fix: TotalV1Trades={totalV1Trades}, TotalV1TradesInsideActivatedPeriods={totalV1Inside}, TotalExactEntriesAfterFix={totalExactAfterFix}, CandidatesWithExactEntries={withExactAfterFix}. EvaluatorReplayPresentCount and EvaluatorOpenTradeOverlapCount are retained per candidate as diagnostics only.";

        var historicalNote =
            "This is still HISTORICAL RESEARCH, not forward proof. Exact-entry frequency measures how often base entries landed inside past activation windows; it does not guarantee future entries, fills, or profitability.";

        var liveTestnetGuard =
            "Do NOT enable live or testnet trading on the basis of this study. No orders are placed and none are recommended. Promotion toward live/testnet still requires passing stress quality, sufficient exact-entry frequency, and current execution readiness checks together.";

        return new CrossCandidateExactEntryFrequencyStudyV1PlainEnglish
        {
            CountingBugFixNote = countingBugFixNote,
            FixedFrequencyMethodNote = fixedFrequencyMethodNote,
            WhichCandidatesProduceExactEntriesMostOften = mostOften,
            WhichAreStressPositiveAndNotTooRare = stressNotRare,
            WhichAreProfitableButTooRare = profitableTooRare,
            WhichShouldWatcherFocusOn = watcherFocus,
            WorthMovingTowardTestnetOrderPreparation = testnetPrep,
            HistoricalNotForwardProofNote = historicalNote,
            LiveTestnetGuardNote = liveTestnetGuard
        };
    }
}
