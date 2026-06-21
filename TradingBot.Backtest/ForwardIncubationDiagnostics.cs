using System.Globalization;

namespace TradingBot.Backtest;
public sealed record SkipReasonCountRow
{
    public string Reason { get; init; } = string.Empty;
    public int Count { get; init; }
}

public sealed record ForwardIncubationPeriodNoTradeDiagnostic
{
    public DateTime CheckpointUtc { get; init; }
    public DateTime ActivationStartUtc { get; init; }
    public DateTime ActivationEndUtc { get; init; }
    public string Classification { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public int TradesInWindow { get; init; }
    public decimal NetInWindow { get; init; }
}

public sealed record ForwardIncubationTradeDiagnostic
{
    public int TradeIndex { get; init; }
    public DateTime EntryTimeUtc { get; init; }
    public DateTime ExitTimeUtc { get; init; }
    public decimal NetPnlQuote { get; init; }
}

/// <summary>
/// Reporting-only diagnostics explaining why forward incubation did or did not trade.
/// Does not influence activation, entry, or verdict logic.
/// </summary>
public sealed record ForwardIncubationNoTradeReasonSummary
{
    public string CompactSummaryLine { get; init; } = string.Empty;
    /// <summary>Same value as <see cref="LatestRunStatus"/>; kept for backward compatibility.</summary>
    public string ReportStatus { get; init; } = string.Empty;
    public string LatestRunStatus { get; init; } = string.Empty;
    public DateTime? PreviousRunForwardWindowEndUtc { get; init; }
    public DateTime CurrentRunForwardWindowEndUtc { get; init; }
    public bool DataAdvancedSincePreviousRun { get; init; }
    public int NewTradesSincePreviousRun { get; init; }
    public decimal NewNetModerateSincePreviousRun { get; init; }
    public decimal NewNetStressPlusSincePreviousRun { get; init; }
    public DateTime ForwardWindowStartUtc { get; init; }
    public DateTime ForwardWindowEndUtc { get; init; }
    public decimal ForwardSpanDays { get; init; }
    public DateTime LatestDataUtc { get; init; }
    public DateTime LatestCandleUtc { get; init; }
    public string FrozenProfileName { get; init; } = string.Empty;
    /// <summary>Legacy combined hash label; prefer track-specific fields below.</summary>
    public string FrozenHashStatus { get; init; } = string.Empty;
    public string BnbFrozenHashStatus { get; init; } = string.Empty;
    public string SolFrozenHashStatus { get; init; } = string.Empty;
    public string FrozenFilesTouched { get; init; } = string.Empty;
    public int ActivationCheckpointCount { get; init; }
    public int ActivatedCheckpointCount { get; init; }
    public int ActivationFailedCheckpointCount { get; init; }
    public int ActivatedButNoEntryCount { get; init; }
    public int ForwardTrades { get; init; }
    public decimal NetModerate { get; init; }
    public decimal NetModerateLatency002 { get; init; }
    public decimal NetStressPlus { get; init; }
    public int HealthGatesPassed { get; init; }
    public int HealthGatesTotal { get; init; }
    public string FailedGates { get; init; } = string.Empty;
    public string Verdict { get; init; } = string.Empty;
    public string NextAction { get; init; } = string.Empty;
    public IReadOnlyList<SkipReasonCountRow> TopActivationSkipReasons { get; init; } = [];
    public IReadOnlyList<SkipReasonCountRow> TopEntrySkipReasons { get; init; } = [];
    public IReadOnlyList<ForwardIncubationPeriodNoTradeDiagnostic> PeriodDiagnostics { get; init; } = [];
    public IReadOnlyList<ForwardIncubationTradeDiagnostic> TradeDiagnostics { get; init; } = [];
    public NormalizedRiskPnlMetrics NormalizedRisk { get; init; } = new();
}

public static class ForwardIncubationDiagnosticsBuilder
{
    public static ForwardIncubationNoTradeReasonSummary BuildForBnb(
        FrozenCandidateState state,
        DateTime forwardStart,
        DateTime forwardEnd,
        decimal forwardSpanDays,
        DateTime latestCandleUtc,
        int forwardTrades,
        decimal netModerate,
        decimal netLatency002,
        decimal netStressPlus,
        IReadOnlyList<ShortWindowPeriodRow> periods,
        IReadOnlyList<ShortWindowTradeRow> trades,
        IReadOnlyList<DirectionalRuleV2SkippedSignalRecord> skippedSignals,
        IReadOnlyList<ForwardHealthGateRow> healthGates,
        string verdict,
        ForwardIncubationHistoryEntry? previousHistoryEntry,
        string frozenHashStatus,
        string frozenFilesTouched,
        string outputDirectory)
    {
        var checkpointCount = periods.Count;
        var activatedCount = periods.Count(p => p.Activated);
        var activationFailedCount = periods.Count(p => !p.Activated && !string.IsNullOrEmpty(p.SkipReason));
        var activatedButNoEntryCount = periods.Count(p => p.Activated && p.TradesInActivationWindow == 0);

        var activatedRanges = periods
            .Where(p => p.Activated)
            .Select(p => (p.ActivationStartUtc, p.ActivationEndUtc))
            .ToArray();

        var topActivationSkips = TopReasons(
            periods.Where(p => !p.Activated && !string.IsNullOrEmpty(p.SkipReason)).Select(p => p.SkipReason));
        var topEntrySkips = BuildBnbEntrySkipReasons(periods, skippedSignals, activatedRanges, forwardStart, forwardEnd);

        var previousForwardWindowEndUtc = previousHistoryEntry?.ForwardWindowEndUtc;
        var runDelta = ComputeRunDelta(
            forwardEnd,
            previousHistoryEntry,
            netModerate,
            netStressPlus,
            trades.Select(t => t.EntryTimeUtc),
            trades.Where(t => IsNewTrade(t.EntryTimeUtc, previousForwardWindowEndUtc)).Select(t => t.NetPnlQuote),
            periods.Select(p => (p.CheckpointUtc, p.Activated, p.SkipReason, p.TradesInActivationWindow)));

        var nextAction = ResolveNextAction(verdict);
        var maxDrawdown = NormalizedRiskPnlModule.ComputeMaxDrawdownFromNetSeries(
            trades.OrderBy(t => t.ExitTimeUtc).Select(t => t.NetPnlQuote));
        var normalizedRisk = NormalizedRiskPnlModule.Compute(state.Symbol, netModerate, maxDrawdown);
        var compactLine = NormalizedRiskPnlModule.BuildForwardIncubationCompactSummaryLine(
            "BNB", netModerate, state.Symbol, maxDrawdown, verdict, nextAction, outputDirectory);

        return new ForwardIncubationNoTradeReasonSummary
        {
            CompactSummaryLine = compactLine,
            NormalizedRisk = normalizedRisk,
            ReportStatus = runDelta.LatestRunStatus,
            LatestRunStatus = runDelta.LatestRunStatus,
            PreviousRunForwardWindowEndUtc = previousForwardWindowEndUtc,
            CurrentRunForwardWindowEndUtc = forwardEnd,
            DataAdvancedSincePreviousRun = runDelta.DataAdvancedSincePreviousRun,
            NewTradesSincePreviousRun = runDelta.NewTradesSincePreviousRun,
            NewNetModerateSincePreviousRun = runDelta.NewNetModerateSincePreviousRun,
            NewNetStressPlusSincePreviousRun = runDelta.NewNetStressPlusSincePreviousRun,
            ForwardWindowStartUtc = forwardStart,
            ForwardWindowEndUtc = forwardEnd,
            ForwardSpanDays = forwardSpanDays,
            LatestDataUtc = latestCandleUtc,
            LatestCandleUtc = latestCandleUtc,
            FrozenProfileName = state.ProfileName,
            FrozenHashStatus = frozenHashStatus,
            BnbFrozenHashStatus = "NotChecked",
            SolFrozenHashStatus = "NotChecked",
            FrozenFilesTouched = frozenFilesTouched,
            ActivationCheckpointCount = checkpointCount,
            ActivatedCheckpointCount = activatedCount,
            ActivationFailedCheckpointCount = activationFailedCount,
            ActivatedButNoEntryCount = activatedButNoEntryCount,
            ForwardTrades = forwardTrades,
            NetModerate = netModerate,
            NetModerateLatency002 = netLatency002,
            NetStressPlus = netStressPlus,
            HealthGatesPassed = healthGates.Count(g => g.Pass),
            HealthGatesTotal = healthGates.Count,
            FailedGates = string.Join(", ", healthGates.Where(g => !g.Pass).Select(g => g.GateName)),
            Verdict = verdict,
            NextAction = nextAction,
            TopActivationSkipReasons = topActivationSkips,
            TopEntrySkipReasons = topEntrySkips,
            PeriodDiagnostics = BuildBnbPeriodDiagnostics(periods, skippedSignals),
            TradeDiagnostics = trades
                .OrderBy(t => t.EntryTimeUtc)
                .Select((t, i) => new ForwardIncubationTradeDiagnostic
                {
                    TradeIndex = i + 1,
                    EntryTimeUtc = t.EntryTimeUtc,
                    ExitTimeUtc = t.ExitTimeUtc,
                    NetPnlQuote = t.NetPnlQuote
                })
                .ToArray()
        };
    }

