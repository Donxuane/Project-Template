using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// Point-in-time activation and entry evaluation for shadow runner.
/// Mirrors frozen incubation engine decisions without modifying strategy logic.
/// </summary>
internal static class FuturesTestnetShadowEvaluator
{
    internal sealed record ActivationState(bool Passed, string Reason, DateTime? ActivationStartUtc, DateTime? ActivationEndUtc);

    internal sealed record EntryState(
        bool Present,
        string Reason,
        decimal? EntryPrice,
        DateTime? EntryTimeUtc);

    internal static ActivationState EvaluateCrossSymbolActivation(
        CrossSymbolActivationConfig config,
        CrossSymbolComboKey key,
        IReadOnlyList<RegimeDriftDiagnosticTrade> moderateTrades,
        DateTime evalUtc,
        DateTime frozenStartUtc,
        ShortWindowFlowFeatureIndex flowIndex)
    {
        if (config.IsAlwaysOn)
            return new ActivationState(true, "AlwaysOn", frozenStartUtc, evalUtc);

        ActivationState? active = null;
        for (var checkpoint = frozenStartUtc; checkpoint <= evalUtc; checkpoint = checkpoint.AddHours(config.CheckpointFrequencyHours))
        {
            var lookbackCount = 0;
            var lookbackNet = 0m;
            var lookbackPf = 0m;
            var perfPass = true;
            var skipReason = string.Empty;

            if (config.PerfKind != CrossSymbolPerfKind.None)
            {
                var lookbackStart = checkpoint.AddDays(-config.LookbackDays);
                var lookbackTrades = moderateTrades
                    .Where(t => t.ExitTimeUtc < checkpoint && t.EntryTimeUtc >= lookbackStart)
                    .OrderBy(t => t.EntryTimeUtc)
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
            if (activated && evalUtc >= activationStart && evalUtc < activationEnd)
            {
                active = new ActivationState(true, $"Activated at checkpoint {checkpoint:yyyy-MM-dd HH:mm}Z", activationStart, activationEnd);
            }
            else if (!activated && evalUtc >= activationStart && evalUtc < activationEnd && active is null)
            {
                active = new ActivationState(false, skipReason, activationStart, activationEnd);
            }
        }

        return active ?? new ActivationState(false, "NoActiveActivationWindow", null, null);
    }

    internal static ActivationState EvaluateShortWindowActivation(
        ShortWindowActivationConfig config,
        IReadOnlyList<RegimeDriftDiagnosticTrade> moderateTrades,
        DateTime evalUtc,
        DateTime frozenStartUtc,
        ShortWindowFlowFeatureIndex flowIndex)
    {
        if (config.PerfCondition == ShortWindowPerfCondition.AlwaysOn)
            return new ActivationState(true, "AlwaysOn", frozenStartUtc, evalUtc);

        ActivationState? active = null;
        var ordered = moderateTrades.OrderBy(t => t.EntryTimeUtc).ToArray();

        for (var checkpoint = frozenStartUtc; checkpoint <= evalUtc; checkpoint = checkpoint.AddHours(config.CheckpointFrequencyHours))
        {
            var lookbackStart = checkpoint.AddDays(-config.LookbackDays);
            var lookbackTrades = ordered
                .Where(t => t.ExitTimeUtc < checkpoint && t.EntryTimeUtc >= lookbackStart)
                .ToArray();
            var tradeCount = lookbackTrades.Length;
            var net = lookbackTrades.Sum(t => t.NetPnlQuote);
            var grossWin = lookbackTrades.Where(t => t.NetPnlQuote > 0m).Sum(t => t.NetPnlQuote);
            var grossLoss = Math.Abs(lookbackTrades.Where(t => t.NetPnlQuote <= 0m).Sum(t => t.NetPnlQuote));
            var pf = grossLoss == 0m ? (grossWin > 0m ? 999m : 0m) : grossWin / grossLoss;

            var snapshot = flowIndex.Snapshot(checkpoint);
            var (flowAvailable, flowPass) = NoPaidDataShortWindowFlowResearchV1Catalog.EvaluateFlow(config.FlowCondition, snapshot);
            var perfPass = EvaluatePerf(config, tradeCount, net, pf);

            bool activated;
            var skipReason = string.Empty;
            if (config.PerfCondition == ShortWindowPerfCondition.None)
            {
                activated = flowAvailable && flowPass;
                if (!flowAvailable) skipReason = "FlowDataUnavailable";
                else if (!flowPass) skipReason = "FlowConditionFailed";
            }
            else if (!perfPass)
            {
                activated = false;
                skipReason = tradeCount < config.MinLookbackTrades
                    ? $"InsufficientLookbackTrades({tradeCount}<{config.MinLookbackTrades})"
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

            var activationStart = checkpoint;
            var activationEnd = checkpoint.AddHours(config.ActivationPeriodHours);
            if (activated && evalUtc >= activationStart && evalUtc < activationEnd)
                active = new ActivationState(true, $"Activated at checkpoint {checkpoint:yyyy-MM-dd HH:mm}Z", activationStart, activationEnd);
            else if (!activated && evalUtc >= activationStart && evalUtc < activationEnd && active is null)
                active = new ActivationState(false, skipReason, activationStart, activationEnd);
        }

        return active ?? new ActivationState(false, "NoActiveActivationWindow", null, null);
    }

    internal static EntryState EvaluateCrossSymbolEntryNow(
        CrossSymbolComboKey key,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        DateTime evalUtc,
        DateTime frozenStartUtc,
        int cooldownCandles)
    {
        if (intervalCandles.Count < MarketRegimeForwardEdgeScanner.MinimumWarmupCandles + 2)
            return new EntryState(false, "InsufficientIntervalCandles", null, null);

        var intervalMinutes = IntervalMinutes(key.Interval);
        var windowStart = evalUtc.AddMinutes(-intervalMinutes * 2);
        var candidateTrade = baseTrades
            .Where(t => t.TimeUtc >= frozenStartUtc && t.TimeUtc >= windowStart && t.TimeUtc <= evalUtc)
            .OrderByDescending(t => t.TimeUtc)
            .FirstOrDefault();

        if (candidateTrade is null)
            return new EntryState(false, "NoEntrySignalInLatestIntervalWindow", null, null);

        var openTrade = baseTrades.LastOrDefault(t =>
            t.TimeUtc <= evalUtc && TradeExitUtc(t) > evalUtc && t.TimeUtc >= frozenStartUtc);
        if (openTrade is not null)
            return new EntryState(false, "OpenTradeOverlap", null, null);

        var lastExit = baseTrades
            .Where(t => TradeExitUtc(t) <= evalUtc && t.TimeUtc >= frozenStartUtc)
            .OrderByDescending(t => TradeExitUtc(t))
            .FirstOrDefault();
        if (lastExit is not null)
        {
            var idx = FindCandleIndex(intervalCandles, TradeExitUtc(lastExit));
            var nextAllowed = idx + 1 + cooldownCandles;
            var entryIdx = FindCandleIndex(intervalCandles, candidateTrade.TimeUtc);
            if (entryIdx < nextAllowed)
                return new EntryState(false, "CooldownActive", null, null);
        }

        return new EntryState(true, $"NearExtremeShort signal; entry at {candidateTrade.TimeUtc:yyyy-MM-dd HH:mm}Z", candidateTrade.EntryPrice, candidateTrade.TimeUtc);
    }

    internal static EntryState EvaluateBnbRule01EntryNow(
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        DateTime evalUtc,
        DateTime frozenStartUtc,
        int intervalMinutes)
    {
        var windowStart = evalUtc.AddMinutes(-intervalMinutes * 2);
        var candidateTrade = baseTrades
            .Where(t => t.TimeUtc >= frozenStartUtc && t.TimeUtc >= windowStart && t.TimeUtc <= evalUtc)
            .OrderByDescending(t => t.TimeUtc)
            .FirstOrDefault();

        if (candidateTrade is null)
            return new EntryState(false, "NoRule01EntrySignalInLatestIntervalWindow", null, null);

        var openTrade = baseTrades.LastOrDefault(t =>
            t.TimeUtc <= evalUtc && TradeExitUtc(t) > evalUtc && t.TimeUtc >= frozenStartUtc);
        if (openTrade is not null)
            return new EntryState(false, "OpenTradeOverlap", null, null);

        return new EntryState(true, $"Rule01 short signal; entry at {candidateTrade.TimeUtc:yyyy-MM-dd HH:mm}Z", candidateTrade.EntryPrice, candidateTrade.TimeUtc);
    }

    internal static decimal? EstimateNetPnlPer100Usdt(decimal targetPercent, decimal stopPercent, LongShortDirection direction)
    {
        var scenarios = DirectionalRuleFuturesValidationV3CostModel.BuildValidationScenarios();
        var moderate = scenarios.First(s => string.Equals(s.Label, FuturesTestnetShadowCatalog.PrimaryCostScenario, StringComparison.OrdinalIgnoreCase));
        var roundTripCost = DirectionalRuleFuturesValidationV3CostModel.EstimateRoundTripCostPercent(moderate);
        var expectedMove = targetPercent - roundTripCost;
        return Math.Round(expectedMove, 4);
    }

    private static DateTime TradeExitUtc(DirectionalRuleV2TradeRecord trade)
        => trade.TimeUtc.AddMinutes((double)trade.DurationMinutes);

    private static bool EvaluatePerf(ShortWindowActivationConfig config, int tradeCount, decimal net, decimal pf)
    {
        if (config.PerfCondition == ShortWindowPerfCondition.None)
            return true;
        if (tradeCount < config.MinLookbackTrades)
            return false;
        return config.PerfCondition switch
        {
            ShortWindowPerfCondition.RecentNetPositive => net > 0m,
            ShortWindowPerfCondition.RecentProfitFactor => pf > (config.ProfitFactorThreshold ?? 1.1m),
            _ => true
        };
    }

    private static int IntervalMinutes(string interval)
        => interval switch
        {
            "5m" => 5,
            "15m" => 15,
            "30m" => 30,
            "1m" => 1,
            _ => 5
        };

    private static int FindCandleIndex(IReadOnlyList<KlineCandle> candles, DateTime timeUtc)
    {
        var lo = 0;
        var hi = candles.Count;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (candles[mid].OpenTimeUtc > timeUtc)
                hi = mid;
            else
                lo = mid + 1;
        }

        return Math.Max(0, lo - 1);
    }
}
