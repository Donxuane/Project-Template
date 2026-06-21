using System.Globalization;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// Reporting-only bottleneck audit explaining why frozen profiles do or do not produce actionable trades.
/// Does not influence activation, entry, health gates, or verdict logic.
/// </summary>
public static class FrozenProfileBottleneckAuditBuilder
{
    public static FrozenProfileBottleneckAuditRow BuildForBnbRule01(
        FrozenCandidateState state,
        DateTime forwardStart,
        DateTime forwardEnd,
        decimal forwardSpanDays,
        int forwardTrades,
        decimal netModerate,
        decimal netStressPlus,
        IReadOnlyList<ShortWindowPeriodRow> periods,
        IReadOnlyList<ShortWindowTradeRow> takenTrades,
        IReadOnlyList<RegimeDriftDiagnosticTrade> moderateMappedTrades,
        IReadOnlyList<DirectionalRuleV2SkippedSignalRecord> skippedSignals,
        bool shadowActivationPassed,
        bool shadowEntrySignalPresent)
    {
        var checkpointCount = periods.Count;
        var activatedCount = periods.Count(p => p.Activated);
        var activationFailedCount = periods.Count(p => !p.Activated && !string.IsNullOrEmpty(p.SkipReason));
        var activatedButNoEntryCount = periods.Count(p => p.Activated && p.TradesInActivationWindow == 0);

        var topActivationFailures = TopReasons(
            periods.Where(p => !p.Activated && !string.IsNullOrEmpty(p.SkipReason)).Select(p => p.SkipReason));
        var topEntryFailures = BuildBnbEntryFailureReasons(periods, skippedSignals, forwardStart, forwardEnd);

        var forwardMapped = moderateMappedTrades
            .Where(t => t.EntryTimeUtc >= forwardStart && t.EntryTimeUtc < forwardEnd)
            .ToArray();
        var baseSignalsForward = forwardMapped.Length
            + skippedSignals.Count(s => s.TimeUtc >= forwardStart && s.TimeUtc < forwardEnd);

        var activatedRanges = periods.Where(p => p.Activated)
            .Select(p => (p.ActivationStartUtc, p.ActivationEndUtc)).ToArray();
        var failedRanges = periods.Where(p => !p.Activated)
            .Select(p => (p.ActivationStartUtc, p.ActivationEndUtc)).ToArray();

        var baseSignalsActivated = CountSignalsInRangesBnb(
            forwardMapped, skippedSignals, activatedRanges, forwardStart, forwardEnd);
        var baseSignalsFailed = CountSignalsInRangesBnb(
            forwardMapped, skippedSignals, failedRanges, forwardStart, forwardEnd);

        var netAll = SumNet(forwardMapped);
        var netActivated = SumNetInRanges(forwardMapped, activatedRanges);
        var netFailed = SumNetInRanges(forwardMapped, failedRanges);

        var cooldownBlocked = CountBnbCooldownBlocked(periods, skippedSignals, forwardStart, forwardEnd);
        var (hindsightOnly, realMissedWinners, blockedLosers) = BuildBnbOpportunityCounts(
            periods, forwardMapped, skippedSignals, takenTrades, forwardStart, forwardEnd);

        var lookbackByCheckpoint = periods
            .Select(p => new LookbackTradeCountRow
            {
                CheckpointUtc = p.CheckpointUtc,
                LookbackTradeCount = p.LookbackTradeCount
            })
            .ToArray();

        var draft = new FrozenProfileBottleneckAuditRow
        {
            ProfileName = state.ProfileName,
            Symbol = TradingSymbol.BNBUSDT.ToString(),
            Interval = "5m",
            Direction = LongShortDirection.Short.ToString(),
            ForwardWindowStartUtc = forwardStart,
            ForwardWindowEndUtc = forwardEnd,
            ForwardSpanDays = forwardSpanDays,
            ForwardTrades = forwardTrades,
            NetModerate = netModerate,
            NetStressPlus = netStressPlus,
            ActivationCheckpointCount = checkpointCount,
            ActivatedCheckpointCount = activatedCount,
            ActivationFailedCheckpointCount = activationFailedCount,
            ActivatedButNoEntryCount = activatedButNoEntryCount,
            BaseSignalsInsideForwardWindow = baseSignalsForward,
            BaseSignalsInsideActivatedWindows = baseSignalsActivated,
            BaseSignalsInsideActivationFailedWindows = baseSignalsFailed,
            NetIfAllBaseSignalsAllowed = netAll,
            NetIfOnlyActivatedBaseSignalsAllowed = netActivated,
            NetIfActivationFailedBaseSignalsAllowed = netFailed,
            TopActivationFailureReasons = topActivationFailures,
            TopEntryFailureReasons = topEntryFailures,
            LookbackTradeCountsByCheckpoint = lookbackByCheckpoint,
            CooldownBlockedCount = cooldownBlocked,
            HindsightOnlyMoveCount = hindsightOnly,
            RealMissedWinnerCount = realMissedWinners,
            BlockedLoserCount = blockedLosers,
            ShadowActivationPassed = shadowActivationPassed,
            ShadowEntrySignalPresent = shadowEntrySignalPresent
        };

        return FinalizeRow(draft);
    }