    public static ForwardIncubationNoTradeReasonSummary BuildForSol(
        FrozenCandidateState state,
        DateTime forwardStart,
        DateTime forwardEnd,
        decimal forwardSpanDays,
        DateTime latestCandleUtc,
        int forwardTrades,
        decimal netModerate,
        decimal netLatency002,
        decimal netStressPlus,
        IReadOnlyList<CrossSymbolPeriodRow> periods,
        IReadOnlyList<CrossSymbolTradeRow> trades,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        IReadOnlyList<ForwardHealthGateRow> healthGates,
        string verdict,
        ForwardIncubationHistoryEntry? previousHistoryEntry,
        bool bnbFrozenFilesByteIdentical,
        IReadOnlyList<string> bnbFilesChecked,
        string outputDirectory)
    {
        var bnbHashStatus = bnbFrozenFilesByteIdentical ? "Unchanged" : "Changed";
        return BuildForCrossSymbolTrack(
            state,
            forwardStart,
            forwardEnd,
            forwardSpanDays,
            latestCandleUtc,
            forwardTrades,
            netModerate,
            netLatency002,
            netStressPlus,
            periods,
            trades,
            baseTrades,
            healthGates,
            verdict,
            previousHistoryEntry,
            trackLabel: "SOL",
            frozenHashStatus: $"BNB frozen hash {bnbHashStatus.ToLowerInvariant()}",
            bnbFrozenHashStatus: bnbHashStatus,
            solFrozenHashStatus: "NotChecked",
            protectedTracksHashStatus: "NotChecked",
            frozenFilesTouched: string.Join("; ", bnbFilesChecked),
            outputDirectory);
    }

