using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class DirectionalRuleFuturesSimulationV1Aggregator
{
    public const int MinimumMeaningfulTrades = 50;
    private const decimal NearBreakevenNetPerTrade = -0.0005m;

    public static IReadOnlyList<DirectionalRuleFuturesSummaryRow> BuildSummaries(
        IReadOnlyList<DirectionalRuleFuturesTradeRecord> trades)
    {
        return trades
            .Where(t => !string.Equals(t.ExitReason, "InvalidEntry", StringComparison.OrdinalIgnoreCase))
            .GroupBy(t => SummaryKey(t), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var count = g.Count();
                var net = g.Sum(t => t.NetPnlQuote);
                return new DirectionalRuleFuturesSummaryRow
                {
                    RuleName = first.RuleName,
                    Direction = first.Direction,
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    WindowLabel = first.WindowLabel,
                    EntryMode = first.EntryMode,
                    TargetPercent = first.TargetPercent,
                    StopPercent = first.StopPercent,
                    MaxHoldMinutes = first.MaxHoldMinutes,
                    CostScenarioLabel = first.CostScenarioLabel,
                    TradeCount = count,
                    NetWinnerCount = g.Count(t => t.NetPnlQuote > 0m),
                    GrossPnlQuote = g.Sum(t => t.GrossPnlQuote),
                    NetPnlQuote = net,
                    AvgNetPnlPerTrade = count == 0 ? null : Math.Round(net / count, 8),
                    MedianNetPnlPerTrade = Median(g.Select(t => (decimal?)t.NetPnlQuote)),
                    ProfitTargetRate = Rate(g, t => string.Equals(t.ExitReason, "ProfitTarget", StringComparison.OrdinalIgnoreCase)),
                    StopLossRate = Rate(g, t => string.Equals(t.ExitReason, "StopLoss", StringComparison.OrdinalIgnoreCase)),
                    TimeStopRate = Rate(g, t => string.Equals(t.ExitReason, "TimeStop", StringComparison.OrdinalIgnoreCase)),
                    Verdict = ClassifySummaryVerdict(count, net, net / Math.Max(count, 1))
                };
            })
            .OrderBy(r => r.RuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Symbol)
            .ThenBy(r => r.Interval, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<DirectionalRuleFuturesRulePerformanceRow> BuildRulePerformance(
        IReadOnlyList<DirectionalRuleFuturesTradeRecord> trades)
    {
        return trades
            .Where(t => !string.Equals(t.ExitReason, "InvalidEntry", StringComparison.OrdinalIgnoreCase))
            .GroupBy(t => PerformanceKey(t), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var count = g.Count();
                var net = g.Sum(t => t.NetPnlQuote);
                return new DirectionalRuleFuturesRulePerformanceRow
                {
                    RuleName = first.RuleName,
                    Direction = first.Direction,
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    EntryMode = first.EntryMode,
                    TargetPercent = first.TargetPercent,
                    StopPercent = first.StopPercent,
                    MaxHoldMinutes = first.MaxHoldMinutes,
                    CostScenarioLabel = first.CostScenarioLabel,
                    TradeCount = count,
                    NetPnlQuote = net,
                    GrossPnlQuote = g.Sum(t => t.GrossPnlQuote),
                    AvgNetPnlPerTrade = count == 0 ? null : Math.Round(net / count, 8),
                    ProfitTargetRate = Rate(g, t => string.Equals(t.ExitReason, "ProfitTarget", StringComparison.OrdinalIgnoreCase)),
                    StopLossRate = Rate(g, t => string.Equals(t.ExitReason, "StopLoss", StringComparison.OrdinalIgnoreCase)),
                    TimeStopRate = Rate(g, t => string.Equals(t.ExitReason, "TimeStop", StringComparison.OrdinalIgnoreCase)),
                    Verdict = ClassifyRuleVerdict(count, net)
                };
            })
            .OrderByDescending(r => r.NetPnlQuote)
            .ToArray();
    }

    public static IReadOnlyList<DirectionalRuleFuturesWindowRobustnessRow> BuildWindowRobustness(
        IReadOnlyList<DirectionalRuleFuturesSummaryRow> summaries)
    {
        return summaries
            .GroupBy(s => RobustnessKey(s), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var w30 = g.FirstOrDefault(x => x.WindowLabel == "30d");
                var w60 = g.FirstOrDefault(x => x.WindowLabel == "60d");
                var w90 = g.FirstOrDefault(x => x.WindowLabel == "90d");
                var totalNet = (w30?.NetPnlQuote ?? 0m) + (w60?.NetPnlQuote ?? 0m) + (w90?.NetPnlQuote ?? 0m);
                var totalTrades = (w30?.TradeCount ?? 0) + (w60?.TradeCount ?? 0) + (w90?.TradeCount ?? 0);
                var positiveWindows = new[] { w30, w60, w90 }.Count(w => w is not null && w.NetPnlQuote >= 0m);
                return new DirectionalRuleFuturesWindowRobustnessRow
                {
                    RuleName = first.RuleName,
                    Direction = first.Direction,
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    EntryMode = first.EntryMode,
                    TargetPercent = first.TargetPercent,
                    StopPercent = first.StopPercent,
                    MaxHoldMinutes = first.MaxHoldMinutes,
                    CostScenarioLabel = first.CostScenarioLabel,
                    Window30dTrades = w30?.TradeCount ?? 0,
                    Window60dTrades = w60?.TradeCount ?? 0,
                    Window90dTrades = w90?.TradeCount ?? 0,
                    Window30dNetPnl = w30?.NetPnlQuote ?? 0m,
                    Window60dNetPnl = w60?.NetPnlQuote ?? 0m,
                    Window90dNetPnl = w90?.NetPnlQuote ?? 0m,
                    RobustnessVerdict = totalNet >= 0m && totalTrades >= MinimumMeaningfulTrades
                        ? "NonNegativeAcrossWindows"
                        : totalNet >= NearBreakevenNetPerTrade * totalTrades && totalTrades >= MinimumMeaningfulTrades
                            ? "NearBreakevenAcrossWindows"
                            : positiveWindows >= 2
                                ? "MixedWindows"
                                : totalTrades < MinimumMeaningfulTrades
                                    ? "InsufficientSamples"
                                    : "ConsistentlyNegative"
                };
            })
            .OrderBy(r => r.RuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Symbol)
            .ToArray();
    }

    public static IReadOnlyList<DirectionalRuleFuturesCostSensitivityRow> BuildCostSensitivity(
        IReadOnlyList<DirectionalRuleFuturesTradeRecord> trades)
    {
        var scenarios = LongShortFuturesFeasibilityStudyV1CostModel.BuildStudyScenarios()
            .Where(s => DirectionalRuleFuturesSimulationV1Simulator.SimulationCostScenarioLabels
                .Contains(s.Label, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var rows = new List<DirectionalRuleFuturesCostSensitivityRow>();
        foreach (var scenario in scenarios)
        {
            foreach (var group in trades
                         .Where(t => string.Equals(t.CostScenarioLabel, scenario.Label, StringComparison.OrdinalIgnoreCase)
                                     && !string.Equals(t.ExitReason, "InvalidEntry", StringComparison.OrdinalIgnoreCase))
                         .GroupBy(t => $"{t.RuleName}|{t.Direction}", StringComparer.OrdinalIgnoreCase))
            {
                var first = group.First();
                var count = group.Count();
                var net = group.Sum(t => t.NetPnlQuote);
                rows.Add(new DirectionalRuleFuturesCostSensitivityRow
                {
                    RuleName = first.RuleName,
                    Direction = first.Direction,
                    CostScenarioLabel = scenario.Label,
                    RoundTripCostPercent = RangeExpansionV2FeasibilityCostModel.EstimateRoundTripCostPercent(scenario),
                    FundingRatePercentPerHour = scenario.FundingRatePercentPerHour,
                    TradeCount = count,
                    NetPnlQuote = net,
                    AvgNetPnlPerTrade = count == 0 ? null : Math.Round(net / count, 8),
                    MedianNetPnlPerTrade = Median(group.Select(t => (decimal?)t.NetPnlQuote)),
                    Verdict = ClassifyRuleVerdict(count, net)
                });
            }
        }

        return rows
            .OrderBy(r => r.RuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CostScenarioLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildResearchAnswers(
        IReadOnlyList<DirectionalRuleFuturesSummaryRow> summaries,
        IReadOnlyList<DirectionalRuleFuturesRulePerformanceRow> rulePerformance,
        IReadOnlyList<DirectionalRuleFuturesWindowRobustnessRow> windowRobustness,
        IReadOnlyList<DirectionalRuleFuturesCostSensitivityRow> costSensitivity,
        IReadOnlyList<DirectionalRuleDefinition> rules,
        int moderateTradeCount,
        decimal moderateNetPnlQuote,
        IReadOnlyDictionary<string, (decimal Net, int Trades)>? entryModeModerateStats = null)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var moderatePerformance = rulePerformance
            .Where(r => string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var survivors = moderatePerformance
            .Where(r => r.TradeCount >= MinimumMeaningfulTrades && r.NetPnlQuote >= 0m)
            .ToArray();
        var stressSurvivors = costSensitivity
            .Where(c => string.Equals(c.CostScenarioLabel, "futures-stress", StringComparison.OrdinalIgnoreCase)
                        && c.TradeCount >= MinimumMeaningfulTrades
                        && c.NetPnlQuote >= 0m)
            .ToArray();
        var entryModeGroups = (entryModeModerateStats ?? new Dictionary<string, (decimal Net, int Trades)>())
            .Select(kv => new { Mode = kv.Key, Net = kv.Value.Net, Trades = kv.Value.Trades })
            .OrderByDescending(x => x.Net)
            .ToArray();
        var bestTargetStop = moderatePerformance.OrderByDescending(r => r.NetPnlQuote).FirstOrDefault();
        var robustPositive = windowRobustness
            .Where(w => string.Equals(w.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase)
                        && w.RobustnessVerdict is "NonNegativeAcrossWindows" or "NearBreakevenAcrossWindows")
            .ToArray();
        var symbolSpread = moderatePerformance
            .Where(r => r.TradeCount >= MinimumMeaningfulTrades / 2)
            .GroupBy(r => r.Symbol)
            .Select(g => new { Symbol = g.Key, Positive = g.Count(x => x.NetPnlQuote >= 0m), Total = g.Count() })
            .ToArray();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Do the 7 holdout-non-negative rules survive actual trade simulation?",
            Answer = survivors.Length > 0
                ? $"{survivors.Length} rule configuration(s) non-negative under futures-moderate with >= {MinimumMeaningfulTrades} trades across symbol/interval/variant grid."
                : $"No rule configuration reached >= {MinimumMeaningfulTrades} trades with non-negative net under futures-moderate ({rules.Count} rules tested, {moderateTradeCount} simulated trades).",
            Verdict = survivors.Length > 0 ? "RulesSurviveSimulation" : "RulesFailSimulation",
            Details = new Dictionary<string, object?> { ["survivors"] = survivors.Take(15).ToArray(), ["ruleCount"] = rules.Count }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which rules remain positive under futures-moderate costs?",
            Answer = survivors.Length > 0
                ? string.Join("; ", survivors.Take(8).Select(s => $"{s.RuleName} {s.Symbol}/{s.Interval} net={s.NetPnlQuote:F8} ({s.TradeCount} trades)"))
                : "None under the full target/stop/hold/entry grid.",
            Verdict = survivors.Length > 0 ? "ModerateCostPositiveRulesFound" : "NoModerateCostPositiveRules",
            Details = new Dictionary<string, object?> { ["positiveRules"] = survivors }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Do any rules survive futures-stress costs?",
            Answer = stressSurvivors.Length > 0
                ? $"{stressSurvivors.Length} rule(s) non-negative under futures-stress with >= {MinimumMeaningfulTrades} trades: {string.Join(", ", stressSurvivors.Select(s => s.RuleName).Distinct())}."
                : "No rule met >= 50 trades with non-negative net under futures-stress.",
            Verdict = stressSurvivors.Length > 0 ? "StressCostSurvivorsFound" : "NoStressCostSurvivors",
            Details = new Dictionary<string, object?> { ["stressSurvivors"] = stressSurvivors }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does next-open or next-close entry perform better?",
            Answer = entryModeGroups.Length == 0
                ? "No completed trades."
                : string.Join("; ", entryModeGroups.Select(e => $"{e.Mode}: net={e.Net:F8}, trades={e.Trades}")),
            Verdict = entryModeGroups.Length <= 1
                ? "InsufficientEntryComparison"
                : entryModeGroups.First().Mode == "NextOpen"
                    ? "NextOpenBetter"
                    : entryModeGroups.First().Mode == "NextClose"
                        ? "NextCloseBetter"
                        : "EntryModesSimilar",
            Details = new Dictionary<string, object?> { ["entryModeGroups"] = entryModeGroups }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which target/stop pair is best per rule?",
            Answer = bestTargetStop is null
                ? "No completed trades."
                : $"Best overall: {bestTargetStop.RuleName} {bestTargetStop.Symbol}/{bestTargetStop.Interval} target={bestTargetStop.TargetPercent:F2}% stop={bestTargetStop.StopPercent:F2}% hold={bestTargetStop.MaxHoldMinutes}m entry={bestTargetStop.EntryMode} net={bestTargetStop.NetPnlQuote:F8} ({bestTargetStop.TradeCount} trades).",
            Verdict = bestTargetStop is { NetPnlQuote: >= 0m, TradeCount: >= MinimumMeaningfulTrades }
                ? "PositiveTargetStopFound"
                : "NoPositiveTargetStop",
            Details = new Dictionary<string, object?>
            {
                ["topByRule"] = moderatePerformance
                    .GroupBy(r => r.RuleName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(x => x.NetPnlQuote).First())
                    .ToArray()
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are results stable across 30d / 60d / 90d?",
            Answer = robustPositive.Length > 0
                ? $"{robustPositive.Length} configuration(s) non-negative or near-breakeven across windows under futures-moderate."
                : $"No configuration met >= {MinimumMeaningfulTrades} total trades with non-negative net across windows.",
            Verdict = robustPositive.Length > 0 ? "StableAcrossWindows" : "UnstableAcrossWindows",
            Details = new Dictionary<string, object?> { ["robustProfiles"] = robustPositive.Take(15).ToArray() }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are winners concentrated in one symbol/interval or broad enough to matter?",
            Answer = symbolSpread.Length == 0
                ? "Insufficient data."
                : string.Join("; ", symbolSpread.Select(s => $"{s.Symbol}: {s.Positive}/{s.Total} positive configs")),
            Verdict = symbolSpread.Count(s => s.Positive > 0) >= 2
                ? "BroadEnoughAcrossSymbols"
                : symbolSpread.Any(s => s.Positive > 0)
                    ? "ConcentratedInOneSymbol"
                    : "NoWinners",
            Details = new Dictionary<string, object?> { ["symbolSpread"] = symbolSpread }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does any rule have enough trades and positive net without relying on long+short oracle behavior?",
            Answer = survivors.Length > 0
                ? $"{survivors.Length} deterministic-direction configuration(s) with >= {MinimumMeaningfulTrades} trades and non-negative futures-moderate net."
                : "No deterministic rule configuration met sample and net thresholds.",
            Verdict = survivors.Length > 0 ? "DeterministicRulePositive" : "NoDeterministicRulePositive",
            Details = new Dictionary<string, object?>
            {
                ["note"] = "Each rule uses fixed direction from discovery JSON; FuturesLongPlusShort oracle is not used."
            }
        });

        var allFail = moderatePerformance.All(r => r.NetPnlQuote < 0m || r.TradeCount < MinimumMeaningfulTrades)
                        && moderateTradeCount >= MinimumMeaningfulTrades;
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "If all rules fail in trade simulation, does feasibility edge survive execution?",
            Answer = allFail
                ? "Feasibility holdout-non-negative expected-net edge does not survive full trade simulation with target/stop/time-stop and futures-moderate costs. Conclude execution gap."
                : survivors.Length > 0
                    ? "Some rule variants remain non-negative in simulation; edge partially survives but still backtest-only."
                    : "Mixed or low-sample results; edge not proven for live Futures.",
            Verdict = allFail
                ? "FeasibilityEdgeDoesNotSurviveExecution"
                : survivors.Length > 0
                    ? "PartialEdgeSurvivesExecution"
                    : "InconclusiveExecutionGap",
            Details = new Dictionary<string, object?>
            {
                ["totalModerateTrades"] = moderateTradeCount,
                ["totalModerateNet"] = moderateNetPnlQuote,
                ["summaries"] = summaries.Where(s => s.CostScenarioLabel == "futures-moderate").Take(20).ToArray()
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Overall verdict: recommend live Futures from this study?",
            Answer = "Do not recommend live Futures from this branch alone. Continue backtest-only research even if marginal non-negative subsets appear.",
            Verdict = "DoNotRecommendLiveFutures",
            Details = new Dictionary<string, object?>
            {
                ["backtestOnly"] = true,
                ["spotPauseStillApplies"] = true
            }
        });

        return answers;
    }

    internal static string SummaryKey(DirectionalRuleFuturesTradeRecord t)
        => $"{t.RuleName}|{t.Direction}|{t.Symbol}|{t.Interval}|{t.WindowLabel}|{t.EntryMode}|{t.TargetPercent}|{t.StopPercent}|{t.MaxHoldMinutes}|{t.CostScenarioLabel}";

    internal static string ClassifySummaryVerdictPublic(int tradeCount, decimal netPnl, decimal avgNet)
        => ClassifySummaryVerdict(tradeCount, netPnl, avgNet);

    internal static string ClassifyRuleVerdictPublic(int tradeCount, decimal netPnl)
        => ClassifyRuleVerdict(tradeCount, netPnl);

    private static string PerformanceKey(DirectionalRuleFuturesTradeRecord t)
        => $"{t.RuleName}|{t.Direction}|{t.Symbol}|{t.Interval}|{t.EntryMode}|{t.TargetPercent}|{t.StopPercent}|{t.MaxHoldMinutes}|{t.CostScenarioLabel}";

    private static string RobustnessKey(DirectionalRuleFuturesSummaryRow s)
        => $"{s.RuleName}|{s.Direction}|{s.Symbol}|{s.Interval}|{s.EntryMode}|{s.TargetPercent}|{s.StopPercent}|{s.MaxHoldMinutes}|{s.CostScenarioLabel}";

    private static string ClassifySummaryVerdict(int tradeCount, decimal netPnl, decimal avgNet)
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

    private static string ClassifyRuleVerdict(int tradeCount, decimal netPnl)
    {
        if (tradeCount < MinimumMeaningfulTrades)
            return tradeCount == 0 ? "NoTrades" : "InsufficientSamples";
        return netPnl >= 0m ? "NonNegative" : netPnl >= NearBreakevenNetPerTrade * tradeCount ? "NearBreakeven" : "Negative";
    }

    private static decimal Rate<T>(IEnumerable<T> samples, Func<T, bool> predicate)
    {
        var arr = samples.ToArray();
        return arr.Length == 0 ? 0m : Math.Round((decimal)arr.Count(predicate) / arr.Length, 6);
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
}