    public static FrozenProfileBottleneckAuditRow BuildForCrossSymbol(
        FrozenCandidateState state,
        CrossSymbolComboKey frozenKey,
        CrossSymbolActivationConfig frozenConfig,
        DateTime forwardStart,
        DateTime forwardEnd,
        decimal forwardSpanDays,
        int forwardTrades,
        decimal netModerate,
        decimal netStressPlus,
        IReadOnlyList<CrossSymbolPeriodRow> periods,
        IReadOnlyList<RegimeDriftDiagnosticTrade> moderateMappedTrades,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        IReadOnlyList<DateTime> rawSignalEntryTimesUtc,
        IReadOnlyList<KlineCandle> intervalCandles,
        BtcContextIndex btcContext,
        ShortWindowFlowFeatureIndex flowIndex,
        string primaryCostScenario,
        string moderateSlippageScenario,
        string stressPlusScenario,
        bool shadowActivationPassed,
        bool shadowEntrySignalPresent)
    {
        var checkpointCount = periods.Count;
        var activatedCount = periods.Count(p => p.Activated);
        var activationFailedCount = periods.Count(p => !p.Activated && !string.IsNullOrEmpty(p.SkipReason));
        var activatedButNoEntryCount = periods.Count(p => p.Activated && p.TradesInActivationWindow == 0);

        var topActivationFailures = TopReasons(
            periods.Where(p => !p.Activated && !string.IsNullOrEmpty(p.SkipReason)).Select(p => p.SkipReason));
        var topEntryFailures = BuildCrossSymbolEntryFailureReasons(periods, baseTrades, rawSignalEntryTimesUtc);

        var forwardMapped = moderateMappedTrades
            .Where(t => t.EntryTimeUtc >= forwardStart && t.EntryTimeUtc < forwardEnd)
            .ToArray();
        var rawSignalsForward = rawSignalEntryTimesUtc
            .Count(t => t >= forwardStart && t < forwardEnd);

        var activatedRanges = periods.Where(p => p.Activated)
            .Select(p => (p.ActivationStartUtc, p.ActivationEndUtc)).ToArray();
        var failedRanges = periods.Where(p => !p.Activated)
            .Select(p => (p.ActivationStartUtc, p.ActivationEndUtc)).ToArray();

        var baseSignalsActivated = CountTimesInRanges(rawSignalEntryTimesUtc, activatedRanges, forwardStart, forwardEnd);
        var baseSignalsFailed = CountTimesInRanges(rawSignalEntryTimesUtc, failedRanges, forwardStart, forwardEnd);

        var netAll = SumNet(forwardMapped);
        var netActivated = SumNetInRanges(forwardMapped, activatedRanges);
        var netFailed = SumNetInRanges(forwardMapped, failedRanges);

        var cooldownBlocked = CountCrossSymbolCooldownBlocked(
            periods, rawSignalEntryTimesUtc, baseTrades, forwardStart, forwardEnd);

        var accelerated = ForwardIncubationAcceleratedValidationBuilder.Build(
            frozenKey,
            frozenConfig,
            baseTrades,
            intervalCandles,
            btcContext,
            flowIndex,
            forwardStart,
            forwardEnd,
            netModerate,
            primaryCostScenario,
            moderateSlippageScenario,
            stressPlusScenario);

        var forwardOpportunities = accelerated.MissedOpportunityAudit
            .Where(r => r.PeriodLabel == "TrueForward")
            .ToArray();
        var hindsightOnly = forwardOpportunities.Count(r =>
            r.IsHindsightOnly
            && r.Classification is not "NoTradeNoMeaningfulOpportunity" and not "SignalCorrectlyTraded");
        var realMissedWinners = forwardOpportunities.Count(r =>
            r.Classification is "ActivatedButSignalMissedWinner");
        var blockedLosers = forwardOpportunities.Count(r =>
            r.Classification == "ActivationBlockedLoser" && r.WasBaseSignalPresent);

        var lookbackByCheckpoint = periods
            .Select(p => new LookbackTradeCountRow
            {
                CheckpointUtc = p.CheckpointUtc,
                LookbackTradeCount = p.LookbackTradeCount
            })
            .ToArray();

        var draft = new FrozenProfileBottleneckAuditRow
        {
            ProfileName = state.ProfileName,
            Symbol = frozenKey.Symbol.ToString(),
            Interval = frozenKey.Interval,
            Direction = frozenKey.Direction.ToString(),
            ForwardWindowStartUtc = forwardStart,
            ForwardWindowEndUtc = forwardEnd,
            ForwardSpanDays = forwardSpanDays,
            ForwardTrades = forwardTrades,
            NetModerate = netModerate,
            NetStressPlus = netStressPlus,
            ActivationCheckpointCount = checkpointCount,
            ActivatedCheckpointCount = activatedCount,
            ActivationFailedCheckpointCount = activationFailedCount,
            ActivatedButNoEntryCount = activatedButNoEntryCount,
            BaseSignalsInsideForwardWindow = rawSignalsForward,
            BaseSignalsInsideActivatedWindows = baseSignalsActivated,
            BaseSignalsInsideActivationFailedWindows = baseSignalsFailed,
            NetIfAllBaseSignalsAllowed = netAll,
            NetIfOnlyActivatedBaseSignalsAllowed = netActivated,
            NetIfActivationFailedBaseSignalsAllowed = netFailed,
            TopActivationFailureReasons = topActivationFailures,
            TopEntryFailureReasons = topEntryFailures,
            LookbackTradeCountsByCheckpoint = lookbackByCheckpoint,
            CooldownBlockedCount = cooldownBlocked,
            HindsightOnlyMoveCount = hindsightOnly,
            RealMissedWinnerCount = realMissedWinners,
            BlockedLoserCount = blockedLosers,
            ShadowActivationPassed = shadowActivationPassed,
            ShadowEntrySignalPresent = shadowEntrySignalPresent
        };

        return FinalizeRow(draft);
    }