    public static ForwardIncubationNoTradeReasonSummary BuildForCrossSymbolTrack(
        FrozenCandidateState state,
        DateTime forwardStart,
        DateTime forwardEnd,
        decimal forwardSpanDays,
        DateTime latestCandleUtc,
        int forwardTrades,
        decimal netModerate,
        decimal netLatency002,
        decimal netStressPlus,
        IReadOnlyList<CrossSymbolPeriodRow> periods,
        IReadOnlyList<CrossSymbolTradeRow> trades,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        IReadOnlyList<ForwardHealthGateRow> healthGates,
        string verdict,
        ForwardIncubationHistoryEntry? previousHistoryEntry,
        string trackLabel,
        string frozenHashStatus,
        string bnbFrozenHashStatus,
        string solFrozenHashStatus,
        string protectedTracksHashStatus,
        string frozenFilesTouched,
        string outputDirectory)
    {
        var checkpointCount = periods.Count;
        var activatedCount = periods.Count(p => p.Activated);
        var activationFailedCount = periods.Count(p => !p.Activated && !string.IsNullOrEmpty(p.SkipReason));
        var activatedButNoEntryCount = periods.Count(p => p.Activated && p.TradesInActivationWindow == 0);

        var topActivationSkips = TopReasons(
            periods.Where(p => !p.Activated && !string.IsNullOrEmpty(p.SkipReason)).Select(p => p.SkipReason));
        var topEntrySkips = BuildSolEntrySkipReasons(periods, baseTrades);

        var previousForwardWindowEndUtc = previousHistoryEntry?.ForwardWindowEndUtc;

        var runDelta = ComputeRunDelta(
            forwardEnd,
            previousHistoryEntry,
            netModerate,
            netStressPlus,
            trades.Select(t => t.EntryTimeUtc),
            trades.Where(t => IsNewTrade(t.EntryTimeUtc, previousForwardWindowEndUtc)).Select(t => t.NetPnlQuote),
            periods.Select(p => (p.CheckpointUtc, p.Activated, p.SkipReason, p.TradesInActivationWindow)));

        var nextAction = ResolveNextAction(verdict);
        var maxDrawdown = NormalizedRiskPnlModule.ComputeMaxDrawdownFromNetSeries(
            trades.OrderBy(t => t.ExitTimeUtc).Select(t => t.NetPnlQuote));
        var normalizedRisk = NormalizedRiskPnlModule.Compute(state.Symbol, netModerate, maxDrawdown);
        var compactLine = NormalizedRiskPnlModule.BuildForwardIncubationCompactSummaryLine(
            trackLabel, netModerate, state.Symbol, maxDrawdown, verdict, nextAction, outputDirectory);

        return new ForwardIncubationNoTradeReasonSummary
        {
            CompactSummaryLine = compactLine,
            NormalizedRisk = normalizedRisk,
            ReportStatus = runDelta.LatestRunStatus,
            LatestRunStatus = runDelta.LatestRunStatus,
            PreviousRunForwardWindowEndUtc = previousForwardWindowEndUtc,
            CurrentRunForwardWindowEndUtc = forwardEnd,
            DataAdvancedSincePreviousRun = runDelta.DataAdvancedSincePreviousRun,
            NewTradesSincePreviousRun = runDelta.NewTradesSincePreviousRun,
            NewNetModerateSincePreviousRun = runDelta.NewNetModerateSincePreviousRun,
            NewNetStressPlusSincePreviousRun = runDelta.NewNetStressPlusSincePreviousRun,
            ForwardWindowStartUtc = forwardStart,
            ForwardWindowEndUtc = forwardEnd,
            ForwardSpanDays = forwardSpanDays,
            LatestDataUtc = latestCandleUtc,
            LatestCandleUtc = latestCandleUtc,
            FrozenProfileName = state.ProfileName,
            FrozenHashStatus = frozenHashStatus,
            BnbFrozenHashStatus = bnbFrozenHashStatus,
            SolFrozenHashStatus = solFrozenHashStatus,
            FrozenFilesTouched = frozenFilesTouched,
            ActivationCheckpointCount = checkpointCount,
            ActivatedCheckpointCount = activatedCount,
            ActivationFailedCheckpointCount = activationFailedCount,
            ActivatedButNoEntryCount = activatedButNoEntryCount,
            ForwardTrades = forwardTrades,
            NetModerate = netModerate,
            NetModerateLatency002 = netLatency002,
            NetStressPlus = netStressPlus,
            HealthGatesPassed = healthGates.Count(g => g.Pass),
            HealthGatesTotal = healthGates.Count,
            FailedGates = string.Join(", ", healthGates.Where(g => !g.Pass).Select(g => g.GateName)),
            Verdict = verdict,
            NextAction = nextAction,
            TopActivationSkipReasons = topActivationSkips,
            TopEntrySkipReasons = topEntrySkips,
            PeriodDiagnostics = BuildSolPeriodDiagnostics(periods, baseTrades),
            TradeDiagnostics = trades
                .OrderBy(t => t.EntryTimeUtc)
                .Select((t, i) => new ForwardIncubationTradeDiagnostic
                {
                    TradeIndex = i + 1,
                    EntryTimeUtc = t.EntryTimeUtc,
                    ExitTimeUtc = t.ExitTimeUtc,
                    NetPnlQuote = t.NetPnlQuote
                })
                .ToArray()
        };
    }

