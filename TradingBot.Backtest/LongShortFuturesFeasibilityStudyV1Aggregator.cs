using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class LongShortFuturesFeasibilityStudyV1Aggregator
{
    public const int MinimumBroadSamples = 200;
    public const int MinimumTargetBeforeStopEvents = 50;
    public const decimal NearBreakevenNetThreshold = -0.05m;

    private static readonly (string FeatureA, string FeatureB)[] EntryTimeRuleFeaturePairs =
    [
        (nameof(LongShortFuturesFeasibilityObservation.TrendSlopePercent), nameof(LongShortFuturesFeasibilityObservation.AtrPercent)),
        (nameof(LongShortFuturesFeasibilityObservation.RangeWidthPercent), nameof(LongShortFuturesFeasibilityObservation.DistanceFromRecentLowPercent)),
        (nameof(LongShortFuturesFeasibilityObservation.VolumeExpansionRatio), nameof(LongShortFuturesFeasibilityObservation.TrendSlopePercent)),
        (nameof(LongShortFuturesFeasibilityObservation.DistanceFromRecentHighPercent), nameof(LongShortFuturesFeasibilityObservation.AtrPercent)),
        (nameof(LongShortFuturesFeasibilityObservation.BtcReturn30mPercent), nameof(LongShortFuturesFeasibilityObservation.VolatilityRegime)),
        (nameof(LongShortFuturesFeasibilityObservation.MarketWideReturnProxyPercent), nameof(LongShortFuturesFeasibilityObservation.TrendRegime))
    ];

    public static IReadOnlyList<LongShortFuturesFeasibilitySummaryRow> BuildSummary(
        IReadOnlyList<LongShortFuturesFeasibilityObservation> observations)
    {
        var scenarios = LongShortFuturesFeasibilityStudyV1CostModel.BuildStudyScenarios();
        var rows = new List<LongShortFuturesFeasibilitySummaryRow>();

        foreach (var windowGroup in observations.GroupBy(o => $"{o.WindowLabel}|{o.Symbol}|{o.Interval}", StringComparer.OrdinalIgnoreCase))
        {
            var samples = windowGroup.ToArray();
            var first = samples[0];

            foreach (var mode in Enum.GetValues<LongShortTradeMode>())
            {
                var scenario = LongShortFuturesFeasibilityStudyV1CostModel.ResolveScenarioForTradeMode(mode);
                var nets = samples.Select(o => ResolveExpectedNet(o, mode, scenario.Label)).ToArray();
                var targetRate = Rate(samples, o => ResolveTargetBeforeStop(o, mode));
                var medianNet = Median(nets) ?? 0m;
                rows.Add(new LongShortFuturesFeasibilitySummaryRow
                {
                    WindowLabel = first.WindowLabel,
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    TradeMode = mode,
                    CostScenarioLabel = scenario.Label,
                    SampleCount = samples.Length,
                    MedianExpectedNetPercent = medianNet,
                    Target050BeforeStop050Rate = targetRate,
                    EdgeScore = ComputeEdgeScore(medianNet, targetRate, samples.Length),
                    Verdict = ClassifyVerdict(samples.Length, medianNet, targetRate)
                });
            }

            foreach (var scenario in scenarios.Where(s => s.Label != "spot-conservative"))
            {
                foreach (var direction in new[] { LongShortDirection.Long, LongShortDirection.Short })
                {
                    var nets = samples.Select(o => GetDirectionalExpectedNet(o, direction, scenario.Label)).ToArray();
                    var targetRate = Rate(samples, o => direction == LongShortDirection.Long
                        ? o.LongTarget050BeforeStop050
                        : o.ShortTarget050BeforeStop050);
                    var medianNet = Median(nets) ?? 0m;
                    rows.Add(new LongShortFuturesFeasibilitySummaryRow
                    {
                        WindowLabel = first.WindowLabel,
                        Symbol = first.Symbol,
                        Interval = first.Interval,
                        TradeMode = direction == LongShortDirection.Long
                            ? LongShortTradeMode.FuturesLongOnly
                            : LongShortTradeMode.FuturesShortOnly,
                        CostScenarioLabel = scenario.Label,
                        SampleCount = samples.Length,
                        MedianExpectedNetPercent = medianNet,
                        Target050BeforeStop050Rate = targetRate,
                        EdgeScore = ComputeEdgeScore(medianNet, targetRate, samples.Length),
                        Verdict = ClassifyVerdict(samples.Length, medianNet, targetRate)
                    });
                }
            }
        }

        return rows
            .OrderByDescending(r => r.EdgeScore)
            .ThenBy(r => r.Symbol)
            .ThenBy(r => r.Interval, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<LongShortSymbolIntervalRankingRow> BuildSymbolIntervalRanking(
        IReadOnlyList<LongShortFuturesFeasibilityObservation> observations,
        string costScenarioLabel = "futures-moderate")
    {
        var rows = new List<LongShortSymbolIntervalRankingRow>();
        foreach (var direction in new[] { LongShortDirection.Long, LongShortDirection.Short })
        {
            foreach (var group in observations.GroupBy(o => $"{o.WindowLabel}|{o.Symbol}|{o.Interval}", StringComparer.OrdinalIgnoreCase))
            {
                var samples = group.ToArray();
                var first = samples[0];
                var nets = samples.Select(o => GetDirectionalExpectedNet(o, direction, costScenarioLabel)).ToArray();
                var targetRate = Rate(samples, o => direction == LongShortDirection.Long
                    ? o.LongTarget050BeforeStop050
                    : o.ShortTarget050BeforeStop050);
                var medianNet = Median(nets) ?? 0m;
                rows.Add(new LongShortSymbolIntervalRankingRow
                {
                    WindowLabel = first.WindowLabel,
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    Direction = direction,
                    CostScenarioLabel = costScenarioLabel,
                    SampleCount = samples.Length,
                    MedianExpectedNetPercent = medianNet,
                    Target050BeforeStop050Rate = targetRate,
                    EdgeScore = ComputeEdgeScore(medianNet, targetRate, samples.Length),
                    Rank = 0,
                    Verdict = ClassifyVerdict(samples.Length, medianNet, targetRate)
                });
            }
        }

        return rows
            .OrderByDescending(r => r.EdgeScore)
            .Select((row, index) => row with { Rank = index + 1 })
            .ToArray();
    }

    public static IReadOnlyList<LongShortRegimeRankingRow> BuildRegimeRanking(
        IReadOnlyList<LongShortFuturesFeasibilityObservation> observations,
        string costScenarioLabel = "futures-moderate")
    {
        var rows = new List<LongShortRegimeRankingRow>();
        rows.AddRange(BuildCategoricalRegimeRows(observations, "VolatilityRegime", o => o.VolatilityRegime, costScenarioLabel));
        rows.AddRange(BuildCategoricalRegimeRows(observations, "TrendRegime", o => o.TrendRegime, costScenarioLabel));
        rows.AddRange(BuildCategoricalRegimeRows(observations, "SessionBucket", o => o.SessionBucket, costScenarioLabel));
        rows.AddRange(BuildCategoricalRegimeRows(observations, "BtcTrendRegime", o => o.BtcTrendRegime ?? "NoBtc", costScenarioLabel));
        rows.AddRange(BuildCategoricalRegimeRows(observations, "BtcMarketDirectionBucket", o => o.BtcMarketDirectionBucket ?? "NoBtc", costScenarioLabel));
        rows.AddRange(BuildQuantileRegimeRows(observations, "RangeWidthPercent", o => o.RangeWidthPercent, costScenarioLabel));
        rows.AddRange(BuildQuantileRegimeRows(observations, "DistanceFromRecentLowPercent", o => o.DistanceFromRecentLowPercent, costScenarioLabel));
        rows.AddRange(BuildQuantileRegimeRows(observations, "AtrPercent", o => o.AtrPercent, costScenarioLabel));
        rows.AddRange(BuildQuantileRegimeRows(observations, "VolumeExpansionRatio", o => o.VolumeExpansionRatio, costScenarioLabel));

        return rows
            .Where(r => r.SampleCount >= MinimumBroadSamples / 4)
            .OrderByDescending(r => r.EdgeScore)
            .Select((row, index) => row with { Rank = index + 1 })
            .ToArray();
    }

    public static IReadOnlyList<LongShortCostSensitivityRow> BuildCostSensitivity(
        IReadOnlyList<LongShortFuturesFeasibilityObservation> observations)
    {
        var scenarios = LongShortFuturesFeasibilityStudyV1CostModel.BuildStudyScenarios();
        var rows = new List<LongShortCostSensitivityRow>();

        foreach (var scenario in scenarios)
        {
            foreach (var mode in Enum.GetValues<LongShortTradeMode>())
            {
                var nets = observations.Select(o => ResolveExpectedNet(o, mode, scenario.Label)).ToArray();
                var targetRate = Rate(observations, o => ResolveTargetBeforeStop(o, mode));
                var medianNet = Median(nets) ?? 0m;
                rows.Add(new LongShortCostSensitivityRow
                {
                    CostScenarioLabel = scenario.Label,
                    TradeMode = mode,
                    Direction = mode switch
                    {
                        LongShortTradeMode.FuturesShortOnly => LongShortDirection.Short,
                        _ => LongShortDirection.Long
                    },
                    RoundTripCostPercent = RangeExpansionV2FeasibilityCostModel.EstimateRoundTripCostPercent(scenario),
                    FundingRatePercentPerHour = scenario.FundingRatePercentPerHour,
                    SampleCount = observations.Count,
                    MedianExpectedNetPercent = medianNet,
                    Target050BeforeStop050Rate = targetRate,
                    Verdict = ClassifyVerdict(observations.Count, medianNet, targetRate)
                });
            }

            foreach (var direction in new[] { LongShortDirection.Long, LongShortDirection.Short })
            {
                var nets = observations.Select(o => GetDirectionalExpectedNet(o, direction, scenario.Label)).ToArray();
                var targetRate = Rate(observations, o => direction == LongShortDirection.Long
                    ? o.LongTarget050BeforeStop050
                    : o.ShortTarget050BeforeStop050);
                var medianNet = Median(nets) ?? 0m;
                rows.Add(new LongShortCostSensitivityRow
                {
                    CostScenarioLabel = scenario.Label,
                    TradeMode = direction == LongShortDirection.Long
                        ? LongShortTradeMode.FuturesLongOnly
                        : LongShortTradeMode.FuturesShortOnly,
                    Direction = direction,
                    RoundTripCostPercent = RangeExpansionV2FeasibilityCostModel.EstimateRoundTripCostPercent(scenario),
                    FundingRatePercentPerHour = scenario.FundingRatePercentPerHour,
                    SampleCount = observations.Count,
                    MedianExpectedNetPercent = medianNet,
                    Target050BeforeStop050Rate = targetRate,
                    Verdict = ClassifyVerdict(observations.Count, medianNet, targetRate)
                });
            }
        }

        return rows;
    }

    public static IReadOnlyList<LongShortEntryTimeRuleRow> BuildEntryTimeRules(
        IReadOnlyList<LongShortFuturesFeasibilityObservation> observations,
        string costScenarioLabel = "futures-moderate")
    {
        if (observations.Count < MinimumBroadSamples)
            return [];

        var trainWindows = new HashSet<string>(["30d", "60d"], StringComparer.OrdinalIgnoreCase);
        var holdoutWindow = "90d";
        var train = observations.Where(o => trainWindows.Contains(o.WindowLabel)).ToArray();
        var holdout = observations.Where(o => string.Equals(o.WindowLabel, holdoutWindow, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (train.Length < MinimumBroadSamples || holdout.Length < MinimumBroadSamples / 3)
            return [];

        var rules = new List<LongShortEntryTimeRuleRow>();
        foreach (var direction in new[] { LongShortDirection.Long, LongShortDirection.Short })
        {
            foreach (var (featureA, featureB) in EntryTimeRuleFeaturePairs)
            {
                if (featureB is nameof(LongShortFuturesFeasibilityObservation.VolatilityRegime)
                    or nameof(LongShortFuturesFeasibilityObservation.TrendRegime))
                {
                    rules.AddRange(BuildCategoricalRules(train, holdout, direction, featureA, featureB, costScenarioLabel));
                    continue;
                }

                var bucketsA = BuildQuantileBuckets(train.Select(o => GetNumericFeature(o, featureA)).Where(v => v.HasValue).Select(v => v!.Value).ToArray(), 3);
                var bucketsB = BuildQuantileBuckets(train.Select(o => GetNumericFeature(o, featureB)).Where(v => v.HasValue).Select(v => v!.Value).ToArray(), 3);
                if (bucketsA.Count == 0 || bucketsB.Count == 0)
                    continue;

                LongShortEntryTimeRuleRow? best = null;
                foreach (var bucketA in bucketsA)
                {
                    foreach (var bucketB in bucketsB)
                    {
                        var trainSubset = train.Where(o =>
                            InBucket(GetNumericFeature(o, featureA), bucketA)
                            && InBucket(GetNumericFeature(o, featureB), bucketB)).ToArray();
                        if (trainSubset.Length < MinimumBroadSamples)
                            continue;

                        var holdoutSubset = holdout.Where(o =>
                            InBucket(GetNumericFeature(o, featureA), bucketA)
                            && InBucket(GetNumericFeature(o, featureB), bucketB)).ToArray();
                        if (holdoutSubset.Length < MinimumBroadSamples / 4)
                            continue;

                        if (trainSubset.Count(o => ResolveTargetBeforeStop(o, direction)) < MinimumTargetBeforeStopEvents / 2)
                            continue;

                        var row = BuildRuleRow(
                            direction,
                            $"{featureA} {bucketA.Label} AND {featureB} {bucketB.Label}",
                            [featureA, featureB],
                            trainSubset,
                            holdoutSubset,
                            costScenarioLabel);
                        if (best is null || row.HoldoutMedianExpectedNetPercent > best.HoldoutMedianExpectedNetPercent)
                            best = row;
                    }
                }

                if (best is not null)
                    rules.Add(best);
            }
        }

        return rules
            .OrderByDescending(r => r.HoldoutMedianExpectedNetPercent)
            .ThenByDescending(r => r.HoldoutTargetBeforeStopRate)
            .ToArray();
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildResearchAnswers(
        IReadOnlyList<LongShortFuturesFeasibilityObservation> observations,
        IReadOnlyList<LongShortFuturesFeasibilitySummaryRow> summary,
        IReadOnlyList<LongShortSymbolIntervalRankingRow> symbolIntervalRanking,
        IReadOnlyList<LongShortRegimeRankingRow> regimeRanking,
        IReadOnlyList<LongShortTargetStopMatrixRow> targetStopMatrix,
        IReadOnlyList<LongShortCostSensitivityRow> costSensitivity,
        IReadOnlyList<LongShortEntryTimeRuleRow> entryTimeRules)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var longRanking = symbolIntervalRanking.Where(r => r.Direction == LongShortDirection.Long && r.CostScenarioLabel == "futures-moderate").ToArray();
        var shortRanking = symbolIntervalRanking.Where(r => r.Direction == LongShortDirection.Short && r.CostScenarioLabel == "futures-moderate").ToArray();
        var bestLong = longRanking.FirstOrDefault();
        var bestShort = shortRanking.FirstOrDefault();
        var holdoutRules = entryTimeRules
            .Where(r => r.HoldoutMedianExpectedNetPercent >= 0m && r.HoldoutSamples >= MinimumBroadSamples / 4)
            .ToArray();
        var holdoutRulesModerate = holdoutRules.Where(r => r.CostScenarioLabel == "futures-moderate").ToArray();
        var lowCostOnlyRules = entryTimeRules
            .Where(r => r.CostScenarioLabel == "futures-low" && r.HoldoutMedianExpectedNetPercent >= 0m)
            .Where(r => !holdoutRulesModerate.Any(m => m.RuleDescription == r.RuleDescription && m.Direction == r.Direction))
            .ToArray();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does short-side simulation show better edge than long-only?",
            Answer = bestLong is null || bestShort is null
                ? "Insufficient observations to compare long vs short."
                : $"Best long (futures-moderate): {bestLong.Symbol}/{bestLong.Interval} median net={bestLong.MedianExpectedNetPercent:F4}%; best short: {bestShort.Symbol}/{bestShort.Interval} median net={bestShort.MedianExpectedNetPercent:F4}%. Aggregate long median={Median(observations.Select(o => (decimal?)o.LongExpectedNetFuturesModeratePercent)):F4}%, short median={Median(observations.Select(o => (decimal?)o.ShortExpectedNetFuturesModeratePercent)):F4}%.",
            Verdict = bestShort is not null && bestLong is not null && bestShort.MedianExpectedNetPercent > bestLong.MedianExpectedNetPercent
                ? "ShortBeatsLong"
                : bestShort is not null && bestLong is not null && bestShort.MedianExpectedNetPercent >= NearBreakevenNetThreshold && bestLong.MedianExpectedNetPercent < NearBreakevenNetThreshold
                    ? "ShortMarginalAdvantage"
                    : "LongNotClearlyWorseThanShort",
            Details = new Dictionary<string, object?> { ["topLong"] = longRanking.Take(5).ToArray(), ["topShort"] = shortRanking.Take(5).ToArray() }
        });

        var longPlusShortSummary = summary
            .Where(s => s.TradeMode == LongShortTradeMode.FuturesLongPlusShort && s.CostScenarioLabel == "futures-moderate")
            .OrderByDescending(s => s.EdgeScore)
            .FirstOrDefault();
        var futuresLongSummary = summary
            .Where(s => s.TradeMode == LongShortTradeMode.FuturesLongOnly && s.CostScenarioLabel == "futures-moderate")
            .OrderByDescending(s => s.EdgeScore)
            .FirstOrDefault();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does long+short improve net expectancy under Futures-like costs?",
            Answer = longPlusShortSummary is null
                ? "No long+short summary available."
                : $"Best long+short cohort: {longPlusShortSummary.Symbol}/{longPlusShortSummary.Interval} ({longPlusShortSummary.WindowLabel}) median net={longPlusShortSummary.MedianExpectedNetPercent:F4}% vs futures-long-only best={futuresLongSummary?.MedianExpectedNetPercent:F4}%.",
            Verdict = longPlusShortSummary is not null && futuresLongSummary is not null && longPlusShortSummary.MedianExpectedNetPercent > futuresLongSummary.MedianExpectedNetPercent
                ? "LongPlusShortImproves"
                : longPlusShortSummary is not null && longPlusShortSummary.MedianExpectedNetPercent >= 0m
                    ? "LongPlusShortPositive"
                    : "LongPlusShortDoesNotRescue",
            Details = new Dictionary<string, object?> { ["longPlusShort"] = longPlusShortSummary, ["futuresLong"] = futuresLongSummary }
        });

        var bestMatrix = targetStopMatrix
            .Where(m => m.CostScenarioLabel == "futures-moderate")
            .OrderByDescending(m => m.MedianExpectedNetPercent)
            .FirstOrDefault();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which symbol/interval has the best target-before-stop edge?",
            Answer = bestMatrix is null
                ? "No target/stop matrix rows."
                : $"Best matrix cell: {bestMatrix.Symbol}/{bestMatrix.Interval} {bestMatrix.Direction} target={bestMatrix.TargetPercent:F2}% stop={bestMatrix.StopPercent:F2}% median net={bestMatrix.MedianExpectedNetPercent:F4}%, target-before-stop rate={bestMatrix.TargetBeforeStopRate:P1}.",
            Verdict = bestMatrix is not null && bestMatrix.MedianExpectedNetPercent >= 0m && bestMatrix.SampleCount >= MinimumBroadSamples
                ? "PositiveTargetStopEdge"
                : bestMatrix is not null && bestMatrix.MedianExpectedNetPercent >= NearBreakevenNetThreshold
                    ? "NearBreakevenTargetStop"
                    : "NoPositiveTargetStopEdge",
            Details = new Dictionary<string, object?> { ["topMatrix"] = targetStopMatrix.OrderByDescending(m => m.MedianExpectedNetPercent).Take(10).ToArray() }
        });

        var btcBuckets = regimeRanking
            .Where(r => r.BucketType.StartsWith("Btc", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.EdgeScore)
            .ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does BTC context improve direction selection?",
            Answer = btcBuckets.Length == 0
                ? "No BTC-context regime buckets met sample thresholds."
                : string.Join("; ", btcBuckets.Take(4).Select(b => $"{b.BucketLabel} {b.Direction}: median net={b.MedianExpectedNetPercent:F4}%")),
            Verdict = btcBuckets.Any(b => b.MedianExpectedNetPercent >= 0m && b.SampleCount >= MinimumBroadSamples / 2)
                ? "BtcContextHelpsDirection"
                : btcBuckets.Length > 0 ? "BtcContextWeakForDirection" : "NoBtcData",
            Details = new Dictionary<string, object?> { ["btcBuckets"] = btcBuckets.Take(10).ToArray() }
        });

        var longBadShortGood = regimeRanking
            .Where(r => r.CostScenarioLabel == "futures-moderate")
            .GroupBy(r => $"{r.WindowLabel}|{r.Symbol}|{r.Interval}|{r.BucketType}|{r.BucketLabel}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var longRow = g.FirstOrDefault(x => x.Direction == LongShortDirection.Long);
                var shortRow = g.FirstOrDefault(x => x.Direction == LongShortDirection.Short);
                return new { g.Key, longRow, shortRow };
            })
            .Where(x => x.longRow is not null && x.shortRow is not null
                && x.longRow.MedianExpectedNetPercent < NearBreakevenNetThreshold
                && x.shortRow.MedianExpectedNetPercent >= 0m
                && x.longRow.SampleCount >= MinimumBroadSamples / 4
                && x.shortRow.SampleCount >= MinimumBroadSamples / 4)
            .ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are there regimes where long is bad but short is good?",
            Answer = longBadShortGood.Length > 0
                ? $"{longBadShortGood.Length} regime bucket(s) show long negative and short non-negative under futures-moderate costs."
                : "No regime bucket met sample threshold with long-negative / short-non-negative split.",
            Verdict = longBadShortGood.Length > 0 ? "LongBadShortGoodRegimesFound" : "NoLongBadShortGoodRegime",
            Details = new Dictionary<string, object?> { ["examples"] = longBadShortGood.Take(8).ToArray() }
        });

        var moderateCost = costSensitivity.Where(c => c.CostScenarioLabel == "futures-moderate").ToArray();
        var stressCost = costSensitivity.Where(c => c.CostScenarioLabel == "futures-stress").ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is the edge still negative after realistic Futures fees/slippage/funding?",
            Answer = $"Futures-moderate: {moderateCost.Count(c => c.MedianExpectedNetPercent >= 0m)} positive cohort(s) of {moderateCost.Length}; futures-stress: {stressCost.Count(c => c.MedianExpectedNetPercent >= 0m)} positive of {stressCost.Length}. Best moderate median={moderateCost.MaxBy(c => c.MedianExpectedNetPercent)?.MedianExpectedNetPercent:F4}%.",
            Verdict = moderateCost.Any(c => c.MedianExpectedNetPercent >= 0m && c.SampleCount >= MinimumBroadSamples)
                ? "PositiveUnderModerateFuturesCosts"
                : moderateCost.Any(c => c.MedianExpectedNetPercent >= NearBreakevenNetThreshold)
                    ? "NearBreakevenUnderModerateCosts"
                    : "NegativeUnderRealisticFuturesCosts",
            Details = new Dictionary<string, object?> { ["moderate"] = moderateCost, ["stress"] = stressCost }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does any entry-time rule survive 30d/60d train and 90d holdout?",
            Answer = holdoutRulesModerate.Length > 0
                ? $"{holdoutRulesModerate.Length} rule(s) non-negative on 90d holdout under futures-moderate; best holdout median={holdoutRulesModerate.Max(r => r.HoldoutMedianExpectedNetPercent):F4}%."
                : lowCostOnlyRules.Length > 0
                    ? $"No futures-moderate holdout rules; {lowCostOnlyRules.Length} rule(s) positive only under futures-low (research-only)."
                    : $"No entry-time rules survived holdout with median expected net >= 0 ({entryTimeRules.Count} rules tested).",
            Verdict = holdoutRulesModerate.Length > 0
                ? "HoldoutRuleSurvived"
                : lowCostOnlyRules.Length > 0
                    ? "HoldoutPositiveOnlyUnderLowCost"
                    : "NoHoldoutRule",
            Details = new Dictionary<string, object?> { ["holdoutRules"] = holdoutRulesModerate.Take(10).ToArray(), ["lowCostOnly"] = lowCostOnlyRules.Take(5).ToArray() }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "If no long/short edge exists, should we pause candle-rule trading and require richer data?",
            Answer = holdoutRulesModerate.Length > 0
                ? "Holdout-surviving entry-time rules under futures-moderate suggest selective research continuation, not live deployment."
                : lowCostOnlyRules.Length > 0
                    ? "Edge appears only under optimistic futures-low costs. Mark as research-only; do not implement live Futures."
                    : "No robust long/short edge under realistic costs across symbols, intervals, regimes, or holdout rules. Recommend pausing candle-rule strategy implementation and investing in richer data (order book, open interest, funding, news/regime features).",
            Verdict = holdoutRulesModerate.Length > 0
                ? "SelectiveResearchOnly"
                : lowCostOnlyRules.Length > 0
                    ? "ResearchOnlyLowCost"
                    : "PauseCandleRuleTrading",
            Details = new Dictionary<string, object?>
            {
                ["observationCount"] = observations.Count,
                ["positiveModerateCohorts"] = moderateCost.Count(c => c.MedianExpectedNetPercent >= 0m),
                ["spotLongBest"] = summary.Where(s => s.TradeMode == LongShortTradeMode.SpotLongOnly).OrderByDescending(s => s.MedianExpectedNetPercent).FirstOrDefault()
            }
        });

        return answers;
    }

    private static IEnumerable<LongShortRegimeRankingRow> BuildCategoricalRegimeRows(
        IReadOnlyList<LongShortFuturesFeasibilityObservation> observations,
        string bucketType,
        Func<LongShortFuturesFeasibilityObservation, string> selector,
        string costScenarioLabel)
    {
        foreach (var direction in new[] { LongShortDirection.Long, LongShortDirection.Short })
        {
            foreach (var group in observations.GroupBy(o => $"{o.WindowLabel}|{o.Symbol}|{o.Interval}|{selector(o)}", StringComparer.OrdinalIgnoreCase))
            {
                var samples = group.ToArray();
                if (samples.Length == 0)
                    continue;
                var first = samples[0];
                var nets = samples.Select(o => GetDirectionalExpectedNet(o, direction, costScenarioLabel)).ToArray();
                var targetRate = Rate(samples, o => direction == LongShortDirection.Long
                    ? o.LongTarget050BeforeStop050
                    : o.ShortTarget050BeforeStop050);
                var medianNet = Median(nets) ?? 0m;
                yield return new LongShortRegimeRankingRow
                {
                    WindowLabel = first.WindowLabel,
                    BucketType = bucketType,
                    BucketLabel = selector(first),
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    Direction = direction,
                    CostScenarioLabel = costScenarioLabel,
                    SampleCount = samples.Length,
                    MedianExpectedNetPercent = medianNet,
                    Target050BeforeStop050Rate = targetRate,
                    EdgeScore = ComputeEdgeScore(medianNet, targetRate, samples.Length),
                    Rank = 0,
                    Verdict = ClassifyVerdict(samples.Length, medianNet, targetRate)
                };
            }
        }
    }

    private static IEnumerable<LongShortRegimeRankingRow> BuildQuantileRegimeRows(
        IReadOnlyList<LongShortFuturesFeasibilityObservation> observations,
        string featureName,
        Func<LongShortFuturesFeasibilityObservation, decimal> selector,
        string costScenarioLabel)
    {
        foreach (var direction in new[] { LongShortDirection.Long, LongShortDirection.Short })
        {
            foreach (var windowGroup in observations.GroupBy(o => $"{o.WindowLabel}|{o.Symbol}|{o.Interval}", StringComparer.OrdinalIgnoreCase))
            {
                var samples = windowGroup.ToArray();
                var buckets = BuildQuantileBuckets(samples.Select(selector).ToArray(), 4);
                for (var i = 0; i < buckets.Count; i++)
                {
                    var bucket = buckets[i];
                    var bucketRows = samples.Where(o =>
                    {
                        var value = selector(o);
                        return value >= bucket.Min && (i == buckets.Count - 1 ? value <= bucket.Max : value < bucket.Max);
                    }).ToArray();
                    if (bucketRows.Length == 0)
                        continue;

                    var first = bucketRows[0];
                    var nets = bucketRows.Select(o => GetDirectionalExpectedNet(o, direction, costScenarioLabel)).ToArray();
                    var targetRate = Rate(bucketRows, o => direction == LongShortDirection.Long
                        ? o.LongTarget050BeforeStop050
                        : o.ShortTarget050BeforeStop050);
                    var medianNet = Median(nets) ?? 0m;
                    yield return new LongShortRegimeRankingRow
                    {
                        WindowLabel = first.WindowLabel,
                        BucketType = featureName,
                        BucketLabel = bucket.Label,
                        Symbol = first.Symbol,
                        Interval = first.Interval,
                        Direction = direction,
                        CostScenarioLabel = costScenarioLabel,
                        SampleCount = bucketRows.Length,
                        MedianExpectedNetPercent = medianNet,
                        Target050BeforeStop050Rate = targetRate,
                        EdgeScore = ComputeEdgeScore(medianNet, targetRate, bucketRows.Length),
                        Rank = 0,
                        Verdict = ClassifyVerdict(bucketRows.Length, medianNet, targetRate)
                    };
                }
            }
        }
    }

    private static IEnumerable<LongShortEntryTimeRuleRow> BuildCategoricalRules(
        LongShortFuturesFeasibilityObservation[] train,
        LongShortFuturesFeasibilityObservation[] holdout,
        LongShortDirection direction,
        string featureA,
        string featureB,
        string costScenarioLabel)
    {
        foreach (var category in train.Select(o => ResolveCategoricalFeature(featureB)(o)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var trainSubset = train.Where(o => string.Equals(ResolveCategoricalFeature(featureB)(o), category, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (trainSubset.Length < MinimumBroadSamples)
                continue;

            var holdoutSubset = holdout.Where(o => string.Equals(ResolveCategoricalFeature(featureB)(o), category, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (holdoutSubset.Length < MinimumBroadSamples / 4)
                continue;

            if (trainSubset.Count(o => ResolveTargetBeforeStop(o, direction)) < MinimumTargetBeforeStopEvents / 2)
                continue;

            yield return BuildRuleRow(
                direction,
                $"{featureA} in train buckets AND {featureB}={category}",
                [featureA, featureB],
                trainSubset,
                holdoutSubset,
                costScenarioLabel);
        }
    }

    private static LongShortEntryTimeRuleRow BuildRuleRow(
        LongShortDirection direction,
        string description,
        string[] features,
        LongShortFuturesFeasibilityObservation[] trainSubset,
        LongShortFuturesFeasibilityObservation[] holdoutSubset,
        string costScenarioLabel)
    {
        var trainNets = trainSubset.Select(o => GetDirectionalExpectedNet(o, direction, costScenarioLabel)).ToArray();
        var holdoutNets = holdoutSubset.Select(o => GetDirectionalExpectedNet(o, direction, costScenarioLabel)).ToArray();
        var trainMedian = Median(trainNets) ?? 0m;
        var holdoutMedian = Median(holdoutNets) ?? 0m;
        var trainRate = Rate(trainSubset, o => ResolveTargetBeforeStop(o, direction));
        var holdoutRate = Rate(holdoutSubset, o => ResolveTargetBeforeStop(o, direction));
        return new LongShortEntryTimeRuleRow
        {
            Direction = direction,
            RuleDescription = description,
            FeaturesUsed = features,
            TrainWindows = "30d,60d",
            HoldoutWindow = "90d",
            TrainSamples = trainSubset.Length,
            HoldoutSamples = holdoutSubset.Length,
            TrainMedianExpectedNetPercent = trainMedian,
            HoldoutMedianExpectedNetPercent = holdoutMedian,
            TrainTargetBeforeStopRate = trainRate,
            HoldoutTargetBeforeStopRate = holdoutRate,
            CostScenarioLabel = costScenarioLabel,
            Verdict = holdoutMedian >= 0m && holdoutSubset.Length >= MinimumBroadSamples / 4
                ? "HoldoutNonNegative"
                : holdoutMedian >= NearBreakevenNetThreshold
                    ? "HoldoutNearBreakeven"
                    : "HoldoutNegative"
        };
    }

    private static decimal ResolveExpectedNet(
        LongShortFuturesFeasibilityObservation observation,
        LongShortTradeMode mode,
        string costScenarioLabel)
        => mode switch
        {
            LongShortTradeMode.SpotLongOnly => observation.LongExpectedNetSpotConservativePercent,
            LongShortTradeMode.FuturesLongOnly => GetDirectionalExpectedNet(observation, LongShortDirection.Long, costScenarioLabel),
            LongShortTradeMode.FuturesShortOnly => GetDirectionalExpectedNet(observation, LongShortDirection.Short, costScenarioLabel),
            LongShortTradeMode.FuturesLongPlusShort => Math.Max(
                GetDirectionalExpectedNet(observation, LongShortDirection.Long, costScenarioLabel),
                GetDirectionalExpectedNet(observation, LongShortDirection.Short, costScenarioLabel)),
            _ => 0m
        };

    private static bool ResolveTargetBeforeStop(LongShortFuturesFeasibilityObservation observation, LongShortTradeMode mode)
        => mode switch
        {
            LongShortTradeMode.SpotLongOnly or LongShortTradeMode.FuturesLongOnly => observation.LongTarget050BeforeStop050,
            LongShortTradeMode.FuturesShortOnly => observation.ShortTarget050BeforeStop050,
            LongShortTradeMode.FuturesLongPlusShort => observation.BestDirectionFuturesModerate == LongShortDirection.Long
                ? observation.LongTarget050BeforeStop050
                : observation.ShortTarget050BeforeStop050,
            _ => false
        };

    private static bool ResolveTargetBeforeStop(LongShortFuturesFeasibilityObservation observation, LongShortDirection direction)
        => direction == LongShortDirection.Long
            ? observation.LongTarget050BeforeStop050
            : observation.ShortTarget050BeforeStop050;

    private static decimal GetDirectionalExpectedNet(
        LongShortFuturesFeasibilityObservation observation,
        LongShortDirection direction,
        string costScenarioLabel)
        => (direction, costScenarioLabel) switch
        {
            (LongShortDirection.Long, "spot-conservative") => observation.LongExpectedNetSpotConservativePercent,
            (LongShortDirection.Short, "spot-conservative") => observation.ShortExpectedNetSpotConservativePercent,
            (LongShortDirection.Long, "futures-moderate") => observation.LongExpectedNetFuturesModeratePercent,
            (LongShortDirection.Short, "futures-moderate") => observation.ShortExpectedNetFuturesModeratePercent,
            (LongShortDirection.Long, "futures-low") => observation.LongExpectedNetFuturesLowPercent,
            (LongShortDirection.Short, "futures-low") => observation.ShortExpectedNetFuturesLowPercent,
            (LongShortDirection.Long, "futures-stress") => observation.LongExpectedNetFuturesStressPercent,
            (LongShortDirection.Short, "futures-stress") => observation.ShortExpectedNetFuturesStressPercent,
            _ => direction == LongShortDirection.Long
                ? observation.LongExpectedNetFuturesModeratePercent
                : observation.ShortExpectedNetFuturesModeratePercent
        };

    private static decimal ComputeEdgeScore(decimal medianNet, decimal targetRate, int sampleCount)
        => Math.Round(medianNet + targetRate * 0.25m + Math.Min(sampleCount, MinimumBroadSamples) / (decimal)MinimumBroadSamples * 0.05m, 6);

    private static string ClassifyVerdict(int sampleCount, decimal medianNet, decimal targetRate)
    {
        if (sampleCount < MinimumBroadSamples / 4)
            return "Sparse";
        if (medianNet >= 0m && sampleCount >= MinimumBroadSamples)
            return "PositiveEdge";
        if (medianNet >= NearBreakevenNetThreshold)
            return "NearBreakeven";
        if (targetRate >= 0.45m)
            return "HighTargetBeforeStopButNetNegative";
        return "NegativeEdge";
    }

    private static decimal Rate<T>(IReadOnlyList<T> samples, Func<T, bool> predicate)
        => samples.Count == 0 ? 0m : Math.Round((decimal)samples.Count(predicate) / samples.Count, 6);

    private static decimal? Median(IEnumerable<decimal> values)
    {
        var arr = values.OrderBy(v => v).ToArray();
        if (arr.Length == 0)
            return null;
        var mid = arr.Length / 2;
        return arr.Length % 2 == 0
            ? Math.Round((arr[mid - 1] + arr[mid]) / 2m, 6)
            : Math.Round(arr[mid], 6);
    }

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

    private static decimal? GetNumericFeature(LongShortFuturesFeasibilityObservation observation, string featureName)
        => featureName switch
        {
            nameof(LongShortFuturesFeasibilityObservation.TrendSlopePercent) => observation.TrendSlopePercent,
            nameof(LongShortFuturesFeasibilityObservation.AtrPercent) => observation.AtrPercent,
            nameof(LongShortFuturesFeasibilityObservation.RangeWidthPercent) => observation.RangeWidthPercent,
            nameof(LongShortFuturesFeasibilityObservation.VolumeExpansionRatio) => observation.VolumeExpansionRatio,
            nameof(LongShortFuturesFeasibilityObservation.DistanceFromRecentHighPercent) => observation.DistanceFromRecentHighPercent,
            nameof(LongShortFuturesFeasibilityObservation.DistanceFromRecentLowPercent) => observation.DistanceFromRecentLowPercent,
            nameof(LongShortFuturesFeasibilityObservation.BtcReturn30mPercent) => observation.BtcReturn30mPercent,
            nameof(LongShortFuturesFeasibilityObservation.MarketWideReturnProxyPercent) => observation.MarketWideReturnProxyPercent,
            _ => null
        };

    private static Func<LongShortFuturesFeasibilityObservation, string> ResolveCategoricalFeature(string featureName)
        => featureName switch
        {
            nameof(LongShortFuturesFeasibilityObservation.VolatilityRegime) => o => o.VolatilityRegime,
            nameof(LongShortFuturesFeasibilityObservation.TrendRegime) => o => o.TrendRegime,
            _ => _ => "Unknown"
        };

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