    public static FrozenProfileBottleneckAuditSummary BuildSummary(
        DateTime runAtUtc,
        IReadOnlyList<FrozenProfileBottleneckAuditRow> profiles)
    {
        var compact = string.Join(" | ", profiles.Select(p =>
            $"{ShortProfileLabel(p.ProfileName)}:{p.BottleneckClassification}/{p.Recommendation} trades={p.ForwardTrades} net={p.NetModerate:F2}"));

        return new FrozenProfileBottleneckAuditSummary
        {
            RunAtUtc = runAtUtc,
            CompactSummaryLine = compact,
            Profiles = profiles
        };
    }

    private static FrozenProfileBottleneckAuditRow FinalizeRow(FrozenProfileBottleneckAuditRow draft)
    {
        var classification = ClassifyBottleneck(draft);
        var explanation = BuildExplanation(draft, classification);
        var recommendation = ResolveRecommendation(draft, classification);

        return draft with
        {
            BottleneckClassification = classification,
            BottleneckExplanation = explanation,
            Recommendation = recommendation
        };
    }

    private static string ClassifyBottleneck(FrozenProfileBottleneckAuditRow row)
    {
        var totalCheckpoints = row.ActivationCheckpointCount;
        var activationFailRate = totalCheckpoints > 0
            ? (decimal)row.ActivationFailedCheckpointCount / totalCheckpoints
            : 0m;
        var topActivation = row.TopActivationFailureReasons.FirstOrDefault()?.Reason ?? string.Empty;
        var topEntry = row.TopEntryFailureReasons.FirstOrDefault()?.Reason ?? string.Empty;

        if (topActivation.StartsWith("InsufficientLookbackTrades", StringComparison.OrdinalIgnoreCase)
            && row.ActivationFailedCheckpointCount >= row.ActivatedCheckpointCount)
        {
            return "LookbackStarved";
        }

        if (activationFailRate >= 0.5m
            && row.ActivationFailedCheckpointCount > row.ActivatedCheckpointCount
            && !string.IsNullOrEmpty(topActivation))
        {
            return "ActivationTooStrict";
        }

        if (activationFailRate >= 0.5m
            && row.NetIfActivationFailedBaseSignalsAllowed > row.NetModerate + 0.5m
            && row.RealMissedWinnerCount + row.BlockedLoserCount > 0)
        {
            return "ActivationTooStrict";
        }

        if (row.ActivatedCheckpointCount > 0
            && row.ActivatedButNoEntryCount >= Math.Max(1, row.ActivatedCheckpointCount / 2)
            && row.BaseSignalsInsideActivatedWindows < row.ActivatedButNoEntryCount
            && topEntry.Contains("NoBaseSignalsInActivationWindow", StringComparison.OrdinalIgnoreCase))
        {
            return "BaseSignalTooRare";
        }

        if (row.ForwardTrades == 0
            && row.BaseSignalsInsideForwardWindow <= Math.Max(1, (int)Math.Ceiling(row.ForwardSpanDays / 7m))
            && topEntry.Contains("NoBaseSignalsInActivationWindow", StringComparison.OrdinalIgnoreCase))
        {
            return "BaseSignalTooRare";
        }

        if (row.CooldownBlockedCount >= 2
            && row.CooldownBlockedCount >= row.ForwardTrades
            && row.BaseSignalsInsideActivatedWindows > row.ForwardTrades)
        {
            return "CooldownTooRestrictive";
        }

        if ((row.ForwardTrades > 0 || row.BaseSignalsInsideForwardWindow > 0)
            && row.NetModerate > 0m
            && row.NetStressPlus <= 0m)
        {
            return "SignalExistsButStressNegative";
        }

        if (row.NetModerate > 0m && row.NetStressPlus > 0m && row.ForwardTrades < 5)
        {
            return "HealthyButNeedsMoreForwardTrades";
        }

        if (row.ForwardTrades == 0 && row.BaseSignalsInsideForwardWindow == 0)
        {
            return row.ActivationFailedCheckpointCount > row.ActivatedCheckpointCount
                ? "ActivationTooStrict"
                : "BaseSignalTooRare";
        }

        if (row.NetModerate > 0m && row.NetStressPlus > 0m && row.ForwardTrades >= 5)
        {
            return "HealthyButNeedsMoreForwardTrades";
        }

        return "CandidateWeakOrPark";
    }

