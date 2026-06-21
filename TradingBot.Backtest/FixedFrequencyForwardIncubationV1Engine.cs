using System.Globalization;

namespace TradingBot.Backtest;

/// <summary>
/// Authoritative fixed-frequency forward-incubation summary. All metrics are forward-only:
/// computed strictly from trades after the freeze timestamp. Diagnostic/research only.
/// </summary>
public sealed record FixedFrequencyForwardIncubationSummary
{
    public string ProfileName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public int HoldHours { get; init; }
    public string ActivationRule { get; init; } = string.Empty;

    public DateTime FrozenStartUtc { get; init; }
    public DateTime ForwardWindowStartUtc { get; init; }
    public DateTime ForwardWindowEndUtc { get; init; }
    public decimal ForwardSpanDays { get; init; }

    public int ForwardTrades { get; init; }
    public int NewTradesSincePreviousRun { get; init; }
    public decimal NetModerate { get; init; }
    public decimal NetStressPlus { get; init; }
    public decimal WinRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdown { get; init; }
    public int MaxConsecutiveLosses { get; init; }

    public int ActivationCheckpointCount { get; init; }
    public int ActivatedCheckpointCount { get; init; }
    public int ActivationFailedCheckpointCount { get; init; }
    public int ActivatedButNoEntryCount { get; init; }
    public int BaseSignalsInsideForwardWindow { get; init; }
    public int BaseSignalsInsideActivatedWindows { get; init; }

    public bool CurrentExactEntryPresent { get; init; }
    public string LatestStatus { get; init; } = string.Empty;
    public string Verdict { get; init; } = string.Empty;
    public string NextAction { get; init; } = string.Empty;
    public int HealthScore { get; init; }
    public string FailedHealthGates { get; init; } = string.Empty;

    public bool TestnetOrderCandidate { get; init; }
    public bool RealOrdersPlaced { get; init; }
    public bool TestnetOrdersEnabled { get; init; }
    public bool LiveTradingEnabled { get; init; }
    public string DiscoveryEvidenceNote { get; init; } =
        "Discovery/frequency history before FrozenStartUtc is research evidence only, not forward proof.";
    public string CompactSummaryLine { get; init; } = string.Empty;
    public NormalizedRiskPnlMetrics NormalizedRisk { get; init; } = new();
}

public sealed record FixedFrequencyForwardIncubationV1RunResult(
    FixedFrequencyForwardIncubationSummary Summary,
    FrozenCandidateSummaryRow FrozenProfile,
    IReadOnlyList<ForwardHealthGateRow> HealthGates,
    IReadOnlyList<CrossSymbolTradeRow> ForwardTrades,
    IReadOnlyList<ForwardIncubationHistoryEntry> History,
    ForwardIncubationNoTradeReasonSummary NoTradeDiagnostics,
    bool ProtectedFrozenFilesByteIdentical,
    IReadOnlyList<string> ProtectedFilesChecked);

/// <summary>
/// Builds the fixed-frequency forward-incubation summary, health gates, and verdict from the shared
/// simulation core. Health-gate and verdict rules are fixed up-front (never tuned against the sample).
/// </summary>
public static class FixedFrequencyForwardIncubationV1Engine
{
    private sealed record TradeStats(
        decimal Net, decimal WinRate, decimal ProfitFactor, decimal MaxDrawdown, int MaxConsecutiveLosses);

