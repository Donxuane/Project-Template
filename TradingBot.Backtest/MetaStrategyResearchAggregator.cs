namespace TradingBot.Backtest;

public static class MetaStrategyResearchAggregator
{
    public const int MinimumRobustTrades = 50;
    public const int MinimumRobustWindows = 3;

    private static readonly string[] ProfitExitReasons = ["ProfitTarget", "ProfitLock"];
    private static readonly string[] EntryTimeNumericFeatures =
    [
        nameof(MetaStrategyResearchRecord.StopDistancePercent),
        nameof(MetaStrategyResearchRecord.BreakoutBodyStrengthPercent),
        nameof(MetaStrategyResearchRecord.RangeWidthPercent),
        nameof(MetaStrategyResearchRecord.VolumeExpansionRatio),
        nameof(MetaStrategyResearchRecord.AtrExpansionRatio),
        nameof(MetaStrategyResearchRecord.ExpectedMovePercent),
        nameof(MetaStrategyResearchRecord.RequiredGrossMovePercent),
        nameof(MetaStrategyResearchRecord.RewardRisk),
        nameof(MetaStrategyResearchRecord.ShortMaSlopePercent),
        nameof(MetaStrategyResearchRecord.TrendStrengthPercent),
        nameof(MetaStrategyResearchRecord.DistanceToInvalidationPercent),
        nameof(MetaStrategyResearchRecord.StopToLockRatio)
    ];

    private static readonly string[] OutcomeDiagnosticNumericFeatures =
    [
        nameof(MetaStrategyResearchRecord.MfePercent),
        nameof(MetaStrategyResearchRecord.MaePercent),
        nameof(MetaStrategyResearchRecord.ForwardMfe15Percent),
        nameof(MetaStrategyResearchRecord.ForwardMfe30Percent),
        nameof(MetaStrategyResearchRecord.ForwardMfe60Percent),
        nameof(MetaStrategyResearchRecord.ForwardMae15Percent),
        nameof(MetaStrategyResearchRecord.ForwardMae30Percent),
        nameof(MetaStrategyResearchRecord.ForwardMae60Percent)
    ];

    private static readonly string[] NumericFeatures =
    [
        ..EntryTimeNumericFeatures,
        ..OutcomeDiagnosticNumericFeatures
    ];

    private static readonly (string FeatureA, string FeatureB)[] EntryTimeRuleFeaturePairs =
    [
        (nameof(MetaStrategyResearchRecord.BreakoutBodyStrengthPercent), nameof(MetaStrategyResearchRecord.StopDistancePercent)),
        (nameof(MetaStrategyResearchRecord.RangeWidthPercent), nameof(MetaStrategyResearchRecord.StopDistancePercent)),
        (nameof(MetaStrategyResearchRecord.ExpectedMovePercent), nameof(MetaStrategyResearchRecord.StopDistancePercent)),
        (nameof(MetaStrategyResearchRecord.ExpectedMovePercent), nameof(MetaStrategyResearchRecord.RewardRisk)),
        (nameof(MetaStrategyResearchRecord.VolumeExpansionRatio), nameof(MetaStrategyResearchRecord.StopDistancePercent)),
        (nameof(MetaStrategyResearchRecord.AtrExpansionRatio), nameof(MetaStrategyResearchRecord.StopDistancePercent)),
        (nameof(MetaStrategyResearchRecord.ShortMaSlopePercent), nameof(MetaStrategyResearchRecord.RangeWidthPercent))
    ];

    private static readonly (string FeatureA, string FeatureB)[] OutcomeDiagnosticRuleFeaturePairs =
    [
        (nameof(MetaStrategyResearchRecord.ForwardMfe60Percent), nameof(MetaStrategyResearchRecord.ForwardMae60Percent)),
        (nameof(MetaStrategyResearchRecord.ForwardMfe30Percent), nameof(MetaStrategyResearchRecord.ForwardMae30Percent)),
        (nameof(MetaStrategyResearchRecord.MfePercent), nameof(MetaStrategyResearchRecord.MaePercent))
    ];

    public static MetaStrategyResearchDiagnostics Build(
        IReadOnlyList<MetaStrategyResearchRecord> records,
        MetaStrategyResearchImportReport importReport)
    {
        var executed = records.Where(r => r.CandidateWasExecuted).ToArray();
        var strategyFamilySummary = BuildStrategyFamilySummary(records, executed);
        var symbolIntervalSummary = BuildSymbolIntervalSummary(executed);
        var featureBucketSummary = BuildFeatureBucketSummary(executed);
        var exitReasonSummary = BuildExitReasonSummary(executed);
        var bestSubsets = BuildBestSubsets(executed);
        var overfitWarnings = BuildOverfitWarnings(executed, bestSubsets);
        var entryTimeRuleDiscovery = BuildEntryTimeRuleDiscovery(executed);
        var outcomeDiagnosticRuleDiscovery = BuildOutcomeDiagnosticRuleDiscovery(executed);
        var researchAnswers = BuildResearchAnswers(
            records,
            executed,
            strategyFamilySummary,
            symbolIntervalSummary,
            featureBucketSummary,
            exitReasonSummary,
            bestSubsets,
            overfitWarnings,
            entryTimeRuleDiscovery,
            outcomeDiagnosticRuleDiscovery,
            importReport);

        return new MetaStrategyResearchDiagnostics(
            records,
            strategyFamilySummary,
            symbolIntervalSummary,
            featureBucketSummary,
            exitReasonSummary,
            bestSubsets,
            overfitWarnings,
            entryTimeRuleDiscovery,
            outcomeDiagnosticRuleDiscovery,
            researchAnswers,
            importReport);
    }