    private static string ResolveRecommendation(FrozenProfileBottleneckAuditRow row, string classification)
    {
        if (row.ForwardTrades >= 5
            && row.NetModerate > 0m
            && row.NetStressPlus > 0m
            && row.ShadowEntrySignalPresent)
        {
            return "TestnetOrderCandidate";
        }

        if (row.NetModerate > 0m && row.NetStressPlus <= 0m)
        {
            if (row.ForwardTrades >= 2)
                return "KeepSecondary";
            return classification is "LookbackStarved" or "CandidateWeakOrPark" ? "Park" : "KeepSecondary";
        }

        if (row.ForwardTrades == 0 && row.ActivationFailedCheckpointCount > row.ActivatedCheckpointCount)
        {
            return classification is "LookbackStarved" or "CandidateWeakOrPark" or "BaseSignalTooRare"
                ? "Park"
                : "NeedsLogicReview";
        }

        if (row.NetModerate > 0m && row.NetStressPlus > 0m && row.ForwardTrades >= 3)
            return "KeepPrimary";

        if (row.NetModerate > 0m && row.NetStressPlus > 0m)
            return "KeepSecondary";

        if (classification == "HealthyButNeedsMoreForwardTrades")
            return "TestnetShadowOnly";

        if (classification is "ActivationTooStrict" or "BaseSignalTooRare")
            return "NeedsLogicReview";

        return "Park";
    }

