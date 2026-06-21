namespace TradingBot.Backtest;

/// <summary>
/// Walk-forward activation simulator for the cross-symbol V1-style research. At each checkpoint
/// only completed past trades (exit strictly before the checkpoint) and as-of flow snapshots are
/// used; the decision applies to the next activation window only. Supports performance-only,
/// flow-only, and combined performance+flow configurations. Separate from the frozen V1 track.
/// </summary>
public static class NoPaidDataShortWindowFlowResearchV1CrossSymbolEngine
{
    public static CrossSymbolSimOutcome Simulate(
        CrossSymbolComboKey key,
        CrossSymbolActivationConfig config,
        IReadOnlyList<RegimeDriftDiagnosticTrade> allTrades,
        DateTime studyStartUtc,
        DateTime studyEndUtc,
        ShortWindowFlowFeatureIndex flowIndex,
        string costScenario,
        bool collectPeriods)
    {
        var ordered = allTrades.OrderBy(t => t.EntryTimeUtc).ToArray();

        if (config.IsAlwaysOn)
        {
            var baseline = ordered
                .Where(t => t.EntryTimeUtc >= studyStartUtc && t.EntryTimeUtc < studyEndUtc)
                .ToArray();
            var baselineNet = baseline.Sum(t => t.NetPnlQuote);
            var periods = collectPeriods
                ? new[]
                {
                    PeriodRow(key, config, studyStartUtc, studyStartUtc, studyEndUtc, 0, 0m, 0m,
                        true, true, true, true, string.Empty, baseline.Length, baselineNet, costScenario)
                }
                : [];
            return new CrossSymbolSimOutcome(config, periods, baseline, 1, baselineNet > 0m ? 1 : 0,
                baseline.Length > 0 ? 1 : 0, [(studyStartUtc, studyEndUtc)]);
        }

        var rows = collectPeriods ? new List<CrossSymbolPeriodRow>() : null;
        var activeRanges = new List<(DateTime Start, DateTime End)>();
        var activatedCount = 0;
        var positiveCount = 0;

        for (var checkpoint = studyStartUtc; checkpoint < studyEndUtc; checkpoint = checkpoint.AddHours(config.CheckpointFrequencyHours))
        {
            var lookbackCount = 0;
            var lookbackNet = 0m;
            var lookbackPf = 0m;
            var perfPass = true;
            var skipReason = string.Empty;

            if (config.PerfKind != CrossSymbolPerfKind.None)
            {
                var lookbackStart = checkpoint.AddDays(-config.LookbackDays);
                var lookbackTrades = ordered
                    .Where(t => t.ExitTimeUtc < checkpoint && t.EntryTimeUtc >= lookbackStart)
                    .ToArray();
                lookbackCount = lookbackTrades.Length;
                lookbackNet = lookbackTrades.Sum(t => t.NetPnlQuote);
                var grossWin = lookbackTrades.Where(t => t.NetPnlQuote > 0m).Sum(t => t.NetPnlQuote);
                var grossLoss = Math.Abs(lookbackTrades.Where(t => t.NetPnlQuote <= 0m).Sum(t => t.NetPnlQuote));
                lookbackPf = grossLoss == 0m ? (grossWin > 0m ? 999m : 0m) : grossWin / grossLoss;

                if (lookbackCount < config.MinLookbackTrades)
                {
                    perfPass = false;
                    skipReason = $"InsufficientLookbackTrades({lookbackCount}<{config.MinLookbackTrades})";
                }
                else
                {
                    perfPass = config.PerfKind switch
                    {
                        CrossSymbolPerfKind.RecentNetPositive => lookbackNet > 0m,
                        CrossSymbolPerfKind.RecentProfitFactor => lookbackPf > (config.ProfitFactorThreshold ?? 1.1m),
                        _ => true
                    };
                    if (!perfPass)
                        skipReason = "PerfConditionFailed";
                }
            }

            var flowAvailable = true;
            var flowPass = true;
            if (config.FlowGate != MultiSymbolActivationGate.None)
            {
                var snapshot = flowIndex.Snapshot(checkpoint);
                (flowAvailable, flowPass) = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.EvaluateGate(
                    config.FlowGate, key.Direction, snapshot);
                if (perfPass && !flowAvailable)
                    skipReason = "FlowDataUnavailable";
                else if (perfPass && !flowPass)
                    skipReason = "FlowConditionFailed";
            }

            var activated = perfPass && flowAvailable && flowPass;

            var activationStart = checkpoint;
            var activationEnd = checkpoint.AddHours(config.ActivationPeriodHours);
            if (activationEnd > studyEndUtc)
                activationEnd = studyEndUtc;
            if (activated)
            {
                activeRanges.Add((activationStart, activationEnd));
                activatedCount++;
            }

            var windowNet = 0m;
            var windowCount = 0;
            foreach (var t in ordered)
            {
                if (t.EntryTimeUtc >= activationStart && t.EntryTimeUtc < activationEnd)
                {
                    windowCount++;
                    windowNet += t.NetPnlQuote;
                }
            }

            if (activated && windowNet > 0m)
                positiveCount++;

            rows?.Add(PeriodRow(key, config, checkpoint, activationStart, activationEnd,
                lookbackCount, lookbackNet, lookbackPf, perfPass, flowAvailable, flowPass,
                activated, skipReason, windowCount, windowNet, costScenario));
        }

        var merged = MergeRanges(activeRanges);
        var taken = ordered
            .Where(t => merged.Any(r => t.EntryTimeUtc >= r.Start && t.EntryTimeUtc < r.End))
            .ToArray();
        var clusterCount = merged.Count(r => taken.Any(t => t.EntryTimeUtc >= r.Start && t.EntryTimeUtc < r.End));

        return new CrossSymbolSimOutcome(
            config,
            rows is null ? [] : rows,
            taken,
            activatedCount,
            positiveCount,
            clusterCount,
            merged);
    }

