namespace TradingBot.Backtest;

public static class NoPaidDataAdaptiveActivationV1Engine
{
    private sealed record LookbackMetrics(
        int TradeCount,
        decimal NetPnl,
        decimal ProfitFactor,
        decimal WinRate,
        decimal MaxDrawdown,
        int MaxConsecutiveLosses);

    public static AdaptiveActivationSimResult Simulate(
        AdaptiveActivationRuleConfig rule,
        IReadOnlyList<RegimeDriftDiagnosticTrade> baselineTrades,
        IReadOnlyList<RegimeDriftDiagnosticTrade> costedTrades,
        DateTime dataStartUtc,
        DateTime dataEndUtc,
        BtcContextIndex btcContext,
        IReadOnlyList<KlineCandle> bnbIntervalCandles,
        decimal btc30Q3Lower,
        string costScenario)
    {
        if (rule.ConditionType == AdaptiveActivationConditionType.AlwaysOn)
            return BuildAlwaysOn(rule, costedTrades, baselineTrades, dataStartUtc, dataEndUtc, costScenario);

        var ordered = costedTrades.OrderBy(t => t.EntryTimeUtc).ToArray();
        var checkpoints = BuildCheckpoints(dataStartUtc, dataEndUtc, rule.CheckpointFrequencyDays, rule.LookbackDays);
        var periods = new List<AdaptiveActivationPeriodRow>();
        var activeRanges = new List<(DateTime Start, DateTime End)>();
        var cooldownUntil = DateTime.MinValue;

        foreach (var checkpoint in checkpoints)
        {
            var lookbackStart = checkpoint.AddDays(-rule.LookbackDays);
            var lookbackTrades = ordered
                .Where(t => t.ExitTimeUtc < checkpoint && t.EntryTimeUtc >= lookbackStart)
                .ToArray();
            var metrics = ComputeLookbackMetrics(lookbackTrades);
            var regimeOk = PassesRegimeFilter(rule.RegimeFilter, checkpoint, btcContext, bnbIntervalCandles, btc30Q3Lower);

            var activated = false;
            var deactivationReason = string.Empty;

            if (checkpoint < cooldownUntil)
            {
                deactivationReason = $"CooldownUntil{cooldownUntil:yyyy-MM-dd}";
            }
            else
            {
                var basePass = EvaluateBaseCondition(rule, metrics);
                if (!basePass)
                {
                    deactivationReason = metrics.TradeCount < rule.MinLookbackTrades
                        ? $"InsufficientLookbackTrades({metrics.TradeCount}<{rule.MinLookbackTrades})"
                        : "LookbackConditionFailed";
                }
                else if (!regimeOk)
                {
                    deactivationReason = "RegimeFilterFailed";
                }
                else if (rule.ConditionType == AdaptiveActivationConditionType.DrawdownGuard
                         && (metrics.MaxDrawdown > rule.MaxDrawdownQuote
                             || metrics.MaxConsecutiveLosses >= rule.ConsecutiveLossLimit))
                {
                    deactivationReason = metrics.MaxDrawdown > rule.MaxDrawdownQuote
                        ? $"DrawdownExceeded({metrics.MaxDrawdown:F2}>{rule.MaxDrawdownQuote:F0})"
                        : $"ConsecutiveLosses({metrics.MaxConsecutiveLosses}>={rule.ConsecutiveLossLimit})";
                    cooldownUntil = checkpoint.AddDays(rule.CooldownDays);
                }
                else
                {
                    activated = true;
                }
            }

            var activationStart = checkpoint;
            var activationEnd = checkpoint.AddDays(rule.ActivationPeriodDays);
            if (activated)
                activeRanges.Add((activationStart, activationEnd));

            var periodTrades = activated
                ? ordered.Where(t => t.EntryTimeUtc >= activationStart && t.EntryTimeUtc < activationEnd).ToArray()
                : [];
            periods.Add(new AdaptiveActivationPeriodRow
            {
                ActivationRuleName = rule.ActivationRuleName,
                LookbackDays = rule.LookbackDays,
                ActivationPeriodDays = rule.ActivationPeriodDays,
                CheckpointFrequencyDays = rule.CheckpointFrequencyDays,
                CheckpointUtc = checkpoint,
                ActivationStartUtc = activationStart,
                ActivationEndUtc = activationEnd,
                LookbackTradeCount = metrics.TradeCount,
                LookbackNetPnl = Math.Round(metrics.NetPnl, 8),
                LookbackProfitFactor = Math.Round(metrics.ProfitFactor, 6),
                LookbackWinRate = Math.Round(metrics.WinRate, 6),
                Activated = activated,
                DeactivationReason = deactivationReason,
                TradesDuringActivation = periodTrades.Length,
                NetPnlDuringActivation = Math.Round(periodTrades.Sum(t => t.NetPnlQuote), 8),
                CostScenario = costScenario
            });
        }

        var taken = SelectTradesInActiveRanges(ordered, activeRanges);
        var summary = NoPaidDataAdaptiveActivationV1Aggregator.BuildSummaryRow(
            rule, taken, baselineTrades, periods, costScenario, dataEndUtc);
        var tradeRows = taken.Select(t =>
        {
            var range = activeRanges.FirstOrDefault(r => t.EntryTimeUtc >= r.Start && t.EntryTimeUtc < r.End);
            return new AdaptiveActivationTradeRow
            {
                ActivationRuleName = rule.ActivationRuleName,
                EntryTimeUtc = t.EntryTimeUtc,
                ExitTimeUtc = t.ExitTimeUtc,
                NetPnlQuote = Math.Round(t.NetPnlQuote, 8),
                IsWinner = t.IsWinner,
                ExitReason = t.ExitReason,
                CostScenario = costScenario,
                ActivationStartUtc = range.Start,
                ActivationEndUtc = range.End,
                InOlder = t.InOlder,
                InRecent90d = t.InRecent90d,
                MonthKey = t.MonthKey
            };
        }).ToArray();

        return new AdaptiveActivationSimResult(rule, periods, tradeRows, summary);
    }

