namespace TradingBot.Backtest;

/// <summary>
/// Walk-forward short-window activation simulator. At each checkpoint only completed past trades
/// (exit before the checkpoint) and as-of flow features are used; activation applies to the next
/// activation-period window only. No future data can influence an activation decision.
/// </summary>
public static class NoPaidDataShortWindowFlowResearchV1Engine
{
    private sealed record LookbackMetrics(int TradeCount, decimal NetPnl, decimal ProfitFactor, decimal WinRate);

    public static ShortWindowSimResult Simulate(
        ShortWindowActivationConfig config,
        IReadOnlyList<RegimeDriftDiagnosticTrade> allTrades,
        DateTime studyStartUtc,
        DateTime studyEndUtc,
        ShortWindowFlowFeatureIndex flowIndex,
        string costScenario)
    {
        var ordered = allTrades.OrderBy(t => t.EntryTimeUtc).ToArray();
        var baseline = ordered.Where(t => t.EntryTimeUtc >= studyStartUtc && t.EntryTimeUtc < studyEndUtc).ToArray();

        if (config.PerfCondition == ShortWindowPerfCondition.AlwaysOn)
            return BuildAlwaysOn(config, ordered, baseline, studyStartUtc, studyEndUtc, costScenario);

        var periods = new List<ShortWindowPeriodRow>();
        var activeRanges = new List<(DateTime Start, DateTime End, bool Sparse)>();
        var flowUnavailable = 0;

        for (var checkpoint = studyStartUtc; checkpoint < studyEndUtc; checkpoint = checkpoint.AddHours(config.CheckpointFrequencyHours))
        {
            var lookbackStart = checkpoint.AddDays(-config.LookbackDays);
            var lookbackTrades = ordered
                .Where(t => t.ExitTimeUtc < checkpoint && t.EntryTimeUtc >= lookbackStart)
                .ToArray();
            var metrics = ComputeLookbackMetrics(lookbackTrades);
            var sparseLookback = metrics.TradeCount < NoPaidDataShortWindowFlowResearchV1Catalog.SparseLookbackTradeThreshold;

            var snapshot = flowIndex.Snapshot(checkpoint);
            var (flowAvailable, flowPass) = NoPaidDataShortWindowFlowResearchV1Catalog.EvaluateFlow(config.FlowCondition, snapshot);
            if (config.FlowCondition != ShortWindowFlowCondition.None && !flowAvailable)
                flowUnavailable++;

            var perfPass = EvaluatePerfCondition(config, metrics);

            bool activated;
            var skipReason = string.Empty;
            if (config.PerfCondition == ShortWindowPerfCondition.None)
            {
                activated = flowAvailable && flowPass;
                if (!flowAvailable)
                    skipReason = "FlowDataUnavailable";
                else if (!flowPass)
                    skipReason = "FlowConditionFailed";
            }
            else
            {
                if (!perfPass)
                {
                    activated = false;
                    skipReason = metrics.TradeCount < config.MinLookbackTrades
                        ? $"InsufficientLookbackTrades({metrics.TradeCount}<{config.MinLookbackTrades})"
                        : "PerfConditionFailed";
                }
                else if (config.FlowCondition != ShortWindowFlowCondition.None && !flowAvailable)
                {
                    activated = false;
                    skipReason = "FlowDataUnavailable";
                }
                else if (config.FlowCondition != ShortWindowFlowCondition.None && !flowPass)
                {
                    activated = false;
                    skipReason = "FlowConditionFailed";
                }
                else
                {
                    activated = true;
                }
            }

            var activationStart = checkpoint;
            var activationEnd = checkpoint.AddHours(config.ActivationPeriodHours);
            if (activationEnd > studyEndUtc)
                activationEnd = studyEndUtc;
            if (activated)
                activeRanges.Add((activationStart, activationEnd, sparseLookback));

            // Next-period stats are recorded for every checkpoint (activated or not) so that
            // flow-confirmation value can be measured without selection bias.
            var windowTrades = ordered
                .Where(t => t.EntryTimeUtc >= activationStart && t.EntryTimeUtc < activationEnd)
                .ToArray();

            periods.Add(new ShortWindowPeriodRow
            {
                ActivationRuleName = config.ActivationRuleName,
                CheckpointFrequencyHours = config.CheckpointFrequencyHours,
                LookbackDays = config.LookbackDays,
                ActivationPeriodHours = config.ActivationPeriodHours,
                CheckpointUtc = checkpoint,
                ActivationStartUtc = activationStart,
                ActivationEndUtc = activationEnd,
                LookbackTradeCount = metrics.TradeCount,
                LookbackNetPnl = Math.Round(metrics.NetPnl, 8),
                LookbackProfitFactor = Math.Round(metrics.ProfitFactor, 6),
                LookbackWinRate = Math.Round(metrics.WinRate, 6),
                PerfConditionPass = perfPass,
                FlowDataAvailable = flowAvailable,
                FlowConditionPass = flowPass,
                SparseLookback = sparseLookback,
                Activated = activated,
                SkipReason = skipReason,
                TradesInActivationWindow = windowTrades.Length,
                NetInActivationWindow = Math.Round(windowTrades.Sum(t => t.NetPnlQuote), 8),
                OiChange60mPercent = snapshot.OiChange60mPercent,
                TakerImbalance1h = snapshot.TakerImbalance1h ?? snapshot.TakerBuySellImbalance,
                GlobalLongShortZScore = snapshot.GlobalLongShortZScore,
                FundingZScore = snapshot.FundingZScore,
                BtcReturn30mPercent = snapshot.BtcReturn30mPercent,
                BtcReturn60mPercent = snapshot.BtcReturn60mPercent,
                DistanceFromRecentHighPercent = snapshot.DistanceFromRecentHighPercent,
                CostScenario = costScenario
            });
        }

        var merged = MergeRanges(activeRanges);
        var taken = ordered
            .Where(t => merged.Any(r => t.EntryTimeUtc >= r.Start && t.EntryTimeUtc < r.End))
            .ToArray();
        var clusterCount = merged.Count(r => taken.Any(t => t.EntryTimeUtc >= r.Start && t.EntryTimeUtc < r.End));

        var tradeRows = taken.Select(t =>
        {
            var range = merged.First(r => t.EntryTimeUtc >= r.Start && t.EntryTimeUtc < r.End);
            return new ShortWindowTradeRow
            {
                ActivationRuleName = config.ActivationRuleName,
                EntryTimeUtc = t.EntryTimeUtc,
                ExitTimeUtc = t.ExitTimeUtc,
                NetPnlQuote = Math.Round(t.NetPnlQuote, 8),
                IsWinner = t.NetPnlQuote > 0m,
                ExitReason = t.ExitReason,
                CostScenario = costScenario,
                ActivationStartUtc = range.Start,
                ActivationEndUtc = range.End,
                SparseLookbackActivation = range.Sparse
            };
        }).ToArray();

        var summary = NoPaidDataShortWindowFlowResearchV1Aggregator.BuildSummaryRow(
            config, taken, baseline, periods, clusterCount, flowUnavailable, costScenario);

        return new ShortWindowSimResult(config, periods, tradeRows, summary);
    }