    private static CrossSymbolPeriodRow PeriodRow(
        CrossSymbolComboKey key, CrossSymbolActivationConfig config,
        DateTime checkpoint, DateTime activationStart, DateTime activationEnd,
        int lookbackCount, decimal lookbackNet, decimal lookbackPf,
        bool perfPass, bool flowAvailable, bool flowPass, bool activated, string skipReason,
        int windowCount, decimal windowNet, string costScenario)
        => new()
        {
            Symbol = key.Symbol.ToString(),
            Interval = key.Interval,
            Direction = key.Direction.ToString(),
            TargetPercent = key.TargetPercent,
            StopPercent = key.StopPercent,
            ActivationRule = config.ActivationRuleName,
            CheckpointUtc = checkpoint,
            ActivationStartUtc = activationStart,
            ActivationEndUtc = activationEnd,
            LookbackTradeCount = lookbackCount,
            LookbackNetPnl = Math.Round(lookbackNet, 8),
            LookbackProfitFactor = Math.Round(lookbackPf, 6),
            PerfPass = perfPass,
            FlowDataAvailable = flowAvailable,
            FlowPass = flowPass,
            Activated = activated,
            SkipReason = skipReason,
            TradesInActivationWindow = windowCount,
            NetInActivationWindow = Math.Round(windowNet, 8),
            CostScenario = costScenario
        };

    private static List<(DateTime Start, DateTime End)> MergeRanges(
        IReadOnlyList<(DateTime Start, DateTime End)> ranges)
    {
        var sorted = ranges.OrderBy(r => r.Start).ToList();
        var merged = new List<(DateTime Start, DateTime End)>();
        foreach (var range in sorted)
        {
            if (merged.Count == 0 || range.Start > merged[^1].End)
                merged.Add(range);
            else if (range.End > merged[^1].End)
                merged[^1] = (merged[^1].Start, range.End);
        }

        return merged;
    }
}