    private sealed record RunDeltaResult(
        bool DataAdvancedSincePreviousRun,
        int NewTradesSincePreviousRun,
        decimal NewNetModerateSincePreviousRun,
        decimal NewNetStressPlusSincePreviousRun,
        string LatestRunStatus);

    private static RunDeltaResult ComputeRunDelta(
        DateTime currentForwardWindowEndUtc,
        ForwardIncubationHistoryEntry? previousHistoryEntry,
        decimal currentNetModerate,
        decimal currentNetStressPlus,
        IEnumerable<DateTime> allTradeEntryTimesUtc,
        IEnumerable<decimal> newTradeModerateNets,
        IEnumerable<(DateTime CheckpointUtc, bool Activated, string SkipReason, int TradesInActivationWindow)> periods)
    {
        var previousForwardWindowEndUtc = previousHistoryEntry?.ForwardWindowEndUtc;
        var dataAdvanced = !previousForwardWindowEndUtc.HasValue
                           || currentForwardWindowEndUtc > previousForwardWindowEndUtc.Value;

        if (!dataAdvanced)
        {
            return new RunDeltaResult(
                false,
                0,
                0m,
                0m,
                "DataNotAdvanced");
        }

        var newTradeCount = allTradeEntryTimesUtc.Count(t => IsNewTrade(t, previousForwardWindowEndUtc));
        var newModerateNet = newTradeModerateNets.Sum();
        var newStressPlusNet = previousHistoryEntry is null
            ? currentNetStressPlus
            : currentNetStressPlus - previousHistoryEntry.ForwardNetStressPlus;

        var latestRunStatus = ResolveLatestRunStatus(
            newTradeCount,
            periods,
            previousForwardWindowEndUtc);

        return new RunDeltaResult(
            true,
            newTradeCount,
            newModerateNet,
            newStressPlusNet,
            latestRunStatus);
    }