    private static string BuildExplanation(FrozenProfileBottleneckAuditRow row, string classification)
    {
        var activationPart = row.ActivationFailedCheckpointCount > row.ActivatedCheckpointCount
            ? $"Activation failed at {row.ActivationFailedCheckpointCount}/{row.ActivationCheckpointCount} checkpoints"
            : $"Activation passed at {row.ActivatedCheckpointCount}/{row.ActivationCheckpointCount} checkpoints";

        var topActivation = FormatTopReason(row.TopActivationFailureReasons);
        var topEntry = FormatTopReason(row.TopEntryFailureReasons);

        return classification switch
        {
            "ActivationTooStrict" =>
                $"{activationPart}. Counterfactual net in failed windows ({row.NetIfActivationFailedBaseSignalsAllowed:F2}) exceeds realized forward net ({row.NetModerate:F2}). Top activation blockers: {topActivation}.",
            "BaseSignalTooRare" =>
                $"{activationPart}, but only {row.BaseSignalsInsideForwardWindow} base signal(s) in the forward window and {row.ActivatedButNoEntryCount} activated period(s) had no entry. Top entry blockers: {topEntry}.",
            "LookbackStarved" =>
                $"Performance lookback repeatedly starved ({topActivation}). {row.ActivatedCheckpointCount} checkpoint(s) activated despite lookback pressure.",
            "CooldownTooRestrictive" =>
                $"{row.CooldownBlockedCount} signal(s) blocked by cooldown/overlap inside activated windows while only {row.ForwardTrades} forward trade(s) fired.",
            "SignalExistsButStressNegative" =>
                $"Moderate-cost forward net is {row.NetModerate:F2} but stress-plus net is {row.NetStressPlus:F2} with {row.BaseSignalsInsideForwardWindow} base signal(s).",
            "HealthyButNeedsMoreForwardTrades" =>
                $"Forward economics are positive (moderate={row.NetModerate:F2}, stress-plus={row.NetStressPlus:F2}) but only {row.ForwardTrades} true forward trade(s) over {row.ForwardSpanDays:F1} days.",
            _ =>
                $"Forward window produced {row.ForwardTrades} trade(s), net={row.NetModerate:F2}, stress-plus={row.NetStressPlus:F2}. {row.BaseSignalsInsideForwardWindow} base signal(s); activation failures={row.ActivationFailedCheckpointCount}."
        };
    }

    private static IReadOnlyList<SkipReasonCountRow> BuildBnbEntryFailureReasons(
        IReadOnlyList<ShortWindowPeriodRow> periods,
        IReadOnlyList<DirectionalRuleV2SkippedSignalRecord> skippedSignals,
        DateTime forwardStart,
        DateTime forwardEnd)
    {
        var reasons = new List<string>();
        foreach (var period in periods.Where(p => p.Activated && p.TradesInActivationWindow == 0))
        {
            var windowSkips = skippedSignals
                .Where(s => s.TimeUtc >= period.ActivationStartUtc
                            && s.TimeUtc < period.ActivationEndUtc
                            && s.TimeUtc >= forwardStart
                            && s.TimeUtc < forwardEnd)
                .Select(s => s.SkipReason)
                .ToArray();
            if (windowSkips.Length > 0)
                reasons.AddRange(windowSkips);
            else
                reasons.Add("NoBaseSignalsInActivationWindow");
        }

        return TopReasons(reasons);
    }

