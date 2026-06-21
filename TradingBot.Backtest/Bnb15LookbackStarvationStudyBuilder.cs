using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class Bnb15LookbackStarvationStudyBuilder
{
    private static readonly int[] MinLookbackTradesGrid = [2, 3, 4, 5];
    private static readonly int[] LookbackDaysGrid = [3, 5, 7, 10];
    private static readonly int[] CheckpointHoursGrid = [4, 8, 12, 24];
    private static readonly int[] ActivationHoursGrid = [24, 48, 72];
    private static readonly bool[] RequireNetPositiveGrid = [true, false];
    private static readonly bool[] RequireStressPlusGrid = [true, false];

    public static Bnb15LookbackStarvationStudyResult Build(
        DateTime runAtUtc,
        FrozenCandidateState state,
        CrossSymbolComboKey frozenKey,
        CrossSymbolActivationConfig frozenConfig,
        DateTime forwardStart,
        DateTime forwardEnd,
        IReadOnlyList<RegimeDriftDiagnosticTrade> moderateTrades,
        IReadOnlyList<RegimeDriftDiagnosticTrade> stressTrades,
        IReadOnlyList<DateTime> rawSignalEntryTimesUtc,
        CrossSymbolSimOutcome frozenForwardSim)
    {
        var moderateOrdered = moderateTrades.OrderBy(t => t.EntryTimeUtc).ToArray();
        var stressByEntry = stressTrades
            .GroupBy(t => t.EntryTimeUtc)
            .ToDictionary(g => g.Key, g => g.First());

        var checkpoints = BuildCheckpointDiagnostics(
            frozenConfig,
            forwardStart,
            forwardEnd,
            moderateOrdered,
            stressByEntry,
            rawSignalEntryTimesUtc);

        var rootCauseCounts = checkpoints
            .GroupBy(c => c.RootCauseClassification, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var primaryRootCause = rootCauseCounts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => kv.Key)
            .FirstOrDefault() ?? "MinTradeThresholdTooHigh";

        var variants = BuildVariantGrid(
            frozenKey,
            forwardStart,
            forwardEnd,
            moderateOrdered,
            stressTrades);

        var incubationCandidates = variants
            .Where(v => v.Recommendation == "CandidateForNewIncubation")
            .ToArray();

        var plainEnglish = BuildPlainEnglish(
            checkpoints,
            rootCauseCounts,
            primaryRootCause,
            frozenForwardSim,
            incubationCandidates,
            variants);

        var forwardSpanDays = Math.Round((decimal)(forwardEnd - forwardStart).TotalDays, 4);
        var frozenStats = ComputeTradeStats(frozenForwardSim.TakenTrades);

        var summary = new Bnb15LookbackStarvationStudySummary
        {
            RunAtUtc = runAtUtc,
            FrozenProfileName = state.ProfileName,
            Symbol = frozenKey.Symbol.ToString(),
            Interval = frozenKey.Interval,
            Direction = frozenKey.Direction.ToString(),
            TargetPercent = frozenKey.TargetPercent,
            StopPercent = frozenKey.StopPercent,
            FrozenActivationRule = frozenConfig.ActivationRuleName,
            ForwardWindowStartUtc = forwardStart,
            ForwardWindowEndUtc = forwardEnd,
            ForwardSpanDays = forwardSpanDays,
            FrozenForwardTrades = frozenStats.Count,
            FrozenActivatedCheckpointCount = frozenForwardSim.ActivatedPeriodCount,
            FrozenActivationCheckpointCount = frozenForwardSim.Periods.Count,
            PrimaryRootCause = primaryRootCause,
            RootCauseCounts = rootCauseCounts,
            PlainEnglish = plainEnglish,
            CompactSummaryLine =
                $"BNB15 lookback study: forwardTrades={frozenStats.Count} activated={frozenForwardSim.ActivatedPeriodCount}/{frozenForwardSim.Periods.Count} primaryCause={primaryRootCause} incubationVariants={incubationCandidates.Length}",
            DiagnosticVariantCount = variants.Count,
            CandidateForNewIncubationVariantCount = incubationCandidates.Length
        };

        return new Bnb15LookbackStarvationStudyResult(summary, checkpoints, variants);
    }

    private static List<Bnb15LookbackCheckpointDiagnosticRow> BuildCheckpointDiagnostics(
        CrossSymbolActivationConfig frozenConfig,
        DateTime forwardStart,
        DateTime forwardEnd,
        RegimeDriftDiagnosticTrade[] moderateOrdered,
        Dictionary<DateTime, RegimeDriftDiagnosticTrade> stressByEntry,
        IReadOnlyList<DateTime> rawSignalEntryTimesUtc)
    {
        var rows = new List<Bnb15LookbackCheckpointDiagnosticRow>();

        for (var checkpoint = forwardStart; checkpoint < forwardEnd; checkpoint = checkpoint.AddHours(frozenConfig.CheckpointFrequencyHours))
        {
            var lookbackStart = checkpoint.AddDays(-frozenConfig.LookbackDays);
            var lookbackModerate = moderateOrdered
                .Where(t => t.ExitTimeUtc < checkpoint && t.EntryTimeUtc >= lookbackStart)
                .ToArray();
            var lookbackStress = lookbackModerate
                .Select(t => stressByEntry.TryGetValue(t.EntryTimeUtc, out var s) ? s : t)
                .ToArray();

            var lookbackNet = lookbackModerate.Sum(t => t.NetPnlQuote);
            var lookbackStressNet = lookbackStress.Sum(t => t.NetPnlQuote);
            var pf = ProfitFactor(lookbackModerate);

            var perfPass = lookbackModerate.Length >= frozenConfig.MinLookbackTrades && lookbackNet > 0m;
            var skipReason = string.Empty;
            if (lookbackModerate.Length < frozenConfig.MinLookbackTrades)
                skipReason = $"InsufficientLookbackTrades({lookbackModerate.Length}<{frozenConfig.MinLookbackTrades})";
            else if (lookbackNet <= 0m)
                skipReason = "PerfConditionFailed";

            var activationEnd = checkpoint.AddHours(frozenConfig.ActivationPeriodHours);
            if (activationEnd > forwardEnd)
                activationEnd = forwardEnd;

            var windowModerate = moderateOrdered
                .Where(t => t.EntryTimeUtc >= checkpoint && t.EntryTimeUtc < activationEnd)
                .ToArray();
            var windowStress = windowModerate
                .Select(t => stressByEntry.TryGetValue(t.EntryTimeUtc, out var s) ? s : t)
                .ToArray();
            var baseSignals = rawSignalEntryTimesUtc
                .Count(t => t >= checkpoint && t < activationEnd);

            var rootCause = ClassifyCheckpointRootCause(
                frozenConfig,
                checkpoint,
                lookbackStart,
                lookbackModerate,
                moderateOrdered,
                rawSignalEntryTimesUtc,
                skipReason,
                perfPass,
                baseSignals,
                windowModerate.Length);

            rows.Add(new Bnb15LookbackCheckpointDiagnosticRow
            {
                CheckpointUtc = checkpoint,
                LookbackStartUtc = lookbackStart,
                LookbackEndUtc = checkpoint,
                RequiredMinLookbackTrades = frozenConfig.MinLookbackTrades,
                ActualLookbackTrades = lookbackModerate.Length,
                LookbackNetModerate = Math.Round(lookbackNet, 8),
                LookbackNetStressPlus = Math.Round(lookbackStressNet, 8),
                LookbackProfitFactor = Math.Round(pf, 6),
                ActivationPassed = perfPass,
                SkipReason = skipReason,
                ForwardBaseSignalsInActivationWindow = baseSignals,
                ForwardTradesInActivationWindow = windowModerate.Length,
                NetIfActivated = Math.Round(windowModerate.Sum(t => t.NetPnlQuote), 8),
                StressNetIfActivated = Math.Round(windowStress.Sum(t => t.NetPnlQuote), 8),
                RootCauseClassification = rootCause
            });
        }

        return rows;
    }

    private static string ClassifyCheckpointRootCause(
        CrossSymbolActivationConfig frozenConfig,
        DateTime checkpoint,
        DateTime lookbackStart,
        RegimeDriftDiagnosticTrade[] lookbackModerate,
        RegimeDriftDiagnosticTrade[] allModerate,
        IReadOnlyList<DateTime> rawSignalEntryTimesUtc,
        string skipReason,
        bool perfPass,
        int baseSignalsInActivationWindow,
        int tradesInActivationWindow)
    {
        if (perfPass)
        {
            if (tradesInActivationWindow == 0 && baseSignalsInActivationWindow > 0)
                return "ActivationWindowTooNarrow";
            if (tradesInActivationWindow == 0)
                return "CurrentMarketNoOpportunity";
            return "ActivationPassed";
        }

        if (!skipReason.StartsWith("InsufficientLookbackTrades", StringComparison.OrdinalIgnoreCase))
            return "CurrentMarketNoOpportunity";

        var baseSignalsInLookback = rawSignalEntryTimesUtc.Count(t => t >= lookbackStart && t < checkpoint);
        var openTradesExcluded = allModerate.Count(t =>
            t.EntryTimeUtc >= lookbackStart && t.EntryTimeUtc < checkpoint && t.ExitTimeUtc >= checkpoint);

        if (baseSignalsInLookback == 0 && lookbackModerate.Length == 0)
            return "BaseSignalTooSparseForLookback";

        var extendedStart = checkpoint.AddDays(-7);
        var tradesIn7d = allModerate.Count(t => t.ExitTimeUtc < checkpoint && t.EntryTimeUtc >= extendedStart);
        if (lookbackModerate.Length < frozenConfig.MinLookbackTrades
            && tradesIn7d >= frozenConfig.MinLookbackTrades
            && frozenConfig.LookbackDays <= 3)
        {
            return "LookbackWindowTooShort";
        }

        if (openTradesExcluded > 0)
            return "CheckpointAlignmentIssue";

        if (lookbackModerate.Length > 0 && lookbackModerate.Length < frozenConfig.MinLookbackTrades)
            return "MinTradeThresholdTooHigh";

        if (baseSignalsInLookback >= frozenConfig.MinLookbackTrades && lookbackModerate.Length < frozenConfig.MinLookbackTrades)
            return "MinTradeThresholdTooHigh";

        return lookbackModerate.Length == 0
            ? "BaseSignalTooSparseForLookback"
            : "MinTradeThresholdTooHigh";
    }

    private static List<Bnb15LookbackDiagnosticVariantRow> BuildVariantGrid(
        CrossSymbolComboKey frozenKey,
        DateTime forwardStart,
        DateTime forwardEnd,
        RegimeDriftDiagnosticTrade[] moderateOrdered,
        IReadOnlyList<RegimeDriftDiagnosticTrade> stressTrades)
    {
        var variants = new List<Bnb15LookbackDiagnosticVariantRow>();

        foreach (var minTrades in MinLookbackTradesGrid)
        foreach (var lookbackDays in LookbackDaysGrid)
        foreach (var checkpointHours in CheckpointHoursGrid)
        foreach (var activationHours in ActivationHoursGrid)
        foreach (var requireNetPositive in RequireNetPositiveGrid)
        foreach (var requireStressPlus in RequireStressPlusGrid)
        {
            var sim = Bnb15LookbackStarvationStudySimulator.Simulate(
                frozenKey,
                checkpointHours,
                activationHours,
                lookbackDays,
                minTrades,
                requireNetPositive,
                requireStressPlus,
                moderateOrdered,
                stressTrades,
                forwardStart,
                forwardEnd);

            var moderateStats = ComputeTradeStats(sim.TakenTrades);
            var stressTaken = sim.TakenTrades
                .Select(t => stressTrades.FirstOrDefault(s => s.EntryTimeUtc == t.EntryTimeUtc) ?? t)
                .ToArray();
            var stressStats = ComputeTradeStats(stressTaken);

            var recommendation = RecommendVariant(
                sim,
                moderateStats,
                stressStats.Net,
                requireNetPositive,
                requireStressPlus);

            variants.Add(new Bnb15LookbackDiagnosticVariantRow
            {
                VariantName =
                    $"Min{minTrades}_LB{lookbackDays}d_Chk{checkpointHours}h_Act{activationHours}h_NetPos{requireNetPositive}_StressPos{requireStressPlus}",
                MinLookbackTrades = minTrades,
                LookbackDays = lookbackDays,
                CheckpointFrequencyHours = checkpointHours,
                ActivationDurationHours = activationHours,
                RequireNetPositive = requireNetPositive,
                RequireStressPlusPositive = requireStressPlus,
                ActivatedCheckpointCount = sim.ActivatedCheckpointCount,
                ForwardTrades = moderateStats.Count,
                NetModerate = moderateStats.Net,
                NetStressPlus = stressStats.Net,
                WinRate = moderateStats.WinRate,
                ProfitFactor = moderateStats.ProfitFactor,
                MaxDrawdown = moderateStats.MaxDrawdown,
                MaxConsecutiveLosses = moderateStats.MaxConsecutiveLosses,
                StressPassed = stressStats.Net > 0m,
                ForwardTradeCountPassed = moderateStats.Count >= 5,
                Recommendation = recommendation
            });
        }

        return variants;
    }

    private static string RecommendVariant(
        Bnb15LookbackStarvationStudySimulator.DiagnosticSimOutcome sim,
        TradeStats moderateStats,
        decimal stressNet,
        bool requireNetPositive,
        bool requireStressPlus)
    {
        if (sim.ActivatedCheckpointCount == 0)
            return "StillStarved";

        if (moderateStats.Count < 5)
            return sim.ActivatedCheckpointCount > 0 ? "NeedsMoreData" : "StillStarved";

        if (stressNet <= 0m)
            return !requireNetPositive || !requireStressPlus ? "TooLoose" : "Reject";

        if (moderateStats.Net <= 0m)
            return "Reject";

        if (moderateStats.MaxConsecutiveLosses > 3)
            return "Reject";

        if (sim.TotalCheckpoints > 0
            && sim.ActivatedCheckpointCount >= (int)Math.Ceiling(sim.TotalCheckpoints * 0.90m))
        {
            return "TooLoose";
        }

        if (moderateStats.Count >= 5
            && moderateStats.Net > 0m
            && stressNet > 0m
            && moderateStats.MaxConsecutiveLosses <= 3)
        {
            return "CandidateForNewIncubation";
        }

        return "NeedsMoreData";
    }

    private static Bnb15LookbackStarvationPlainEnglishSummary BuildPlainEnglish(
        IReadOnlyList<Bnb15LookbackCheckpointDiagnosticRow> checkpoints,
        IReadOnlyDictionary<string, int> rootCauseCounts,
        string primaryRootCause,
        CrossSymbolSimOutcome frozenSim,
        IReadOnlyList<Bnb15LookbackDiagnosticVariantRow> incubationCandidates,
        IReadOnlyList<Bnb15LookbackDiagnosticVariantRow> allVariants)
    {
        var starvationCheckpoints = checkpoints
            .Where(c => !c.ActivationPassed)
            .ToArray();
        var insufficient = starvationCheckpoints
            .Count(c => c.SkipReason.StartsWith("InsufficientLookbackTrades", StringComparison.OrdinalIgnoreCase));
        var avgLookbackTrades = starvationCheckpoints.Length > 0
            ? (decimal)starvationCheckpoints.Average(c => c.ActualLookbackTrades)
            : 0m;

        var sparseVsStrict = insufficient == starvationCheckpoints.Length && avgLookbackTrades < 3m
            ? "BNB15 forward base signals exist, but completed lookback trades are too sparse for the frozen min-5 gate. Starvation is mostly protective strictness, not missing discovery edge."
            : insufficient > 0
                ? "BNB15 shows a mix of sparse completed lookback trades and strict min-trade gating; discovery stats are strong but forward completion cadence cannot feed the frozen lookback window reliably."
                : "BNB15 lookback failures are not purely min-trade starvation; perf or timing gates dominate in the forward window.";

        var primaryGate = primaryRootCause switch
        {
            "MinTradeThresholdTooHigh" =>
                "The min-5 completed-trade lookback threshold is the dominant starvation gate (lookback repeatedly shows 2-4 trades).",
            "LookbackWindowTooShort" =>
                "The 3-day lookback window is too short; a longer lookback would have supplied enough completed trades at several checkpoints.",
            "BaseSignalTooSparseForLookback" =>
                "Base signals themselves are sparse in the forward window, so lookback cannot accumulate trades even before the min-trade gate.",
            "CheckpointAlignmentIssue" =>
                "Checkpoint boundaries exclude in-flight trades from lookback counts, amplifying starvation.",
            "ActivationWindowTooNarrow" =>
                "Activation passes are not the main issue; entry opportunities are not lining up inside activation windows.",
            _ => $"Primary gate: {primaryRootCause} ({rootCauseCounts.GetValueOrDefault(primaryRootCause)} checkpoints)."
        };

        string safeCandidate;
        if (incubationCandidates.Count > 0)
        {
            var best = incubationCandidates
                .OrderByDescending(v => v.NetStressPlus)
                .ThenByDescending(v => v.NetModerate)
                .First();
            safeCandidate =
                $"Diagnostic variant {best.VariantName} could be a separate incubation experiment (forwardTrades={best.ForwardTrades}, net={best.NetModerate:F2}, stressPlus={best.NetStressPlus:F2}). This does NOT change the frozen profile.";
        }
        else
        {
            var relaxed = allVariants
                .Where(v => v.ForwardTrades >= 5 && v.NetModerate > 0m && v.NetStressPlus <= 0m)
                .OrderByDescending(v => v.ForwardTrades)
                .FirstOrDefault();
            safeCandidate = relaxed is not null
                ? $"No safe new incubation candidate. Lowering gates increases trades (e.g. {relaxed.VariantName}) but stress-plus turns negative — starvation was protective."
                : "No safe new incubation candidate passed stress-plus and trade-count gates in the diagnostic grid.";
        }

        var stayParked = incubationCandidates.Count == 0 || frozenSim.TakenTrades.Count == 0
            ? "Yes — keep the current frozen BNB15 profile parked. Forward window produced 0 executable trades under frozen gates."
            : "Keep parked for now. Diagnostic variants need a separate forward incubation track before any change.";

        var overall = incubationCandidates.Count > 0 ? "ExploreSeparateIncubation" : "KeepParked";

        return new Bnb15LookbackStarvationPlainEnglishSummary
        {
            SparseVsStrictGate = sparseVsStrict,
            PrimaryStarvationGate = primaryGate,
            SafeNewIncubationCandidate = safeCandidate,
            ShouldCurrentBnb15StayParked = stayParked,
            OverallStudyRecommendation = overall
        };
    }

    private static decimal ProfitFactor(IReadOnlyList<RegimeDriftDiagnosticTrade> trades)
    {
        if (trades.Count == 0)
            return 0m;
        var grossWin = trades.Where(t => t.NetPnlQuote > 0m).Sum(t => t.NetPnlQuote);
        var grossLoss = Math.Abs(trades.Where(t => t.NetPnlQuote <= 0m).Sum(t => t.NetPnlQuote));
        return grossLoss == 0m ? (grossWin > 0m ? 999m : 0m) : grossWin / grossLoss;
    }

    private sealed record TradeStats(
        int Count,
        decimal Net,
        decimal WinRate,
        decimal ProfitFactor,
        decimal MaxDrawdown,
        int MaxConsecutiveLosses);

    private static TradeStats ComputeTradeStats(IReadOnlyList<RegimeDriftDiagnosticTrade> trades)
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
        var consec = 0;
        var maxConsec = 0;
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
