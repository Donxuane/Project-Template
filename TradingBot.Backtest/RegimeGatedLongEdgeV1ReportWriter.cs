using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class RegimeGatedLongEdgeV1Aggregator
{
    private const int MinimumMeaningfulTrades = 50;
    private const decimal NearBreakevenNetPerTrade = -0.0005m;

    public static IReadOnlyList<RegimeGatedLongEdgeV1SummaryRow> BuildSummaries(
        string windowLabel,
        IReadOnlyList<RegimeGatedLongEdgeV1TradeRecord> trades,
        IReadOnlyList<RegimeGatedLongEdgeV1BlockedSignalRecord> blockedSignals)
    {
        var tradeGroups = trades
            .GroupBy(t => $"{t.RuleName}|{t.ProfileName}|{t.Interval}|{t.Symbol}|{t.TargetPercent}|{t.StopPercent}|{t.TimeStopHours}|{t.EntryConfirmationMode}", StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var blockedGroups = blockedSignals
            .GroupBy(b => $"{b.RuleName}|{b.ProfileName}|{b.Interval}|{b.Symbol}|{b.TargetPercent}|{b.StopPercent}|{b.TimeStopHours}|{b.EntryConfirmationMode}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return tradeGroups
            .Select(g =>
            {
                var first = g.First();
                var completed = g.Where(t => !string.IsNullOrWhiteSpace(t.ExitReason)).ToArray();
                var key = $"{first.RuleName}|{first.ProfileName}|{first.Interval}|{first.Symbol}|{first.TargetPercent}|{first.StopPercent}|{first.TimeStopHours}|{first.EntryConfirmationMode}";
                blockedGroups.TryGetValue(key, out var blockedCount);
                var netPnl = completed.Sum(t => t.NetPnlQuote);
                var avgNet = completed.Length == 0 ? (decimal?)null : Math.Round(netPnl / completed.Length, 8);
                var medianNet = Median(completed.Select(t => (decimal?)t.NetPnlQuote));
                return new RegimeGatedLongEdgeV1SummaryRow
                {
                    WindowLabel = windowLabel,
                    RuleName = first.RuleName,
                    ProfileName = first.ProfileName,
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    TargetPercent = first.TargetPercent,
                    StopPercent = first.StopPercent,
                    TimeStopHours = first.TimeStopHours,
                    EntryConfirmationMode = first.EntryConfirmationMode,
                    SignalCount = completed.Length + blockedCount,
                    BlockedSignalCount = blockedCount,
                    TradeCount = completed.Length,
                    NetWinnerCount = completed.Count(t => t.NetPnlQuote > 0m),
                    GrossPnlQuote = completed.Sum(t => t.GrossPnlQuote),
                    NetPnlQuote = netPnl,
                    AvgNetPnlPerTrade = avgNet,
                    MedianNetPnlPerTrade = medianNet,
                    Verdict = ClassifySummaryVerdict(completed.Length, netPnl, avgNet)
                };
            })
            .OrderBy(x => x.RuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Symbol)
            .ThenBy(x => x.TargetPercent)
            .ToArray();
    }

    public static IReadOnlyList<RegimeGatedLongEdgeV1RulePerformanceRow> BuildRulePerformance(
        IReadOnlyList<RegimeGatedLongEdgeV1TradeRecord> trades)
    {
        return trades
            .Where(t => !string.IsNullOrWhiteSpace(t.ExitReason))
            .GroupBy(t => $"{t.RuleName}|{t.Symbol}|{t.Interval}|{t.TargetPercent}|{t.StopPercent}|{t.TimeStopHours}|{t.EntryConfirmationMode}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var count = g.Count();
                var net = g.Sum(t => t.NetPnlQuote);
                var stopLossRate = Rate(g, t => string.Equals(t.ExitReason, "StopLoss", StringComparison.OrdinalIgnoreCase));
                var profitTargetRate = Rate(g, t => string.Equals(t.ExitReason, "ProfitTarget", StringComparison.OrdinalIgnoreCase));
                var timeStopRate = Rate(g, t => string.Equals(t.ExitReason, "TimeStop", StringComparison.OrdinalIgnoreCase));
                return new RegimeGatedLongEdgeV1RulePerformanceRow
                {
                    RuleName = first.RuleName,
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    TargetPercent = first.TargetPercent,
                    StopPercent = first.StopPercent,
                    TimeStopHours = first.TimeStopHours,
                    EntryConfirmationMode = first.EntryConfirmationMode,
                    TradeCount = count,
                    NetPnlQuote = net,
                    GrossPnlQuote = g.Sum(t => t.GrossPnlQuote),
                    AvgNetPnlPerTrade = count == 0 ? null : Math.Round(net / count, 8),
                    StopLossRate = stopLossRate,
                    ProfitTargetRate = profitTargetRate,
                    TimeStopRate = timeStopRate,
                    Verdict = ClassifyRuleVerdict(count, net)
                };
            })
            .OrderByDescending(r => r.NetPnlQuote)
            .ToArray();
    }

    public static IReadOnlyList<RegimeGatedLongEdgeV1WindowRobustnessRow> BuildWindowRobustness(
        IReadOnlyList<RegimeGatedLongEdgeV1SummaryRow> summaries)
    {
        return summaries
            .GroupBy(s => $"{s.RuleName}|{s.ProfileName}|{s.Symbol}|{s.Interval}|{s.TargetPercent}|{s.StopPercent}|{s.TimeStopHours}|{s.EntryConfirmationMode}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var w30 = g.FirstOrDefault(x => x.WindowLabel == "30d");
                var w60 = g.FirstOrDefault(x => x.WindowLabel == "60d");
                var w90 = g.FirstOrDefault(x => x.WindowLabel == "90d");
                var totalNet = (w30?.NetPnlQuote ?? 0m) + (w60?.NetPnlQuote ?? 0m) + (w90?.NetPnlQuote ?? 0m);
                var totalTrades = (w30?.TradeCount ?? 0) + (w60?.TradeCount ?? 0) + (w90?.TradeCount ?? 0);
                var positiveWindows = new[] { w30, w60, w90 }.Count(w => w is not null && w.NetPnlQuote >= 0m);
                return new RegimeGatedLongEdgeV1WindowRobustnessRow
                {
                    RuleName = first.RuleName,
                    ProfileName = first.ProfileName,
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    TargetPercent = first.TargetPercent,
                    StopPercent = first.StopPercent,
                    TimeStopHours = first.TimeStopHours,
                    EntryConfirmationMode = first.EntryConfirmationMode,
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
            .OrderBy(x => x.RuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Symbol)
            .ToArray();
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildResearchAnswers(
        IReadOnlyList<RegimeGatedLongEdgeV1TradeRecord> trades,
        IReadOnlyList<RegimeGatedLongEdgeV1SummaryRow> summaries,
        IReadOnlyList<RegimeGatedLongEdgeV1RulePerformanceRow> rulePerformance,
        IReadOnlyList<RegimeGatedLongEdgeV1WindowRobustnessRow> windowRobustness,
        IReadOnlyList<RegimeGatedLongEdgeV1BlockedSignalRecord> blockedSignals,
        decimal roundTripCostPercent,
        IReadOnlyDictionary<string, string> gateThresholds)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var completedTrades = trades.Where(t => !string.IsNullOrWhiteSpace(t.ExitReason)).ToArray();
        var baseline = rulePerformance
            .Where(r => string.Equals(r.RuleName, RegimeGatedLongEdgeV1GateName.Bnb30mUnconditionalPositiveBaseline.ToString(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.NetPnlQuote)
            .FirstOrDefault();
        var elevatedVol = rulePerformance
            .Where(r => string.Equals(r.RuleName, RegimeGatedLongEdgeV1GateName.ElevatedVolMarketReturnGate.ToString(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.NetPnlQuote)
            .FirstOrDefault();
        var wideRange = rulePerformance
            .Where(r => string.Equals(r.RuleName, RegimeGatedLongEdgeV1GateName.WideRangeNearLowGate.ToString(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.NetPnlQuote)
            .FirstOrDefault();
        var bestTargetStop = rulePerformance.OrderByDescending(r => r.NetPnlQuote).FirstOrDefault();
        var robustPositive = windowRobustness.Where(w => w.RobustnessVerdict is "NonNegativeAcrossWindows" or "NearBreakevenAcrossWindows").ToArray();
        var confirmationGroups = completedTrades
            .GroupBy(t => t.EntryConfirmationMode, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Mode = g.Key, Net = g.Sum(t => t.NetPnlQuote), Trades = g.Count() })
            .OrderByDescending(x => x.Net)
            .ToArray();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does BNB 30m unconditional edge survive actual trade simulation?",
            Answer = baseline is null
                ? "No BNB 30m baseline trades were simulated."
                : $"Baseline trades={baseline.TradeCount}, net={baseline.NetPnlQuote:F8}, avg/trade={baseline.AvgNetPnlPerTrade:F8}, stop-rate={baseline.StopLossRate:P1}.",
            Verdict = baseline is { NetPnlQuote: >= 0m, TradeCount: >= MinimumMeaningfulTrades }
                ? "BaselineSurvivesSimulation"
                : baseline is { AvgNetPnlPerTrade: >= NearBreakevenNetPerTrade, TradeCount: >= MinimumMeaningfulTrades }
                    ? "BaselineNearBreakeven"
                    : "BaselineNegative",
            Details = new Dictionary<string, object?> { ["baseline"] = baseline }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does ElevatedVolMarketReturnGate improve net PnL versus BNB 30m baseline?",
            Answer = elevatedVol is null || baseline is null
                ? "Insufficient trades for gate vs baseline comparison."
                : $"ElevatedVol net={elevatedVol.NetPnlQuote:F8} ({elevatedVol.TradeCount} trades) vs baseline net={baseline.NetPnlQuote:F8} ({baseline.TradeCount} trades). Delta={(elevatedVol.NetPnlQuote - baseline.NetPnlQuote):F8}.",
            Verdict = elevatedVol is not null && baseline is not null && elevatedVol.NetPnlQuote > baseline.NetPnlQuote
                ? "GateImprovesBaseline"
                : elevatedVol is not null && baseline is not null && elevatedVol.NetPnlQuote >= baseline.NetPnlQuote - 0.01m
                    ? "GateSimilarToBaseline"
                    : "GateDoesNotImproveBaseline",
            Details = new Dictionary<string, object?> { ["elevatedVol"] = elevatedVol, ["baseline"] = baseline, ["thresholds"] = gateThresholds }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does WideRangeNearLowGate improve net PnL versus baseline?",
            Answer = wideRange is null || baseline is null
                ? "Insufficient trades for gate vs baseline comparison."
                : $"WideRange net={wideRange.NetPnlQuote:F8} ({wideRange.TradeCount} trades) vs baseline net={baseline.NetPnlQuote:F8} ({baseline.TradeCount} trades). Delta={(wideRange.NetPnlQuote - baseline.NetPnlQuote):F8}.",
            Verdict = wideRange is not null && baseline is not null && wideRange.NetPnlQuote > baseline.NetPnlQuote
                ? "GateImprovesBaseline"
                : wideRange is not null && baseline is not null && wideRange.NetPnlQuote >= baseline.NetPnlQuote - 0.01m
                    ? "GateSimilarToBaseline"
                    : "GateDoesNotImproveBaseline",
            Details = new Dictionary<string, object?> { ["wideRange"] = wideRange, ["baseline"] = baseline, ["thresholds"] = gateThresholds }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which target/stop pair performs best under conservative Spot costs?",
            Answer = bestTargetStop is null
                ? "No completed trades."
                : $"Best: {bestTargetStop.RuleName} {bestTargetStop.Symbol}/{bestTargetStop.Interval} target={bestTargetStop.TargetPercent:F2}% stop={bestTargetStop.StopPercent:F2}% hold={bestTargetStop.TimeStopHours:F0}h net={bestTargetStop.NetPnlQuote:F8} ({bestTargetStop.TradeCount} trades). Round-trip cost ~{roundTripCostPercent:F2}%.",
            Verdict = bestTargetStop is { NetPnlQuote: >= 0m } ? "PositiveTargetStopFound" : "AllTargetStopsNegative",
            Details = new Dictionary<string, object?> { ["topRules"] = rulePerformance.Take(10).ToArray() }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does any rule stay non-negative or near-breakeven across 30d/60d/90d?",
            Answer = robustPositive.Length > 0
                ? $"{robustPositive.Length} configuration(s) non-negative or near-breakeven with >= {MinimumMeaningfulTrades} trades: {string.Join(", ", robustPositive.Select(r => r.ProfileName))}."
                : $"No configuration met >= {MinimumMeaningfulTrades} trades with non-negative total net across windows.",
            Verdict = robustPositive.Length > 0 ? "RobustNonNegativeFound" : "NoRobustNonNegative",
            Details = new Dictionary<string, object?> { ["robustProfiles"] = robustPositive }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are results stable or only one-window artifacts?",
            Answer = string.Join("; ", windowRobustness.Select(w =>
                $"{w.ProfileName}: 30d={w.Window30dNetPnl:F4}, 60d={w.Window60dNetPnl:F4}, 90d={w.Window90dNetPnl:F4} ({w.RobustnessVerdict})")),
            Verdict = windowRobustness.Any(w => w.RobustnessVerdict is "NonNegativeAcrossWindows" or "NearBreakevenAcrossWindows")
                ? "StableAcrossWindows"
                : windowRobustness.Any(w => w.RobustnessVerdict == "MixedWindows")
                    ? "MixedStability"
                    : "SingleWindowOrNegative",
            Details = new Dictionary<string, object?> { ["windowRobustness"] = windowRobustness }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does bullish confirmation help or reduce edge?",
            Answer = confirmationGroups.Length == 0
                ? "No confirmation-mode trades."
                : string.Join("; ", confirmationGroups.Select(c => $"{c.Mode}: net={c.Net:F8}, trades={c.Trades}")),
            Verdict = confirmationGroups.Length <= 1
                ? "InsufficientConfirmationComparison"
                : confirmationGroups.First().Mode is "NextClose" or "NextOpen"
                    ? "SimpleEntryBest"
                    : "ConfirmationHelps",
            Details = new Dictionary<string, object?> { ["confirmationGroups"] = confirmationGroups, ["blockedSignals"] = blockedSignals.Count }
        });

        var btcGatedTrades = completedTrades
            .Where(t => t.RuleName.Contains("BtcFavorable", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var nonBtcGatedTrades = completedTrades
            .Where(t => !t.RuleName.Contains("BtcFavorable", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does BTC context improve BNB 30m regime-gated simulation net PnL?",
            Answer = btcGatedTrades.Length == 0
                ? "No BTC-context gated trades in this run."
                : $"BTC-context gates: trades={btcGatedTrades.Length}, net={btcGatedTrades.Sum(t => t.NetPnlQuote):F8}, avg/trade={(btcGatedTrades.Length == 0 ? 0m : btcGatedTrades.Sum(t => t.NetPnlQuote) / btcGatedTrades.Length):F8}. Non-BTC gates: trades={nonBtcGatedTrades.Length}, net={nonBtcGatedTrades.Sum(t => t.NetPnlQuote):F8}.",
            Verdict = btcGatedTrades.Length > 0 && btcGatedTrades.Sum(t => t.NetPnlQuote) > nonBtcGatedTrades.Sum(t => t.NetPnlQuote)
                ? "BtcContextImprovesSimulation"
                : btcGatedTrades.Length > 0
                    && btcGatedTrades.Average(t => t.NetPnlQuote) >= (nonBtcGatedTrades.Length == 0 ? 0m : nonBtcGatedTrades.Average(t => t.NetPnlQuote))
                    ? "BtcContextMarginallyBetter"
                    : "BtcContextDoesNotImproveSimulation",
            Details = new Dictionary<string, object?> { ["btcGatedTrades"] = btcGatedTrades.Length, ["nonBtcGatedTrades"] = nonBtcGatedTrades.Length }
        });

        var stopLossRate = completedTrades.Length == 0 ? 0m : (decimal)completedTrades.Count(t => string.Equals(t.ExitReason, "StopLoss", StringComparison.OrdinalIgnoreCase)) / completedTrades.Length;
        var btcStopLossRate = btcGatedTrades.Length == 0 ? 0m : (decimal)btcGatedTrades.Count(t => string.Equals(t.ExitReason, "StopLoss", StringComparison.OrdinalIgnoreCase)) / btcGatedTrades.Length;
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does BTC context reduce stop-loss rate?",
            Answer = btcGatedTrades.Length == 0
                ? "No BTC-context trades to compare stop-loss rate."
                : $"BTC-context stop-loss rate={btcStopLossRate:P1} vs all trades={stopLossRate:P1}.",
            Verdict = btcGatedTrades.Length > 0 && btcStopLossRate < stopLossRate ? "BtcContextLowersStopRate" : "BtcContextDoesNotLowerStopRate",
            Details = new Dictionary<string, object?> { ["btcStopLossRate"] = btcStopLossRate, ["overallStopLossRate"] = stopLossRate }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Should BTC context be added before continuing?",
            Answer = btcGatedTrades.Length > 0
                ? "BTC context enabled in this run via BTCUSDT 1m bootstrap."
                : "BTC context not enabled in this run.",
            Verdict = btcGatedTrades.Length > 0 && btcGatedTrades.Sum(t => t.NetPnlQuote) >= 0m
                ? "BtcContextHelpful"
                : btcGatedTrades.Length > 0
                    ? "BtcContextEnabledButStillNegative"
                    : "AddBtcContextBeforeContinuing",
            Details = new Dictionary<string, object?>
            {
                ["totalNetPnl"] = completedTrades.Sum(t => t.NetPnlQuote),
                ["btcGatedNetPnl"] = btcGatedTrades.Sum(t => t.NetPnlQuote)
            }
        });

        var allNegative = rulePerformance.All(r => r.NetPnlQuote < 0m) && completedTrades.Length >= MinimumMeaningfulTrades;
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Overall verdict: continue Spot long-only research or pause?",
            Answer = allNegative
                ? $"All regime-gated prototypes negative under ~{roundTripCostPercent:F2}% round-trip Spot costs ({completedTrades.Length} trades). Recommend pausing Spot long-only research until BTC context, more data, or lower-fee simulation."
                : robustPositive.Length > 0
                    ? "Marginal non-negative subset found; continue only as backtest research, not live."
                    : $"Mixed results; edge remains thin ({completedTrades.Sum(t => t.NetPnlQuote):F8} total net).",
            Verdict = allNegative ? "PauseSpotLongOnlyResearch" : robustPositive.Length > 0 ? "CautiousContinuation" : "ThinEdgeContinueResearchOnly",
            Details = new Dictionary<string, object?>
            {
                ["summaries"] = summaries.Take(20).ToArray(),
                ["gateThresholds"] = gateThresholds
            }
        });

        return answers;
    }

    private static string ClassifySummaryVerdict(int tradeCount, decimal netPnl, decimal? avgNet)
    {
        if (tradeCount < MinimumMeaningfulTrades / 3)
            return "Sparse";
        if (tradeCount < MinimumMeaningfulTrades)
            return netPnl >= 0m ? "PositiveButLowSample" : "NegativeLowSample";
        if (netPnl >= 0m)
            return "NonNegative";
        if (avgNet is >= NearBreakevenNetPerTrade)
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

public sealed class RegimeGatedLongEdgeV1ReportWriter(string outputDirectory)
{
    public async Task WriteAsync(
        IReadOnlyList<RegimeGatedLongEdgeV1SummaryRow> summaries,
        IReadOnlyList<RegimeGatedLongEdgeV1TradeRecord> trades,
        IReadOnlyList<RegimeGatedLongEdgeV1BlockedSignalRecord> blockedSignals,
        IReadOnlyList<RegimeGatedLongEdgeV1RulePerformanceRow> rulePerformance,
        IReadOnlyList<RegimeGatedLongEdgeV1WindowRobustnessRow> windowRobustness,
        IReadOnlyList<ReachabilityResearchAnswer> researchAnswers,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        await WriteJsonAsync("regime-gated-v1-summary.json", summaries, cancellationToken);
        await WriteJsonAsync("regime-gated-v1-trades.json", trades, cancellationToken);
        await WriteJsonAsync("regime-gated-v1-blocked-signals.json", blockedSignals, cancellationToken);
        await WriteJsonAsync("regime-gated-v1-rule-performance.json", rulePerformance, cancellationToken);
        await WriteJsonAsync("regime-gated-v1-window-robustness.json", windowRobustness, cancellationToken);
        await WriteJsonAsync("regime-gated-v1-research-answers.json", researchAnswers, cancellationToken);

        await WriteSummaryCsvAsync("regime-gated-v1-summary.csv", summaries, cancellationToken);
        await WriteTradesCsvAsync("regime-gated-v1-trades.csv", trades, cancellationToken);
        await WriteBlockedCsvAsync("regime-gated-v1-blocked-signals.csv", blockedSignals, cancellationToken);
        await WriteRulePerformanceCsvAsync("regime-gated-v1-rule-performance.csv", rulePerformance, cancellationToken);
        await WriteWindowRobustnessCsvAsync("regime-gated-v1-window-robustness.csv", windowRobustness, cancellationToken);
    }

    private async Task WriteJsonAsync<T>(string fileName, T payload, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, fileName),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private async Task WriteSummaryCsvAsync(string fileName, IReadOnlyList<RegimeGatedLongEdgeV1SummaryRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WindowLabel,RuleName,ProfileName,Symbol,Interval,TargetPercent,StopPercent,TimeStopHours,EntryConfirmationMode,SignalCount,BlockedSignalCount,TradeCount,NetWinnerCount,GrossPnlQuote,NetPnlQuote,AvgNetPnlPerTrade,MedianNetPnlPerTrade,Verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.WindowLabel), Csv(row.RuleName), Csv(row.ProfileName), row.Symbol, Csv(row.Interval),
                row.TargetPercent.ToString(CultureInfo.InvariantCulture),
                row.StopPercent.ToString(CultureInfo.InvariantCulture),
                row.TimeStopHours.ToString(CultureInfo.InvariantCulture),
                Csv(row.EntryConfirmationMode), row.SignalCount, row.BlockedSignalCount, row.TradeCount,
                row.NetWinnerCount, row.GrossPnlQuote, row.NetPnlQuote,
                row.AvgNetPnlPerTrade?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.MedianNetPnlPerTrade?.ToString(CultureInfo.InvariantCulture) ?? "",
                Csv(row.Verdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private async Task WriteTradesCsvAsync(string fileName, IReadOnlyList<RegimeGatedLongEdgeV1TradeRecord> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,Symbol,Interval,TimeUtc,EntryPrice,ExitPrice,ExitReason,TargetPercent,StopPercent,TimeStopHours,GrossPnlQuote,NetPnlQuote,VolatilityRegime,MarketWideReturnProxyPercent,RangeWidthPercent,DistanceFromRecentLowPercent,DistanceFromRecentHighPercent,TrendSlopePercent,AtrPercent,VolumeExpansionRatio,MfePercent,MaePercent,DurationMinutes");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.RuleName), row.Symbol, Csv(row.Interval), row.TimeUtc.ToString("O", CultureInfo.InvariantCulture),
                row.EntryPrice, row.ExitPrice, Csv(row.ExitReason),
                row.TargetPercent, row.StopPercent, row.TimeStopHours,
                row.GrossPnlQuote, row.NetPnlQuote, Csv(row.VolatilityRegime),
                row.MarketWideReturnProxyPercent?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.RangeWidthPercent, row.DistanceFromRecentLowPercent, row.DistanceFromRecentHighPercent,
                row.TrendSlopePercent, row.AtrPercent, row.VolumeExpansionRatio,
                row.MfePercent?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.MaePercent?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.DurationMinutes));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private async Task WriteBlockedCsvAsync(string fileName, IReadOnlyList<RegimeGatedLongEdgeV1BlockedSignalRecord> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,Symbol,Interval,TimeUtc,RejectionReason,VolatilityRegime,MarketWideReturnProxyPercent,RangeWidthPercent,DistanceFromRecentLowPercent,DistanceFromRecentHighPercent,TrendSlopePercent,AtrPercent,VolumeExpansionRatio,TargetPercent,StopPercent,TimeStopHours,EntryConfirmationMode");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.RuleName), row.Symbol, Csv(row.Interval), row.TimeUtc.ToString("O", CultureInfo.InvariantCulture),
                Csv(row.RejectionReason), Csv(row.VolatilityRegime),
                row.MarketWideReturnProxyPercent?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.RangeWidthPercent, row.DistanceFromRecentLowPercent, row.DistanceFromRecentHighPercent,
                row.TrendSlopePercent, row.AtrPercent, row.VolumeExpansionRatio,
                row.TargetPercent, row.StopPercent, row.TimeStopHours, Csv(row.EntryConfirmationMode)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private async Task WriteRulePerformanceCsvAsync(string fileName, IReadOnlyList<RegimeGatedLongEdgeV1RulePerformanceRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,Symbol,Interval,TargetPercent,StopPercent,TimeStopHours,EntryConfirmationMode,TradeCount,NetPnlQuote,GrossPnlQuote,AvgNetPnlPerTrade,StopLossRate,ProfitTargetRate,TimeStopRate,Verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.RuleName), row.Symbol, Csv(row.Interval),
                row.TargetPercent, row.StopPercent, row.TimeStopHours, Csv(row.EntryConfirmationMode),
                row.TradeCount, row.NetPnlQuote, row.GrossPnlQuote,
                row.AvgNetPnlPerTrade?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.StopLossRate, row.ProfitTargetRate, row.TimeStopRate, Csv(row.Verdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private async Task WriteWindowRobustnessCsvAsync(string fileName, IReadOnlyList<RegimeGatedLongEdgeV1WindowRobustnessRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,ProfileName,Symbol,Interval,TargetPercent,StopPercent,TimeStopHours,EntryConfirmationMode,Window30dTrades,Window60dTrades,Window90dTrades,Window30dNetPnl,Window60dNetPnl,Window90dNetPnl,RobustnessVerdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.RuleName), Csv(row.ProfileName), row.Symbol, Csv(row.Interval),
                row.TargetPercent, row.StopPercent, row.TimeStopHours, Csv(row.EntryConfirmationMode),
                row.Window30dTrades, row.Window60dTrades, row.Window90dTrades,
                row.Window30dNetPnl, row.Window60dNetPnl, row.Window90dNetPnl, Csv(row.RobustnessVerdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