    private static IReadOnlyList<SkipReasonCountRow> BuildCrossSymbolEntryFailureReasons(
        IReadOnlyList<CrossSymbolPeriodRow> periods,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        IReadOnlyList<DateTime> rawSignalEntryTimesUtc)
    {
        var reasons = new List<string>();
        foreach (var period in periods.Where(p => p.Activated && p.TradesInActivationWindow == 0))
        {
            var rawInWindow = rawSignalEntryTimesUtc.Count(t =>
                t >= period.ActivationStartUtc && t < period.ActivationEndUtc);
            var tradesInWindow = baseTrades.Count(t =>
                t.TimeUtc >= period.ActivationStartUtc && t.TimeUtc < period.ActivationEndUtc);
            if (rawInWindow > tradesInWindow)
                reasons.Add("SkippedCooldownOrOverlap");
            else if (rawInWindow > 0)
                reasons.Add("SkippedCooldownOrOverlap");
            else
                reasons.Add("NoBaseSignalsInActivationWindow");
        }

        return TopReasons(reasons);
    }

    private static (int HindsightOnly, int RealMissedWinners, int BlockedLosers) BuildBnbOpportunityCounts(
        IReadOnlyList<ShortWindowPeriodRow> periods,
        IReadOnlyList<RegimeDriftDiagnosticTrade> forwardMapped,
        IReadOnlyList<DirectionalRuleV2SkippedSignalRecord> skippedSignals,
        IReadOnlyList<ShortWindowTradeRow> takenTrades,
        DateTime forwardStart,
        DateTime forwardEnd)
    {
        var hindsightOnly = 0;
        var realMissedWinners = 0;
        var blockedLosers = 0;

        foreach (var period in periods.Where(p => p.ActivationStartUtc < forwardEnd && p.ActivationEndUtc > forwardStart))
        {
            var windowStart = period.ActivationStartUtc < forwardStart ? forwardStart : period.ActivationStartUtc;
            var windowEnd = period.ActivationEndUtc > forwardEnd ? forwardEnd : period.ActivationEndUtc;
            if (windowEnd <= windowStart)
                continue;

            var takenInWindow = takenTrades.Any(t =>
                t.EntryTimeUtc >= windowStart && t.EntryTimeUtc < windowEnd);
            if (takenInWindow)
                continue;

            var signalNets = forwardMapped
                .Where(t => t.EntryTimeUtc >= windowStart && t.EntryTimeUtc < windowEnd)
                .Select(t => t.NetPnlQuote)
                .ToArray();
            var hasSignal = signalNets.Length > 0
                || skippedSignals.Any(s => s.TimeUtc >= windowStart && s.TimeUtc < windowEnd);

            if (!hasSignal)
            {
                if (period.Activated)
                    hindsightOnly++;
                continue;
            }

            var bestNet = signalNets.Length > 0 ? signalNets.Max() : 0m;
            if (period.Activated)
            {
                if (bestNet > 0m)
                    realMissedWinners++;
                else if (bestNet < 0m)
                    hindsightOnly++;
            }
            else if (bestNet <= 0m)
            {
                blockedLosers++;
            }
        }

        return (hindsightOnly, realMissedWinners, blockedLosers);
    }

    private static int CountBnbCooldownBlocked(
        IReadOnlyList<ShortWindowPeriodRow> periods,
        IReadOnlyList<DirectionalRuleV2SkippedSignalRecord> skippedSignals,
        DateTime forwardStart,
        DateTime forwardEnd)
        => skippedSignals.Count(s =>
            s.TimeUtc >= forwardStart
            && s.TimeUtc < forwardEnd
            && periods.Any(p => p.Activated
                                && s.TimeUtc >= p.ActivationStartUtc
                                && s.TimeUtc < p.ActivationEndUtc)
            && (s.SkipReason.Contains("Cooldown", StringComparison.OrdinalIgnoreCase)
                || s.SkipReason.Contains("Overlap", StringComparison.OrdinalIgnoreCase)));