    private static AdaptiveActivationSimResult BuildAlwaysOn(
        AdaptiveActivationRuleConfig rule,
        IReadOnlyList<RegimeDriftDiagnosticTrade> costedTrades,
        IReadOnlyList<RegimeDriftDiagnosticTrade> baselineTrades,
        DateTime dataStartUtc,
        DateTime dataEndUtc,
        string costScenario)
    {
        var ordered = costedTrades.OrderBy(t => t.EntryTimeUtc).ToArray();
        var periods = new List<AdaptiveActivationPeriodRow>
        {
            new()
            {
                ActivationRuleName = rule.ActivationRuleName,
                LookbackDays = 0,
                ActivationPeriodDays = (int)Math.Max(1, (dataEndUtc - dataStartUtc).TotalDays),
                CheckpointFrequencyDays = 1,
                CheckpointUtc = dataStartUtc,
                ActivationStartUtc = dataStartUtc,
                ActivationEndUtc = dataEndUtc,
                LookbackTradeCount = 0,
                Activated = true,
                DeactivationReason = string.Empty,
                TradesDuringActivation = ordered.Length,
                NetPnlDuringActivation = Math.Round(ordered.Sum(t => t.NetPnlQuote), 8),
                CostScenario = costScenario
            }
        };
        var summary = NoPaidDataAdaptiveActivationV1Aggregator.BuildSummaryRow(
            rule, ordered, baselineTrades, periods, costScenario, dataEndUtc);
        var tradeRows = ordered.Select(t => new AdaptiveActivationTradeRow
        {
            ActivationRuleName = rule.ActivationRuleName,
            EntryTimeUtc = t.EntryTimeUtc,
            ExitTimeUtc = t.ExitTimeUtc,
            NetPnlQuote = Math.Round(t.NetPnlQuote, 8),
            IsWinner = t.IsWinner,
            ExitReason = t.ExitReason,
            CostScenario = costScenario,
            ActivationStartUtc = dataStartUtc,
            ActivationEndUtc = dataEndUtc,
            InOlder = t.InOlder,
            InRecent90d = t.InRecent90d,
            MonthKey = t.MonthKey
        }).ToArray();
        return new AdaptiveActivationSimResult(rule, periods, tradeRows, summary);
    }

    private static List<RegimeDriftDiagnosticTrade> SelectTradesInActiveRanges(
        IReadOnlyList<RegimeDriftDiagnosticTrade> ordered,
        IReadOnlyList<(DateTime Start, DateTime End)> activeRanges)
    {
        if (activeRanges.Count == 0)
            return [];
        var merged = MergeRanges(activeRanges);
        return ordered.Where(t => merged.Any(r => t.EntryTimeUtc >= r.Start && t.EntryTimeUtc < r.End)).ToList();
    }

    private static List<(DateTime Start, DateTime End)> MergeRanges(IReadOnlyList<(DateTime Start, DateTime End)> ranges)
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

    private static IReadOnlyList<DateTime> BuildCheckpoints(
        DateTime dataStartUtc, DateTime dataEndUtc, int frequencyDays, int lookbackDays)
    {
        var first = dataStartUtc.AddDays(lookbackDays).Date;
        var last = dataEndUtc.Date;
        var points = new List<DateTime>();
        for (var cursor = first; cursor < last; cursor = cursor.AddDays(frequencyDays))
            points.Add(cursor);
        return points;
    }