    private static bool IsNewTrade(DateTime entryTimeUtc, DateTime? previousForwardWindowEndUtc)
        => !previousForwardWindowEndUtc.HasValue || entryTimeUtc > previousForwardWindowEndUtc.Value;

    private static string ResolveLatestRunStatus(
        int newTradesSincePreviousRun,
        IEnumerable<(DateTime CheckpointUtc, bool Activated, string SkipReason, int TradesInActivationWindow)> periods,
        DateTime? previousForwardWindowEndUtc)
    {
        if (newTradesSincePreviousRun > 0)
            return "NewTradesFired";

        var periodList = periods.ToArray();
        var newPeriods = periodList
            .Where(p => !previousForwardWindowEndUtc.HasValue || p.CheckpointUtc >= previousForwardWindowEndUtc.Value)
            .ToArray();

        if (newPeriods.Length == 0 && previousForwardWindowEndUtc.HasValue)
        {
            var tailPeriod = periodList
                .Where(p => p.CheckpointUtc < previousForwardWindowEndUtc.Value)
                .OrderByDescending(p => p.CheckpointUtc)
                .FirstOrDefault();
            if (tailPeriod != default)
                newPeriods = [tailPeriod];
        }

        if (newPeriods.Any(p => !p.Activated && !string.IsNullOrEmpty(p.SkipReason)))
            return "DataAdvancedActivationFailed";
        if (newPeriods.Any(p => p.Activated && p.TradesInActivationWindow == 0))
            return "DataAdvancedActivatedButNoEntry";

        return "DataAdvancedActivatedButNoEntry";
    }

    private static IReadOnlyList<SkipReasonCountRow> TopReasons(IEnumerable<string> reasons)
        => reasons
            .GroupBy(r => r, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SkipReasonCountRow { Reason = g.Key, Count = g.Count() })
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Reason, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<SkipReasonCountRow> BuildBnbEntrySkipReasons(
        IReadOnlyList<ShortWindowPeriodRow> periods,
        IReadOnlyList<DirectionalRuleV2SkippedSignalRecord> skippedSignals,
        IReadOnlyList<(DateTime Start, DateTime End)> activatedRanges,
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

        foreach (var skip in skippedSignals.Where(s => InActivatedRange(s.TimeUtc, activatedRanges, forwardStart, forwardEnd)))
            reasons.Add(skip.SkipReason);

        return TopReasons(reasons);
    }

    private static IReadOnlyList<SkipReasonCountRow> BuildSolEntrySkipReasons(
        IReadOnlyList<CrossSymbolPeriodRow> periods,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades)
    {
        var reasons = new List<string>();
        foreach (var period in periods.Where(p => p.Activated && p.TradesInActivationWindow == 0))
        {
            var signalsInWindow = baseTrades.Count(t =>
                t.TimeUtc >= period.ActivationStartUtc && t.TimeUtc < period.ActivationEndUtc);
            reasons.Add(signalsInWindow > 0
                ? "SkippedCooldownOrOverlap"
                : "NoBaseSignalsInActivationWindow");
        }

        return TopReasons(reasons);
    }

