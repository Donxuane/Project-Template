using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class DirectionalRuleFuturesRegimeDriftAnalysisV1Aggregator
{
    private static readonly (string FeatureA, string FeatureB)[] EntryTimeRuleFeaturePairs =
    [
        (nameof(RegimeDriftDiagnosticTrade.TrendSlopePercent), nameof(RegimeDriftDiagnosticTrade.AtrPercent)),
        (nameof(RegimeDriftDiagnosticTrade.RangeWidthPercent), nameof(RegimeDriftDiagnosticTrade.DistanceFromRecentLowPercent)),
        (nameof(RegimeDriftDiagnosticTrade.DistanceFromRecentHighPercent), nameof(RegimeDriftDiagnosticTrade.AtrPercent)),
        (nameof(RegimeDriftDiagnosticTrade.BtcReturn30mPercent), nameof(RegimeDriftDiagnosticTrade.VolatilityRegime)),
        (nameof(RegimeDriftDiagnosticTrade.TrendSlopePercent), nameof(RegimeDriftDiagnosticTrade.SessionBucket))
    ];

    public static IReadOnlyList<RegimeDriftSummaryRow> BuildSummary(
        IReadOnlyList<RegimeDriftDiagnosticTrade> trades,
        string costScenarioLabel)
    {
        var filtered = trades
            .Where(t => string.Equals(t.CostScenarioLabel, costScenarioLabel, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var rows = new List<RegimeDriftSummaryRow>
        {
            BuildPeriodSummary("30d", filtered.Where(t => t.InRecent30d).ToArray(), costScenarioLabel),
            BuildPeriodSummary("60d", filtered.Where(t => t.InRecent60d).ToArray(), costScenarioLabel),
            BuildPeriodSummary("90d", filtered.Where(t => t.InRecent90d).ToArray(), costScenarioLabel),
            BuildPeriodSummary("older", filtered.Where(t => t.InOlder).ToArray(), costScenarioLabel),
            BuildPeriodSummary("trainReference", filtered.Where(t => t.InTrainReference).ToArray(), costScenarioLabel),
            BuildPeriodSummary("holdout30d", filtered.Where(t => t.InHoldout30d).ToArray(), costScenarioLabel),
            BuildPeriodSummary("365d", filtered, costScenarioLabel)
        };

        return rows;
    }

    public static IReadOnlyList<RegimeDriftFeatureComparisonRow> BuildFeatureComparison(
        IReadOnlyList<RegimeDriftDiagnosticTrade> trades,
        string costScenarioLabel)
    {
        var filtered = trades
            .Where(t => string.Equals(t.CostScenarioLabel, costScenarioLabel, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return
        [
            BuildComparisonRow("RecentWinners", filtered.Where(t => t.InRecent90d && t.IsWinner).ToArray()),
            BuildComparisonRow("RecentLosers", filtered.Where(t => t.InRecent90d && !t.IsWinner).ToArray()),
            BuildComparisonRow("OlderWinners", filtered.Where(t => t.InOlder && t.IsWinner).ToArray()),
            BuildComparisonRow("OlderLosers", filtered.Where(t => t.InOlder && !t.IsWinner).ToArray()),
            BuildComparisonRow("RecentAll", filtered.Where(t => t.InRecent90d).ToArray()),
            BuildComparisonRow("OlderAll", filtered.Where(t => t.InOlder).ToArray())
        ];
    }

    public static IReadOnlyList<RegimeDriftMonthlyPerformanceRow> BuildMonthlyPerformance(
        IReadOnlyList<RegimeDriftDiagnosticTrade> trades,
        string costScenarioLabel)
        => trades
            .Where(t => string.Equals(t.CostScenarioLabel, costScenarioLabel, StringComparison.OrdinalIgnoreCase))
            .GroupBy(t => t.MonthKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var winCount = g.Count(t => t.IsWinner);
                var net = g.Sum(t => t.NetPnlQuote);
                return new RegimeDriftMonthlyPerformanceRow
                {
                    MonthKey = g.Key,
                    CostScenarioLabel = costScenarioLabel,
                    TradeCount = g.Count(),
                    WinCount = winCount,
                    WinRate = g.Count() == 0 ? 0m : Math.Round((decimal)winCount / g.Count(), 6),
                    NetPnlQuote = Math.Round(net, 8),
                    AvgNetPerTrade = g.Count() == 0 ? null : Math.Round(net / g.Count(), 8),
                    MonthPositive = net >= 0m
                };
            })
            .ToArray();

    public static IReadOnlyList<RegimeDriftEntryTimeRuleRow> BuildEntryTimeRules(
        IReadOnlyList<RegimeDriftDiagnosticTrade> trades,
        string costScenarioLabel)
    {
        var filtered = trades
            .Where(t => string.Equals(t.CostScenarioLabel, costScenarioLabel, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var rules = new List<RegimeDriftEntryTimeRuleRow>();
        rules.AddRange(DiscoverRules(filtered, "older", "recent90d", t => t.InOlder, t => t.InRecent90d));
        rules.AddRange(DiscoverRules(filtered, "recent90d", "older", t => t.InRecent90d, t => t.InOlder));
        return rules
            .OrderByDescending(r => r.BothPeriodsPositive)
            .ThenByDescending(r => r.TestNetPnlQuote)
            .ToArray();
    }

    public static IReadOnlyList<RegimeDriftOutcomeRuleRow> BuildOutcomeRules(
        IReadOnlyList<RegimeDriftDiagnosticTrade> trades,
        IReadOnlyList<RegimeDriftEntryTimeRuleRow> entryTimeRules,
        string costScenarioLabel)
    {
        var filtered = trades
            .Where(t => string.Equals(t.CostScenarioLabel, costScenarioLabel, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var rows = new List<RegimeDriftOutcomeRuleRow>();

        foreach (var rule in entryTimeRules.Where(r => !r.SparseWarning).Take(12))
        {
            var predicate = BuildPredicate(rule.RuleDescription);
            if (predicate is null)
                continue;

            var train = FilterByPeriod(filtered, rule.TrainPeriod);
            var test = FilterByPeriod(filtered, rule.TestPeriod);
            var trainFiltered = train.Where(predicate).ToArray();
            var testFiltered = test.Where(predicate).ToArray();
            var baselineTrain = train.Sum(t => t.NetPnlQuote);
            var baselineTest = test.Sum(t => t.NetPnlQuote);

            var filteredTrainNet = trainFiltered.Sum(t => t.NetPnlQuote);
            var filteredTestNet = testFiltered.Sum(t => t.NetPnlQuote);
            var hasEnoughFiltered = trainFiltered.Length >= DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.MinimumRuleTestSamples
                                    && testFiltered.Length >= DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.MinimumRuleTestSamples;
            rows.Add(new RegimeDriftOutcomeRuleRow
            {
                RuleName = rule.RuleName,
                RuleDescription = rule.RuleDescription,
                TrainPeriod = rule.TrainPeriod,
                TestPeriod = rule.TestPeriod,
                CostScenarioLabel = costScenarioLabel,
                BaselineTrades = train.Length,
                FilteredTrades = trainFiltered.Length,
                BaselineNetPnlQuote = Math.Round(baselineTrain, 8),
                FilteredNetPnlQuote = Math.Round(filteredTrainNet, 8),
                BaselineTestNetPnlQuote = Math.Round(baselineTest, 8),
                FilteredTestNetPnlQuote = Math.Round(filteredTestNet, 8),
                RemovesOlderLosers = trainFiltered.Length < train.Length && filteredTrainNet > baselineTrain,
                KeepsRecentWinners = testFiltered.Length > 0 && testFiltered.Count(t => t.IsWinner) >= testFiltered.Length / 2,
                SurvivesBothPeriods = hasEnoughFiltered && filteredTrainNet >= 0m && filteredTestNet >= 0m,
                Verdict = ClassifyOutcomeVerdict(trainFiltered, testFiltered)
            });
        }

        return rows
            .OrderByDescending(r => r.SurvivesBothPeriods)
            .ThenByDescending(r => r.FilteredTestNetPnlQuote)
            .ToArray();
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildAnswers(
        IReadOnlyList<RegimeDriftSummaryRow> summary,
        IReadOnlyList<RegimeDriftFeatureComparisonRow> featureComparison,
        IReadOnlyList<RegimeDriftMonthlyPerformanceRow> monthly,
        IReadOnlyList<RegimeDriftEntryTimeRuleRow> entryTimeRules,
        IReadOnlyList<RegimeDriftOutcomeRuleRow> outcomeRules,
        int totalTrades,
        DateTime dataStartUtc,
        DateTime dataEndUtc)
    {
        var recent90 = summary.FirstOrDefault(s => s.PeriodLabel == "90d");
        var older = summary.FirstOrDefault(s => s.PeriodLabel == "older");
        var trainRef = summary.FirstOrDefault(s => s.PeriodLabel == "trainReference");
        var bestBoth = outcomeRules.FirstOrDefault(r => r.SurvivesBothPeriods);
        var bestRecentOnly = outcomeRules.FirstOrDefault(r => r.KeepsRecentWinners && !r.SurvivesBothPeriods);
        var recentAll = featureComparison.FirstOrDefault(r => r.ComparisonGroup == "RecentAll");
        var olderAll = featureComparison.FirstOrDefault(r => r.ComparisonGroup == "OlderAll");
        var positiveMonths = monthly.Count(m => m.MonthPositive);
        var answers = new List<ReachabilityResearchAnswer>();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "What changed between older losing period and recent winning period?",
            Answer = recent90 is null || older is null
                ? "Insufficient trade samples."
                : $"Recent90d net={recent90.NetPnlQuote:F2} ({recent90.TradeCount} trades) vs older net={older.NetPnlQuote:F2} ({older.TradeCount} trades). ATR recent={recentAll?.AvgAtrPercent:F4} older={olderAll?.AvgAtrPercent:F4}; BTC30m recent={recentAll?.AvgBtcReturn30mPercent:F4} older={olderAll?.AvgBtcReturn30mPercent:F4}; session recent={recentAll?.TopSessionBucket} older={olderAll?.TopSessionBucket}.",
            Verdict = recent90.PeriodPositive && !older.PeriodPositive ? "RecentRegimeShift" : "NoClearShift",
            Details = new Dictionary<string, object?> { ["featureComparison"] = featureComparison }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is recent performance explained by BTC context, volatility, trend slope, session, or market regime?",
            Answer = $"Recent top vol={recentAll?.TopVolatilityRegime}, BTC trend={recentAll?.TopBtcTrendRegime}, session={recentAll?.TopSessionBucket}, hour={recentAll?.TopHourOfDayUtc}. Older top vol={olderAll?.TopVolatilityRegime}, BTC trend={olderAll?.TopBtcTrendRegime}, session={olderAll?.TopSessionBucket}.",
            Verdict = recentAll?.TopVolatilityRegime != olderAll?.TopVolatilityRegime
                      || recentAll?.TopBtcTrendRegime != olderAll?.TopBtcTrendRegime
                ? "RegimeMixShift"
                : "SimilarRegimeMix",
            Details = null
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Can an entry-time filter remove older losing trades while keeping recent winners?",
            Answer = bestBoth is not null
                ? $"Best dual-period rule: {bestBoth.RuleDescription} train={bestBoth.FilteredNetPnlQuote:F2} test={bestBoth.FilteredTestNetPnlQuote:F2}."
                : bestRecentOnly is not null
                    ? $"Only recent-positive filter found: {bestRecentOnly.RuleDescription} test={bestRecentOnly.FilteredTestNetPnlQuote:F2}."
                    : "No entry-time filter met dual-period criteria.",
            Verdict = bestBoth is not null ? "FilterSurvivesBothPeriods" : bestRecentOnly is not null ? "RecentOnlyFilter" : "NoUsefulFilter",
            Details = new Dictionary<string, object?> { ["outcomeRules"] = outcomeRules.Take(8).ToArray() }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does that filter survive both older and recent periods, or is it just another recent overfit?",
            Answer = entryTimeRules.Count(r => r.BothPeriodsPositive) > 0
                ? $"{entryTimeRules.Count(r => r.BothPeriodsPositive)} rules positive in both train and test splits."
                : $"0/{entryTimeRules.Count} rules positive in both periods; {entryTimeRules.Count(r => r.TrainPositive && !r.TestPositive)} recent-train rules fail on older test.",
            Verdict = entryTimeRules.Any(r => r.BothPeriodsPositive) ? "NotRecentOnlyOverfit" : "LikelyRecentOverfit",
            Details = new Dictionary<string, object?> { ["entryTimeRules"] = entryTimeRules.Take(10).ToArray() }
        });

        var overfitWarning = recent90 is not null && recent90.PeriodPositive && trainRef is not null && !trainRef.PeriodPositive;
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is Rule01 short worth further research, or should it be parked?",
            Answer = overfitWarning
                ? $"Recent-positive but train-reference net={trainRef?.NetPnlQuote:F2}; monthly positive={positiveMonths}/{monthly.Count}."
                : $"365d aggregate weak; recent 90d net={recent90?.NetPnlQuote:F2}.",
            Verdict = bestBoth is not null ? "WorthFilteredResearch" : "ParkUnlessNewRegimeFilter",
            Details = new Dictionary<string, object?> { ["totalTrades"] = totalTrades, ["dataStartUtc"] = dataStartUtc, ["dataEndUtc"] = dataEndUtc }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Overall verdict: recommend live Futures from regime drift analysis?",
            Answer = "Do not recommend live Futures. Backtest-only diagnostic; Rule01 short is a recent-regime artifact unless a dual-period entry-time filter is found.",
            Verdict = bestBoth is not null ? "ResearchOnlyDualPeriodFilter" : "DoNotRecommendLiveFutures",
            Details = new Dictionary<string, object?> { ["backtestOnly"] = true, ["liveFuturesRecommended"] = false }
        });

        return answers;
    }

    private static IEnumerable<RegimeDriftEntryTimeRuleRow> DiscoverRules(
        RegimeDriftDiagnosticTrade[] all,
        string trainLabel,
        string testLabel,
        Func<RegimeDriftDiagnosticTrade, bool> trainFilter,
        Func<RegimeDriftDiagnosticTrade, bool> testFilter)
    {
        var train = all.Where(trainFilter).ToArray();
        var test = all.Where(testFilter).ToArray();
        if (train.Length < DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.MinimumRuleTrainSamples / 2)
        {
            yield return SparseRule(trainLabel, testLabel, train.Length, test.Length);
            yield break;
        }

        var ruleIndex = 0;
        foreach (var (featureA, featureB) in EntryTimeRuleFeaturePairs)
        {
            if (IsCategorical(featureB))
            {
                var best = DiscoverCategoricalRule(train, test, featureA, featureB, trainLabel, testLabel, ref ruleIndex);
                if (best is not null)
                    yield return best;
                continue;
            }

            var bucketsA = BuildQuantileBuckets(train.Select(t => GetNumeric(t, featureA)).Where(v => v.HasValue).Select(v => v!.Value).ToArray(), 3);
            var bucketsB = BuildQuantileBuckets(train.Select(t => GetNumeric(t, featureB)).Where(v => v.HasValue).Select(v => v!.Value).ToArray(), 3);
            if (bucketsA.Count == 0 || bucketsB.Count == 0)
                continue;

            RegimeDriftEntryTimeRuleRow? bestNumeric = null;
            foreach (var bucketA in bucketsA)
            {
                foreach (var bucketB in bucketsB)
                {
                    var trainSubset = train.Where(t =>
                        InBucket(GetNumeric(t, featureA), bucketA)
                        && InBucket(GetNumeric(t, featureB), bucketB)).ToArray();
                    if (trainSubset.Length < DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.MinimumRuleTrainSamples / 2)
                        continue;

                    var testSubset = test.Where(t =>
                        InBucket(GetNumeric(t, featureA), bucketA)
                        && InBucket(GetNumeric(t, featureB), bucketB)).ToArray();
                    if (testSubset.Length < DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.MinimumRuleTestSamples)
                        continue;

                    var row = BuildRuleRow(
                        ++ruleIndex,
                        trainLabel,
                        testLabel,
                        $"{featureA} {bucketA.Label} AND {featureB} {bucketB.Label}",
                        $"{featureA},{featureB}",
                        trainSubset,
                        testSubset);
                    if (bestNumeric is null || row.TestNetPnlQuote > bestNumeric.TestNetPnlQuote)
                        bestNumeric = row;
                }
            }

            if (bestNumeric is not null)
                yield return bestNumeric;
        }
    }

    private static RegimeDriftEntryTimeRuleRow? DiscoverCategoricalRule(
        RegimeDriftDiagnosticTrade[] train,
        RegimeDriftDiagnosticTrade[] test,
        string featureA,
        string featureB,
        string trainLabel,
        string testLabel,
        ref int ruleIndex)
    {
        RegimeDriftEntryTimeRuleRow? best = null;
        foreach (var cat in train.Select(t => GetCategorical(t, featureB)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var bucketsA = BuildQuantileBuckets(
                train.Where(t => string.Equals(GetCategorical(t, featureB), cat, StringComparison.OrdinalIgnoreCase))
                    .Select(t => GetNumeric(t, featureA)).Where(v => v.HasValue).Select(v => v!.Value).ToArray(), 3);
            foreach (var bucketA in bucketsA)
            {
                var trainSubset = train.Where(t =>
                    string.Equals(GetCategorical(t, featureB), cat, StringComparison.OrdinalIgnoreCase)
                    && InBucket(GetNumeric(t, featureA), bucketA)).ToArray();
                if (trainSubset.Length < DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.MinimumRuleTrainSamples / 2)
                    continue;

                var testSubset = test.Where(t =>
                    string.Equals(GetCategorical(t, featureB), cat, StringComparison.OrdinalIgnoreCase)
                    && InBucket(GetNumeric(t, featureA), bucketA)).ToArray();
                if (testSubset.Length < DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.MinimumRuleTestSamples)
                    continue;

                var row = BuildRuleRow(
                    ++ruleIndex,
                    trainLabel,
                    testLabel,
                    $"{featureA} {bucketA.Label} AND {featureB}={cat}",
                    $"{featureA},{featureB}",
                    trainSubset,
                    testSubset);
                if (best is null || row.TestNetPnlQuote > best.TestNetPnlQuote)
                    best = row;
            }
        }

        return best;
    }

    private static RegimeDriftEntryTimeRuleRow BuildRuleRow(
        int index,
        string trainLabel,
        string testLabel,
        string description,
        string featuresUsed,
        RegimeDriftDiagnosticTrade[] trainSubset,
        RegimeDriftDiagnosticTrade[] testSubset)
    {
        var trainNet = trainSubset.Sum(t => t.NetPnlQuote);
        var testNet = testSubset.Sum(t => t.NetPnlQuote);
        var trainWinRate = trainSubset.Length == 0 ? 0m : (decimal)trainSubset.Count(t => t.IsWinner) / trainSubset.Length;
        var testWinRate = testSubset.Length == 0 ? 0m : (decimal)testSubset.Count(t => t.IsWinner) / testSubset.Length;
        var sparse = trainSubset.Length < DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.MinimumMeaningfulTrades
                     || testSubset.Length < DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.MinimumRuleTestSamples;

        return new RegimeDriftEntryTimeRuleRow
        {
            RuleName = $"DriftRule{index:D2}",
            TrainPeriod = trainLabel,
            TestPeriod = testLabel,
            RuleDescription = description,
            FeaturesUsed = featuresUsed,
            TrainSamples = trainSubset.Length,
            TestSamples = testSubset.Length,
            TrainNetPnlQuote = Math.Round(trainNet, 8),
            TestNetPnlQuote = Math.Round(testNet, 8),
            TrainMedianNetPerTrade = Median(trainSubset.Select(t => (decimal?)t.NetPnlQuote)),
            TestMedianNetPerTrade = Median(testSubset.Select(t => (decimal?)t.NetPnlQuote)),
            TrainWinRate = Math.Round(trainWinRate, 6),
            TestWinRate = Math.Round(testWinRate, 6),
            TrainPositive = trainNet >= 0m,
            TestPositive = testNet >= 0m,
            BothPeriodsPositive = trainNet >= 0m && testNet >= 0m,
            SparseWarning = sparse,
            Verdict = sparse ? "SparseSamples"
                : trainNet >= 0m && testNet >= 0m ? "DualPeriodPositive"
                : trainNet >= 0m && testNet < 0m ? "RecentOverfitRisk"
                : "Negative"
        };
    }

    private static RegimeDriftEntryTimeRuleRow SparseRule(string trainLabel, string testLabel, int trainCount, int testCount)
        => new()
        {
            RuleName = "SparseWarning",
            TrainPeriod = trainLabel,
            TestPeriod = testLabel,
            RuleDescription = "Insufficient train samples for rule discovery.",
            TrainSamples = trainCount,
            TestSamples = testCount,
            SparseWarning = true,
            Verdict = "SparseSamples"
        };

    private static RegimeDriftSummaryRow BuildPeriodSummary(
        string label,
        RegimeDriftDiagnosticTrade[] trades,
        string costScenarioLabel)
    {
        var winCount = trades.Count(t => t.IsWinner);
        var net = trades.Sum(t => t.NetPnlQuote);
        var sufficient = trades.Length >= DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.MinimumMeaningfulTrades;
        return new RegimeDriftSummaryRow
        {
            PeriodLabel = label,
            CostScenarioLabel = costScenarioLabel,
            TradeCount = trades.Length,
            WinCount = winCount,
            WinRate = trades.Length == 0 ? 0m : Math.Round((decimal)winCount / trades.Length, 6),
            NetPnlQuote = Math.Round(net, 8),
            AvgNetPerTrade = trades.Length == 0 ? null : Math.Round(net / trades.Length, 8),
            MedianNetPerTrade = Median(trades.Select(t => (decimal?)t.NetPnlQuote)),
            TradeCountSufficient = sufficient,
            PeriodPositive = net >= 0m,
            Verdict = ClassifyPeriodVerdict(net, sufficient, label)
        };
    }

    private static RegimeDriftFeatureComparisonRow BuildComparisonRow(
        string group,
        IReadOnlyList<RegimeDriftDiagnosticTrade> rows)
        => new()
        {
            ComparisonGroup = group,
            TradeCount = rows.Count,
            AvgDistanceFromRecentHighPercent = Average(rows.Select(r => (decimal?)r.DistanceFromRecentHighPercent)),
            AvgDistanceFromRecentLowPercent = Average(rows.Select(r => (decimal?)r.DistanceFromRecentLowPercent)),
            AvgRangeWidthPercent = Average(rows.Select(r => (decimal?)r.RangeWidthPercent)),
            AvgAtrPercent = Average(rows.Select(r => (decimal?)r.AtrPercent)),
            AvgTrendSlopePercent = Average(rows.Select(r => (decimal?)r.TrendSlopePercent)),
            AvgBtcReturn30mPercent = Average(rows.Select(r => r.BtcReturn30mPercent)),
            AvgBtcReturn60mPercent = Average(rows.Select(r => r.BtcReturn60mPercent)),
            AvgNetPnlQuote = Average(rows.Select(r => (decimal?)r.NetPnlQuote)),
            AvgMfePercent = Average(rows.Select(r => r.MfePercent)),
            AvgMaePercent = Average(rows.Select(r => r.MaePercent)),
            TopVolatilityRegime = Mode(rows.Select(r => r.VolatilityRegime)),
            TopSessionBucket = Mode(rows.Select(r => r.SessionBucket)),
            TopBtcTrendRegime = Mode(rows.Select(r => r.BtcTrendRegime ?? "Unknown")),
            TopHourOfDayUtc = rows.Count == 0 ? 0 : rows.GroupBy(r => r.HourOfDayUtc).OrderByDescending(g => g.Count()).First().Key
        };

    private static RegimeDriftDiagnosticTrade[] FilterByPeriod(
        RegimeDriftDiagnosticTrade[] trades,
        string periodLabel)
        => periodLabel switch
        {
            "older" => trades.Where(t => t.InOlder).ToArray(),
            "recent90d" => trades.Where(t => t.InRecent90d).ToArray(),
            "trainReference" => trades.Where(t => t.InTrainReference).ToArray(),
            "holdout30d" => trades.Where(t => t.InHoldout30d).ToArray(),
            _ => trades
        };

    private static Func<RegimeDriftDiagnosticTrade, bool>? BuildPredicate(string description)
    {
        if (description.Contains("Insufficient", StringComparison.OrdinalIgnoreCase))
            return null;

        var parts = description.Split(" AND ", StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return null;

        var checks = new List<Func<RegimeDriftDiagnosticTrade, bool>>();
        foreach (var part in parts)
        {
            if (part.Contains('='))
            {
                var split = part.Split('=', 2);
                var feature = split[0].Trim();
                var value = split[1].Trim();
                checks.Add(t => string.Equals(GetCategorical(t, feature), value, StringComparison.OrdinalIgnoreCase));
                continue;
            }

            var open = part.IndexOf('[');
            var close = part.IndexOf(']');
            if (open < 0 || close < 0)
                return null;
            var left = part[..open].Trim();
            var featureName = left.Contains(' ', StringComparison.Ordinal)
                ? left[..left.LastIndexOf(' ')].Trim()
                : left;
            var bounds = part[(open + 1)..close].Split(',', 2);
            if (!decimal.TryParse(bounds[0], out var min) || !decimal.TryParse(bounds[1], out var max))
                return null;
            checks.Add(t =>
            {
                var value = GetNumeric(t, featureName);
                return value.HasValue && value.Value >= min && value.Value <= max;
            });
        }

        return t => checks.All(c => c(t));
    }

    private static bool IsCategorical(string feature)
        => feature is nameof(RegimeDriftDiagnosticTrade.VolatilityRegime)
            or nameof(RegimeDriftDiagnosticTrade.SessionBucket)
            or nameof(RegimeDriftDiagnosticTrade.BtcTrendRegime);

    private static decimal? GetNumeric(RegimeDriftDiagnosticTrade trade, string feature)
        => feature switch
        {
            nameof(RegimeDriftDiagnosticTrade.TrendSlopePercent) => trade.TrendSlopePercent,
            nameof(RegimeDriftDiagnosticTrade.AtrPercent) => trade.AtrPercent,
            nameof(RegimeDriftDiagnosticTrade.RangeWidthPercent) => trade.RangeWidthPercent,
            nameof(RegimeDriftDiagnosticTrade.DistanceFromRecentHighPercent) => trade.DistanceFromRecentHighPercent,
            nameof(RegimeDriftDiagnosticTrade.DistanceFromRecentLowPercent) => trade.DistanceFromRecentLowPercent,
            nameof(RegimeDriftDiagnosticTrade.BtcReturn30mPercent) => trade.BtcReturn30mPercent,
            nameof(RegimeDriftDiagnosticTrade.BtcReturn60mPercent) => trade.BtcReturn60mPercent,
            _ => null
        };

    private static string GetCategorical(RegimeDriftDiagnosticTrade trade, string feature)
        => feature switch
        {
            nameof(RegimeDriftDiagnosticTrade.VolatilityRegime) => trade.VolatilityRegime,
            nameof(RegimeDriftDiagnosticTrade.SessionBucket) => trade.SessionBucket,
            nameof(RegimeDriftDiagnosticTrade.BtcTrendRegime) => trade.BtcTrendRegime ?? "Unknown",
            _ => "Unknown"
        };

    private static string ClassifyPeriodVerdict(decimal net, bool sufficient, string label)
    {
        if (!sufficient && label is "older" or "trainReference")
            return "SparseButInformative";
        if (net >= 0m && sufficient)
            return "Positive";
        if (net >= 0m)
            return "PositiveSmallSample";
        if (label is "30d" or "60d" or "90d" && net >= 0m)
            return "RecentPositive";
        return label is "older" or "trainReference" ? "OlderNegative" : "Negative";
    }

    private static string ClassifyOutcomeVerdict(RegimeDriftDiagnosticTrade[] trainFiltered, RegimeDriftDiagnosticTrade[] testFiltered)
    {
        var trainNet = trainFiltered.Sum(t => t.NetPnlQuote);
        var testNet = testFiltered.Sum(t => t.NetPnlQuote);
        if (trainFiltered.Length < DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.MinimumRuleTestSamples)
            return "SparseFiltered";
        if (trainNet >= 0m && testNet >= 0m)
            return "DualPeriodFilter";
        if (trainNet >= 0m && testNet < 0m)
            return "RecentOverfitFilter";
        return "FilterInsufficient";
    }

    private sealed record QuantileBucket(string Label, decimal Min, decimal Max);

    private static List<QuantileBucket> BuildQuantileBuckets(decimal[] values, int bucketCount)
    {
        if (values.Length < bucketCount * 5)
            return [];
        var sorted = values.OrderBy(v => v).ToArray();
        var buckets = new List<QuantileBucket>();
        for (var i = 0; i < bucketCount; i++)
        {
            var startIdx = (int)Math.Floor((decimal)i / bucketCount * sorted.Length);
            var endIdx = (int)Math.Floor((decimal)(i + 1) / bucketCount * sorted.Length) - 1;
            endIdx = Math.Clamp(endIdx, startIdx, sorted.Length - 1);
            buckets.Add(new QuantileBucket($"Q{i + 1}[{sorted[startIdx]:F4},{sorted[endIdx]:F4}]", sorted[startIdx], sorted[endIdx]));
        }

        return buckets;
    }

    private static bool InBucket(decimal? value, QuantileBucket bucket)
        => value.HasValue && value.Value >= bucket.Min && value.Value <= bucket.Max;

    private static decimal? Average(IEnumerable<decimal?> values)
    {
        var arr = values.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        return arr.Length == 0 ? null : Math.Round(arr.Average(), 6);
    }

    private static decimal? Median(IEnumerable<decimal?> values)
    {
        var arr = values.Where(v => v.HasValue).Select(v => v!.Value).OrderBy(v => v).ToArray();
        if (arr.Length == 0)
            return null;
        var mid = arr.Length / 2;
        return arr.Length % 2 == 0
            ? Math.Round((arr[mid - 1] + arr[mid]) / 2m, 8)
            : Math.Round(arr[mid], 8);
    }

    private static string Mode(IEnumerable<string> values)
    {
        var groups = values.Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        return groups?.Key ?? "Unknown";
    }
}