    public static FixedFrequencyForwardIncubationV1RunResult Build(
        CrossSymbolForwardIncubationRunResult core,
        CrossSymbolComboKey comboKey,
        string activationRule,
        bool currentExactEntryPresent,
        string trackLabel,
        string outputDirectory)
    {
        var s = core.NoTradeReasonSummary;
        var stats = ComputeStats(core.ForwardTrades);

        var healthGates = BuildHealthGates(
            core.ForwardTrades.Count,
            s.NetModerate,
            s.NetStressPlus,
            stats.MaxConsecutiveLosses,
            stats.Net,
            core.ForwardTrades,
            core.ForwardClusterCount,
            currentExactEntryPresent);

        var passed = healthGates.Count(g => g.Pass);
        var total = healthGates.Count;
        var healthScore = total == 0 ? 0 : (int)Math.Round(100m * passed / total, MidpointRounding.AwayFromZero);
        var failedGates = string.Join(", ", healthGates.Where(g => !g.Pass).Select(g => g.GateName));
        var allGatesPass = passed == total;

        var verdict = ResolveVerdict(
            core.ForwardTrades.Count,
            core.ForwardSpanDays,
            s.ActivationCheckpointCount,
            s.ActivatedCheckpointCount,
            s.NetModerate,
            s.NetStressPlus,
            allGatesPass);
        var nextAction = ResolveNextAction(verdict);
        var testnetOrderCandidate = verdict == "TestnetOrderCandidate";

        var symbol = comboKey.Symbol.ToString();
        var normalizedRisk = NormalizedRiskPnlModule.Compute(symbol, s.NetModerate, stats.MaxDrawdown);
        var compactLine = NormalizedRiskPnlModule.BuildForwardIncubationCompactSummaryLine(
            trackLabel, s.NetModerate, symbol, stats.MaxDrawdown, verdict, nextAction, outputDirectory);

        var summary = new FixedFrequencyForwardIncubationSummary
        {
            ProfileName = s.FrozenProfileName,
            Symbol = comboKey.Symbol.ToString(),
            Interval = comboKey.Interval,
            Direction = comboKey.Direction.ToString(),
            TargetPercent = comboKey.TargetPercent,
            StopPercent = comboKey.StopPercent,
            HoldHours = comboKey.MaxHoldMinutes / 60,
            ActivationRule = activationRule,
            FrozenStartUtc = core.FrozenStartUtc,
            ForwardWindowStartUtc = core.FrozenStartUtc,
            ForwardWindowEndUtc = core.ForwardWindowEndUtc,
            ForwardSpanDays = core.ForwardSpanDays,
            ForwardTrades = core.ForwardTrades.Count,
            NewTradesSincePreviousRun = s.NewTradesSincePreviousRun,
            NetModerate = s.NetModerate,
            NetStressPlus = s.NetStressPlus,
            WinRate = stats.WinRate,
            ProfitFactor = stats.ProfitFactor,
            MaxDrawdown = stats.MaxDrawdown,
            MaxConsecutiveLosses = stats.MaxConsecutiveLosses,
            ActivationCheckpointCount = s.ActivationCheckpointCount,
            ActivatedCheckpointCount = s.ActivatedCheckpointCount,
            ActivationFailedCheckpointCount = s.ActivationFailedCheckpointCount,
            ActivatedButNoEntryCount = s.ActivatedButNoEntryCount,
            BaseSignalsInsideForwardWindow = core.BaseSignalsInsideForwardWindow,
            BaseSignalsInsideActivatedWindows = core.BaseSignalsInsideActivatedWindows,
            CurrentExactEntryPresent = currentExactEntryPresent,
            LatestStatus = s.LatestRunStatus,
            Verdict = verdict,
            NextAction = nextAction,
            HealthScore = healthScore,
            FailedHealthGates = failedGates,
            TestnetOrderCandidate = testnetOrderCandidate,
            RealOrdersPlaced = false,
            TestnetOrdersEnabled = false,
            LiveTradingEnabled = false,
            CompactSummaryLine = compactLine,
            NormalizedRisk = normalizedRisk
        };

        var noTradeDiagnostics = s with
        {
            CompactSummaryLine = compactLine,
            NormalizedRisk = normalizedRisk,
            Verdict = verdict,
            NextAction = nextAction,
            FailedGates = failedGates,
            HealthGatesPassed = passed,
            HealthGatesTotal = total
        };

        return new FixedFrequencyForwardIncubationV1RunResult(
            summary,
            core.FrozenSummary,
            healthGates,
            core.ForwardTrades,
            core.History,
            noTradeDiagnostics,
            core.ProtectedFrozenFilesByteIdentical,
            core.ProtectedFilesChecked);
    }