    private static IReadOnlyList<ForwardIncubationPeriodNoTradeDiagnostic> BuildBnbPeriodDiagnostics(
        IReadOnlyList<ShortWindowPeriodRow> periods,
        IReadOnlyList<DirectionalRuleV2SkippedSignalRecord> skippedSignals)
    {
        return periods.Select(p =>
        {
            var (classification, reason) = ClassifyPeriod(
                p.Activated,
                p.SkipReason,
                p.TradesInActivationWindow,
                () =>
                {
                    var windowSkips = skippedSignals
                        .Where(s => s.TimeUtc >= p.ActivationStartUtc && s.TimeUtc < p.ActivationEndUtc)
                        .GroupBy(s => s.SkipReason, StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(g => g.Count())
                        .Select(g => g.Key)
                        .FirstOrDefault();
                    return string.IsNullOrEmpty(windowSkips) ? "NoBaseSignalsInActivationWindow" : windowSkips!;
                });

            return new ForwardIncubationPeriodNoTradeDiagnostic
            {
                CheckpointUtc = p.CheckpointUtc,
                ActivationStartUtc = p.ActivationStartUtc,
                ActivationEndUtc = p.ActivationEndUtc,
                Classification = classification,
                Reason = reason,
                TradesInWindow = p.TradesInActivationWindow,
                NetInWindow = p.NetInActivationWindow
            };
        }).ToArray();
    }

    private static IReadOnlyList<ForwardIncubationPeriodNoTradeDiagnostic> BuildSolPeriodDiagnostics(
        IReadOnlyList<CrossSymbolPeriodRow> periods,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades)
    {
        return periods.Select(p =>
        {
            var (classification, reason) = ClassifyPeriod(
                p.Activated,
                p.SkipReason,
                p.TradesInActivationWindow,
                () =>
                {
                    var signalsInWindow = baseTrades.Count(t =>
                        t.TimeUtc >= p.ActivationStartUtc && t.TimeUtc < p.ActivationEndUtc);
                    return signalsInWindow > 0
                        ? "SkippedCooldownOrOverlap"
                        : "NoBaseSignalsInActivationWindow";
                });

            return new ForwardIncubationPeriodNoTradeDiagnostic
            {
                CheckpointUtc = p.CheckpointUtc,
                ActivationStartUtc = p.ActivationStartUtc,
                ActivationEndUtc = p.ActivationEndUtc,
                Classification = classification,
                Reason = reason,
                TradesInWindow = p.TradesInActivationWindow,
                NetInWindow = p.NetInActivationWindow
            };
        }).ToArray();
    }

    private static (string Classification, string Reason) ClassifyPeriod(
        bool activated,
        string skipReason,
        int tradesInWindow,
        Func<string> activatedNoEntryReason)
    {
        if (!activated)
            return ("ActivationFailed", string.IsNullOrEmpty(skipReason) ? "UnknownActivationSkip" : skipReason);
        if (tradesInWindow > 0)
            return ("TradesFired", $"{tradesInWindow} trade(s) in activation window");
        return ("ActivatedButNoEntry", activatedNoEntryReason());
    }

    private static bool InActivatedRange(
        DateTime timeUtc,
        IReadOnlyList<(DateTime Start, DateTime End)> ranges,
        DateTime forwardStart,
        DateTime forwardEnd)
        => timeUtc >= forwardStart
           && timeUtc < forwardEnd
           && ranges.Any(r => timeUtc >= r.Start && timeUtc < r.End);

    public static string ResolveNextAction(string verdict) => verdict switch
    {
        "NotEnoughForwardDataYet" => "Keep collecting free flow data and re-run; no judgment possible yet.",
        "KeepIncubating" => "Keep incubating; re-run after more forward data accumulates.",
        "CandidateImproving" => "Keep incubating; forward results positive but gates not all passed yet.",
        "CandidateDeteriorating" => "Keep incubating with caution; if deterioration persists, park the candidate.",
        "CandidateFailed" => "Park the candidate; forward window contradicts discovery.",
        "CandidateEligibleForPaperLater" => "Review for paper/sandbox planning after at least one more independent forward window.",
        _ => "Keep incubating."
    };

}