    private static bool EvaluateBaseCondition(AdaptiveActivationRuleConfig rule, LookbackMetrics metrics)
    {
        if (metrics.TradeCount < rule.MinLookbackTrades)
            return false;

        return rule.ConditionType switch
        {
            AdaptiveActivationConditionType.RecentNetPositive => metrics.NetPnl > 0m,
            AdaptiveActivationConditionType.RecentProfitFactor =>
                metrics.ProfitFactor > (rule.ProfitFactorThreshold ?? 1.1m),
            AdaptiveActivationConditionType.RecentWinRateAndNet =>
                metrics.WinRate > 0.5m && metrics.NetPnl > 0m,
            AdaptiveActivationConditionType.DrawdownGuard => metrics.NetPnl > 0m,
            AdaptiveActivationConditionType.RegimeRecentPerformance => metrics.NetPnl > 0m,
            _ => false
        };
    }

    private static bool PassesRegimeFilter(
        AdaptiveRegimeFilterKind filter,
        DateTime checkpoint,
        BtcContextIndex btcContext,
        IReadOnlyList<KlineCandle> bnbIntervalCandles,
        decimal btc30Q3Lower)
    {
        if (filter == AdaptiveRegimeFilterKind.None)
            return true;

        var btc = btcContext.GetSnapshot(checkpoint);
        var bnbVol = ResolveBnbVolatilityRegime(bnbIntervalCandles, checkpoint);

        return filter switch
        {
            AdaptiveRegimeFilterKind.BtcReturn30mPositive =>
                btc?.BtcReturn30mPercent.HasValue == true && btc.BtcReturn30mPercent.Value > 0m,
            AdaptiveRegimeFilterKind.BtcReturn60mPositive =>
                btc?.BtcReturn60mPercent.HasValue == true && btc.BtcReturn60mPercent.Value > 0m,
            AdaptiveRegimeFilterKind.VolatilityNormal =>
                string.Equals(bnbVol, "Normal", StringComparison.OrdinalIgnoreCase),
            AdaptiveRegimeFilterKind.Btc30Q3VolNormal =>
                btc?.BtcReturn30mPercent.HasValue == true
                && btc.BtcReturn30mPercent.Value >= btc30Q3Lower
                && string.Equals(bnbVol, "Normal", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private static string? ResolveBnbVolatilityRegime(IReadOnlyList<KlineCandle> candles, DateTime checkpoint)
    {
        if (candles.Count <= MarketRegimeForwardEdgeScanner.MinimumWarmupCandles + 2)
            return null;
        var idx = FindCandleIndex(candles, checkpoint);
        if (idx < MarketRegimeForwardEdgeScanner.MinimumWarmupCandles)
            return null;
        var features = MarketRegimeForwardEdgeScanner.ComputeRegimeCandleFeatures(
            candles, idx, null, null, candles[idx].OpenTimeUtc);
        return features.VolatilityRegime;
    }

    private static int FindCandleIndex(IReadOnlyList<KlineCandle> candles, DateTime timeUtc)
    {
        var lo = 0;
        var hi = candles.Count - 1;
        var result = 0;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (candles[mid].OpenTimeUtc <= timeUtc)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return result;
    }

    private static LookbackMetrics ComputeLookbackMetrics(IReadOnlyList<RegimeDriftDiagnosticTrade> trades)
    {
        if (trades.Count == 0)
            return new LookbackMetrics(0, 0m, 0m, 0m, 0m, 0);

        var net = trades.Sum(t => t.NetPnlQuote);
        var wins = trades.Count(t => t.NetPnlQuote > 0m);
        var grossWin = trades.Where(t => t.NetPnlQuote > 0m).Sum(t => t.NetPnlQuote);
        var grossLoss = Math.Abs(trades.Where(t => t.NetPnlQuote <= 0m).Sum(t => t.NetPnlQuote));
        var pf = grossLoss == 0m ? (grossWin > 0m ? 999m : 0m) : grossWin / grossLoss;

        decimal peak = 0m, equity = 0m, maxDd = 0m;
        var maxConsec = 0;
        var consec = 0;
        foreach (var trade in trades.OrderBy(t => t.ExitTimeUtc))
        {
            equity += trade.NetPnlQuote;
            if (equity > peak)
                peak = equity;
            var dd = peak - equity;
            if (dd > maxDd)
                maxDd = dd;
            if (trade.NetPnlQuote <= 0m)
            {
                consec++;
                if (consec > maxConsec)
                    maxConsec = consec;
            }
            else
            {
                consec = 0;
            }
        }

        return new LookbackMetrics(
            trades.Count,
            net,
            pf,
            (decimal)wins / trades.Count,
            maxDd,
            maxConsec);
    }
}