    private static ShortWindowSimResult BuildAlwaysOn(
        ShortWindowActivationConfig config,
        IReadOnlyList<RegimeDriftDiagnosticTrade> ordered,
        IReadOnlyList<RegimeDriftDiagnosticTrade> baseline,
        DateTime studyStartUtc,
        DateTime studyEndUtc,
        string costScenario)
    {
        var periods = new List<ShortWindowPeriodRow>
        {
            new()
            {
                ActivationRuleName = config.ActivationRuleName,
                CheckpointFrequencyHours = config.CheckpointFrequencyHours,
                LookbackDays = 0,
                ActivationPeriodHours = (int)Math.Max(1, (studyEndUtc - studyStartUtc).TotalHours),
                CheckpointUtc = studyStartUtc,
                ActivationStartUtc = studyStartUtc,
                ActivationEndUtc = studyEndUtc,
                PerfConditionPass = true,
                FlowDataAvailable = true,
                FlowConditionPass = true,
                Activated = true,
                TradesInActivationWindow = baseline.Count,
                NetInActivationWindow = Math.Round(baseline.Sum(t => t.NetPnlQuote), 8),
                CostScenario = costScenario
            }
        };

        var tradeRows = baseline.Select(t => new ShortWindowTradeRow
        {
            ActivationRuleName = config.ActivationRuleName,
            EntryTimeUtc = t.EntryTimeUtc,
            ExitTimeUtc = t.ExitTimeUtc,
            NetPnlQuote = Math.Round(t.NetPnlQuote, 8),
            IsWinner = t.NetPnlQuote > 0m,
            ExitReason = t.ExitReason,
            CostScenario = costScenario,
            ActivationStartUtc = studyStartUtc,
            ActivationEndUtc = studyEndUtc,
            SparseLookbackActivation = false
        }).ToArray();

        var summary = NoPaidDataShortWindowFlowResearchV1Aggregator.BuildSummaryRow(
            config, baseline, baseline, periods, baseline.Count > 0 ? 1 : 0, 0, costScenario);

        return new ShortWindowSimResult(config, periods, tradeRows, summary);
    }

