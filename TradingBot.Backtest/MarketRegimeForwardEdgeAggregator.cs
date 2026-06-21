using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class MarketRegimeForwardEdgeAggregator
{
    public const int MinimumBroadRegimeSamples = 200;
    public const int MinimumTargetBeforeStopEvents = 50;
    public const decimal NearBreakevenNetThreshold = -0.05m;

    private static readonly (string FeatureA, string FeatureB)[] EntryTimeRuleFeaturePairs =
    [
        (nameof(MarketRegimeForwardEdgeObservation.TrendSlopePercent), nameof(MarketRegimeForwardEdgeObservation.AtrPercent)),
        (nameof(MarketRegimeForwardEdgeObservation.RangeWidthPercent), nameof(MarketRegimeForwardEdgeObservation.DistanceFromRecentLowPercent)),
        (nameof(MarketRegimeForwardEdgeObservation.VolumeExpansionRatio), nameof(MarketRegimeForwardEdgeObservation.TrendStrengthPercent)),
        (nameof(MarketRegimeForwardEdgeObservation.DistanceFromRecentHighPercent), nameof(MarketRegimeForwardEdgeObservation.AtrPercent)),
        (nameof(MarketRegimeForwardEdgeObservation.BtcReturn60mPercent), nameof(MarketRegimeForwardEdgeObservation.TrendSlopePercent)),
        (nameof(MarketRegimeForwardEdgeObservation.MarketWideReturnProxyPercent), nameof(MarketRegimeForwardEdgeObservation.VolatilityRegime))
    ];

    private static readonly (string FeatureA, string FeatureB)[] BtcContextEntryTimeRuleFeaturePairs =
    [
        (nameof(MarketRegimeForwardEdgeObservation.BtcReturn30mPercent), nameof(MarketRegimeForwardEdgeObservation.VolatilityRegime)),
        (nameof(MarketRegimeForwardEdgeObservation.SymbolReturnRelativeToBtc60mPercent), nameof(MarketRegimeForwardEdgeObservation.DistanceFromRecentLowPercent)),
        (nameof(MarketRegimeForwardEdgeObservation.BtcReturn60mPercent), nameof(MarketRegimeForwardEdgeObservation.RangeWidthPercent)),
        (nameof(MarketRegimeForwardEdgeObservation.BtcTrendSlopePercent), nameof(MarketRegimeForwardEdgeObservation.AtrPercent))
    ];

    private static readonly (string FeatureA, string FeatureB)[] BtcContextCategoricalRulePairs =
    [
        (nameof(MarketRegimeForwardEdgeObservation.BtcMarketDirectionBucket), nameof(MarketRegimeForwardEdgeObservation.VolatilityRegime)),
        (nameof(MarketRegimeForwardEdgeObservation.BtcTrendRegime), nameof(MarketRegimeForwardEdgeObservation.VolatilityRegime)),
        (nameof(MarketRegimeForwardEdgeObservation.BtcVolatilityRegime), nameof(MarketRegimeForwardEdgeObservation.TrendRegime)),
        ("BtcAboveMediumMa", nameof(MarketRegimeForwardEdgeObservation.RangeWidthPercent))
    ];

    public static IReadOnlyList<MarketRegimeForwardEdgeSummaryRow> BuildSummary(
        IReadOnlyList<MarketRegimeForwardEdgeObservation> observations)
    {
        return observations
            .GroupBy(o => $"{o.WindowLabel}|{o.Symbol}|{o.Interval}", StringComparer.OrdinalIgnoreCase)
            .Select(g => BuildSummaryRow(g.Key, g.ToArray()))
            .OrderByDescending(r => r.LongEdgeScore)
            .ThenBy(r => r.Symbol)
            .ThenBy(r => r.Interval, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<SymbolIntervalEdgeRankingRow> BuildSymbolIntervalRanking(
        IReadOnlyList<MarketRegimeForwardEdgeSummaryRow> summary)
    {
        return summary
            .Select((row, index) => new SymbolIntervalEdgeRankingRow
            {
                WindowLabel = row.WindowLabel,
                Symbol = row.Symbol,
                Interval = row.Interval,
                SampleCount = row.SampleCount,
                MedianForwardMfePercent = row.MedianForwardMfePercent,
                MedianForwardMaePercent = row.MedianForwardMaePercent,
                MedianExpectedNetAfterCostPercent = row.MedianExpectedNetAfterCostPercent,
                Target050BeforeStop050Rate = row.Target050BeforeStop050Rate,
                LongEdgeScore = row.LongEdgeScore,
                Rank = index + 1,
                Verdict = row.Verdict
            })
            .ToArray();
    }

    public static IReadOnlyList<RegimeBucketEdgeRankingRow> BuildRegimeBucketRanking(
        IReadOnlyList<MarketRegimeForwardEdgeObservation> observations)
    {
        var rows = new List<RegimeBucketEdgeRankingRow>();
        rows.AddRange(BuildCategoricalBucketRows(observations, "VolatilityRegime", o => o.VolatilityRegime));
        rows.AddRange(BuildCategoricalBucketRows(observations, "TrendRegime", o => o.TrendRegime));
        rows.AddRange(BuildCategoricalBucketRows(observations, "SessionBucket", o => o.SessionBucket));
        rows.AddRange(BuildCategoricalBucketRows(observations, "BtcTrendRegime", o => o.BtcTrendRegime ?? "NoBtc"));
        rows.AddRange(BuildCategoricalBucketRows(observations, "BtcVolatilityRegime", o => o.BtcVolatilityRegime ?? "NoBtc"));
        rows.AddRange(BuildCategoricalBucketRows(observations, "BtcMarketDirectionBucket", o => o.BtcMarketDirectionBucket ?? "NoBtc"));
        rows.AddRange(BuildCategoricalBucketRows(observations, "BtcAboveMediumMa", o => o.BtcAboveMediumMa?.ToString() ?? "NoBtc"));
        rows.AddRange(BuildCategoricalBucketRows(observations, "MarketWideDirection", o => o.MarketWideDirection ?? "Unknown"));
        rows.AddRange(BuildQuantileBucketRows(observations, "RangeWidthPercent", o => o.RangeWidthPercent));
        rows.AddRange(BuildQuantileBucketRows(observations, "DistanceFromRecentLowPercent", o => o.DistanceFromRecentLowPercent));
        rows.AddRange(BuildQuantileBucketRows(observations, "VolumeExpansionRatio", o => o.VolumeExpansionRatio));
        rows.AddRange(BuildQuantileBucketRows(observations, "AtrPercent", o => o.AtrPercent));

        return rows
            .Where(r => r.SampleCount >= MinimumBroadRegimeSamples / 4)
            .OrderByDescending(r => r.LongEdgeScore)
            .Select((row, index) => row with { Rank = index + 1 })
            .ToArray();
    }

    public static IReadOnlyList<SessionEdgeRankingRow> BuildSessionRanking(
        IReadOnlyList<MarketRegimeForwardEdgeObservation> observations)
    {
        return observations
            .GroupBy(o => $"{o.WindowLabel}|{o.Symbol}|{o.Interval}|{o.SessionBucket}|{o.HourOfDayUtc}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var samples = g.ToArray();
                return new SessionEdgeRankingRow
                {
                    WindowLabel = first.WindowLabel,
                    SessionBucket = first.SessionBucket,
                    HourOfDayUtc = first.HourOfDayUtc,
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    SampleCount = samples.Length,
                    MedianExpectedNetAfterCostPercent = Median(samples.Select(x => (decimal?)x.ExpectedNetAfterCostPercent)) ?? 0m,
                    Target050BeforeStop050Rate = Rate(samples, x => x.Target050BeforeStop050),
                    LongEdgeScore = Math.Round(samples.Average(x => x.LongEdgeScore), 6),
                    Verdict = ClassifyVerdict(samples.Length, Median(samples.Select(x => (decimal?)x.ExpectedNetAfterCostPercent)), Rate(samples, x => x.Target050BeforeStop050))
                };
            })
            .Where(r => r.SampleCount >= 50)
            .OrderByDescending(r => r.LongEdgeScore)
            .Select((row, index) => row with { Rank = index + 1 })
            .ToArray();
    }

    public static IReadOnlyList<BtcContextEdgeRankingRow> BuildBtcContextRanking(
        IReadOnlyList<MarketRegimeForwardEdgeObservation> observations)
    {
        var rows = new List<BtcContextEdgeRankingRow>();
        rows.AddRange(BuildBtcContextCategoricalRows(observations, "BtcTrendRegime", o => o.BtcTrendRegime ?? "NoBtc"));
        rows.AddRange(BuildBtcContextCategoricalRows(observations, "BtcVolatilityRegime", o => o.BtcVolatilityRegime ?? "NoBtc"));
        rows.AddRange(BuildBtcContextCategoricalRows(observations, "BtcMarketDirectionBucket", o => o.BtcMarketDirectionBucket ?? "NoBtc"));
        rows.AddRange(BuildBtcContextCategoricalRows(observations, "BtcAboveMediumMa", o => o.BtcAboveMediumMa?.ToString() ?? "NoBtc"));
        rows.AddRange(BuildBtcContextQuantileRows(observations, "BtcReturn30mPercent", o => o.BtcReturn30mPercent));
        rows.AddRange(BuildBtcContextQuantileRows(observations, "SymbolReturnRelativeToBtc60mPercent", o => o.SymbolReturnRelativeToBtc60mPercent));

        return rows
            .Where(r => r.SampleCount >= MinimumBroadRegimeSamples / 4)
            .OrderByDescending(r => r.LongEdgeScore)
            .Select((row, index) => row with { Rank = index + 1 })
            .ToArray();
    }

    public static IReadOnlyList<MarketRegimeEntryTimeRuleRow> BuildEntryTimeRules(
        IReadOnlyList<MarketRegimeForwardEdgeObservation> observations,
        bool includeBtcContextRules = false)
    {
        if (observations.Count < MinimumBroadRegimeSamples)
            return [];

        var trainWindows = new HashSet<string>(["30d", "60d"], StringComparer.OrdinalIgnoreCase);
        var holdoutWindow = "90d";
        var train = observations.Where(o => trainWindows.Contains(o.WindowLabel)).ToArray();
        var holdout = observations.Where(o => string.Equals(o.WindowLabel, holdoutWindow, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (train.Length < MinimumBroadRegimeSamples || holdout.Length < MinimumBroadRegimeSamples / 3)
            return [];

        var rules = new List<MarketRegimeEntryTimeRuleRow>();
        var featurePairs = includeBtcContextRules
            ? EntryTimeRuleFeaturePairs.Concat(BtcContextEntryTimeRuleFeaturePairs).ToArray()
            : EntryTimeRuleFeaturePairs;

        foreach (var (featureA, featureB) in featurePairs)
        {
            if (featureB is nameof(MarketRegimeForwardEdgeObservation.VolatilityRegime))
            {
                rules.AddRange(BuildCategoricalRules(train, holdout, featureA, featureB, o => o.VolatilityRegime));
                continue;
            }

            var bucketsA = BuildQuantileBuckets(train.Select(o => GetNumericFeature(o, featureA)).Where(v => v.HasValue).Select(v => v!.Value).ToArray(), 3);
            var bucketsB = BuildQuantileBuckets(train.Select(o => GetNumericFeature(o, featureB)).Where(v => v.HasValue).Select(v => v!.Value).ToArray(), 3);
            if (bucketsA.Count == 0 || bucketsB.Count == 0)
                continue;

            MarketRegimeEntryTimeRuleRow? best = null;
            foreach (var bucketA in bucketsA)
            {
                foreach (var bucketB in bucketsB)
                {
                    var trainSubset = train.Where(o =>
                        InBucket(GetNumericFeature(o, featureA), bucketA)
                        && InBucket(GetNumericFeature(o, featureB), bucketB)).ToArray();
                    if (trainSubset.Length < MinimumBroadRegimeSamples)
                        continue;

                    var holdoutSubset = holdout.Where(o =>
                        InBucket(GetNumericFeature(o, featureA), bucketA)
                        && InBucket(GetNumericFeature(o, featureB), bucketB)).ToArray();
                    if (holdoutSubset.Length < MinimumBroadRegimeSamples / 4)
                        continue;

                    var trainEvents = trainSubset.Count(x => x.Target050BeforeStop050 || !x.Target050BeforeStop050);
                    var holdoutEvents = holdoutSubset.Count(x => x.Target050BeforeStop050 || !x.Target050BeforeStop050);
                    if (trainSubset.Count(x => x.Target050BeforeStop050) < MinimumTargetBeforeStopEvents / 2)
                        continue;

                    var row = BuildRuleRow(
                        $"{featureA} {bucketA.Label} AND {featureB} {bucketB.Label}",
                        [featureA, featureB],
                        trainSubset,
                        holdoutSubset,
                        trainEvents,
                        holdoutEvents);

                    if (best is null || row.HoldoutMedianExpectedNetPercent > best.HoldoutMedianExpectedNetPercent)
                        best = row;
                }
            }

            if (best is not null)
                rules.Add(best);
        }

        if (includeBtcContextRules)
        {
            foreach (var (featureA, featureB) in BtcContextCategoricalRulePairs)
            {
                if (featureB is nameof(MarketRegimeForwardEdgeObservation.VolatilityRegime)
                    && featureA is nameof(MarketRegimeForwardEdgeObservation.BtcMarketDirectionBucket)
                        or nameof(MarketRegimeForwardEdgeObservation.BtcTrendRegime)
                        or nameof(MarketRegimeForwardEdgeObservation.BtcVolatilityRegime))
                {
                    rules.AddRange(BuildTwoCategoricalRules(
                        train, holdout, featureA, featureB,
                        ResolveCategoricalFeature(featureA),
                        ResolveCategoricalFeature(featureB)));
                    continue;
                }

                if (featureB is nameof(MarketRegimeForwardEdgeObservation.TrendRegime)
                    && featureA is nameof(MarketRegimeForwardEdgeObservation.BtcVolatilityRegime))
                {
                    rules.AddRange(BuildTwoCategoricalRules(
                        train, holdout, featureA, featureB,
                        ResolveCategoricalFeature(featureA),
                        ResolveCategoricalFeature(featureB)));
                    continue;
                }

                if (featureA == "BtcAboveMediumMa")
                {
                    var bucketsA = BuildQuantileBuckets(train.Select(o => GetNumericFeature(o, featureB)).Where(v => v.HasValue).Select(v => v!.Value).ToArray(), 3);
                    if (bucketsA.Count == 0)
                        continue;

                    MarketRegimeEntryTimeRuleRow? best = null;
                    foreach (var bucketA in bucketsA)
                    {
                        foreach (var category in new[] { "True", "False" })
                        {
                            var trainSubset = train.Where(o =>
                                string.Equals(ResolveCategoricalFeature(featureA)(o), category, StringComparison.OrdinalIgnoreCase)
                                && InBucket(GetNumericFeature(o, featureB), bucketA)).ToArray();
                            if (trainSubset.Length < MinimumBroadRegimeSamples)
                                continue;

                            var holdoutSubset = holdout.Where(o =>
                                string.Equals(ResolveCategoricalFeature(featureA)(o), category, StringComparison.OrdinalIgnoreCase)
                                && InBucket(GetNumericFeature(o, featureB), bucketA)).ToArray();
                            if (holdoutSubset.Length < MinimumBroadRegimeSamples / 4)
                                continue;

                            var row = BuildRuleRow(
                                $"{featureA}={category} AND {featureB} {bucketA.Label}",
                                [featureA, featureB],
                                trainSubset,
                                holdoutSubset,
                                trainSubset.Length,
                                holdoutSubset.Length);
                            if (best is null || row.HoldoutMedianExpectedNetPercent > best.HoldoutMedianExpectedNetPercent)
                                best = row;
                        }
                    }

                    if (best is not null)
                        rules.Add(best);
                }
            }
        }

        return rules
            .OrderByDescending(r => r.HoldoutMedianExpectedNetPercent)
            .ThenByDescending(r => r.HoldoutTargetBeforeStopRate)
            .ToArray();
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildResearchAnswers(
        IReadOnlyList<MarketRegimeForwardEdgeObservation> observations,
        IReadOnlyList<SymbolIntervalEdgeRankingRow> symbolIntervalRanking,
        IReadOnlyList<RegimeBucketEdgeRankingRow> regimeBucketRanking,
        IReadOnlyList<SessionEdgeRankingRow> sessionRanking,
        IReadOnlyList<TargetBeforeStopMatrixRow> targetBeforeStopMatrix,
        IReadOnlyList<MarketRegimeEntryTimeRuleRow> entryTimeRules,
        decimal roundTripCostPercent,
        IReadOnlyList<BtcContextEdgeRankingRow>? btcContextRanking = null,
        bool btcContextEnabled = false)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var bestSymbolInterval = symbolIntervalRanking.FirstOrDefault();
        var positiveSymbolIntervals = symbolIntervalRanking
            .Where(r => r.SampleCount >= MinimumBroadRegimeSamples && r.MedianExpectedNetAfterCostPercent >= 0m)
            .ToArray();
        var nearBreakevenSymbolIntervals = symbolIntervalRanking
            .Where(r => r.SampleCount >= MinimumBroadRegimeSamples && r.MedianExpectedNetAfterCostPercent >= NearBreakevenNetThreshold)
            .ToArray();
        var positiveRegimes = regimeBucketRanking
            .Where(r => r.SampleCount >= MinimumBroadRegimeSamples && r.MedianExpectedNetAfterCostPercent >= 0m)
            .ToArray();
        var holdoutRules = entryTimeRules
            .Where(r => r.HoldoutMedianExpectedNetPercent >= 0m && r.HoldoutSamples >= MinimumBroadRegimeSamples / 4)
            .ToArray();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which symbol/interval has the best forward edge before strategy rules?",
            Answer = bestSymbolInterval is null
                ? "No observations collected."
                : $"Best: {bestSymbolInterval.Symbol}/{bestSymbolInterval.Interval} ({bestSymbolInterval.WindowLabel}) median expected net={bestSymbolInterval.MedianExpectedNetAfterCostPercent:F4}%, target-before-stop rate={bestSymbolInterval.Target050BeforeStop050Rate:P1}, samples={bestSymbolInterval.SampleCount}.",
            Verdict = positiveSymbolIntervals.Length > 0 ? "PositiveSymbolIntervalFound" : "NoPositiveSymbolInterval",
            Details = new Dictionary<string, object?> { ["top10"] = symbolIntervalRanking.Take(10).ToArray() }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are any regimes positive after conservative Spot costs?",
            Answer = positiveRegimes.Length > 0
                ? $"{positiveRegimes.Length} regime bucket(s) have median expected net >= 0 after ~{roundTripCostPercent:F2}% round-trip cost."
                : nearBreakevenSymbolIntervals.Length > 0
                    ? $"No positive regime buckets; {nearBreakevenSymbolIntervals.Length} symbol/interval cohort(s) near breakeven (>={NearBreakevenNetThreshold:F2}% median net)."
                    : $"No regime bucket or symbol/interval cohort met >= {MinimumBroadRegimeSamples} samples with non-negative expected net.",
            Verdict = positiveRegimes.Length > 0 ? "PositiveRegimeFound" : nearBreakevenSymbolIntervals.Length > 0 ? "NearBreakevenOnly" : "NoPositiveRegime",
            Details = new Dictionary<string, object?> { ["positiveRegimes"] = positiveRegimes.Take(10).ToArray() }
        });

        var bestSession = sessionRanking.FirstOrDefault();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are there times/sessions where long trades are structurally better?",
            Answer = bestSession is null
                ? "No session cohort met minimum sample threshold."
                : $"Best session slice: {bestSession.Symbol}/{bestSession.Interval} {bestSession.SessionBucket} hour={bestSession.HourOfDayUtc} median net={bestSession.MedianExpectedNetAfterCostPercent:F4}%, target-before-stop={bestSession.Target050BeforeStop050Rate:P1}.",
            Verdict = sessionRanking.Any(s => s.SampleCount >= MinimumBroadRegimeSamples && s.MedianExpectedNetAfterCostPercent >= 0m)
                ? "PositiveSessionFound"
                : sessionRanking.Any(s => s.MedianExpectedNetAfterCostPercent >= NearBreakevenNetThreshold)
                    ? "SessionEdgeMarginal"
                    : "NoSessionEdge",
            Details = new Dictionary<string, object?> { ["topSessions"] = sessionRanking.Take(10).ToArray() }
        });

        var btcBuckets = regimeBucketRanking.Where(r => r.BucketType == "BtcTrendRegime").OrderByDescending(r => r.LongEdgeScore).ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does BTC context improve symbol edge?",
            Answer = btcBuckets.Length == 0
                ? "BTC data unavailable or no BTC-context buckets met sample thresholds."
                : string.Join("; ", btcBuckets.Take(4).Select(b => $"{b.BucketLabel}: median net={b.MedianExpectedNetAfterCostPercent:F4}%, rate={b.Target050BeforeStop050Rate:P1}")),
            Verdict = btcBuckets.Any(b => b.MedianExpectedNetAfterCostPercent >= 0m && b.SampleCount >= MinimumBroadRegimeSamples / 2)
                ? "BtcContextHelps"
                : btcBuckets.Length > 0 ? "BtcContextWeak" : "NoBtcData",
            Details = new Dictionary<string, object?> { ["btcBuckets"] = btcBuckets }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are previous strategies failing because they trade bad regimes?",
            Answer = $"Across unconditional long entries, median expected net={Median(observations.Select(o => (decimal?)o.ExpectedNetAfterCostPercent)):F4}%. Best regime bucket score={regimeBucketRanking.FirstOrDefault()?.LongEdgeScore:F4}. Prior families were stop-loss dominated even when selective; raw forward edge is {(positiveRegimes.Length > 0 ? "positive in some buckets" : "negative or marginal in most buckets")}.",
            Verdict = positiveRegimes.Length > 0 ? "RegimeSelectionMayHelp" : "NoRegimeRescuesPriorFamilies",
            Details = new Dictionary<string, object?> { ["topRegimes"] = regimeBucketRanking.Take(10).ToArray() }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is there any evidence for Spot long-only edge at all?",
            Answer = $"Observations={observations.Count}, positive symbol/interval cohorts={positiveSymbolIntervals.Length}, positive regime buckets={positiveRegimes.Length}, holdout-surviving entry-time rules={holdoutRules.Length}, matrix 0.50%/-0.50% best rate={targetBeforeStopMatrix.MaxBy(m => m.TargetBeforeStopRate)?.TargetBeforeStopRate:P1}.",
            Verdict = holdoutRules.Length > 0 || positiveRegimes.Any(r => r.SampleCount >= MinimumBroadRegimeSamples)
                ? "LimitedSpotLongEdge"
                : nearBreakevenSymbolIntervals.Length > 0
                    ? "MarginalSpotLongEdge"
                    : "NoSpotLongEdge",
            Details = new Dictionary<string, object?>
            {
                ["entryTimeRules"] = entryTimeRules.Take(10).ToArray(),
                ["targetBeforeStopMatrix"] = targetBeforeStopMatrix.Take(12).ToArray()
            }
        });

        if (btcContextEnabled)
        {
            var positiveBtcBuckets = (btcContextRanking ?? [])
                .Where(r => r.SampleCount >= MinimumBroadRegimeSamples / 4 && r.MedianExpectedNetAfterCostPercent >= 0m)
                .ToArray();
            var btcHoldoutRules = entryTimeRules
                .Where(r => r.FeaturesUsed.Any(f => f.Contains("Btc", StringComparison.OrdinalIgnoreCase))
                    && r.HoldoutMedianExpectedNetPercent >= 0m)
                .ToArray();
            answers.Add(new ReachabilityResearchAnswer
            {
                Question = "Does BTC context improve holdout-positive entry-time rules?",
                Answer = btcHoldoutRules.Length > 0
                    ? $"{btcHoldoutRules.Length} BTC-context entry-time rule(s) non-negative on 90d holdout; best holdout median={btcHoldoutRules.Max(r => r.HoldoutMedianExpectedNetPercent):F4}%."
                    : $"No BTC-context entry-time rules survived holdout with median expected net >= 0 ({entryTimeRules.Count(r => r.FeaturesUsed.Any(f => f.Contains("Btc", StringComparison.OrdinalIgnoreCase)))} BTC-feature rules tested).",
                Verdict = btcHoldoutRules.Length > 0 ? "BtcContextImprovesRules" : "BtcContextDoesNotImproveRules",
                Details = new Dictionary<string, object?> { ["btcHoldoutRules"] = btcHoldoutRules.Take(10).ToArray() }
            });
            answers.Add(new ReachabilityResearchAnswer
            {
                Question = "Which BTC context buckets improve symbol edge?",
                Answer = positiveBtcBuckets.Length > 0
                    ? string.Join("; ", positiveBtcBuckets.Take(5).Select(b => $"{b.Symbol}/{b.Interval} {b.BtcContextBucketType}={b.BtcContextBucketLabel}: median net={b.MedianExpectedNetAfterCostPercent:F4}%"))
                    : "No BTC context bucket met sample threshold with non-negative median expected net.",
                Verdict = positiveBtcBuckets.Length > 0 ? "PositiveBtcContextBucketFound" : "NoPositiveBtcContextBucket",
                Details = new Dictionary<string, object?> { ["topBtcBuckets"] = (btcContextRanking ?? []).Take(15).ToArray() }
            });
        }

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Should next strategy be trend, mean-reversion, breakout, or should Spot research be paused?",
            Answer = holdoutRules.Length > 0
                ? $"Entry-time rules with non-negative 90d holdout suggest targeted {InferStyle(holdoutRules)} research on robust regime slices only."
                : positiveRegimes.Length == 0
                    ? "No holdout-surviving regime edge under conservative Spot costs. Recommend pausing Spot long-only strategy research or pivoting to lower-fee/Futures-sim/data-feature expansion."
                    : $"Weak positive regime buckets only ({positiveRegimes.Length}); selective {InferStyleFromRegimes(positiveRegimes)} research may be warranted but not live deployment.",
            Verdict = holdoutRules.Length > 0
                ? "SelectiveNextFamily"
                : positiveRegimes.Length == 0 && positiveSymbolIntervals.Length == 0
                    ? "PauseSpotResearch"
                    : "CautiousContinuation",
            Details = new Dictionary<string, object?>
            {
                ["holdoutRules"] = holdoutRules,
                ["positiveRegimes"] = positiveRegimes.Take(5).ToArray()
            }
        });

        return answers;
    }

    private static IEnumerable<RegimeBucketEdgeRankingRow> BuildCategoricalBucketRows(
        IReadOnlyList<MarketRegimeForwardEdgeObservation> observations,
        string bucketType,
        Func<MarketRegimeForwardEdgeObservation, string> selector)
    {
        return observations
            .GroupBy(o => $"{o.WindowLabel}|{o.Symbol}|{o.Interval}|{selector(o)}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var samples = g.ToArray();
                return new RegimeBucketEdgeRankingRow
                {
                    WindowLabel = first.WindowLabel,
                    BucketType = bucketType,
                    BucketLabel = selector(first),
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    SampleCount = samples.Length,
                    MedianForwardMfePercent = Median(samples.Select(x => x.ForwardMfePercent)) ?? 0m,
                    MedianForwardMaePercent = Median(samples.Select(x => x.ForwardMaePercent)) ?? 0m,
                    MedianExpectedNetAfterCostPercent = Median(samples.Select(x => (decimal?)x.ExpectedNetAfterCostPercent)) ?? 0m,
                    Target050BeforeStop050Rate = Rate(samples, x => x.Target050BeforeStop050),
                    LongEdgeScore = Math.Round(samples.Average(x => x.LongEdgeScore), 6),
                    Verdict = ClassifyVerdict(samples.Length, Median(samples.Select(x => (decimal?)x.ExpectedNetAfterCostPercent)), Rate(samples, x => x.Target050BeforeStop050))
                };
            });
    }

    private static IEnumerable<RegimeBucketEdgeRankingRow> BuildQuantileBucketRows(
        IReadOnlyList<MarketRegimeForwardEdgeObservation> observations,
        string featureName,
        Func<MarketRegimeForwardEdgeObservation, decimal> selector)
    {
        foreach (var windowGroup in observations.GroupBy(o => $"{o.WindowLabel}|{o.Symbol}|{o.Interval}", StringComparer.OrdinalIgnoreCase))
        {
            var samples = windowGroup.ToArray();
            var buckets = BuildQuantileBuckets(samples.Select(selector).ToArray(), 4);
            for (var i = 0; i < buckets.Count; i++)
            {
                var bucket = buckets[i];
                var bucketRows = samples
                    .Where(o =>
                    {
                        var value = selector(o);
                        return value >= bucket.Min && (i == buckets.Count - 1 ? value <= bucket.Max : value < bucket.Max);
                    })
                    .ToArray();
                if (bucketRows.Length == 0)
                    continue;

                var first = bucketRows[0];
                yield return new RegimeBucketEdgeRankingRow
                {
                    WindowLabel = first.WindowLabel,
                    BucketType = featureName,
                    BucketLabel = bucket.Label,
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    SampleCount = bucketRows.Length,
                    MedianForwardMfePercent = Median(bucketRows.Select(x => x.ForwardMfePercent)) ?? 0m,
                    MedianForwardMaePercent = Median(bucketRows.Select(x => x.ForwardMaePercent)) ?? 0m,
                    MedianExpectedNetAfterCostPercent = Median(bucketRows.Select(x => (decimal?)x.ExpectedNetAfterCostPercent)) ?? 0m,
                    Target050BeforeStop050Rate = Rate(bucketRows, x => x.Target050BeforeStop050),
                    LongEdgeScore = Math.Round(bucketRows.Average(x => x.LongEdgeScore), 6),
                    Verdict = ClassifyVerdict(bucketRows.Length, Median(bucketRows.Select(x => (decimal?)x.ExpectedNetAfterCostPercent)), Rate(bucketRows, x => x.Target050BeforeStop050))
                };
            }
        }
    }

    private static IEnumerable<MarketRegimeEntryTimeRuleRow> BuildTwoCategoricalRules(
        MarketRegimeForwardEdgeObservation[] train,
        MarketRegimeForwardEdgeObservation[] holdout,
        string featureA,
        string featureB,
        Func<MarketRegimeForwardEdgeObservation, string> selectorA,
        Func<MarketRegimeForwardEdgeObservation, string> selectorB)
    {
        var rules = new List<MarketRegimeEntryTimeRuleRow>();
        foreach (var categoryA in train.Select(selectorA).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var categoryB in train.Select(selectorB).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var trainSubset = train.Where(o =>
                    string.Equals(selectorA(o), categoryA, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(selectorB(o), categoryB, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (trainSubset.Length < MinimumBroadRegimeSamples)
                    continue;

                var holdoutSubset = holdout.Where(o =>
                    string.Equals(selectorA(o), categoryA, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(selectorB(o), categoryB, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (holdoutSubset.Length < MinimumBroadRegimeSamples / 4)
                    continue;

                if (trainSubset.Count(x => x.Target050BeforeStop050) < MinimumTargetBeforeStopEvents / 2)
                    continue;

                rules.Add(BuildRuleRow(
                    $"{featureA}={categoryA} AND {featureB}={categoryB}",
                    [featureA, featureB],
                    trainSubset,
                    holdoutSubset,
                    trainSubset.Length,
                    holdoutSubset.Length));
            }
        }

        return rules;
    }

    private static IEnumerable<MarketRegimeEntryTimeRuleRow> BuildCategoricalRules(
        MarketRegimeForwardEdgeObservation[] train,
        MarketRegimeForwardEdgeObservation[] holdout,
        string featureA,
        string featureB,
        Func<MarketRegimeForwardEdgeObservation, string> categoricalSelector)
    {
        var rules = new List<MarketRegimeEntryTimeRuleRow>();
        foreach (var category in train.Select(categoricalSelector).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var trainSubset = train.Where(o => string.Equals(categoricalSelector(o), category, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (trainSubset.Length < MinimumBroadRegimeSamples)
                continue;

            var holdoutSubset = holdout.Where(o => string.Equals(categoricalSelector(o), category, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (holdoutSubset.Length < MinimumBroadRegimeSamples / 4)
                continue;

            if (trainSubset.Count(x => x.Target050BeforeStop050) < MinimumTargetBeforeStopEvents / 2)
                continue;

            rules.Add(BuildRuleRow(
                $"{featureA} in train buckets AND {featureB}={category}",
                [featureA, featureB],
                trainSubset,
                holdoutSubset,
                trainSubset.Length,
                holdoutSubset.Length));
        }

        return rules;
    }

    private static MarketRegimeEntryTimeRuleRow BuildRuleRow(
        string description,
        string[] features,
        MarketRegimeForwardEdgeObservation[] trainSubset,
        MarketRegimeForwardEdgeObservation[] holdoutSubset,
        int trainEvents,
        int holdoutEvents)
    {
        var trainMedian = Median(trainSubset.Select(x => (decimal?)x.ExpectedNetAfterCostPercent)) ?? 0m;
        var holdoutMedian = Median(holdoutSubset.Select(x => (decimal?)x.ExpectedNetAfterCostPercent)) ?? 0m;
        var trainRate = Rate(trainSubset, x => x.Target050BeforeStop050);
        var holdoutRate = Rate(holdoutSubset, x => x.Target050BeforeStop050);
        return new MarketRegimeEntryTimeRuleRow
        {
            RuleDescription = description,
            FeaturesUsed = features,
            TrainWindows = "30d,60d",
            HoldoutWindow = "90d",
            TrainSamples = trainSubset.Length,
            HoldoutSamples = holdoutSubset.Length,
            TrainTargetBeforeStopEvents = trainSubset.Count(x => x.Target050BeforeStop050),
            HoldoutTargetBeforeStopEvents = holdoutSubset.Count(x => x.Target050BeforeStop050),
            TrainMedianExpectedNetPercent = trainMedian,
            HoldoutMedianExpectedNetPercent = holdoutMedian,
            TrainTargetBeforeStopRate = trainRate,
            HoldoutTargetBeforeStopRate = holdoutRate,
            UsesFutureInformation = false,
            TradableRule = true,
            Verdict = holdoutMedian >= 0m && holdoutSubset.Length >= MinimumBroadRegimeSamples / 4
                ? "HoldoutNonNegative"
                : holdoutMedian >= NearBreakevenNetThreshold
                    ? "HoldoutNearBreakeven"
                    : "HoldoutNegative"
        };
    }

    private static MarketRegimeForwardEdgeSummaryRow BuildSummaryRow(string _, MarketRegimeForwardEdgeObservation[] samples)
    {
        var first = samples[0];
        var medianNet = Median(samples.Select(x => (decimal?)x.ExpectedNetAfterCostPercent)) ?? 0m;
        var targetRate = Rate(samples, x => x.Target050BeforeStop050);
        return new MarketRegimeForwardEdgeSummaryRow
        {
            WindowLabel = first.WindowLabel,
            Symbol = first.Symbol,
            Interval = first.Interval,
            SampleCount = samples.Length,
            MedianForwardMfePercent = Median(samples.Select(x => x.ForwardMfePercent)) ?? 0m,
            MedianForwardMaePercent = Median(samples.Select(x => x.ForwardMaePercent)) ?? 0m,
            MedianExpectedNetAfterCostPercent = medianNet,
            Target050BeforeStop050Rate = targetRate,
            LongEdgeScore = Math.Round(samples.Average(x => x.LongEdgeScore), 6),
            Verdict = ClassifyVerdict(samples.Length, medianNet, targetRate)
        };
    }

    private static string ClassifyVerdict(int sampleCount, decimal? medianNet, decimal targetRate)
    {
        if (sampleCount < MinimumBroadRegimeSamples / 4)
            return "Sparse";
        if (medianNet >= 0m && sampleCount >= MinimumBroadRegimeSamples)
            return "PositiveEdge";
        if (medianNet >= NearBreakevenNetThreshold)
            return "NearBreakeven";
        if (targetRate >= 0.45m)
            return "HighTargetBeforeStopButNetNegative";
        return "NegativeEdge";
    }

    private static string InferStyle(IReadOnlyList<MarketRegimeEntryTimeRuleRow> rules)
    {
        var text = string.Join(" ", rules.SelectMany(r => r.FeaturesUsed));
        if (text.Contains("Trend", StringComparison.OrdinalIgnoreCase))
            return "trend-continuation";
        if (text.Contains("DistanceFromRecentLow", StringComparison.OrdinalIgnoreCase))
            return "mean-reversion";
        if (text.Contains("RangeWidth", StringComparison.OrdinalIgnoreCase))
            return "breakout/range-expansion";
        return "regime-filtered";
    }

    private static string InferStyleFromRegimes(IReadOnlyList<RegimeBucketEdgeRankingRow> regimes)
    {
        var text = string.Join(" ", regimes.Select(r => $"{r.BucketType}:{r.BucketLabel}"));
        if (text.Contains("TrendRegime:Uptrend", StringComparison.OrdinalIgnoreCase))
            return "trend";
        if (text.Contains("DistanceFromRecentLow", StringComparison.OrdinalIgnoreCase))
            return "mean-reversion";
        return "regime-filtered";
    }

    private static decimal Rate<T>(IReadOnlyList<T> samples, Func<T, bool> predicate)
        => samples.Count == 0 ? 0m : Math.Round((decimal)samples.Count(predicate) / samples.Count, 6);

    private static decimal? Median(IEnumerable<decimal?> values)
    {
        var arr = values.Where(v => v.HasValue).Select(v => v!.Value).OrderBy(v => v).ToArray();
        if (arr.Length == 0)
            return null;
        var mid = arr.Length / 2;
        return arr.Length % 2 == 0
            ? Math.Round((arr[mid - 1] + arr[mid]) / 2m, 6)
            : Math.Round(arr[mid], 6);
    }

    private static decimal? GetNumericFeature(MarketRegimeForwardEdgeObservation observation, string featureName)
        => featureName switch
        {
            nameof(MarketRegimeForwardEdgeObservation.TrendSlopePercent) => observation.TrendSlopePercent,
            nameof(MarketRegimeForwardEdgeObservation.TrendStrengthPercent) => observation.TrendStrengthPercent,
            nameof(MarketRegimeForwardEdgeObservation.AtrPercent) => observation.AtrPercent,
            nameof(MarketRegimeForwardEdgeObservation.RangeWidthPercent) => observation.RangeWidthPercent,
            nameof(MarketRegimeForwardEdgeObservation.VolumeExpansionRatio) => observation.VolumeExpansionRatio,
            nameof(MarketRegimeForwardEdgeObservation.DistanceFromRecentHighPercent) => observation.DistanceFromRecentHighPercent,
            nameof(MarketRegimeForwardEdgeObservation.DistanceFromRecentLowPercent) => observation.DistanceFromRecentLowPercent,
            nameof(MarketRegimeForwardEdgeObservation.BtcReturn15mPercent) => observation.BtcReturn15mPercent,
            nameof(MarketRegimeForwardEdgeObservation.BtcReturn30mPercent) => observation.BtcReturn30mPercent,
            nameof(MarketRegimeForwardEdgeObservation.BtcReturn60mPercent) => observation.BtcReturn60mPercent,
            nameof(MarketRegimeForwardEdgeObservation.BtcTrendSlopePercent) => observation.BtcTrendSlopePercent,
            nameof(MarketRegimeForwardEdgeObservation.SymbolReturnRelativeToBtc60mPercent) => observation.SymbolReturnRelativeToBtc60mPercent,
            nameof(MarketRegimeForwardEdgeObservation.MarketWideReturnProxyPercent) => observation.MarketWideReturnProxyPercent,
            _ => null
        };

    private static Func<MarketRegimeForwardEdgeObservation, string> ResolveCategoricalFeature(string featureName)
        => featureName switch
        {
            nameof(MarketRegimeForwardEdgeObservation.VolatilityRegime) => o => o.VolatilityRegime,
            nameof(MarketRegimeForwardEdgeObservation.TrendRegime) => o => o.TrendRegime,
            nameof(MarketRegimeForwardEdgeObservation.BtcTrendRegime) => o => o.BtcTrendRegime ?? "NoBtc",
            nameof(MarketRegimeForwardEdgeObservation.BtcVolatilityRegime) => o => o.BtcVolatilityRegime ?? "NoBtc",
            nameof(MarketRegimeForwardEdgeObservation.BtcMarketDirectionBucket) => o => o.BtcMarketDirectionBucket ?? "NoBtc",
            "BtcAboveMediumMa" => o => o.BtcAboveMediumMa?.ToString() ?? "NoBtc",
            _ => _ => "Unknown"
        };

    private static IEnumerable<BtcContextEdgeRankingRow> BuildBtcContextCategoricalRows(
        IReadOnlyList<MarketRegimeForwardEdgeObservation> observations,
        string bucketType,
        Func<MarketRegimeForwardEdgeObservation, string> selector)
    {
        foreach (var group in observations.GroupBy(o => $"{o.WindowLabel}|{o.Symbol}|{o.Interval}|{selector(o)}", StringComparer.OrdinalIgnoreCase))
        {
            var samples = group.ToArray();
            if (samples.Length == 0)
                continue;
            var first = samples[0];
            yield return BuildBtcContextRow(first.WindowLabel, first.Symbol, first.Interval, bucketType, selector(first), samples);
        }
    }

    private static IEnumerable<BtcContextEdgeRankingRow> BuildBtcContextQuantileRows(
        IReadOnlyList<MarketRegimeForwardEdgeObservation> observations,
        string featureName,
        Func<MarketRegimeForwardEdgeObservation, decimal?> selector)
    {
        foreach (var windowGroup in observations.GroupBy(o => $"{o.WindowLabel}|{o.Symbol}|{o.Interval}", StringComparer.OrdinalIgnoreCase))
        {
            var samples = windowGroup.ToArray();
            var buckets = BuildQuantileBuckets(samples.Select(o => selector(o)).Where(v => v.HasValue).Select(v => v!.Value).ToArray(), 4);
            for (var i = 0; i < buckets.Count; i++)
            {
                var bucket = buckets[i];
                var bucketRows = samples.Where(o =>
                {
                    var value = selector(o);
                    return value.HasValue && value.Value >= bucket.Min && (i == buckets.Count - 1 ? value.Value <= bucket.Max : value.Value < bucket.Max);
                }).ToArray();
                if (bucketRows.Length == 0)
                    continue;
                var first = bucketRows[0];
                yield return BuildBtcContextRow(first.WindowLabel, first.Symbol, first.Interval, featureName, bucket.Label, bucketRows);
            }
        }
    }

    private static BtcContextEdgeRankingRow BuildBtcContextRow(
        string windowLabel,
        TradingSymbol symbol,
        string interval,
        string bucketType,
        string bucketLabel,
        MarketRegimeForwardEdgeObservation[] samples)
    {
        var medianNet = Median(samples.Select(x => (decimal?)x.ExpectedNetAfterCostPercent)) ?? 0m;
        var targetRate = Rate(samples, x => x.Target050BeforeStop050);
        return new BtcContextEdgeRankingRow
        {
            WindowLabel = windowLabel,
            Symbol = symbol,
            Interval = interval,
            BtcContextBucketType = bucketType,
            BtcContextBucketLabel = bucketLabel,
            SampleCount = samples.Length,
            MedianForwardMfePercent = Median(samples.Select(x => x.ForwardMfePercent)) ?? 0m,
            MedianForwardMaePercent = Median(samples.Select(x => x.ForwardMaePercent)) ?? 0m,
            MedianExpectedNetAfterCostPercent = medianNet,
            Target050BeforeStop050Rate = targetRate,
            LongEdgeScore = Math.Round(samples.Average(x => x.LongEdgeScore), 6),
            Verdict = ClassifyVerdict(samples.Length, medianNet, targetRate)
        };
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
}