    public static IReadOnlyList<MetaStrategyFamilySummaryRow> BuildStrategyFamilySummary(
        IReadOnlyList<MetaStrategyResearchRecord> allRecords,
        IReadOnlyList<MetaStrategyResearchRecord> executed)
    {
        return allRecords
            .GroupBy(r => r.StrategyFamily, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var familyExecuted = executed.Where(x => string.Equals(x.StrategyFamily, g.Key, StringComparison.OrdinalIgnoreCase)).ToArray();
                var trades = familyExecuted.Length;
                var net = familyExecuted.Sum(x => x.NetPnlQuote ?? 0m);
                return new MetaStrategyFamilySummaryRow
                {
                    StrategyFamily = g.Key,
                    Trades = trades,
                    ExecutedCandidates = familyExecuted.Length,
                    BlockedCandidates = g.Count(x => !x.CandidateWasExecuted),
                    NetWinners = familyExecuted.Count(x => x.IsNetWinner == true),
                    NetPnlQuote = net,
                    NetPerTrade = trades == 0 ? 0m : Math.Round(net / trades, 8),
                    NetWinnerRate = trades == 0 ? 0m : Math.Round((decimal)familyExecuted.Count(x => x.IsNetWinner == true) / trades, 6),
                    StopLossRate = Rate(familyExecuted, "StopLoss"),
                    TimeStopRate = Rate(familyExecuted, "TimeStop"),
                    ProfitExitRate = ProfitExitRate(familyExecuted),
                    WindowCount = familyExecuted.Select(x => x.WindowLabel).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                };
            })
            .OrderBy(x => x.StrategyFamily, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<MetaSymbolIntervalSummaryRow> BuildSymbolIntervalSummary(
        IReadOnlyList<MetaStrategyResearchRecord> executed)
    {
        return executed
            .GroupBy(r => $"{r.StrategyFamily}|{r.Symbol}|{r.Interval}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var trades = g.Count();
                var net = g.Sum(x => x.NetPnlQuote ?? 0m);
                var windows = g.Select(x => x.WindowLabel).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                return new MetaSymbolIntervalSummaryRow
                {
                    StrategyFamily = first.StrategyFamily,
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    Trades = trades,
                    NetWinners = g.Count(x => x.IsNetWinner == true),
                    NetPnlQuote = net,
                    NetPerTrade = trades == 0 ? 0m : Math.Round(net / trades, 8),
                    NetWinnerRate = trades == 0 ? 0m : Math.Round((decimal)g.Count(x => x.IsNetWinner == true) / trades, 6),
                    StopLossRate = Rate(g, "StopLoss"),
                    WindowCount = windows,
                    MeetsMinimumSample = trades >= MinimumRobustTrades && windows >= MinimumRobustWindows
                };
            })
            .OrderByDescending(x => x.NetPnlQuote)
            .ThenByDescending(x => x.Trades)
            .ToArray();
    }

    public static IReadOnlyList<MetaFeatureBucketSummaryRow> BuildFeatureBucketSummary(
        IReadOnlyList<MetaStrategyResearchRecord> executed)
    {
        var rows = new List<MetaFeatureBucketSummaryRow>();
        foreach (var feature in NumericFeatures)
        {
            var values = executed
                .Select(r => (Record: r, Value: GetFeatureValue(r, feature)))
                .Where(x => x.Value.HasValue)
                .ToArray();
            if (values.Length < 20)
                continue;

            var buckets = BuildQuantileBuckets(values.Select(x => x.Value!.Value).ToArray(), bucketCount: 5);
            for (var i = 0; i < buckets.Count; i++)
            {
                var bucket = buckets[i];
                var bucketRows = values
                    .Where(x => x.Value >= bucket.Min && (i == buckets.Count - 1 ? x.Value <= bucket.Max : x.Value < bucket.Max))
                    .Select(x => x.Record)
                    .ToArray();
                if (bucketRows.Length == 0)
                    continue;

                rows.Add(BuildFeatureBucketRow(feature, bucket.Label, i, bucket.Min, bucket.Max, bucketRows));
            }
        }

        return rows
            .OrderBy(x => x.FeatureName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.BucketIndex)
            .ToArray();
    }