    private static bool EvaluatePerfCondition(ShortWindowActivationConfig config, LookbackMetrics metrics)
    {
        if (config.PerfCondition == ShortWindowPerfCondition.None)
            return true;
        if (metrics.TradeCount < config.MinLookbackTrades)
            return false;

        return config.PerfCondition switch
        {
            ShortWindowPerfCondition.RecentNetPositive => metrics.NetPnl > 0m,
            ShortWindowPerfCondition.RecentProfitFactor => metrics.ProfitFactor > (config.ProfitFactorThreshold ?? 1.1m),
            ShortWindowPerfCondition.RecentWinRateAndNet => metrics.WinRate > 0.5m && metrics.NetPnl > 0m,
            _ => false
        };
    }

    private static List<(DateTime Start, DateTime End, bool Sparse)> MergeRanges(
        IReadOnlyList<(DateTime Start, DateTime End, bool Sparse)> ranges)
    {
        var sorted = ranges.OrderBy(r => r.Start).ToList();
        var merged = new List<(DateTime Start, DateTime End, bool Sparse)>();
        foreach (var range in sorted)
        {
            if (merged.Count == 0 || range.Start > merged[^1].End)
                merged.Add(range);
            else if (range.End > merged[^1].End)
                merged[^1] = (merged[^1].Start, range.End, merged[^1].Sparse || range.Sparse);
            else if (range.Sparse)
                merged[^1] = (merged[^1].Start, merged[^1].End, true);
        }

        return merged;
    }

    private static LookbackMetrics ComputeLookbackMetrics(IReadOnlyList<RegimeDriftDiagnosticTrade> trades)
    {
        if (trades.Count == 0)
            return new LookbackMetrics(0, 0m, 0m, 0m);

        var net = trades.Sum(t => t.NetPnlQuote);
        var wins = trades.Count(t => t.NetPnlQuote > 0m);
        var grossWin = trades.Where(t => t.NetPnlQuote > 0m).Sum(t => t.NetPnlQuote);
        var grossLoss = Math.Abs(trades.Where(t => t.NetPnlQuote <= 0m).Sum(t => t.NetPnlQuote));
        var pf = grossLoss == 0m ? (grossWin > 0m ? 999m : 0m) : grossWin / grossLoss;
        return new LookbackMetrics(trades.Count, net, pf, (decimal)wins / trades.Count);
    }
}