    private static IReadOnlyList<ForwardHealthGateRow> BuildHealthGates(
        int forwardTrades,
        decimal netModerate,
        decimal netStressPlus,
        int maxConsecutiveLosses,
        decimal net,
        IReadOnlyList<CrossSymbolTradeRow> trades,
        int clusterCount,
        bool currentExactEntryPresent)
    {
        var gates = new List<ForwardHealthGateRow>
        {
            Gate("MinForwardTrades",
                $">= {FixedFrequencyForwardIncubationV1Catalog.MinForwardTrades} forward trades after freeze",
                forwardTrades.ToString(CultureInfo.InvariantCulture),
                true,
                forwardTrades >= FixedFrequencyForwardIncubationV1Catalog.MinForwardTrades),
            Gate("NetModeratePositive",
                "Forward net > 0 under futures-moderate",
                netModerate.ToString("F2", CultureInfo.InvariantCulture),
                true,
                netModerate > 0m),
            Gate("NetStressPlusPositive",
                "Forward net > 0 under futures-stress-plus",
                netStressPlus.ToString("F2", CultureInfo.InvariantCulture),
                true,
                netStressPlus > FixedFrequencyForwardIncubationV1Catalog.StressPlusFloorQuote),
            Gate("MaxConsecutiveLosses",
                $"<= {FixedFrequencyForwardIncubationV1Catalog.MaxConsecutiveLossesLimit} consecutive losses",
                maxConsecutiveLosses.ToString(CultureInfo.InvariantCulture),
                true,
                maxConsecutiveLosses <= FixedFrequencyForwardIncubationV1Catalog.MaxConsecutiveLossesLimit)
        };

        var dailyNets = trades
            .GroupBy(t => t.ExitTimeUtc.Date)
            .Select(g => g.Sum(t => t.NetPnlQuote))
            .ToArray();
        var maxDay = dailyNets.Length > 0 ? dailyNets.Max() : 0m;
        var dayShare = net > 0m ? Math.Round(maxDay / net, 4) : (decimal?)null;
        var notConcentrated = net > 0m
                              && dayShare.HasValue
                              && dayShare.Value <= FixedFrequencyForwardIncubationV1Catalog.MaxSingleDayProfitShare
                              && clusterCount >= 2;
        gates.Add(Gate("ProfitsNotConcentrated",
            $"No single day > {FixedFrequencyForwardIncubationV1Catalog.MaxSingleDayProfitShare:P0} of profit and spread across >= 2 activation clusters",
            net > 0m
                ? $"daysShare={(dayShare.HasValue ? dayShare.Value.ToString("P1", CultureInfo.InvariantCulture) : "n/a")}, clusters={clusterCount}"
                : "n/a (net <= 0)",
            net > 0m,
            notConcentrated,
            net > 0m ? string.Empty : "Not applicable while forward net is non-positive."));

        gates.Add(Gate("CurrentExactEntrySignalPresent",
            "Current watcher or shadow has an exact entry signal for this candidate",
            currentExactEntryPresent ? "true" : "false",
            true,
            currentExactEntryPresent,
            currentExactEntryPresent
                ? string.Empty
                : "No current exact shadow/watcher entry signal; cannot be a testnet-order candidate yet."));

        return gates;
    }

    private static ForwardHealthGateRow Gate(
        string name, string requirement, string observed, bool applicable, bool pass, string notes = "")
        => new()
        {
            GateName = name,
            Requirement = requirement,
            ObservedValue = observed,
            Applicable = applicable,
            Pass = pass,
            Notes = notes
        };

    private static string ResolveVerdict(
        int forwardTrades,
        decimal forwardSpanDays,
        int checkpointCount,
        int activatedCheckpointCount,
        decimal netModerate,
        decimal netStressPlus,
        bool allHealthGatesPass)
    {
        if (forwardTrades == 0)
        {
            if (checkpointCount > 0
                && activatedCheckpointCount > 0
                && forwardSpanDays >= FixedFrequencyForwardIncubationV1Catalog.MinForwardSpanDaysForJudgment)
                return "ActivatedButNoEntry";
            return "NotEnoughForwardDataYet";
        }

        if (forwardSpanDays < FixedFrequencyForwardIncubationV1Catalog.MinForwardSpanDaysForJudgment || checkpointCount == 0)
            return "NotEnoughForwardDataYet";

        if (allHealthGatesPass)
            return "TestnetOrderCandidate";

        // Stress-plus non-positive caps the verdict at KeepSecondary (still moderate-positive) or Park.
        if (netStressPlus <= FixedFrequencyForwardIncubationV1Catalog.StressPlusFloorQuote)
            return netModerate > 0m ? "KeepSecondary" : "Park";

        if (netModerate > 0m)
            return "KeepIncubating";
        return "KeepSecondary";
    }

    public static string ResolveNextAction(string verdict) => verdict switch
    {
        "NotEnoughForwardDataYet" =>
            "Keep collecting forward data after the freeze timestamp and re-run; no forward judgment possible yet. No order.",
        "ActivatedButNoEntry" =>
            "Activation fired in the forward window but no exact entry yet; keep watching for a current exact entry. No order.",
        "KeepIncubating" =>
            "Keep incubating; forward results positive but not all testnet-order health gates pass yet. No order.",
        "KeepSecondary" =>
            "Keep as a secondary candidate; stress-plus not positive so it cannot graduate. No testnet/live. No order.",
        "Park" =>
            "Park the candidate; forward window contradicts the discovery evidence. No order.",
        "TestnetOrderCandidate" =>
            "All forward health gates pass. Eligible for testnet-order consideration only after explicit human review; orders stay disabled in this diagnostic run.",
        _ => "Keep incubating. No order."
    };

    private static TradeStats ComputeStats(IReadOnlyList<CrossSymbolTradeRow> trades)
    {
        if (trades.Count == 0)
            return new TradeStats(0m, 0m, 0m, 0m, 0);

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
            Math.Round(net, 8),
            Math.Round((decimal)wins / ordered.Length, 6),
            pf,
            Math.Round(maxDd, 8),
            maxConsec);
    }
}