    public static IReadOnlyList<MetaExitReasonSummaryRow> BuildExitReasonSummary(
        IReadOnlyList<MetaStrategyResearchRecord> executed)
    {
        var total = executed.Count;
        return executed
            .GroupBy(r => $"{r.StrategyFamily}|{r.ExitReason ?? "Unknown"}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var count = g.Count();
                return new MetaExitReasonSummaryRow
                {
                    StrategyFamily = first.StrategyFamily,
                    ExitReason = first.ExitReason ?? "Unknown",
                    Count = count,
                    NetPnlQuote = g.Sum(x => x.NetPnlQuote ?? 0m),
                    GrossPnlQuote = g.Sum(x => x.GrossPnlQuote ?? 0m),
                    ShareOfExits = total == 0 ? 0m : Math.Round((decimal)count / total, 6)
                };
            })
            .OrderByDescending(x => Math.Abs(x.NetPnlQuote))
            .ThenByDescending(x => x.Count)
            .ToArray();
    }

    public static IReadOnlyList<MetaBestSubsetRow> BuildBestSubsets(IReadOnlyList<MetaStrategyResearchRecord> executed)
    {
        var all = new List<MetaBestSubsetRow>();

        all.AddRange(executed
            .GroupBy(r => $"{r.StrategyFamily}|{r.Symbol}|{r.Interval}", StringComparer.OrdinalIgnoreCase)
            .Select(g => BuildSubsetRow(
                "StrategyFamily + Symbol + Interval",
                g.Key,
                $"{g.First().StrategyFamily} + {g.First().Symbol} + {g.First().Interval}",
                g.ToArray())));

        all.AddRange(executed
            .GroupBy(r => $"{r.StrategyFamily}|{r.Symbol}|{r.Interval}|{r.TargetModelName ?? "Unknown"}", StringComparer.OrdinalIgnoreCase)
            .Select(g => BuildSubsetRow(
                "StrategyFamily + Symbol + Interval + TargetModel",
                g.Key,
                $"{g.First().StrategyFamily} + {g.First().Symbol} + {g.First().Interval} + {g.First().TargetModelName ?? "Unknown"}",
                g.ToArray())));

        all.AddRange(executed
            .Where(r => r.RangeWidthPercent.HasValue)
            .GroupBy(r => $"{r.Symbol}|{r.Interval}|{BucketLabel("RangeWidthPercent", r.RangeWidthPercent)}", StringComparer.OrdinalIgnoreCase)
            .Select(g => BuildSubsetRow(
                "Symbol + Interval + RangeWidth bucket",
                g.Key,
                $"{g.First().Symbol} + {g.First().Interval} + RangeWidth {BucketLabel("RangeWidthPercent", g.First().RangeWidthPercent)}",
                g.ToArray())));

        all.AddRange(executed
            .Where(r => r.BreakoutBodyStrengthPercent.HasValue || r.StopDistancePercent.HasValue)
            .GroupBy(r => $"{BucketLabel("BreakoutBodyStrengthPercent", r.BreakoutBodyStrengthPercent)}|{BucketLabel("StopDistancePercent", r.StopDistancePercent)}", StringComparer.OrdinalIgnoreCase)
            .Select(g => BuildSubsetRow(
                "BodyStrength + StopDistance buckets",
                g.Key,
                $"BodyStrength {BucketLabel("BreakoutBodyStrengthPercent", g.First().BreakoutBodyStrengthPercent)} + StopDistance {BucketLabel("StopDistancePercent", g.First().StopDistancePercent)}",
                g.ToArray())));

        all.AddRange(executed
            .Where(r => r.ForwardMfe60Percent.HasValue || r.ForwardMae60Percent.HasValue)
            .GroupBy(r => $"{BucketLabel("ForwardMfe60Percent", r.ForwardMfe60Percent)}|{BucketLabel("ForwardMae60Percent", r.ForwardMae60Percent)}", StringComparer.OrdinalIgnoreCase)
            .Select(g => BuildSubsetRow(
                "ForwardMFE60 + ForwardMAE60 buckets",
                g.Key,
                $"ForwardMFE60 {BucketLabel("ForwardMfe60Percent", g.First().ForwardMfe60Percent)} + ForwardMAE60 {BucketLabel("ForwardMae60Percent", g.First().ForwardMae60Percent)}",
                g.ToArray())));

        return all
            .OrderByDescending(x => x.MeetsRobustnessCriteria)
            .ThenByDescending(x => x.NetPnlQuote)
            .ThenByDescending(x => x.Trades)
            .Take(200)
            .ToArray();
    }

    public static IReadOnlyList<MetaOverfitWarningRow> BuildOverfitWarnings(
        IReadOnlyList<MetaStrategyResearchRecord> executed,
        IReadOnlyList<MetaBestSubsetRow> bestSubsets)
    {
        var warnings = new List<MetaOverfitWarningRow>();

        foreach (var subset in bestSubsets.Where(s => !s.MeetsRobustnessCriteria && s.NetPnlQuote > 0m))
        {
            warnings.Add(new MetaOverfitWarningRow
            {
                WarningType = subset.Trades < MinimumRobustTrades ? "SparseSample" : "InsufficientWindows",
                SubsetKey = subset.SubsetKey,
                Trades = subset.Trades,
                WindowCount = subset.WindowCount,
                NetPnlQuote = subset.NetPnlQuote,
                Message = subset.Trades < MinimumRobustTrades
                    ? $"Positive net with only {subset.Trades} trades (<{MinimumRobustTrades})."
                    : $"Positive net but only {subset.WindowCount} window(s) represented (<{MinimumRobustWindows})."
            });
        }

        foreach (var group in executed.GroupBy(r => $"{r.StrategyFamily}|{r.ProfileName}|{r.Symbol}|{r.Interval}", StringComparer.OrdinalIgnoreCase))
        {
            var trades = group.Count();
            if (trades >= MinimumRobustTrades)
                continue;

            var net = group.Sum(x => x.NetPnlQuote ?? 0m);
            if (net <= 0m)
                continue;

            warnings.Add(new MetaOverfitWarningRow
            {
                WarningType = "ProfilePositiveSparse",
                SubsetKey = group.Key,
                Trades = trades,
                WindowCount = group.Select(x => x.WindowLabel).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                NetPnlQuote = net,
                Message = $"Profile-level positive net ({net:F8}) with only {trades} trades."
            });
        }

        return warnings
            .OrderByDescending(x => x.NetPnlQuote)
            .ThenBy(x => x.Trades)
            .ToArray();
    }

    public static IReadOnlyList<MetaRuleDiscoveryRow> BuildEntryTimeRuleDiscovery(IReadOnlyList<MetaStrategyResearchRecord> executed)
        => BuildRuleDiscoveryInternal(
            executed,
            EntryTimeRuleFeaturePairs,
            ruleGroup: "EntryTime",
            usesFutureInformation: false,
            tradableRule: true);

    public static IReadOnlyList<MetaRuleDiscoveryRow> BuildOutcomeDiagnosticRuleDiscovery(IReadOnlyList<MetaStrategyResearchRecord> executed)
        => BuildRuleDiscoveryInternal(
            executed,
            OutcomeDiagnosticRuleFeaturePairs,
            ruleGroup: "OutcomeDiagnostic",
            usesFutureInformation: true,
            tradableRule: false);

    private static IReadOnlyList<MetaRuleDiscoveryRow> BuildRuleDiscoveryInternal(
        IReadOnlyList<MetaStrategyResearchRecord> executed,
        IReadOnlyList<(string FeatureA, string FeatureB)> featurePairs,
        string ruleGroup,
        bool usesFutureInformation,
        bool tradableRule)
    {
        if (executed.Count < MinimumRobustTrades)
            return [];

        var trainWindows = new HashSet<string>(["30d", "60d"], StringComparer.OrdinalIgnoreCase);
        var holdoutWindows = new HashSet<string>(["90d"], StringComparer.OrdinalIgnoreCase);
        var train = executed.Where(r => trainWindows.Contains(r.WindowLabel)).ToArray();
        var holdout = executed.Where(r => holdoutWindows.Contains(r.WindowLabel)).ToArray();
        if (train.Length < MinimumRobustTrades || holdout.Length < 10)
            return [];

        var rules = new List<MetaRuleDiscoveryRow>();
        foreach (var (featureA, featureB) in featurePairs)
        {
            if (!IsFeatureAllowedForGroup(featureA, ruleGroup) || !IsFeatureAllowedForGroup(featureB, ruleGroup))
                continue;

            var trainBucketsA = BuildQuantileBuckets(
                train.Select(r => GetFeatureValue(r, featureA)).Where(v => v.HasValue).Select(v => v!.Value).ToArray(), 3);
            var trainBucketsB = BuildQuantileBuckets(
                train.Select(r => GetFeatureValue(r, featureB)).Where(v => v.HasValue).Select(v => v!.Value).ToArray(), 3);
            if (trainBucketsA.Count == 0 || trainBucketsB.Count == 0)
                continue;

            MetaRuleDiscoveryRow? best = null;
            foreach (var bucketA in trainBucketsA)
            {
                foreach (var bucketB in trainBucketsB)
                {
                    var trainSubset = train.Where(r =>
                        InBucket(GetFeatureValue(r, featureA), bucketA)
                        && InBucket(GetFeatureValue(r, featureB), bucketB)).ToArray();
                    if (trainSubset.Length < MinimumRobustTrades)
                        continue;

                    var holdoutSubset = holdout.Where(r =>
                        InBucket(GetFeatureValue(r, featureA), bucketA)
                        && InBucket(GetFeatureValue(r, featureB), bucketB)).ToArray();
                    if (holdoutSubset.Length < 10)
                        continue;

                    var trainNet = trainSubset.Sum(x => x.NetPnlQuote ?? 0m);
                    var holdoutNet = holdoutSubset.Sum(x => x.NetPnlQuote ?? 0m);
                    var row = new MetaRuleDiscoveryRow
                    {
                        RuleGroup = ruleGroup,
                        RuleDescription = $"{featureA} {bucketA.Label} AND {featureB} {bucketB.Label}",
                        FeaturesUsed = [featureA, featureB],
                        UsesFutureInformation = usesFutureInformation,
                        TradableRule = tradableRule,
                        TrainWindows = string.Join(",", trainWindows),
                        HoldoutWindows = string.Join(",", holdoutWindows),
                        TrainTrades = trainSubset.Length,
                        HoldoutTrades = holdoutSubset.Length,
                        TrainNetPnlQuote = trainNet,
                        HoldoutNetPnlQuote = holdoutNet,
                        TrainNetPerTrade = Math.Round(trainNet / trainSubset.Length, 8),
                        HoldoutNetPerTrade = Math.Round(holdoutNet / holdoutSubset.Length, 8),
                        Verdict = holdoutNet >= 0m ? "HoldoutNonNegative" : "HoldoutNegative"
                    };

                    if (best is null || row.HoldoutNetPnlQuote > best.HoldoutNetPnlQuote)
                        best = row;
                }
            }

            if (best is not null)
                rules.Add(best);
        }

        return rules
            .OrderByDescending(r => r.HoldoutNetPnlQuote)
            .ThenByDescending(r => r.TrainNetPnlQuote)
            .ToArray();
    }

    private static bool IsFeatureAllowedForGroup(string featureName, string ruleGroup)
        => ruleGroup switch
        {
            "EntryTime" => EntryTimeNumericFeatures.Contains(featureName, StringComparer.Ordinal),
            "OutcomeDiagnostic" => OutcomeDiagnosticNumericFeatures.Contains(featureName, StringComparer.Ordinal),
            _ => false
        };

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildResearchAnswers(
        IReadOnlyList<MetaStrategyResearchRecord> allRecords,
        IReadOnlyList<MetaStrategyResearchRecord> executed,
        IReadOnlyList<MetaStrategyFamilySummaryRow> familySummary,
        IReadOnlyList<MetaSymbolIntervalSummaryRow> symbolIntervalSummary,
        IReadOnlyList<MetaFeatureBucketSummaryRow> featureBuckets,
        IReadOnlyList<MetaExitReasonSummaryRow> exitReasonSummary,
        IReadOnlyList<MetaBestSubsetRow> bestSubsets,
        IReadOnlyList<MetaOverfitWarningRow> overfitWarnings,
        IReadOnlyList<MetaRuleDiscoveryRow> entryTimeRuleDiscovery,
        IReadOnlyList<MetaRuleDiscoveryRow> outcomeDiagnosticRuleDiscovery,
        MetaStrategyResearchImportReport importReport)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var robustPositive = symbolIntervalSummary
            .Where(x => x.MeetsMinimumSample && x.NetPnlQuote >= 0m)
            .ToArray();
        var robustNearBreakeven = symbolIntervalSummary
            .Where(x => x.MeetsMinimumSample && x.NetPerTrade >= -0.001m)
            .ToArray();

        var tradableEntryTimeRules = entryTimeRuleDiscovery
            .Where(r => r.TradableRule && !r.UsesFutureInformation)
            .ToArray();
        var robustEntryTimeSubsets = bestSubsets
            .Where(x => x.MeetsRobustnessCriteria && x.NetPnlQuote >= -0.05m)
            .ToArray();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Across all imported families, is there any entry-time subset with >=50 trades, 3 windows, and non-negative or near-breakeven net?",
            Answer = robustPositive.Length > 0
                ? $"{robustPositive.Length} symbol/interval subset(s) have net >= 0 with >= {MinimumRobustTrades} trades and >= {MinimumRobustWindows} windows."
                : robustNearBreakeven.Length > 0
                    ? $"No non-negative subset; {robustNearBreakeven.Length} near-breakeven subset(s) (net/trade >= -0.001) met sample/window criteria."
                    : $"No entry-time subset met >= {MinimumRobustTrades} trades, >= {MinimumRobustWindows} windows, and non-negative/near-breakeven net.",
            Verdict = robustPositive.Length > 0 ? "EntryTimeRobustPositiveSubset" : robustNearBreakeven.Length > 0 ? "EntryTimeNearBreakevenOnly" : "NoEntryTimeRobustSubset",
            Details = new Dictionary<string, object?>
            {
                ["topCandidates"] = symbolIntervalSummary.Where(x => x.MeetsMinimumSample).Take(10).ToArray(),
                ["nearBreakeven"] = robustNearBreakeven.Take(10).ToArray()
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Do any entry-time rules survive without future information?",
            Answer = tradableEntryTimeRules.Any(r => r.HoldoutNetPnlQuote >= 0m && r.TrainTrades >= MinimumRobustTrades)
                ? $"{tradableEntryTimeRules.Count(r => r.HoldoutNetPnlQuote >= 0m)} tradable entry-time rule(s) have non-negative 90d holdout."
                : tradableEntryTimeRules.Length == 0
                    ? "No entry-time quantile rules met minimum train/holdout sample thresholds."
                    : "Entry-time rules exist but all have negative 90d holdout under train 30d/60d split.",
            Verdict = tradableEntryTimeRules.Any(r => r.HoldoutNetPnlQuote >= 0m && r.TrainTrades >= MinimumRobustTrades)
                ? "TradableEntryTimeRuleFound"
                : "NoTradableEntryTimeRule",
            Details = new Dictionary<string, object?>
            {
                ["entryTimeRules"] = tradableEntryTimeRules.Take(10).ToArray(),
                ["outcomeDiagnosticRulesExcluded"] = outcomeDiagnosticRuleDiscovery.Count
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Across all families, is there any symbol/interval with positive net and meaningful sample?",
            Answer = robustPositive.Length > 0
                ? $"{robustPositive.Length} symbol/interval subset(s) have net >= 0 with >= {MinimumRobustTrades} trades and >= {MinimumRobustWindows} windows."
                : $"No symbol/interval subset met >= {MinimumRobustTrades} trades, >= {MinimumRobustWindows} windows, and non-negative net.",
            Verdict = robustPositive.Length > 0 ? "RobustPositiveSubsetFound" : "NoRobustPositiveSubset",
            Details = new Dictionary<string, object?>
            {
                ["topCandidates"] = symbolIntervalSummary.Where(x => x.MeetsMinimumSample).Take(10).ToArray(),
                ["nearBreakeven"] = robustNearBreakeven.Take(10).ToArray()
            }
        });

        var stopLoss = executed.Where(r => string.Equals(r.ExitReason, "StopLoss", StringComparison.OrdinalIgnoreCase)).ToArray();
        var stopLossFeature = featureBuckets
            .Where(x => x.FeatureName is "StopDistancePercent" or "ForwardMae60Percent" or "BreakoutBodyStrengthPercent")
            .OrderByDescending(x => x.StopLossRate)
            .Take(6)
            .ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are stop-loss losers predictable from entry-time features?",
            Answer = stopLoss.Length == 0
                ? "No stop-loss exits in imported executed trades."
                : $"Stop-loss exits={stopLoss.Length} ({(executed.Count == 0 ? 0m : Math.Round((decimal)stopLoss.Length / executed.Count, 6)):P1} of executed). Highest stop-loss buckets: {string.Join("; ", stopLossFeature.Select(x => $"{x.FeatureName} {x.BucketLabel}={x.StopLossRate:P1}"))}.",
            Verdict = stopLossFeature.Any(x => x.StopLossRate >= 0.65m && x.Trades >= 30) ? "StopLossFeatureSignal" : "StopLossDominantButWeakFeatureSeparation",
            Details = new Dictionary<string, object?> { ["stopLossFeatureBuckets"] = stopLossFeature }
        });

        var winnerBuckets = featureBuckets
            .Where(x => x.Trades >= 30)
            .OrderByDescending(x => x.NetWinnerRate)
            .Take(8)
            .ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which features repeatedly appear in winners across different strategy families?",
            Answer = winnerBuckets.Length == 0
                ? "Insufficient cross-family bucket data."
                : string.Join("; ", winnerBuckets.Select(x => $"{x.FeatureName} {x.BucketLabel} netWinnerRate={x.NetWinnerRate:P1} trades={x.Trades}")),
            Verdict = winnerBuckets.Any(x => x.NetWinnerRate >= 0.45m) ? "RepeatedWinnerFeatures" : "NoStrongCrossFamilyWinnerFeatures",
            Details = new Dictionary<string, object?> { ["topWinnerBuckets"] = winnerBuckets }
        });

        var totalNet = executed.Sum(x => x.NetPnlQuote ?? 0m);
        var totalGross = executed.Sum(x => x.GrossPnlQuote ?? 0m);
        var feeDrag = totalGross - totalNet;
        var stopNet = exitReasonSummary.Where(x => string.Equals(x.ExitReason, "StopLoss", StringComparison.OrdinalIgnoreCase)).Sum(x => x.NetPnlQuote);
        var profitNet = exitReasonSummary.Where(x => ProfitExitReasons.Contains(x.ExitReason, StringComparer.OrdinalIgnoreCase)).Sum(x => x.NetPnlQuote);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is the problem mainly Spot fee/spread, bad stop placement, or bad candidate generation?",
            Answer = $"Gross={totalGross:F4}, Net={totalNet:F4}, fee/spread drag≈{feeDrag:F4}. StopLoss net={stopNet:F4}, profit-exit net={profitNet:F4}. Blocked candidates imported={allRecords.Count(x => !x.CandidateWasExecuted)}.",
            Verdict = Math.Abs(stopNet) > Math.Abs(feeDrag) && Math.Abs(stopNet) > Math.Abs(profitNet)
                ? "StopPlacementDominant"
                : feeDrag > Math.Abs(totalGross) * 0.5m ? "SpotCostDominant" : "CandidateGenerationWeak",
            Details = new Dictionary<string, object?> { ["exitReasonSummary"] = exitReasonSummary.Take(12).ToArray() }
        });

        var robustSubsets = bestSubsets.Where(x => x.MeetsRobustnessCriteria).ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does any subset survive conservative Spot costs?",
            Answer = robustSubsets.Any(x => x.NetPnlQuote >= 0m)
                ? $"{robustSubsets.Count(x => x.NetPnlQuote >= 0m)} robust subset(s) are non-negative under imported Spot-cost backtests."
                : $"No robust subset (>= {MinimumRobustTrades} trades, >= {MinimumRobustWindows} windows) is non-negative.",
            Verdict = robustSubsets.Any(x => x.NetPnlQuote >= 0m) ? "SpotSubsetSurvives" : "NoSpotSubsetSurvives",
            Details = new Dictionary<string, object?> { ["robustSubsets"] = robustSubsets.Take(15).ToArray() }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Should we continue strategy research in Spot, or move future research to lower-fee/Futures simulation only?",
            Answer = totalNet < 0m && robustPositive.Length == 0
                ? "Current families remain net-negative at Spot costs with no robust positive subset. Prioritize lower-fee/Futures-sim feasibility for signal validation, while using meta-features to redesign entries/exits."
                : "Some subsets show viability; continue Spot research only on validated robust slices.",
            Verdict = totalNet < 0m && robustPositive.Length == 0 ? "PivotToFuturesSim" : "ContinueSelectiveSpotResearch",
            Details = new Dictionary<string, object?> { ["familySummary"] = familySummary }
        });

        var leastBadFamily = familySummary
            .OrderByDescending(x => x.NetPerTrade)
            .FirstOrDefault();
        var nextFamilyHint = symbolIntervalSummary
            .Where(x => x.MeetsMinimumSample)
            .OrderByDescending(x => x.NetPerTrade)
            .FirstOrDefault();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which family is least bad after meaningful-sample filtering?",
            Answer = leastBadFamily is null
                ? "No family data imported."
                : $"{leastBadFamily.StrategyFamily}: net/trade={leastBadFamily.NetPerTrade:F6}, trades={leastBadFamily.Trades}, stopLossRate={leastBadFamily.StopLossRate:P1}. Best slice: {nextFamilyHint?.StrategyFamily} {nextFamilyHint?.Symbol} {nextFamilyHint?.Interval} net/trade={nextFamilyHint?.NetPerTrade:F6}.",
            Verdict = "LeastBadFamilyIdentified",
            Details = new Dictionary<string, object?> { ["familySummary"] = familySummary }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are losses mostly strategy-specific, or universal stop-loss/fee drag?",
            Answer = familySummary.Count <= 1
                ? $"Single family imported; stopLossRate={familySummary.FirstOrDefault()?.StopLossRate:P1}, fee drag≈{feeDrag:F4} on gross={totalGross:F4}."
                : $"Across {familySummary.Count} families: stop-loss share and fee drag vary by family; compare meta-exit-reason-summary and family summary.",
            Verdict = Math.Abs(stopNet) > feeDrag ? "UniversalStopLossDrag" : "MixedStrategyAndCostDrag",
            Details = new Dictionary<string, object?> { ["familySummary"] = familySummary, ["exitReasonSummary"] = exitReasonSummary.Take(20).ToArray() }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does any strategy deserve V2 research, or should we move to new models / lower-fee simulation?",
            Answer = robustPositive.Length > 0 || tradableEntryTimeRules.Any(r => r.HoldoutNetPnlQuote >= 0m)
                ? "Targeted V2 on robust entry-time slices only."
                : "No entry-time edge under Spot costs; pivot to new model design and lower-fee/Futures-sim validation. Outcome-diagnostic rules are explanatory only.",
            Verdict = robustPositive.Length > 0 ? "SelectiveV2Warranted" : "NewModelsOrFuturesSim",
            Details = new Dictionary<string, object?>
            {
                ["entryTimeRules"] = tradableEntryTimeRules.Take(5).ToArray(),
                ["outcomeDiagnosticRules"] = outcomeDiagnosticRuleDiscovery.Take(5).ToArray(),
                ["robustEntryTimeSubsets"] = robustEntryTimeSubsets.Take(10).ToArray()
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "What is the next best strategy family to test based on entry-time feature evidence?",
            Answer = nextFamilyHint is null
                ? "No meaningful-sample symbol/interval slice; design next family around lower stop-loss rate and smaller required gross move."
                : $"Best meaningful-sample slice: {nextFamilyHint.StrategyFamily} {nextFamilyHint.Symbol} {nextFamilyHint.Interval} net/trade={nextFamilyHint.NetPerTrade:F6}, stopLossRate={nextFamilyHint.StopLossRate:P1}. Use entry-time buckets only for tradable filters.",
            Verdict = "FeatureGuidedNextFamily",
            Details = new Dictionary<string, object?>
            {
                ["importSources"] = importReport.Sources,
                ["overfitWarnings"] = overfitWarnings.Take(10).ToArray(),
                ["entryTimeFeatureBuckets"] = featureBuckets.Where(x => EntryTimeNumericFeatures.Contains(x.FeatureName, StringComparer.Ordinal)).Take(12).ToArray()
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Meta-research import coverage",
            Answer = string.Join("; ", importReport.Sources.Select(s =>
                $"{Path.GetFileName(s.Directory)}:{s.StrategyFamily} executed={s.ExecutedImported} blocked={s.BlockedImported}")),
            Verdict = importReport.Sources.Any(s => s.ExecutedImported > 0) ? "ImportOk" : "ImportEmpty",
            Details = new Dictionary<string, object?> { ["importReport"] = importReport }
        });

        return answers;
    }

    private static MetaBestSubsetRow BuildSubsetRow(string subsetType, string key, string description, IReadOnlyList<MetaStrategyResearchRecord> rows)
    {
        var trades = rows.Count;
        var net = rows.Sum(x => x.NetPnlQuote ?? 0m);
        var windows = rows.Select(x => x.WindowLabel).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        var windowCount = windows.Length;
        var meets = trades >= MinimumRobustTrades && windowCount >= MinimumRobustWindows;
        return new MetaBestSubsetRow
        {
            SubsetKey = $"{subsetType}|{key}",
            RuleDescription = description,
            Trades = trades,
            WindowCount = windowCount,
            WindowsRepresented = windows,
            NetPnlQuote = net,
            NetPerTrade = trades == 0 ? 0m : Math.Round(net / trades, 8),
            NetWinnerRate = trades == 0 ? 0m : Math.Round((decimal)rows.Count(x => x.IsNetWinner == true) / trades, 6),
            StopLossRate = Rate(rows, "StopLoss"),
            MeetsRobustnessCriteria = meets,
            Verdict = meets
                ? net >= 0m ? "RobustNonNegative" : "RobustNegative"
                : trades < MinimumRobustTrades ? "Sparse" : "SingleWindowRisk"
        };
    }

    private static MetaFeatureBucketSummaryRow BuildFeatureBucketRow(
        string feature,
        string label,
        int index,
        decimal min,
        decimal max,
        IReadOnlyList<MetaStrategyResearchRecord> rows)
    {
        var trades = rows.Count;
        var net = rows.Sum(x => x.NetPnlQuote ?? 0m);
        return new MetaFeatureBucketSummaryRow
        {
            FeatureName = feature,
            BucketLabel = label,
            BucketIndex = index,
            BucketMin = min,
            BucketMax = max,
            Trades = trades,
            NetPnlQuote = net,
            NetPerTrade = trades == 0 ? 0m : Math.Round(net / trades, 8),
            NetWinnerRate = trades == 0 ? 0m : Math.Round((decimal)rows.Count(x => x.IsNetWinner == true) / trades, 6),
            ProfitExitRate = ProfitExitRate(rows),
            StopLossRate = Rate(rows, "StopLoss"),
            TimeStopRate = Rate(rows, "TimeStop"),
            MedianMfePercent = Median(rows.Select(x => x.MfePercent)),
            MedianMaePercent = Median(rows.Select(x => x.MaePercent))
        };
    }

    private static decimal Rate(IEnumerable<MetaStrategyResearchRecord> rows, string exitReason)
    {
        var array = rows as IReadOnlyList<MetaStrategyResearchRecord> ?? rows.ToArray();
        if (array.Count == 0)
            return 0m;
        var count = array.Count(x => string.Equals(x.ExitReason, exitReason, StringComparison.OrdinalIgnoreCase));
        return Math.Round((decimal)count / array.Count, 6);
    }

    private static decimal ProfitExitRate(IEnumerable<MetaStrategyResearchRecord> rows)
    {
        var array = rows as IReadOnlyList<MetaStrategyResearchRecord> ?? rows.ToArray();
        if (array.Count == 0)
            return 0m;
        var count = array.Count(x => ProfitExitReasons.Contains(x.ExitReason ?? string.Empty, StringComparer.OrdinalIgnoreCase));
        return Math.Round((decimal)count / array.Count, 6);
    }

    private static decimal? GetFeatureValue(MetaStrategyResearchRecord record, string featureName)
        => featureName switch
        {
            nameof(MetaStrategyResearchRecord.StopDistancePercent) => record.StopDistancePercent,
            nameof(MetaStrategyResearchRecord.BreakoutBodyStrengthPercent) => record.BreakoutBodyStrengthPercent,
            nameof(MetaStrategyResearchRecord.RangeWidthPercent) => record.RangeWidthPercent,
            nameof(MetaStrategyResearchRecord.VolumeExpansionRatio) => record.VolumeExpansionRatio,
            nameof(MetaStrategyResearchRecord.AtrExpansionRatio) => record.AtrExpansionRatio,
            nameof(MetaStrategyResearchRecord.ExpectedMovePercent) => record.ExpectedMovePercent,
            nameof(MetaStrategyResearchRecord.RequiredGrossMovePercent) => record.RequiredGrossMovePercent,
            nameof(MetaStrategyResearchRecord.RewardRisk) => record.RewardRisk,
            nameof(MetaStrategyResearchRecord.ShortMaSlopePercent) => record.ShortMaSlopePercent,
            nameof(MetaStrategyResearchRecord.TrendStrengthPercent) => record.TrendStrengthPercent,
            nameof(MetaStrategyResearchRecord.DistanceToInvalidationPercent) => record.DistanceToInvalidationPercent,
            nameof(MetaStrategyResearchRecord.StopToLockRatio) => record.StopToLockRatio,
            nameof(MetaStrategyResearchRecord.MfePercent) => record.MfePercent,
            nameof(MetaStrategyResearchRecord.MaePercent) => record.MaePercent,
            nameof(MetaStrategyResearchRecord.ForwardMfe15Percent) => record.ForwardMfe15Percent,
            nameof(MetaStrategyResearchRecord.ForwardMfe30Percent) => record.ForwardMfe30Percent,
            nameof(MetaStrategyResearchRecord.ForwardMfe60Percent) => record.ForwardMfe60Percent,
            nameof(MetaStrategyResearchRecord.ForwardMae15Percent) => record.ForwardMae15Percent,
            nameof(MetaStrategyResearchRecord.ForwardMae30Percent) => record.ForwardMae30Percent,
            nameof(MetaStrategyResearchRecord.ForwardMae60Percent) => record.ForwardMae60Percent,
            _ => null
        };

    private sealed record QuantileBucket(string Label, decimal Min, decimal Max);

    private static List<QuantileBucket> BuildQuantileBuckets(IReadOnlyList<decimal> values, int bucketCount)
    {
        if (values.Count == 0)
            return [];

        var sorted = values.OrderBy(x => x).ToArray();
        var buckets = new List<QuantileBucket>();
        for (var i = 0; i < bucketCount; i++)
        {
            var startIdx = (int)Math.Floor(i * (decimal)sorted.Length / bucketCount);
            var endIdx = (int)Math.Floor((i + 1) * (decimal)sorted.Length / bucketCount) - 1;
            if (startIdx < 0) startIdx = 0;
            if (endIdx < startIdx) endIdx = startIdx;
            if (endIdx >= sorted.Length) endIdx = sorted.Length - 1;
            var min = sorted[startIdx];
            var max = sorted[endIdx];
            buckets.Add(new QuantileBucket($"{min:F4}-{max:F4}", min, max));
        }

        return buckets;
    }

    private static bool InBucket(decimal? value, QuantileBucket bucket)
        => value.HasValue && value.Value >= bucket.Min && value.Value <= bucket.Max;

    private static string BucketLabel(string feature, decimal? value)
    {
        if (!value.HasValue)
            return $"{feature}:NA";
        return $"{feature}:{value.Value:F4}";
    }

    private static decimal? Median(IEnumerable<decimal?> values)
    {
        var sorted = values.Where(v => v.HasValue).Select(v => v!.Value).OrderBy(v => v).ToArray();
        if (sorted.Length == 0)
            return null;
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[mid]
            : Math.Round((sorted[mid - 1] + sorted[mid]) / 2m, 8);
    }
}
