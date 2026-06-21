using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class DirectionalRuleFuturesValidationV2Aggregator
{
    public const int MinimumMeaningfulTrades = 50;
    private const decimal NearBreakevenNetPerTrade = -0.0005m;

    public static IReadOnlyList<DirectionalRuleV2SummaryRow> ApplyRobustnessLabels(
        IReadOnlyList<DirectionalRuleV2SummaryRow> summaries)
    {
        var windowGroups = summaries
            .GroupBy(RobustnessKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);

        return summaries.Select(row =>
        {
            if (!windowGroups.TryGetValue(RobustnessKey(row), out var group))
                return row;

            var w30 = group.FirstOrDefault(x => x.WindowLabel == "30d");
            var w60 = group.FirstOrDefault(x => x.WindowLabel == "60d");
            var w90 = group.FirstOrDefault(x => x.WindowLabel == "90d");
            var aggregateNet = group.Sum(x => x.NetPnlQuote);
            var window30Positive = w30 is { NetPnlQuote: >= 0m };
            var window60Positive = w60 is { NetPnlQuote: >= 0m };
            var window90Positive = w90 is { NetPnlQuote: >= 0m };
            var allWindowsPositive = window30Positive && window60Positive && window90Positive;
            var isStress = IsStressScenario(row.CostScenarioLabel);

            return row with
            {
                AggregateNetPositive = aggregateNet >= 0m,
                Window30dNetPositive = window30Positive,
                Window60dNetPositive = window60Positive,
                Window90dNetPositive = window90Positive,
                AllWindowsPositive = allWindowsPositive,
                Holdout90dPositive = window90Positive,
                StressAggregatePositive = isStress && aggregateNet >= 0m,
                StressAllWindowsPositive = isStress && allWindowsPositive
            };
        }).ToArray();
    }

    public static IReadOnlyList<DirectionalRuleV2WindowRobustnessRow> BuildWindowRobustness(
        IReadOnlyList<DirectionalRuleV2SummaryRow> summaries)
    {
        return summaries
            .GroupBy(RobustnessKey, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var w30 = g.FirstOrDefault(x => x.WindowLabel == "30d");
                var w60 = g.FirstOrDefault(x => x.WindowLabel == "60d");
                var w90 = g.FirstOrDefault(x => x.WindowLabel == "90d");
                var aggregateNet = g.Sum(x => x.NetPnlQuote);
                var window30Positive = w30 is { NetPnlQuote: >= 0m };
                var window60Positive = w60 is { NetPnlQuote: >= 0m };
                var window90Positive = w90 is { NetPnlQuote: >= 0m };
                var allWindowsPositive = window30Positive && window60Positive && window90Positive;
                var totalTrades = g.Sum(x => x.ExecutedTrades);
                var isStress = IsStressScenario(first.CostScenarioLabel);

                return new DirectionalRuleV2WindowRobustnessRow
                {
                    ProfileKey = first.ProfileKey,
                    RuleName = first.RuleName,
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    EntryMode = first.EntryMode,
                    OverlapPolicy = first.OverlapPolicy,
                    CooldownCandlesAfterExit = first.CooldownCandlesAfterExit,
                    MaxHoldMinutes = first.MaxHoldMinutes,
                    CostScenarioLabel = first.CostScenarioLabel,
                    Window30dTrades = w30?.ExecutedTrades ?? 0,
                    Window60dTrades = w60?.ExecutedTrades ?? 0,
                    Window90dTrades = w90?.ExecutedTrades ?? 0,
                    Window30dNetPnl = w30?.NetPnlQuote ?? 0m,
                    Window60dNetPnl = w60?.NetPnlQuote ?? 0m,
                    Window90dNetPnl = w90?.NetPnlQuote ?? 0m,
                    AggregateNetPnl = aggregateNet,
                    AggregateNetPositive = aggregateNet >= 0m,
                    Window30dNetPositive = window30Positive,
                    Window60dNetPositive = window60Positive,
                    Window90dNetPositive = window90Positive,
                    AllWindowsPositive = allWindowsPositive,
                    Holdout90dPositive = window90Positive,
                    StressAggregatePositive = isStress && aggregateNet >= 0m,
                    StressAllWindowsPositive = isStress && allWindowsPositive,
                    RobustnessVerdict = ClassifyRobustnessVerdict(totalTrades, aggregateNet, allWindowsPositive, window90Positive)
                };
            })
            .OrderBy(r => r.ProfileKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CostScenarioLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<DirectionalRuleV2CostSensitivityRow> BuildCostSensitivity(
        IReadOnlyList<DirectionalRuleV2SummaryRow> summaries,
        IReadOnlyList<DirectionalRuleV2WindowRobustnessRow> windowRobustness)
    {
        var robustnessByKey = windowRobustness.ToDictionary(
            r => $"{r.ProfileKey}|{r.CostScenarioLabel}",
            StringComparer.OrdinalIgnoreCase);

        var scenarios = DirectionalRuleFuturesValidationV2CostModel.BuildValidationScenarios()
            .ToDictionary(s => s.Label, StringComparer.OrdinalIgnoreCase);

        return summaries
            .GroupBy(s => $"{s.ProfileKey}|{s.CostScenarioLabel}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var tradeCount = g.Sum(x => x.ExecutedTrades);
                var net = g.Sum(x => x.NetPnlQuote);
                scenarios.TryGetValue(first.CostScenarioLabel, out var scenario);
                scenario ??= DirectionalRuleFuturesValidationV2CostModel.BuildValidationScenarios().First();
                robustnessByKey.TryGetValue($"{first.ProfileKey}|{first.CostScenarioLabel}", out var robust);

                return new DirectionalRuleV2CostSensitivityRow
                {
                    ProfileKey = first.ProfileKey,
                    RuleName = first.RuleName,
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    EntryMode = first.EntryMode,
                    OverlapPolicy = first.OverlapPolicy,
                    CooldownCandlesAfterExit = first.CooldownCandlesAfterExit,
                    MaxHoldMinutes = first.MaxHoldMinutes,
                    CostScenarioLabel = first.CostScenarioLabel,
                    RoundTripCostPercent = DirectionalRuleFuturesValidationV2CostModel.EstimateRoundTripCostPercent(scenario),
                    ExtraAdverseSlippagePercentPerSide = scenario.ExtraAdverseSlippagePercentPerSide,
                    TradeCount = tradeCount,
                    NetPnlQuote = net,
                    AvgNetPnlPerTrade = tradeCount == 0 ? null : Math.Round(net / tradeCount, 8),
                    AggregateNetPositive = net >= 0m,
                    StressAggregatePositive = IsStressScenario(first.CostScenarioLabel) && net >= 0m,
                    StressAllWindowsPositive = robust?.StressAllWindowsPositive ?? false,
                    Verdict = ClassifySummaryVerdict(tradeCount, net, tradeCount == 0 ? 0m : net / tradeCount)
                };
            })
            .OrderBy(r => r.ProfileKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CostScenarioLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<DirectionalRuleV2DrawdownRow> ApplyWorstWindowNet(
        IReadOnlyList<DirectionalRuleV2DrawdownRow> drawdownRows,
        IReadOnlyList<DirectionalRuleV2WindowRobustnessRow> windowRobustness)
    {
        var worstByProfile = windowRobustness
            .Where(r => string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                r => r.ProfileKey,
                r => Math.Min(r.Window30dNetPnl, Math.Min(r.Window60dNetPnl, r.Window90dNetPnl)),
                StringComparer.OrdinalIgnoreCase);

        return drawdownRows.Select(row =>
        {
            worstByProfile.TryGetValue(row.ProfileKey, out var worst);
            return row with { WorstWindowNet = worst };
        }).ToArray();
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildResearchAnswers(
        IReadOnlyList<DirectionalRuleV2SummaryRow> summaries,
        IReadOnlyList<DirectionalRuleV2WindowRobustnessRow> windowRobustness,
        IReadOnlyList<DirectionalRuleV2CostSensitivityRow> costSensitivity,
        IReadOnlyList<DirectionalRuleV2DrawdownRow> drawdown,
        IReadOnlyList<DirectionalRuleV2OverlapAnalysisRow> overlapAnalysis,
        long executedTradeCount,
        long skippedSignalCount)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var moderateRobust = windowRobustness
            .Where(r => string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var noOverlapPositive = moderateRobust
            .Where(r => r.OverlapPolicy != nameof(DirectionalRuleV2OverlapPolicy.AllowOverlap)
                        && r.AggregateNetPositive
                        && r.Window30dTrades + r.Window60dTrades + r.Window90dTrades >= MinimumMeaningfulTrades)
            .ToArray();

        var holdoutPositive = moderateRobust
            .Where(r => r.Holdout90dPositive
                        && r.Window30dTrades + r.Window60dTrades + r.Window90dTrades >= MinimumMeaningfulTrades)
            .ToArray();

        var stressAggregatePositive = costSensitivity
            .Where(c => IsStressScenario(c.CostScenarioLabel) && c.AggregateNetPositive && c.TradeCount >= MinimumMeaningfulTrades)
            .ToArray();

        var stressAllWindowsPositive = windowRobustness
            .Where(r => IsStressScenario(r.CostScenarioLabel) && r.StressAllWindowsPositive
                        && r.Window30dTrades + r.Window60dTrades + r.Window90dTrades >= MinimumMeaningfulTrades)
            .ToArray();

        var nextOpenModerate = moderateRobust
            .Where(r => r.EntryMode == nameof(DirectionalRuleEntryMode.NextOpen))
            .OrderByDescending(r => r.AggregateNetPnl)
            .FirstOrDefault();
        var nextCloseModerate = moderateRobust
            .Where(r => r.EntryMode == nameof(DirectionalRuleEntryMode.NextClose))
            .OrderByDescending(r => r.AggregateNetPnl)
            .FirstOrDefault();

        var symbolConcentration = moderateRobust
            .Where(r => r.AggregateNetPositive)
            .GroupBy(r => r.Symbol)
            .Select(g => new { Symbol = g.Key, Count = g.Count(), Net = g.Sum(x => x.AggregateNetPnl) })
            .ToArray();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Do Rule01/Rule05 short configs remain positive when overlapping trades are prevented?",
            Answer = noOverlapPositive.Length > 0
                ? $"{noOverlapPositive.Length} profile(s) non-negative under futures-moderate with overlap constraints and >= {MinimumMeaningfulTrades} executed trades."
                : $"No profile met >= {MinimumMeaningfulTrades} executed trades with aggregate-positive futures-moderate net under non-overlap policies.",
            Verdict = noOverlapPositive.Length > 0 ? "NonOverlapConfigsRemainPositive" : "NonOverlapConfigsFail",
            Details = new Dictionary<string, object?>
            {
                ["label"] = "AggregateNetPositive + non-AllowOverlap",
                ["profiles"] = noOverlapPositive.Take(12).ToArray()
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Do they remain positive in the 90d holdout specifically?",
            Answer = holdoutPositive.Length > 0
                ? $"{holdoutPositive.Length} profile(s) with Holdout90dPositive under futures-moderate (explicit label, not conflated with aggregate)."
                : "No profile with >= 50 total executed trades had Window90dNetPositive under futures-moderate.",
            Verdict = holdoutPositive.Length > 0 ? "Holdout90dPositiveFound" : "Holdout90dNotPositive",
            Details = new Dictionary<string, object?>
            {
                ["label"] = "Holdout90dPositive",
                ["profiles"] = holdoutPositive.Take(12).ToArray()
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Do they survive stress or stress-plus costs?",
            Answer = stressAggregatePositive.Length > 0
                ? $"{stressAggregatePositive.Length} profile/scenario row(s) with StressAggregatePositive (aggregate net >= 0 under stress-family scenarios). StressAllWindowsPositive is reported separately and is stricter."
                : "No profile reached StressAggregatePositive with >= 50 trades. StressAllWindowsPositive is reported separately.",
            Verdict = stressAggregatePositive.Length > 0 ? "StressAggregatePositiveFound" : "NoStressAggregatePositive",
            Details = new Dictionary<string, object?>
            {
                ["labelAggregate"] = "StressAggregatePositive",
                ["labelAllWindows"] = "StressAllWindowsPositive",
                ["stressAggregatePositive"] = stressAggregatePositive.Take(12).ToArray(),
                ["stressAllWindowsPositive"] = stressAllWindowsPositive.Take(12).ToArray(),
                ["note"] = "StressAggregatePositive does not imply StressAllWindowsPositive or holdout survival."
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are profits concentrated in one window or one symbol?",
            Answer = symbolConcentration.Length == 0
                ? "No aggregate-positive moderate profiles."
                : string.Join("; ", symbolConcentration.Select(s => $"{s.Symbol}: {s.Count} positive profiles, net={s.Net:F4}")),
            Verdict = symbolConcentration.Length >= 2 ? "BroadAcrossSymbols" : symbolConcentration.Length == 1 ? "ConcentratedInOneSymbol" : "NoPositiveProfiles",
            Details = new Dictionary<string, object?> { ["symbolConcentration"] = symbolConcentration }
        });

        var maxDdProfile = drawdown
            .Where(d => string.Equals(d.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.MaxDrawdownQuote)
            .FirstOrDefault();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "What is max drawdown and loss streak?",
            Answer = maxDdProfile is null
                ? "No drawdown metrics available."
                : $"Worst moderate drawdown: {maxDdProfile.ProfileKey} maxDD={maxDdProfile.MaxDrawdownQuote:F4}, maxConsecutiveLosses={maxDdProfile.MaxConsecutiveLosses}, worstTrade={maxDdProfile.WorstTradeNet:F4}.",
            Verdict = maxDdProfile is { MaxDrawdownQuote: < 50m } ? "DrawdownAcceptableInSimulation" : "DrawdownElevated",
            Details = new Dictionary<string, object?>
            {
                ["topDrawdown"] = drawdown
                    .Where(d => string.Equals(d.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(d => d.MaxDrawdownQuote)
                    .Take(8)
                    .ToArray()
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is NextOpen still better after no-overlap constraints?",
            Answer = nextOpenModerate is null || nextCloseModerate is null
                ? "Insufficient comparison data."
                : $"NextOpen best aggregate net={nextOpenModerate.AggregateNetPnl:F4}; NextClose best aggregate net={nextCloseModerate.AggregateNetPnl:F4}.",
            Verdict = nextOpenModerate is not null && nextCloseModerate is not null && nextOpenModerate.AggregateNetPnl >= nextCloseModerate.AggregateNetPnl
                ? "NextOpenStillBetter"
                : "NextCloseCompetitiveOrBetter",
            Details = new Dictionary<string, object?>
            {
                ["nextOpenBest"] = nextOpenModerate,
                ["nextCloseBest"] = nextCloseModerate
            }
        });

        var coFire = overlapAnalysis.FirstOrDefault();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Do Rule01 and Rule05 often fire together?",
            Answer = coFire is null
                ? "No overlap diagnostic rows."
                : $"ETH/BNB diagnostic: Rule01 signals={coFire.Rule01SignalCount}, Rule05={coFire.Rule05SignalCount}, co-fire within 30m={coFire.CoFireWithin30mCount} (rate vs Rule01={coFire.CoFireRateVsRule01:P2}).",
            Verdict = coFire is { CoFireRateVsRule01: > 0.20m } ? "RulesOftenCoFire" : "RulesMostlyIndependent",
            Details = new Dictionary<string, object?> { ["overlapAnalysis"] = overlapAnalysis }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is there enough evidence for future research, not live?",
            Answer = noOverlapPositive.Length > 0 && holdoutPositive.Length > 0
                ? "Some narrow configs pass non-overlap and holdout labels under moderate costs, but stress-family and drawdown metrics remain mixed. Continue backtest-only research; do not recommend live Futures."
                : "Validation did not meet combined success criteria (non-overlap positive + holdout90d). Continue backtest-only research only.",
            Verdict = "ContinueBacktestOnlyResearch",
            Details = new Dictionary<string, object?>
            {
                ["executedTrades"] = executedTradeCount,
                ["skippedSignals"] = skippedSignalCount,
                ["successCriteria"] = new
                {
                    minimumTrades = MinimumMeaningfulTrades,
                    requiresAggregateNetPositive = true,
                    requiresHoldout90dPositive = true,
                    requiresStressSurvival = "optional explicit StressAggregatePositive label",
                    liveFuturesRecommended = false
                }
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Overall verdict: recommend live Futures from this validation?",
            Answer = "Do not recommend live Futures from DirectionalRuleFuturesValidationV2. Backtest-only research branch.",
            Verdict = "DoNotRecommendLiveFutures",
            Details = new Dictionary<string, object?>
            {
                ["backtestOnly"] = true,
                ["spotPauseStillApplies"] = true
            }
        });

        return answers;
    }

    internal static string SummaryKey(DirectionalRuleV2TradeRecord t)
        => $"{t.ProfileKey}|{t.WindowLabel}|{t.CostScenarioLabel}";

    private static string RobustnessKey(DirectionalRuleV2SummaryRow s)
        => $"{s.ProfileKey}|{s.CostScenarioLabel}";

    internal static string ClassifySummaryVerdict(int tradeCount, decimal netPnl, decimal avgNet)
    {
        if (tradeCount < MinimumMeaningfulTrades / 3)
            return "Sparse";
        if (tradeCount < MinimumMeaningfulTrades)
            return netPnl >= 0m ? "PositiveButLowSample" : "NegativeLowSample";
        if (netPnl >= 0m)
            return "NonNegative";
        if (avgNet >= NearBreakevenNetPerTrade)
            return "NearBreakeven";
        return "Negative";
    }

    private static string ClassifyRobustnessVerdict(
        int totalTrades,
        decimal aggregateNet,
        bool allWindowsPositive,
        bool holdout90Positive)
    {
        if (totalTrades < MinimumMeaningfulTrades)
            return "InsufficientSamples";
        if (allWindowsPositive && aggregateNet >= 0m)
            return "AllWindowsPositive";
        if (holdout90Positive && aggregateNet >= 0m)
            return "AggregatePositiveHoldout90dPositive";
        if (aggregateNet >= 0m)
            return "AggregateNetPositive";
        if (aggregateNet >= NearBreakevenNetPerTrade * totalTrades)
            return "NearBreakevenAggregate";
        return "NegativeAggregate";
    }

    private static bool IsStressScenario(string label)
        => label.Contains("stress", StringComparison.OrdinalIgnoreCase);
}
