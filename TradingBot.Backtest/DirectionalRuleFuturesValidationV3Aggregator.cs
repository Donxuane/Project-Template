using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class DirectionalRuleFuturesValidationV3Aggregator
{
    public const int MinimumMeaningfulTrades = 50;
    private static readonly string[] RollingWindowLabels = ["30d", "60d", "90d", "120d", "180d"];
    private static readonly string[] DrawdownWindowPreference = ["180d", "120d", "90d", "60d", "30d", "holdout30d"];

    public static IReadOnlyDictionary<string, string> LabelDefinitions { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AggregatePositive"] = "True when NetPnlQuote for the row's window and cost scenario is >= 0.",
            ["AllWindowsPositive"] = "True when every configured rolling window (30d-180d present in the run) has net >= 0 for this profile and cost scenario.",
            ["HoldoutPositive"] = "True when holdout30d net >= 0, or 30d net when holdout30d is absent.",
            ["StressPositive"] = "True for stress/latency cost scenarios when the sum of rolling-window nets is >= 0.",
            ["StressAllWindowsPositive"] = "True for stress/latency cost scenarios when every present rolling window net is >= 0.",
            ["TradeCountBelowThreshold"] = "Verdict when executed trades are > 0 but below MinimumMeaningfulTrades (50).",
            ["InsufficientSamples"] = "Verdict when no executed trades are available for the reference window.",
            ["CountMismatch"] = "True when a summary ExecutedTrades count does not match actual trade rows for the same profile/window/cost bucket."
        };

    public static IReadOnlyList<DirectionalRuleV3FocusedSummaryRow> ApplyCrossWindowLabels(
        IReadOnlyList<DirectionalRuleV3FocusedSummaryRow> summaries,
        IReadOnlyList<DirectionalRuleV3WindowRobustnessRow> windowRobustness)
    {
        var robustByKey = windowRobustness.ToDictionary(
            r => $"{r.ProfileKey}|{r.CostScenarioLabel}",
            StringComparer.OrdinalIgnoreCase);

        return summaries.Select(row =>
        {
            robustByKey.TryGetValue($"{row.ProfileKey}|{row.CostScenarioLabel}", out var robust);
            return row with
            {
                AllWindowsPositive = robust?.AllWindowsPositive ?? false,
                HoldoutPositive = robust?.HoldoutPositive ?? false,
                StressPositive = robust?.StressPositive ?? false,
                StressAllWindowsPositive = robust?.StressAllWindowsPositive ?? false,
                AggregatePositive = row.NetPnlQuote >= 0m
            };
        }).ToArray();
    }

    public static IReadOnlyList<DirectionalRuleV3WindowRobustnessRow> BuildWindowRobustness(
        IReadOnlyList<DirectionalRuleV3FocusedSummaryRow> summaries)
    {
        return summaries
            .GroupBy(s => $"{s.ProfileKey}|{s.CostScenarioLabel}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                decimal Net(string label) => g.FirstOrDefault(x => x.WindowLabel == label)?.NetPnlQuote ?? 0m;
                int Trades(string label) => g.FirstOrDefault(x => x.WindowLabel == label)?.ExecutedTrades ?? 0;
                var rollingNets = RollingWindowLabels
                    .Where(l => g.Any(x => x.WindowLabel == l))
                    .Select(Net)
                    .ToArray();
                var allRollingPositive = rollingNets.Length > 0 && rollingNets.All(n => n >= 0m);
                var holdoutNet = Net("holdout30d");
                if (holdoutNet == 0m && g.Any(x => x.WindowLabel == "30d"))
                    holdoutNet = Net("30d");
                var isStress = IsStressScenario(first.CostScenarioLabel);
                var aggregateRolling = rollingNets.Sum();
                var row = new DirectionalRuleV3WindowRobustnessRow
                {
                    ProfileKey = first.ProfileKey,
                    VariantLabel = first.VariantLabel,
                    IsPrimaryCandidate = first.IsPrimaryCandidate,
                    IsSmokeBestCandidate = first.IsSmokeBestCandidate,
                    EntryMode = first.EntryMode,
                    OverlapPolicy = first.OverlapPolicy,
                    CooldownCandlesAfterExit = first.CooldownCandlesAfterExit,
                    TargetPercent = first.TargetPercent,
                    StopPercent = first.StopPercent,
                    MaxHoldMinutes = first.MaxHoldMinutes,
                    CostScenarioLabel = first.CostScenarioLabel,
                    Window30dTrades = Trades("30d"),
                    Window60dTrades = Trades("60d"),
                    Window90dTrades = Trades("90d"),
                    Window120dTrades = Trades("120d"),
                    Window180dTrades = Trades("180d"),
                    Holdout30dTrades = Trades("holdout30d") > 0 ? Trades("holdout30d") : Trades("30d"),
                    TrainReferenceTrades = Trades("trainReference"),
                    Window30dNetPnl = Net("30d"),
                    Window60dNetPnl = Net("60d"),
                    Window90dNetPnl = Net("90d"),
                    Window120dNetPnl = Net("120d"),
                    Window180dNetPnl = Net("180d"),
                    Holdout30dNetPnl = holdoutNet,
                    TrainReferenceNetPnl = Net("trainReference"),
                    AggregateNetPnl = aggregateRolling,
                    AggregatePositive = aggregateRolling >= 0m,
                    AllWindowsPositive = allRollingPositive,
                    HoldoutPositive = holdoutNet >= 0m,
                    StressPositive = isStress && aggregateRolling >= 0m,
                    StressAllWindowsPositive = isStress && allRollingPositive
                };
                return row with
                {
                    RobustnessVerdict = ClassifyRobustnessVerdict(row, ResolveReferenceTradeCount(row))
                };
            })
            .OrderBy(r => r.ProfileKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CostScenarioLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<DirectionalRuleV3CostSensitivityRow> BuildCostSensitivity(
        IReadOnlyList<DirectionalRuleV3WindowRobustnessRow> windowRobustness)
    {
        var scenarios = DirectionalRuleFuturesValidationV3CostModel.BuildValidationScenarios()
            .ToDictionary(s => s.Label, StringComparer.OrdinalIgnoreCase);

        return windowRobustness
            .Select(row =>
            {
                scenarios.TryGetValue(row.CostScenarioLabel, out var scenario);
                scenario ??= DirectionalRuleFuturesValidationV3CostModel.BuildValidationScenarios().First();
                var trades = ResolveReferenceTradeCount(row);
                var avg = trades == 0 ? (decimal?)null : Math.Round(row.AggregateNetPnl / trades, 8);
                return new DirectionalRuleV3CostSensitivityRow
                {
                    ProfileKey = row.ProfileKey,
                    VariantLabel = row.VariantLabel,
                    IsPrimaryCandidate = row.IsPrimaryCandidate,
                    IsSmokeBestCandidate = row.IsSmokeBestCandidate,
                    EntryMode = row.EntryMode,
                    OverlapPolicy = row.OverlapPolicy,
                    CooldownCandlesAfterExit = row.CooldownCandlesAfterExit,
                    MaxHoldMinutes = row.MaxHoldMinutes,
                    TargetPercent = row.TargetPercent,
                    StopPercent = row.StopPercent,
                    CostScenarioLabel = row.CostScenarioLabel,
                    RoundTripCostPercent = DirectionalRuleFuturesValidationV3CostModel.EstimateRoundTripCostPercent(scenario),
                    ExtraAdverseSlippagePercentPerSide = scenario.ExtraAdverseSlippagePercentPerSide,
                    TradeCount = trades,
                    NetPnlQuote = row.AggregateNetPnl,
                    AvgNetPnlPerTrade = avg,
                    AggregatePositive = row.AggregatePositive,
                    StressPositive = row.StressPositive,
                    StressAllWindowsPositive = row.StressAllWindowsPositive,
                    Verdict = ClassifySummaryVerdict(trades, row.AggregateNetPnl, avg ?? 0m)
                };
            })
            .ToArray();
    }

    public static IReadOnlyList<DirectionalRuleV3DrawdownRow> ApplyWorstWindowNet(
        IReadOnlyList<DirectionalRuleV3DrawdownRow> drawdown,
        IReadOnlyList<DirectionalRuleV3WindowRobustnessRow> windowRobustness)
    {
        var worstByProfile = windowRobustness
            .Where(r => string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                r => r.ProfileKey,
                r =>
                {
                    var nets = RollingWindowLabels
                        .Select(label => label switch
                        {
                            "30d" => r.Window30dNetPnl,
                            "60d" => r.Window60dNetPnl,
                            "90d" => r.Window90dNetPnl,
                            "120d" => r.Window120dNetPnl,
                            "180d" => r.Window180dNetPnl,
                            _ => 0m
                        })
                        .Where(n => n != 0m)
                        .ToArray();
                    return nets.Length == 0 ? 0m : nets.Min();
                },
                StringComparer.OrdinalIgnoreCase);

        return drawdown.Select(row =>
        {
            worstByProfile.TryGetValue(row.ProfileKey, out var worst);
            return row with { WorstWindowNet = worst };
        }).ToArray();
    }

    public static IReadOnlyList<DirectionalRuleV3VariantComparisonRow> BuildVariantComparison(
        IReadOnlyList<DirectionalRuleV3WindowRobustnessRow> windowRobustness,
        IReadOnlyList<DirectionalRuleV3DrawdownRow> drawdown)
    {
        var moderate = windowRobustness
            .Where(r => string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var drawdownByKey = drawdown
            .Where(d => string.Equals(d.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase))
            .GroupBy(d => $"{d.ProfileKey}|{d.WindowLabel}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return moderate.Select(row =>
        {
            var dd = ResolveDrawdownRow(row, drawdownByKey);
            var latencyKey = windowRobustness.FirstOrDefault(r =>
                r.ProfileKey == row.ProfileKey
                && string.Equals(r.CostScenarioLabel, "futures-moderate-latency-002", StringComparison.OrdinalIgnoreCase));

            var trades = ResolveReferenceTradeCount(row);
            var verdict = ClassifyVariantVerdict(row, trades, latencyKey);

            return new DirectionalRuleV3VariantComparisonRow
            {
                ProfileKey = row.ProfileKey,
                VariantLabel = row.VariantLabel,
                IsPrimaryCandidate = row.IsPrimaryCandidate,
                IsSmokeBestCandidate = row.IsSmokeBestCandidate,
                EntryMode = row.EntryMode,
                OverlapPolicy = row.OverlapPolicy,
                CooldownCandlesAfterExit = row.CooldownCandlesAfterExit,
                MaxHoldMinutes = row.MaxHoldMinutes,
                TargetPercent = row.TargetPercent,
                StopPercent = row.StopPercent,
                ExecutedTrades = trades,
                AggregateNetPnl = row.AggregateNetPnl,
                Holdout30dNetPnl = row.Holdout30dNetPnl,
                Window90dNetPnl = row.Window90dNetPnl,
                MaxDrawdownQuote = dd?.MaxDrawdownQuote ?? 0m,
                MaxConsecutiveLosses = dd?.MaxConsecutiveLosses ?? 0,
                AllRollingWindowsPositive = row.AllWindowsPositive,
                HoldoutPositive = row.HoldoutPositive,
                StressModerateLatencyPositive = latencyKey?.AggregatePositive ?? false,
                ComparisonVerdict = verdict
            };
        })
        .OrderByDescending(r => r.IsPrimaryCandidate)
        .ThenByDescending(r => r.IsSmokeBestCandidate)
        .ThenByDescending(r => r.AggregateNetPnl)
        .ToArray();
    }

    public static IReadOnlyList<DirectionalRuleV3ReportConsistencyRow> BuildReportConsistency(
        IReadOnlyList<DirectionalRuleV3FocusedSummaryRow> summaries,
        IReadOnlyList<DirectionalRuleV3TradeRecord> trades,
        IReadOnlyList<int> configuredRollingWindows)
    {
        var expectedCostScenarios = DirectionalRuleFuturesValidationV3CostModel.BuildValidationScenarios()
            .Select(s => s.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedWindowLabels = configuredRollingWindows
            .Select(d => $"{d}d")
            .Concat(["holdout30d", "trainReference"])
            .ToArray();

        var actualCounts = trades
            .Where(t => !string.Equals(t.ExitReason, "InvalidEntry", StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                t => $"{t.ProfileKey}|{t.WindowLabel}|{t.CostScenarioLabel}",
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var exitReasonsByBucket = trades
            .GroupBy(
                t => $"{t.ProfileKey}|{t.WindowLabel}|{t.CostScenarioLabel}",
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(t => t.ExitReason).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var profiles = summaries
            .Select(s => s.ProfileKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rows = new List<DirectionalRuleV3ReportConsistencyRow>();
        foreach (var summary in summaries)
        {
            var bucketKey = $"{summary.ProfileKey}|{summary.WindowLabel}|{summary.CostScenarioLabel}";
            actualCounts.TryGetValue(bucketKey, out var actualCount);
            var missingWindows = string.Join(';', expectedWindowLabels.Where(label =>
                !summaries.Any(s =>
                    s.ProfileKey == summary.ProfileKey
                    && string.Equals(s.WindowLabel, label, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(s.CostScenarioLabel, summary.CostScenarioLabel, StringComparison.OrdinalIgnoreCase))));
            var missingCosts = string.Join(';', expectedCostScenarios.Where(label =>
                !summaries.Any(s =>
                    s.ProfileKey == summary.ProfileKey
                    && string.Equals(s.WindowLabel, summary.WindowLabel, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(s.CostScenarioLabel, label, StringComparison.OrdinalIgnoreCase))));
            exitReasonsByBucket.TryGetValue(bucketKey, out var exitReasons);
            rows.Add(new DirectionalRuleV3ReportConsistencyRow
            {
                ProfileKey = summary.ProfileKey,
                VariantLabel = summary.VariantLabel,
                WindowLabel = summary.WindowLabel,
                CostScenarioLabel = summary.CostScenarioLabel,
                ReportedTradeCount = summary.ExecutedTrades,
                ActualTradeRowCount = actualCount,
                CountMismatch = summary.ExecutedTrades != actualCount,
                MissingWindowLabels = missingWindows,
                MissingCostScenarioLabels = missingCosts,
                MissingExitReasons = exitReasons is null || exitReasons.Length == 0 ? "None" : string.Join(';', exitReasons)
            });
        }

        foreach (var profileKey in profiles)
        {
            foreach (var windowLabel in expectedWindowLabels)
            {
                foreach (var costLabel in expectedCostScenarios)
                {
                    if (summaries.Any(s =>
                            s.ProfileKey == profileKey
                            && string.Equals(s.WindowLabel, windowLabel, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(s.CostScenarioLabel, costLabel, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var bucketKey = $"{profileKey}|{windowLabel}|{costLabel}";
                    actualCounts.TryGetValue(bucketKey, out var actualCount);
                    rows.Add(new DirectionalRuleV3ReportConsistencyRow
                    {
                        ProfileKey = profileKey,
                        VariantLabel = summaries.FirstOrDefault(s => s.ProfileKey == profileKey)?.VariantLabel ?? profileKey,
                        WindowLabel = windowLabel,
                        CostScenarioLabel = costLabel,
                        ReportedTradeCount = 0,
                        ActualTradeRowCount = actualCount,
                        CountMismatch = actualCount != 0,
                        MissingWindowLabels = windowLabel,
                        MissingCostScenarioLabels = costLabel,
                        MissingExitReasons = "NoSummaryRow"
                    });
                }
            }
        }

        return rows
            .OrderBy(r => r.ProfileKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.WindowLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CostScenarioLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildResearchAnswers(
        IReadOnlyList<DirectionalRuleV3VariantComparisonRow> variants,
        IReadOnlyList<DirectionalRuleV3WindowRobustnessRow> windowRobustness,
        IReadOnlyList<DirectionalRuleV3DrawdownRow> drawdown,
        IReadOnlyList<DirectionalRuleV3ReportConsistencyRow> reportConsistency,
        long expandedTradeRowCount,
        long skippedSignalCount,
        int historyDaysAvailable)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var primary = variants.Where(v => v.IsPrimaryCandidate).OrderByDescending(v => v.AggregateNetPnl).FirstOrDefault();
        var smokeBest = variants.Where(v => v.IsSmokeBestCandidate).OrderByDescending(v => v.AggregateNetPnl).FirstOrDefault();
        var primaryModerate = windowRobustness.FirstOrDefault(r =>
            r.IsPrimaryCandidate
            && string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase));
        var smokeModerate = windowRobustness.FirstOrDefault(r =>
            r.IsSmokeBestCandidate
            && string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase));
        var mismatchCount = reportConsistency.Count(r => r.CountMismatch);

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does the primary BNB 5m Rule01 short candidate survive 90d validation?",
            Answer = primary is { ExecutedTrades: >= MinimumMeaningfulTrades, AggregateNetPnl: >= 0m, AllRollingWindowsPositive: true }
                ? $"Primary candidate ({primary.VariantLabel}) passed moderate aggregate and all rolling windows with {primary.ExecutedTrades} trades."
                : primary is null
                    ? "Primary candidate profile not found in results."
                    : $"Primary candidate net={primary.AggregateNetPnl:F2}, trades={primary.ExecutedTrades}, allWindows={primary.AllRollingWindowsPositive}, holdout={primary.HoldoutPositive}.",
            Verdict = primary is { ExecutedTrades: >= MinimumMeaningfulTrades, AggregateNetPnl: >= 0m, AllRollingWindowsPositive: true, HoldoutPositive: true }
                ? "FocusedValidationSurvives"
                : primary is { ExecutedTrades: > 0 and < MinimumMeaningfulTrades }
                    ? "TradeCountBelowThreshold"
                    : "FocusedValidationFails",
            Details = new Dictionary<string, object?>
            {
                ["primary"] = primary,
                ["labelDefinitions"] = LabelDefinitions
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does the smoke-best 1.75/1.00 hold8 cooldown6 variant outperform primary across windows?",
            Answer = primary is null || smokeBest is null
                ? "Primary or smoke-best row missing."
                : $"Primary aggregate={primary.AggregateNetPnl:F2}, smoke-best aggregate={smokeBest.AggregateNetPnl:F2}; 30d={smokeModerate?.Window30dNetPnl:F2} vs {primaryModerate?.Window30dNetPnl:F2}, 60d={smokeModerate?.Window60dNetPnl:F2} vs {primaryModerate?.Window60dNetPnl:F2}, 90d={smokeModerate?.Window90dNetPnl:F2} vs {primaryModerate?.Window90dNetPnl:F2}.",
            Verdict = smokeBest is { AggregateNetPnl: > 0m } && primary is not null && smokeBest.AggregateNetPnl > primary.AggregateNetPnl
                ? "SmokeBestOutperformsPrimary"
                : "PrimaryEqualOrBetter",
            Details = new Dictionary<string, object?> { ["primary"] = primary, ["smokeBest"] = smokeBest }
        });

        AddStressAnswer(answers, windowRobustness, "futures-stress", "Does either variant survive futures-stress?");
        AddStressAnswer(answers, windowRobustness, "futures-stress-plus", "Does either survive stress-plus?");

        var latencyRows = windowRobustness.Where(r =>
            (r.IsPrimaryCandidate || r.IsSmokeBestCandidate)
            && (r.CostScenarioLabel.Contains("latency", StringComparison.OrdinalIgnoreCase))).ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does either survive high latency/slippage?",
            Answer = latencyRows.Length == 0
                ? "No latency rows."
                : string.Join("; ", latencyRows.Select(s => $"{s.VariantLabel}/{s.CostScenarioLabel}: agg={s.AggregateNetPnl:F2}")),
            Verdict = latencyRows.Any(r => (r.IsPrimaryCandidate || r.IsSmokeBestCandidate) && r.AggregatePositive)
                ? "LatencyStressSurvives"
                : "LatencyStressFails",
            Details = new Dictionary<string, object?> { ["latencyRows"] = latencyRows }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are profits stable across windows or concentrated in one period?",
            Answer = primaryModerate is null
                ? "No primary moderate row."
                : $"30d={primaryModerate.Window30dNetPnl:F2}, 60d={primaryModerate.Window60dNetPnl:F2}, 90d={primaryModerate.Window90dNetPnl:F2}, trainRef={primaryModerate.TrainReferenceNetPnl:F2}, holdout={primaryModerate.Holdout30dNetPnl:F2}.",
            Verdict = primaryModerate is { AllWindowsPositive: true }
                ? "StableAcrossWindows"
                : "ConcentratedOrMixed",
            Details = new Dictionary<string, object?> { ["primaryModerate"] = primaryModerate }
        });

        var primaryDd = ResolveDrawdownRow(primaryModerate, drawdown
            .Where(d => string.Equals(d.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase))
            .GroupBy(d => $"{d.ProfileKey}|{d.WindowLabel}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase));
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are drawdown and loss streak acceptable in simulation?",
            Answer = primaryDd is null
                ? "No drawdown row."
                : $"maxDD={primaryDd.MaxDrawdownQuote:F2}, maxLossStreak={primaryDd.MaxConsecutiveLosses}, worstTrade={primaryDd.WorstTradeNet:F2}, worstWindow={primaryDd.WorstWindowNet:F2}.",
            Verdict = primaryDd is { MaxDrawdownQuote: < 80m, MaxConsecutiveLosses: < 12 }
                ? "DrawdownAcceptable"
                : "DrawdownElevated",
            Details = new Dictionary<string, object?> { ["drawdown"] = primaryDd }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Should we proceed to 120d/180d validation?",
            Answer = primary is { ExecutedTrades: >= MinimumMeaningfulTrades, AllRollingWindowsPositive: true, AggregateNetPnl: >= 0m }
                && primaryModerate is { Window90dNetPnl: >= 0m }
                ? "Yes — 90d checks passed; proceed to 120d then 180d bootstrap if runtime allows."
                : "Not yet — complete or pass focused 90d validation first.",
            Verdict = primary is { ExecutedTrades: >= MinimumMeaningfulTrades, AllRollingWindowsPositive: true }
                ? "ProceedToLongerHistory"
                : "HoldAt90d",
            Details = new Dictionary<string, object?> { ["historyDaysAvailable"] = historyDaysAvailable }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is this still research-only or clearly invalidated?",
            Answer = primary is { AggregateNetPnl: < 0m } && smokeBest is { AggregateNetPnl: < 0m }
                ? "Clearly invalidated under moderate costs in this run."
                : primary is { ExecutedTrades: >= MinimumMeaningfulTrades, AllRollingWindowsPositive: true, HoldoutPositive: true }
                    ? "Still a research candidate; not live-ready."
                    : "Research-only pending more samples or longer history.",
            Verdict = primary is { AggregateNetPnl: < 0m } && smokeBest is { AggregateNetPnl: < 0m }
                ? "Invalidated"
                : "ResearchOnly",
            Details = new Dictionary<string, object?>
            {
                ["expandedTradeRowCount"] = expandedTradeRowCount,
                ["skippedSignals"] = skippedSignalCount,
                ["reportConsistencyMismatches"] = mismatchCount,
                ["liveFuturesRecommended"] = false
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Overall verdict: recommend live Futures from this validation?",
            Answer = "Do not recommend live Futures from DirectionalRuleFuturesValidationV3 alone. Backtest-only research branch.",
            Verdict = "DoNotRecommendLiveFutures",
            Details = new Dictionary<string, object?>
            {
                ["backtestOnly"] = true,
                ["labelDefinitions"] = LabelDefinitions
            }
        });

        return answers;
    }

    internal static string TradeBucketKey(DirectionalRuleV3TradeRecord t)
        => $"{t.ProfileKey}|{t.WindowLabel}|{t.CostScenarioLabel}";

    public static int ResolveReferenceTradeCount(DirectionalRuleV3WindowRobustnessRow row)
    {
        if (row.Window180dTrades > 0) return row.Window180dTrades;
        if (row.Window120dTrades > 0) return row.Window120dTrades;
        if (row.Window90dTrades > 0) return row.Window90dTrades;
        if (row.Window60dTrades > 0) return row.Window60dTrades;
        if (row.Window30dTrades > 0) return row.Window30dTrades;
        if (row.Holdout30dTrades > 0) return row.Holdout30dTrades;
        return row.TrainReferenceTrades;
    }

    internal static string? ResolveReferenceWindowLabel(DirectionalRuleV3WindowRobustnessRow row)
    {
        if (row.Window180dTrades > 0) return "180d";
        if (row.Window120dTrades > 0) return "120d";
        if (row.Window90dTrades > 0) return "90d";
        if (row.Window60dTrades > 0) return "60d";
        if (row.Window30dTrades > 0) return "30d";
        if (row.Holdout30dTrades > 0) return "holdout30d";
        return row.TrainReferenceTrades > 0 ? "trainReference" : null;
    }

    internal static DirectionalRuleV3DrawdownRow? ResolveDrawdownRow(
        DirectionalRuleV3WindowRobustnessRow? row,
        IReadOnlyDictionary<string, DirectionalRuleV3DrawdownRow> drawdownByKey)
    {
        if (row is null)
            return null;

        foreach (var label in DrawdownWindowPreference)
        {
            var trades = label switch
            {
                "180d" => row.Window180dTrades,
                "120d" => row.Window120dTrades,
                "90d" => row.Window90dTrades,
                "60d" => row.Window60dTrades,
                "30d" => row.Window30dTrades,
                "holdout30d" => row.Holdout30dTrades,
                _ => 0
            };
            if (trades <= 0)
                continue;

            var key = $"{row.ProfileKey}|{label}";
            if (drawdownByKey.TryGetValue(key, out var dd))
                return dd;
        }

        return null;
    }

    internal static string ClassifySummaryVerdict(int tradeCount, decimal netPnl, decimal avgNet)
    {
        if (tradeCount == 0)
            return "InsufficientSamples";
        if (tradeCount < MinimumMeaningfulTrades)
            return netPnl >= 0m ? "TradeCountBelowThreshold" : "NegativeLowSample";
        if (netPnl >= 0m)
            return "NonNegative";
        if (avgNet >= -0.0005m)
            return "NearBreakeven";
        return "Negative";
    }

    private static void AddStressAnswer(
        List<ReachabilityResearchAnswer> answers,
        IReadOnlyList<DirectionalRuleV3WindowRobustnessRow> windowRobustness,
        string scenarioLabel,
        string question)
    {
        var rows = windowRobustness.Where(r =>
            (r.IsPrimaryCandidate || r.IsSmokeBestCandidate)
            && string.Equals(r.CostScenarioLabel, scenarioLabel, StringComparison.OrdinalIgnoreCase)).ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = question,
            Answer = rows.Length == 0
                ? $"No {scenarioLabel} rows for primary/smoke-best."
                : string.Join("; ", rows.Select(s => $"{s.VariantLabel}: agg={s.AggregateNetPnl:F2}, allWin={s.AllWindowsPositive}")),
            Verdict = rows.Any(s => s.AggregatePositive)
                ? $"{scenarioLabel}Survives"
                : $"{scenarioLabel}Fails",
            Details = new Dictionary<string, object?>
            {
                ["labelAggregate"] = "StressPositive",
                ["labelAllWindows"] = "StressAllWindowsPositive",
                ["rows"] = rows
            }
        });
    }

    private static string ClassifyRobustnessVerdict(
        DirectionalRuleV3WindowRobustnessRow? row,
        int tradeCount)
    {
        if (tradeCount == 0)
            return "InsufficientSamples";
        if (tradeCount < MinimumMeaningfulTrades)
            return row is { AggregateNetPnl: >= 0m } ? "TradeCountBelowThreshold" : "NegativeLowSample";
        if (row is { AllWindowsPositive: true, HoldoutPositive: true })
            return "AllWindowsAndHoldoutPositive";
        if (row is { AllWindowsPositive: true })
            return "AllWindowsPositive";
        if (row is { HoldoutPositive: true, AggregateNetPnl: >= 0m })
            return "HoldoutPositive";
        if (row is { AggregateNetPnl: >= 0m })
            return "AggregatePositive";
        return "Negative";
    }

    private static string ClassifyVariantVerdict(
        DirectionalRuleV3WindowRobustnessRow row,
        int trades,
        DirectionalRuleV3WindowRobustnessRow? latency)
    {
        if (trades == 0)
            return "InsufficientSamples";
        if (trades < MinimumMeaningfulTrades)
            return row.AggregatePositive ? "TradeCountBelowThreshold" : "NegativeLowSample";
        if (row.AllWindowsPositive && row.HoldoutPositive && row.AggregatePositive)
            return row.IsPrimaryCandidate ? "PrimaryCandidateStrong" : row.IsSmokeBestCandidate ? "SmokeBestStrong" : "VariantStrong";
        if (row.AggregatePositive)
            return "AggregatePositiveOnly";
        return "Negative";
    }

    private static bool IsStressScenario(string label)
        => label.Contains("stress", StringComparison.OrdinalIgnoreCase)
           || label.Contains("latency", StringComparison.OrdinalIgnoreCase);

}
