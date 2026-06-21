using System.Globalization;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// Diagnostic-only pre-freeze replay and missed-opportunity audit. Does not affect forward
/// health gates, verdict, or frozen profile state.
/// </summary>
public static class ForwardIncubationAcceleratedValidationBuilder
{
    private static readonly int[] ReplayDayWindows = [3, 7, 14];
    private const decimal MeaningfulMoveFloorPercent = 0.75m;

    private sealed record TradeStats(
        int Count, decimal Net, decimal WinRate, decimal ProfitFactor, decimal MaxDrawdown, int MaxConsecutiveLosses);

    public static ForwardIncubationAcceleratedValidationSummary Build(
        CrossSymbolComboKey frozenKey,
        CrossSymbolActivationConfig frozenConfig,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        IReadOnlyList<KlineCandle> intervalCandles,
        BtcContextIndex btcContext,
        ShortWindowFlowFeatureIndex flowIndex,
        DateTime frozenStartUtc,
        DateTime forwardEndUtc,
        decimal trueForwardNetModerate,
        string primaryCostScenario,
        string moderateSlippageScenario,
        string stressPlusScenario)
    {
        var replayRows = new List<HistoricalStressReplayRow>();
        var netByReplayDays = new Dictionary<int, decimal>();

        foreach (var days in ReplayDayWindows)
        {
            var replayStart = frozenStartUtc.AddDays(-days);
            var replayEnd = frozenStartUtc;
            if (replayStart >= replayEnd)
                continue;

            var moderateTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
                baseTrades, primaryCostScenario, btcContext, replayEnd);
            var latencyTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
                baseTrades, moderateSlippageScenario, btcContext, replayEnd);
            var stressTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
                baseTrades, stressPlusScenario, btcContext, replayEnd);

            var sim = NoPaidDataShortWindowFlowResearchV1CrossSymbolEngine.Simulate(
                frozenKey, frozenConfig, moderateTrades, replayStart, replayEnd, flowIndex,
                primaryCostScenario, collectPeriods: true);
            var stats = Stats(sim.TakenTrades);
            var latencySim = NoPaidDataShortWindowFlowResearchV1CrossSymbolEngine.Simulate(
                frozenKey, frozenConfig, latencyTrades, replayStart, replayEnd, flowIndex,
                moderateSlippageScenario, collectPeriods: false);
            var stressSim = NoPaidDataShortWindowFlowResearchV1CrossSymbolEngine.Simulate(
                frozenKey, frozenConfig, stressTrades, replayStart, replayEnd, flowIndex,
                stressPlusScenario, collectPeriods: false);

            netByReplayDays[days] = stats.Net;

            var activatedButNoEntry = sim.Periods.Count(p => p.Activated && p.TradesInActivationWindow == 0);
            replayRows.Add(new HistoricalStressReplayRow
            {
                Label = "PreFreezeReplay",
                ReplayDaysBeforeFreeze = days,
                ReplayStartUtc = replayStart,
                ReplayEndUtc = replayEnd,
                Trades = stats.Count,
                NetModerate = stats.Net,
                NetModerateLatency002 = Stats(latencySim.TakenTrades).Net,
                NetStressPlus = Stats(stressSim.TakenTrades).Net,
                WinRate = stats.WinRate,
                ProfitFactor = stats.ProfitFactor,
                MaxDrawdown = stats.MaxDrawdown,
                MaxConsecutiveLosses = stats.MaxConsecutiveLosses,
                ActivationCheckpointCount = sim.Periods.Count,
                ActivatedCheckpointCount = sim.Periods.Count(p => p.Activated),
                ActivationFailedCheckpointCount = sim.Periods.Count(p => !p.Activated && !string.IsNullOrEmpty(p.SkipReason)),
                ActivatedButNoEntryCount = activatedButNoEntry,
                TopActivationSkipReasons = TopReasons(
                    sim.Periods.Where(p => !p.Activated && !string.IsNullOrEmpty(p.SkipReason)).Select(p => p.SkipReason)),
                TopEntrySkipReasons = BuildEntrySkipReasons(sim.Periods, baseTrades, replayStart, replayEnd)
            });
        }

        var auditStart = frozenStartUtc.AddDays(-14);
        var auditEnd = forwardEndUtc > frozenStartUtc ? forwardEndUtc : frozenStartUtc;
        var auditModerateTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
            baseTrades, primaryCostScenario, btcContext, auditEnd);
        var auditSim = NoPaidDataShortWindowFlowResearchV1CrossSymbolEngine.Simulate(
            frozenKey, frozenConfig, auditModerateTrades, auditStart, auditEnd, flowIndex,
            primaryCostScenario, collectPeriods: true);
        var forwardModerateTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
            baseTrades, primaryCostScenario, btcContext, auditEnd);
        var forwardSim = NoPaidDataShortWindowFlowResearchV1CrossSymbolEngine.Simulate(
            frozenKey, frozenConfig, forwardModerateTrades, frozenStartUtc, auditEnd, flowIndex,
            primaryCostScenario, collectPeriods: true);

        var auditRows = BuildMissedOpportunityAudit(
            frozenKey,
            frozenStartUtc,
            auditStart,
            auditEnd,
            intervalCandles,
            baseTrades,
            btcContext,
            auditSim,
            forwardSim,
            primaryCostScenario,
            moderateSlippageScenario,
            stressPlusScenario);

        var missedWinners = auditRows.Count(r =>
            r.Classification is "ActivatedButSignalMissedWinner" or "ActivationBlockedWinner");
        var blockedLosers = auditRows.Count(r => r.Classification == "ActivationBlockedLoser");

        var net3 = netByReplayDays.GetValueOrDefault(3);
        var net7 = netByReplayDays.GetValueOrDefault(7);
        var net14 = netByReplayDays.GetValueOrDefault(14);
        var mainFinding = ResolveMainFinding(trueForwardNetModerate, net3, net7, net14, missedWinners, blockedLosers, auditRows.Count);

        var compact = string.Join(" | ",
            $"TrueForwardNet={trueForwardNetModerate:F2}",
            $"PreFreezeReplayNet3d={net3:F2}",
            $"PreFreezeReplayNet7d={net7:F2}",
            $"PreFreezeReplayNet14d={net14:F2}",
            $"MissedWinners={missedWinners}",
            $"BlockedLosers={blockedLosers}",
            $"MainFinding={mainFinding}");

        return new ForwardIncubationAcceleratedValidationSummary
        {
            TrueForwardNet = trueForwardNetModerate,
            PreFreezeReplayNet3d = net3,
            PreFreezeReplayNet7d = net7,
            PreFreezeReplayNet14d = net14,
            MissedWinnersCount = missedWinners,
            BlockedLosersCount = blockedLosers,
            MainFinding = mainFinding,
            CompactSummaryLine = compact,
            HistoricalStressReplay = replayRows,
            MissedOpportunityAudit = auditRows
        };
    }

    private sealed record HypotheticalTradeOutcome(
        decimal? EntryPrice,
        decimal? ExitPrice,
        string ExitReason,
        decimal NetModerate,
        decimal NetLatency002,
        decimal NetStressPlus,
        bool WouldHitTarget,
        bool WouldHitStop,
        bool IsHindsightOnly);

    private static List<MissedOpportunityAuditRow> BuildMissedOpportunityAudit(
        CrossSymbolComboKey frozenKey,
        DateTime frozenStartUtc,
        DateTime auditStartUtc,
        DateTime auditEndUtc,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        BtcContextIndex btcContext,
        CrossSymbolSimOutcome preFreezeSim,
        CrossSymbolSimOutcome forwardSim,
        string primaryCostScenario,
        string moderateSlippageScenario,
        string stressPlusScenario)
    {
        var rows = new List<MissedOpportunityAuditRow>();
        if (auditEndUtc <= auditStartUtc)
            return rows;

        var moderateMapped = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
            baseTrades, primaryCostScenario, btcContext, auditEndUtc);
        var latencyMapped = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
            baseTrades, moderateSlippageScenario, btcContext, auditEndUtc);
        var stressMapped = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
            baseTrades, stressPlusScenario, btcContext, auditEndUtc);

        var day = auditStartUtc.Date;
        var lastDay = auditEndUtc.Date;
        if (auditEndUtc > lastDay)
            lastDay = lastDay.AddDays(1);

        while (day < lastDay)
        {
            var windowStart = day;
            var windowEnd = day.AddDays(1);
            if (windowEnd <= auditStartUtc || windowStart >= auditEndUtc)
            {
                day = day.AddDays(1);
                continue;
            }

            if (windowStart < auditStartUtc)
                windowStart = auditStartUtc;
            if (windowEnd > auditEndUtc)
                windowEnd = auditEndUtc;

            var periodLabel = windowEnd <= frozenStartUtc
                ? "PreFreezeReplay"
                : windowStart >= frozenStartUtc
                    ? "TrueForward"
                    : "PreFreezeReplay";

            var candlesInWindow = intervalCandles
                .Where(c => c.OpenTimeUtc >= windowStart && c.OpenTimeUtc < windowEnd)
                .ToArray();
            var maxMove = ComputeMaxFavorableShortMovePercent(candlesInWindow);
            var meaningfulThreshold = Math.Max(MeaningfulMoveFloorPercent, frozenKey.TargetPercent * 0.5m);
            var meaningful = maxMove >= meaningfulThreshold;

            var signalsInWindow = baseTrades
                .Where(t => t.Direction == frozenKey.Direction
                            && t.TimeUtc >= windowStart
                            && t.TimeUtc < windowEnd)
                .OrderBy(t => t.TimeUtc)
                .ToArray();

            var sim = windowStart >= frozenStartUtc ? forwardSim : preFreezeSim;
            var takenInWindow = sim.TakenTrades
                .Where(t => t.EntryTimeUtc >= windowStart && t.EntryTimeUtc < windowEnd)
                .OrderBy(t => t.EntryTimeUtc)
                .ToArray();

            var activated = IsActivatedInWindow(sim, windowStart, windowEnd);
            var wasActivationPassed = activated;
            var wasBaseSignalPresent = signalsInWindow.Length > 0;
            var baseSignalCount = signalsInWindow.Length;
            var activationState = activated ? "Activated" : "NotActivated";
            var entrySignalState = wasBaseSignalPresent
                ? $"SignalCount={baseSignalCount}"
                : "NoBaseSignal";

            var hypo = BuildHypotheticalOutcome(
                frozenKey,
                signalsInWindow,
                candlesInWindow,
                takenInWindow,
                moderateMapped,
                latencyMapped,
                stressMapped,
                btcContext,
                auditEndUtc,
                primaryCostScenario,
                moderateSlippageScenario,
                stressPlusScenario);

            var wouldHitTarget = hypo.WouldHitTarget;
            var wouldHitStop = hypo.WouldHitStop;
            var wouldWin = hypo.NetModerate > 0m;

            string classification;
            string reason;
            if (!meaningful)
            {
                classification = "NoTradeNoMeaningfulOpportunity";
                reason = $"MaxFavorableShortMove={maxMove:F2}% below threshold {meaningfulThreshold:F2}%";
            }
            else if (takenInWindow.Length > 0)
            {
                classification = "SignalCorrectlyTraded";
                var trade = takenInWindow[0];
                reason = $"TradeTaken net={trade.NetPnlQuote:F2} exit={NormalizeHypotheticalExitReason(trade.ExitReason)}";
            }
            else if (wasActivationPassed)
            {
                if (wouldWin)
                {
                    classification = "ActivatedButSignalMissedWinner";
                    reason = wasBaseSignalPresent
                        ? "Activated with base signal but no trade taken; hypothetical winner"
                        : "Activated but no trade; hindsight favorable move (diagnostic only)";
                }
                else
                {
                    classification = "ActivatedButSignalMissedLoser";
                    reason = wasBaseSignalPresent
                        ? "Activated with base signal but no trade taken; hypothetical loser"
                        : "Activated but no trade; hindsight move not profitable";
                }
            }
            else if (wouldWin)
            {
                if (wasBaseSignalPresent)
                {
                    classification = "ActivationBlockedWinner";
                    reason = "Activation blocked; base signal present; hypothetical winner";
                }
                else
                {
                    classification = "ActivationBlockedMoveOnly";
                    reason = "Activation blocked; favorable move without base signal (hindsight only)";
                }
            }
            else
            {
                classification = "ActivationBlockedLoser";
                reason = wasBaseSignalPresent
                    ? "Activation blocked; base signal present; hypothetical loser — filter avoided loss"
                    : "Activation blocked; no base signal; hindsight move not profitable";
            }

            rows.Add(new MissedOpportunityAuditRow
            {
                Symbol = frozenKey.Symbol.ToString(),
                Interval = frozenKey.Interval,
                PeriodLabel = periodLabel,
                WindowStartUtc = windowStart,
                WindowEndUtc = windowEnd,
                MaxFavorableShortMovePercent = Math.Round(maxMove, 4),
                WouldHitTarget = wouldHitTarget,
                WouldHitStop = wouldHitStop,
                EstimatedNetModerate = Math.Round(hypo.NetModerate, 8),
                HypotheticalEntryPrice = hypo.EntryPrice,
                HypotheticalExitPrice = hypo.ExitPrice,
                HypotheticalExitReason = hypo.ExitReason,
                HypotheticalNetModerate = Math.Round(hypo.NetModerate, 8),
                HypotheticalNetLatency002 = Math.Round(hypo.NetLatency002, 8),
                HypotheticalNetStressPlus = Math.Round(hypo.NetStressPlus, 8),
                WasBaseSignalPresent = wasBaseSignalPresent,
                BaseSignalCount = baseSignalCount,
                WasActivationPassed = wasActivationPassed,
                IsHindsightOnly = hypo.IsHindsightOnly,
                ActivationState = activationState,
                EntrySignalState = entrySignalState,
                Classification = classification,
                Reason = reason
            });

            day = day.AddDays(1);
        }

        return rows;
    }

    private static bool IsActivatedInWindow(CrossSymbolSimOutcome sim, DateTime windowStart, DateTime windowEnd)
        => sim.Periods.Any(p => p.Activated
                                && p.ActivationStartUtc < windowEnd
                                && p.ActivationEndUtc > windowStart);

    private static decimal ComputeMaxFavorableShortMovePercent(IReadOnlyList<KlineCandle> candles)
    {
        if (candles.Count == 0)
            return 0m;

        decimal maxMove = 0m;
        for (var i = 0; i < candles.Count; i++)
        {
            var entry = candles[i].High;
            if (entry <= 0m)
                continue;
            var minLow = candles[i].Low;
            for (var j = i; j < candles.Count; j++)
            {
                if (candles[j].Low < minLow)
                    minLow = candles[j].Low;
                var move = (entry - minLow) / entry * 100m;
                if (move > maxMove)
                    maxMove = move;
            }
        }

        return maxMove;
    }

    private static HypotheticalTradeOutcome BuildHypotheticalOutcome(
        CrossSymbolComboKey key,
        IReadOnlyList<DirectionalRuleV2TradeRecord> signalsInWindow,
        IReadOnlyList<KlineCandle> candlesInWindow,
        IReadOnlyList<RegimeDriftDiagnosticTrade> takenInWindow,
        IReadOnlyList<RegimeDriftDiagnosticTrade> moderateMapped,
        IReadOnlyList<RegimeDriftDiagnosticTrade> latencyMapped,
        IReadOnlyList<RegimeDriftDiagnosticTrade> stressMapped,
        BtcContextIndex btcContext,
        DateTime dataEndUtc,
        string primaryCostScenario,
        string moderateSlippageScenario,
        string stressPlusScenario)
    {
        if (takenInWindow.Count > 0)
            return FromTakenTrade(takenInWindow[0], signalsInWindow, moderateMapped, latencyMapped, stressMapped, key);

        if (signalsInWindow.Count > 0)
            return FromBaseSignal(signalsInWindow[0], moderateMapped, latencyMapped, stressMapped, key);

        return FromHindsightSimulation(
            key, candlesInWindow, btcContext, dataEndUtc,
            primaryCostScenario, moderateSlippageScenario, stressPlusScenario);
    }

    private static HypotheticalTradeOutcome FromTakenTrade(
        RegimeDriftDiagnosticTrade taken,
        IReadOnlyList<DirectionalRuleV2TradeRecord> signalsInWindow,
        IReadOnlyList<RegimeDriftDiagnosticTrade> moderateMapped,
        IReadOnlyList<RegimeDriftDiagnosticTrade> latencyMapped,
        IReadOnlyList<RegimeDriftDiagnosticTrade> stressMapped,
        CrossSymbolComboKey key)
    {
        var signal = signalsInWindow.FirstOrDefault(s => s.TimeUtc == taken.EntryTimeUtc);
        var exitReason = NormalizeHypotheticalExitReason(taken.ExitReason);
        var hitTarget = exitReason == "ProfitTarget"
                        || (taken.MfePercent ?? signal?.MfePercent ?? 0m) >= key.TargetPercent;
        var hitStop = exitReason == "StopLoss"
                      || (taken.MaePercent ?? signal?.MaePercent ?? 0m) >= key.StopPercent;

        return new HypotheticalTradeOutcome(
            signal?.EntryPrice,
            signal?.ExitPrice,
            exitReason,
            taken.NetPnlQuote,
            LookupNetByEntryTime(latencyMapped, taken.EntryTimeUtc),
            LookupNetByEntryTime(stressMapped, taken.EntryTimeUtc),
            hitTarget,
            hitStop,
            IsHindsightOnly: false);
    }

    private static HypotheticalTradeOutcome FromBaseSignal(
        DirectionalRuleV2TradeRecord signal,
        IReadOnlyList<RegimeDriftDiagnosticTrade> moderateMapped,
        IReadOnlyList<RegimeDriftDiagnosticTrade> latencyMapped,
        IReadOnlyList<RegimeDriftDiagnosticTrade> stressMapped,
        CrossSymbolComboKey key)
    {
        var exitReason = NormalizeHypotheticalExitReason(signal.ExitReason);
        var hitTarget = exitReason == "ProfitTarget" || (signal.MfePercent ?? 0m) >= key.TargetPercent;
        var hitStop = exitReason == "StopLoss" || (signal.MaePercent ?? 0m) >= key.StopPercent;

        return new HypotheticalTradeOutcome(
            signal.EntryPrice,
            signal.ExitPrice,
            exitReason,
            LookupNetByEntryTime(moderateMapped, signal.TimeUtc),
            LookupNetByEntryTime(latencyMapped, signal.TimeUtc),
            LookupNetByEntryTime(stressMapped, signal.TimeUtc),
            hitTarget,
            hitStop,
            IsHindsightOnly: false);
    }

    private static HypotheticalTradeOutcome FromHindsightSimulation(
        CrossSymbolComboKey key,
        IReadOnlyList<KlineCandle> candlesInWindow,
        BtcContextIndex btcContext,
        DateTime dataEndUtc,
        string primaryCostScenario,
        string moderateSlippageScenario,
        string stressPlusScenario)
    {
        if (candlesInWindow.Count == 0)
            return EmptyHypotheticalOutcome(isHindsightOnly: true);

        var simulated = SimulateHindsightShort(key, candlesInWindow);
        if (simulated is null)
            return EmptyHypotheticalOutcome(isHindsightOnly: true);

        var moderateNet = MapSingleTradeNet(
            simulated, key, btcContext, dataEndUtc, primaryCostScenario);
        var latencyNet = MapSingleTradeNet(
            simulated, key, btcContext, dataEndUtc, moderateSlippageScenario);
        var stressNet = MapSingleTradeNet(
            simulated, key, btcContext, dataEndUtc, stressPlusScenario);

        return new HypotheticalTradeOutcome(
            simulated.EntryPrice,
            simulated.ExitPrice,
            NormalizeHypotheticalExitReason(simulated.ExitReason),
            moderateNet,
            latencyNet,
            stressNet,
            simulated.WouldHitTarget,
            simulated.WouldHitStop,
            IsHindsightOnly: true);
    }

    private sealed record HindsightSimResult(
        DateTime EntryTimeUtc,
        decimal EntryPrice,
        decimal ExitPrice,
        string ExitReason,
        bool WouldHitTarget,
        bool WouldHitStop,
        decimal DurationMinutes);

    private static HindsightSimResult? SimulateHindsightShort(
        CrossSymbolComboKey key,
        IReadOnlyList<KlineCandle> candles)
    {
        var bestIdx = 0;
        for (var i = 1; i < candles.Count; i++)
        {
            if (candles[i].High > candles[bestIdx].High)
                bestIdx = i;
        }

        var entryPrice = candles[bestIdx].High;
        if (entryPrice <= 0m)
            return null;

        var entryTime = candles[bestIdx].OpenTimeUtc;
        var targetPrice = entryPrice * (1m - key.TargetPercent / 100m);
        var stopPrice = entryPrice * (1m + key.StopPercent / 100m);
        var holdEnd = entryTime.AddMinutes(key.MaxHoldMinutes);

        var exitReason = "TimeStop";
        var exitPrice = candles[^1].Close;
        var exitTime = candles[^1].OpenTimeUtc;
        var wouldHitTarget = false;
        var wouldHitStop = false;

        for (var j = bestIdx + 1; j < candles.Count; j++)
        {
            var candle = candles[j];
            if (candle.OpenTimeUtc > holdEnd)
                break;

            if (candle.Low <= targetPrice)
            {
                exitReason = "ProfitTarget";
                exitPrice = targetPrice;
                exitTime = candle.OpenTimeUtc;
                wouldHitTarget = true;
                break;
            }

            if (candle.High >= stopPrice)
            {
                exitReason = "StopLoss";
                exitPrice = stopPrice;
                exitTime = candle.OpenTimeUtc;
                wouldHitStop = true;
                break;
            }

            exitPrice = candle.Close;
            exitTime = candle.OpenTimeUtc;
        }

        var durationMinutes = (decimal)Math.Max(0, (exitTime - entryTime).TotalMinutes);
        return new HindsightSimResult(entryTime, entryPrice, exitPrice, exitReason, wouldHitTarget, wouldHitStop, durationMinutes);
    }

    private static DirectionalRuleV2TradeRecord BuildHindsightBaseTrade(
        CrossSymbolComboKey key,
        HindsightSimResult sim)
        => new()
        {
            Direction = key.Direction,
            Symbol = key.Symbol,
            Interval = key.Interval,
            TimeUtc = sim.EntryTimeUtc,
            EntryPrice = sim.EntryPrice,
            ExitPrice = sim.ExitPrice,
            ExitReason = sim.ExitReason,
            TargetPercent = key.TargetPercent,
            StopPercent = key.StopPercent,
            MaxHoldMinutes = key.MaxHoldMinutes,
            GrossPnlQuote = sim.EntryPrice - sim.ExitPrice,
            NetPnlQuote = sim.EntryPrice - sim.ExitPrice,
            DurationMinutes = sim.DurationMinutes,
            MfePercent = sim.WouldHitTarget ? key.TargetPercent : null,
            MaePercent = sim.WouldHitStop ? key.StopPercent : null
        };

    private static decimal MapSingleTradeNet(
        HindsightSimResult sim,
        CrossSymbolComboKey key,
        BtcContextIndex btcContext,
        DateTime dataEndUtc,
        string scenarioLabel)
    {
        var baseTrade = BuildHindsightBaseTrade(key, sim);
        var mapped = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
            [baseTrade], scenarioLabel, btcContext, dataEndUtc);
        return mapped.Length > 0 ? mapped[0].NetPnlQuote : 0m;
    }

    private static decimal LookupNetByEntryTime(
        IReadOnlyList<RegimeDriftDiagnosticTrade> mapped,
        DateTime entryTimeUtc)
        => mapped.FirstOrDefault(t => t.EntryTimeUtc == entryTimeUtc)?.NetPnlQuote ?? 0m;

    private static HypotheticalTradeOutcome EmptyHypotheticalOutcome(bool isHindsightOnly)
        => new(null, null, "NoEntry", 0m, 0m, 0m, false, false, isHindsightOnly);

    private static string NormalizeHypotheticalExitReason(string? exitReason)
    {
        if (string.IsNullOrWhiteSpace(exitReason))
            return "NoEntry";

        if (exitReason.Contains("Profit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(exitReason, "ProfitTarget", StringComparison.OrdinalIgnoreCase)
            || string.Equals(exitReason, "ProfitLock", StringComparison.OrdinalIgnoreCase))
            return "ProfitTarget";

        if (string.Equals(exitReason, "StopLoss", StringComparison.OrdinalIgnoreCase)
            || (exitReason.Contains("Stop", StringComparison.OrdinalIgnoreCase)
                && !exitReason.Contains("Time", StringComparison.OrdinalIgnoreCase)))
            return "StopLoss";

        if (exitReason.Contains("Time", StringComparison.OrdinalIgnoreCase)
            || string.Equals(exitReason, "TimeStop", StringComparison.OrdinalIgnoreCase))
            return "TimeStop";

        if (string.Equals(exitReason, "InvalidEntry", StringComparison.OrdinalIgnoreCase)
            || string.Equals(exitReason, "NoEntry", StringComparison.OrdinalIgnoreCase))
            return "NoEntry";

        return exitReason;
    }

    private static IReadOnlyList<SkipReasonCountRow> BuildEntrySkipReasons(
        IReadOnlyList<CrossSymbolPeriodRow> periods,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        DateTime windowStart,
        DateTime windowEnd)
    {
        var reasons = new List<string>();
        foreach (var period in periods.Where(p => p.Activated && p.TradesInActivationWindow == 0))
        {
            var signals = baseTrades.Count(t =>
                t.TimeUtc >= period.ActivationStartUtc
                && t.TimeUtc < period.ActivationEndUtc
                && t.TimeUtc >= windowStart
                && t.TimeUtc < windowEnd);
            reasons.Add(signals > 0 ? "SignalsPresentButNotTaken" : "NoBaseSignalsInActivationWindow");
        }

        return TopReasons(reasons);
    }

    private static string ResolveMainFinding(
        decimal trueForwardNet,
        decimal net3,
        decimal net7,
        decimal net14,
        int missedWinners,
        int blockedLosers,
        int auditWindowCount)
    {
        if (auditWindowCount == 0)
            return "Insufficient audit windows; re-run after more data accumulates.";

        var replayPositive = new[] { net3, net7, net14 }.Count(n => n > 0m);
        if (trueForwardNet > 0m)
            return "True forward net positive; pre-freeze replay is supplementary context only.";
        if (replayPositive >= 2 && missedWinners <= blockedLosers)
            return "Pre-freeze replays mostly positive with more blocked losers than missed winners; await true forward data.";
        if (missedWinners > blockedLosers * 2)
            return "Many missed winners in diagnostic audit; activation or entry timing may be leaving opportunity on the table (diagnostic only).";
        if (blockedLosers > missedWinners)
            return "Activation filter blocked more losers than winners missed; conservative filter behavior in replay.";
        return "Mixed diagnostic signals; pre-freeze replay does not override true forward evidence.";
    }

    private static IReadOnlyList<SkipReasonCountRow> TopReasons(IEnumerable<string> reasons)
        => reasons
            .GroupBy(r => r, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SkipReasonCountRow { Reason = g.Key, Count = g.Count() })
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Reason, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static TradeStats Stats(IReadOnlyList<RegimeDriftDiagnosticTrade> trades)
    {
        if (trades.Count == 0)
            return new TradeStats(0, 0m, 0m, 0m, 0m, 0);

        var ordered = trades.OrderBy(t => t.ExitTimeUtc).ToArray();
        var net = ordered.Sum(t => t.NetPnlQuote);
        var wins = ordered.Count(t => t.NetPnlQuote > 0m);
        var grossWin = ordered.Where(t => t.NetPnlQuote > 0m).Sum(t => t.NetPnlQuote);
        var grossLoss = Math.Abs(ordered.Where(t => t.NetPnlQuote <= 0m).Sum(t => t.NetPnlQuote));
        var pf = grossLoss == 0m ? (grossWin > 0m ? 999m : 0m) : Math.Round(grossWin / grossLoss, 6);

        decimal equity = 0m, peak = 0m, maxDd = 0m;
        int consec = 0, maxConsec = 0;
        foreach (var t in ordered)
        {
            equity += t.NetPnlQuote;
            if (equity > peak) peak = equity;
            var dd = peak - equity;
            if (dd > maxDd) maxDd = dd;
            if (t.NetPnlQuote <= 0m)
            {
                consec++;
                if (consec > maxConsec) maxConsec = consec;
            }
            else
            {
                consec = 0;
            }
        }

        return new TradeStats(
            ordered.Length,
            Math.Round(net, 8),
            Math.Round((decimal)wins / ordered.Length, 6),
            pf,
            Math.Round(maxDd, 8),
            maxConsec);
    }
}
