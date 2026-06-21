using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// Walk-forward activation simulator for the multi-symbol V2 branch. At each checkpoint only
/// completed past trades (exit strictly before the checkpoint) and as-of flow snapshots are used;
/// the decision applies to the next activation window only. Direction-aware gates. This engine is
/// intentionally separate from the V1 engine so the frozen incubation track stays untouched.
/// </summary>
public static class NoPaidDataShortWindowMultiSymbolResearchV2Engine
{
    public static MultiSymbolActivationSimResult Simulate(
        MultiSymbolComboKey key,
        MultiSymbolActivationConfig config,
        IReadOnlyList<RegimeDriftDiagnosticTrade> allTrades,
        DateTime studyStartUtc,
        DateTime studyEndUtc,
        ShortWindowFlowFeatureIndex flowIndex,
        string costScenario)
    {
        var ordered = allTrades.OrderBy(t => t.EntryTimeUtc).ToArray();

        if (config.IsAlwaysOn)
        {
            var baseline = ordered
                .Where(t => t.EntryTimeUtc >= studyStartUtc && t.EntryTimeUtc < studyEndUtc)
                .ToArray();
            var baselinePeriod = new MultiSymbolActivationPeriodRow
            {
                Symbol = key.Symbol.ToString(),
                Interval = key.Interval,
                Direction = key.Direction.ToString(),
                RuleFamily = key.Family.ToString(),
                ActivationRule = config.ActivationRuleName,
                CheckpointUtc = studyStartUtc,
                ActivationStartUtc = studyStartUtc,
                ActivationEndUtc = studyEndUtc,
                GateDataAvailable = true,
                GatePass = true,
                Activated = true,
                TradesInActivationWindow = baseline.Length,
                NetInActivationWindow = Math.Round(baseline.Sum(t => t.NetPnlQuote), 8),
                CostScenario = costScenario
            };
            return new MultiSymbolActivationSimResult(
                config, [baselinePeriod], baseline,
                ActivatedPeriodCount: 1,
                PositivePeriodCount: baseline.Sum(t => t.NetPnlQuote) > 0m ? 1 : 0,
                ClusterCount: baseline.Length > 0 ? 1 : 0,
                ActiveRanges: [(studyStartUtc, studyEndUtc)]);
        }

        var periods = new List<MultiSymbolActivationPeriodRow>();
        var activeRanges = new List<(DateTime Start, DateTime End)>();

        for (var checkpoint = studyStartUtc; checkpoint < studyEndUtc; checkpoint = checkpoint.AddHours(config.CheckpointFrequencyHours))
        {
            var snapshot = flowIndex.Snapshot(checkpoint);

            bool activated;
            var gateAvailable = true;
            var gatePass = true;
            var skipReason = string.Empty;
            var lookbackCount = 0;
            var lookbackNet = 0m;

            if (config.Gate == MultiSymbolActivationGate.RecentNetPositive)
            {
                var lookbackStart = checkpoint.AddDays(-config.LookbackDays);
                var lookbackTrades = ordered
                    .Where(t => t.ExitTimeUtc < checkpoint && t.EntryTimeUtc >= lookbackStart)
                    .ToArray();
                lookbackCount = lookbackTrades.Length;
                lookbackNet = lookbackTrades.Sum(t => t.NetPnlQuote);

                if (lookbackCount < config.MinLookbackTrades)
                {
                    activated = false;
                    skipReason = $"InsufficientLookbackTrades({lookbackCount}<{config.MinLookbackTrades})";
                }
                else
                {
                    activated = lookbackNet > 0m;
                    if (!activated)
                        skipReason = "RecentNetNotPositive";
                }
            }
            else
            {
                (gateAvailable, gatePass) = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.EvaluateGate(
                    config.Gate, key.Direction, snapshot);
                activated = gateAvailable && gatePass;
                if (!gateAvailable)
                    skipReason = "GateDataUnavailable";
                else if (!gatePass)
                    skipReason = "GateConditionFailed";
            }

            var activationStart = checkpoint;
            var activationEnd = checkpoint.AddHours(config.ActivationPeriodHours);
            if (activationEnd > studyEndUtc)
                activationEnd = studyEndUtc;
            if (activated)
                activeRanges.Add((activationStart, activationEnd));

            var windowTrades = ordered
                .Where(t => t.EntryTimeUtc >= activationStart && t.EntryTimeUtc < activationEnd)
                .ToArray();

            periods.Add(new MultiSymbolActivationPeriodRow
            {
                Symbol = key.Symbol.ToString(),
                Interval = key.Interval,
                Direction = key.Direction.ToString(),
                RuleFamily = key.Family.ToString(),
                ActivationRule = config.ActivationRuleName,
                CheckpointUtc = checkpoint,
                ActivationStartUtc = activationStart,
                ActivationEndUtc = activationEnd,
                LookbackTradeCount = lookbackCount,
                LookbackNetPnl = Math.Round(lookbackNet, 8),
                GateDataAvailable = gateAvailable,
                GatePass = gatePass,
                Activated = activated,
                SkipReason = skipReason,
                TradesInActivationWindow = windowTrades.Length,
                NetInActivationWindow = Math.Round(windowTrades.Sum(t => t.NetPnlQuote), 8),
                CostScenario = costScenario
            });
        }

        var merged = MergeRanges(activeRanges);
        var taken = ordered
            .Where(t => merged.Any(r => t.EntryTimeUtc >= r.Start && t.EntryTimeUtc < r.End))
            .ToArray();
        var clusterCount = merged.Count(r => taken.Any(t => t.EntryTimeUtc >= r.Start && t.EntryTimeUtc < r.End));
        var activatedPeriods = periods.Count(p => p.Activated);
        var positivePeriods = periods.Count(p => p.Activated && p.NetInActivationWindow > 0m);

        return new MultiSymbolActivationSimResult(
            config, periods, taken, activatedPeriods, positivePeriods, clusterCount, merged);
    }

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