    private static int CountCrossSymbolCooldownBlocked(
        IReadOnlyList<CrossSymbolPeriodRow> periods,
        IReadOnlyList<DateTime> rawSignalEntryTimesUtc,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        DateTime forwardStart,
        DateTime forwardEnd)
    {
        var blocked = 0;
        foreach (var period in periods.Where(p => p.Activated))
        {
            var raw = rawSignalEntryTimesUtc.Count(t =>
                t >= period.ActivationStartUtc && t < period.ActivationEndUtc
                && t >= forwardStart && t < forwardEnd);
            var executed = baseTrades.Count(t =>
                t.TimeUtc >= period.ActivationStartUtc && t.TimeUtc < period.ActivationEndUtc
                && t.TimeUtc >= forwardStart && t.TimeUtc < forwardEnd);
            if (raw > executed)
                blocked += raw - executed;
        }

        return blocked;
    }

    private static int CountSignalsInRangesBnb(
        IReadOnlyList<RegimeDriftDiagnosticTrade> mappedTrades,
        IReadOnlyList<DirectionalRuleV2SkippedSignalRecord> skippedSignals,
        IReadOnlyList<(DateTime Start, DateTime End)> ranges,
        DateTime forwardStart,
        DateTime forwardEnd)
    {
        var tradeCount = mappedTrades.Count(t => InAnyRange(t.EntryTimeUtc, ranges, forwardStart, forwardEnd));
        var skipCount = skippedSignals.Count(s => InAnyRange(s.TimeUtc, ranges, forwardStart, forwardEnd));
        return tradeCount + skipCount;
    }

    private static int CountTimesInRanges(
        IReadOnlyList<DateTime> times,
        IReadOnlyList<(DateTime Start, DateTime End)> ranges,
        DateTime forwardStart,
        DateTime forwardEnd)
        => times.Count(t => InAnyRange(t, ranges, forwardStart, forwardEnd));

    private static bool InAnyRange(
        DateTime timeUtc,
        IReadOnlyList<(DateTime Start, DateTime End)> ranges,
        DateTime forwardStart,
        DateTime forwardEnd)
        => timeUtc >= forwardStart
           && timeUtc < forwardEnd
           && ranges.Any(r => timeUtc >= r.Start && timeUtc < r.End);

    private static decimal SumNet(IReadOnlyList<RegimeDriftDiagnosticTrade> trades)
        => Math.Round(trades.Sum(t => t.NetPnlQuote), 8);

    private static decimal SumNetInRanges(
        IReadOnlyList<RegimeDriftDiagnosticTrade> trades,
        IReadOnlyList<(DateTime Start, DateTime End)> ranges)
        => Math.Round(trades
            .Where(t => ranges.Any(r => t.EntryTimeUtc >= r.Start && t.EntryTimeUtc < r.End))
            .Sum(t => t.NetPnlQuote), 8);

    private static IReadOnlyList<SkipReasonCountRow> TopReasons(IEnumerable<string> reasons)
        => reasons
            .GroupBy(r => r, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SkipReasonCountRow { Reason = g.Key, Count = g.Count() })
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Reason, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string FormatTopReason(IReadOnlyList<SkipReasonCountRow> rows)
        => rows.Count == 0
            ? "none"
            : string.Join("; ", rows.Take(3).Select(r => $"{r.Reason}={r.Count}"));

    private static string ShortProfileLabel(string profileName)
    {
        if (profileName.Contains("Rule01", StringComparison.OrdinalIgnoreCase))
            return "BNB5m";
        if (profileName.Contains("SOL", StringComparison.OrdinalIgnoreCase) && profileName.Contains("5m", StringComparison.OrdinalIgnoreCase))
            return "SOL5m";
        if (profileName.Contains("BNB", StringComparison.OrdinalIgnoreCase) && profileName.Contains("15m", StringComparison.OrdinalIgnoreCase))
            return "BNB15m";
        if (profileName.Contains("SOL", StringComparison.OrdinalIgnoreCase) && profileName.Contains("15m", StringComparison.OrdinalIgnoreCase))
            return "SOL15m";
        return profileName;
    }
}
