using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed record RangeExpansionV24ExitPolicyImpactRow
{
    public string VariantLabel { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string ExitPolicyGroup { get; init; } = string.Empty;
    public string ProfitLockThreshold { get; init; } = string.Empty;
    public int CandidateCount { get; init; }
    public int TradeCount { get; init; }
    public int NetWinnerCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal NetPerTrade { get; init; }
    public int ProfitLockCount { get; init; }
    public decimal ProfitLockNetPnlQuote { get; init; }
    public decimal AvgProfitLockCapturedMfePercent { get; init; }
    public int StopLossCount { get; init; }
    public decimal StopLossNetPnlQuote { get; init; }
    public int TimeStopCount { get; init; }
    public decimal TimeStopNetPnlQuote { get; init; }
    public int HalfLockBreakevenCount { get; init; }
    public decimal HalfLockBreakevenNetPnlQuote { get; init; }
    public int CostCoveredBreakevenCount { get; init; }
    public decimal CostCoveredBreakevenNetPnlQuote { get; init; }
    public int NoProgressExitCount { get; init; }
    public decimal NoProgressExitNetPnlQuote { get; init; }
    public int ReachedHalfLockBeforeNoProgressDeadlineCount { get; init; }
    public int ProfitableExitCount { get; init; }
    public decimal ProfitLockPreservationRateVsBody80Current { get; init; }
    public decimal TimeStopReductionRateVsBody80Current { get; init; }
    public decimal HalfLockReductionRateVsBody80Current { get; init; }
    public decimal DeltaVsBody80Current { get; init; }
    public bool OverfitWarning { get; init; }
    public bool MeaningfulSample { get; init; }
}

public sealed record RangeExpansionV24WindowRobustnessRow
{
    public string VariantLabel { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string WindowLabel { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public int ProfitLockCount { get; init; }
    public int ProfitableExitCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal NetPerTrade { get; init; }
}

public sealed record RangeExpansionV24CostSensitivityRow
{
    public string VariantLabel { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string CostScenario { get; init; } = string.Empty;
    public decimal FeeRatePercent { get; init; }
    public decimal SpreadPercent { get; init; }
    public int TradeCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal NetPerTrade { get; init; }
    public int NetWinnerCount { get; init; }
}

public sealed record RangeExpansionV24ExtendedDiagnostics(
    IReadOnlyList<RangeExpansionV24ExitPolicyImpactRow> ExitPolicyImpact,
    IReadOnlyList<RangeExpansionV24WindowRobustnessRow> WindowRobustness,
    IReadOnlyList<RangeExpansionV24CostSensitivityRow> CostSensitivity,
    IReadOnlyList<ReachabilityResearchAnswer> ResearchAnswers);

public static class RangeExpansionV24DiagnosticsAggregator
{
    private const string Body80CurrentVariant = "v24-body80-halflock-current";
    private const decimal Body80CurrentNetReference = -1.777430507331625500000m;
    private const int MinMeaningfulTrades = 50;
    private const int MinMeaningfulProfitableExits = 20;

    public static RangeExpansionV24ExtendedDiagnostics Build(
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades,
        decimal currentFeeRatePercent,
        decimal currentSpreadPercent)
    {
        var exitPolicyImpact = BuildExitPolicyImpact(candidates, trades);
        var windowRobustness = BuildWindowRobustness(candidates, trades);
        var costSensitivity = BuildCostSensitivity(trades, currentFeeRatePercent, currentSpreadPercent);
        var researchAnswers = BuildResearchAnswers(exitPolicyImpact, windowRobustness, costSensitivity, trades);

        return new RangeExpansionV24ExtendedDiagnostics(exitPolicyImpact, windowRobustness, costSensitivity, researchAnswers);
    }

    public static IReadOnlyList<RangeExpansionV24ExitPolicyImpactRow> BuildExitPolicyImpact(
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades)
    {
        var body80Current = SummarizeProfile(trades, Body80CurrentVariant);
        var body80CurrentNet = body80Current?.NetPnl ?? Body80CurrentNetReference;

        var profileNames = candidates.Select(c => c.ProfileName)
            .Concat(trades.Select(t => t.ProfileName))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return profileNames
            .Select(profileName =>
            {
                var profileCandidates = candidates.Where(c =>
                    string.Equals(c.ProfileName, profileName, StringComparison.OrdinalIgnoreCase)).ToList();
                var profileTrades = trades.Where(t =>
                    string.Equals(t.ProfileName, profileName, StringComparison.OrdinalIgnoreCase)).ToList();
                var variant = ExtractVariantLabel(profileName);
                var summary = SummarizeTrades(profileTrades);
                var profitableExitCount = summary.ProfitLockCount + summary.CostCoveredBreakevenCount;

                return new RangeExpansionV24ExitPolicyImpactRow
                {
                    VariantLabel = variant,
                    ProfileName = profileName,
                    ExitPolicyGroup = ClassifyExitPolicyGroup(variant),
                    ProfitLockThreshold = ResolveProfitLockThreshold(profileTrades),
                    CandidateCount = profileCandidates.Count,
                    TradeCount = summary.TradeCount,
                    NetWinnerCount = summary.NetWinnerCount,
                    NetPnlQuote = summary.NetPnl,
                    NetPerTrade = summary.NetPerTrade,
                    ProfitLockCount = summary.ProfitLockCount,
                    ProfitLockNetPnlQuote = summary.ProfitLockNet,
                    AvgProfitLockCapturedMfePercent = summary.AvgProfitLockCapturedMfePercent,
                    StopLossCount = summary.StopLossCount,
                    StopLossNetPnlQuote = summary.StopLossNet,
                    TimeStopCount = summary.TimeStopCount,
                    TimeStopNetPnlQuote = summary.TimeStopNet,
                    HalfLockBreakevenCount = summary.HalfLockCount,
                    HalfLockBreakevenNetPnlQuote = summary.HalfLockNet,
                    CostCoveredBreakevenCount = summary.CostCoveredBreakevenCount,
                    CostCoveredBreakevenNetPnlQuote = summary.CostCoveredBreakevenNet,
                    NoProgressExitCount = summary.NoProgressExitCount,
                    NoProgressExitNetPnlQuote = summary.NoProgressExitNet,
                    ReachedHalfLockBeforeNoProgressDeadlineCount = summary.ReachedHalfLockBeforeNoProgressDeadlineCount,
                    ProfitableExitCount = profitableExitCount,
                    ProfitLockPreservationRateVsBody80Current = body80Current?.ProfitLockCount > 0
                        ? Math.Round(summary.ProfitLockCount * 100m / body80Current!.ProfitLockCount, 2)
                        : 0m,
                    TimeStopReductionRateVsBody80Current = body80Current?.TimeStopCount > 0
                        ? Math.Round((body80Current.TimeStopCount - summary.TimeStopCount) * 100m / body80Current.TimeStopCount, 2)
                        : 0m,
                    HalfLockReductionRateVsBody80Current = body80Current?.HalfLockCount > 0
                        ? Math.Round((body80Current.HalfLockCount - summary.HalfLockCount) * 100m / body80Current.HalfLockCount, 2)
                        : 0m,
                    DeltaVsBody80Current = Math.Round(summary.NetPnl - body80CurrentNet, 8),
                    OverfitWarning = summary.TradeCount < MinMeaningfulTrades || profitableExitCount < MinMeaningfulProfitableExits,
                    MeaningfulSample = summary.TradeCount >= MinMeaningfulTrades && profitableExitCount >= MinMeaningfulProfitableExits
                };
            })
            .OrderBy(r => r.ExitPolicyGroup, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.VariantLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<RangeExpansionV24WindowRobustnessRow> BuildWindowRobustness(
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades)
    {
        var windowByTradeKey = candidates
            .Where(c => c.Executed)
            .GroupBy(c => TradeKey(c.ProfileName, c.Symbol, c.TimeUtc), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().WindowLabel, StringComparer.OrdinalIgnoreCase);

        return trades
            .Select(t => new
            {
                Trade = t,
                Window = windowByTradeKey.TryGetValue(TradeKey(t.ProfileName, t.Symbol, t.EntryTimeUtc), out var window)
                    ? window
                    : "unknown"
            })
            .GroupBy(x => $"{x.Trade.ProfileName}|{x.Window}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var profileTrades = g.Select(x => x.Trade).ToList();
                var summary = SummarizeTrades(profileTrades);
                return new RangeExpansionV24WindowRobustnessRow
                {
                    VariantLabel = ExtractVariantLabel(first.Trade.ProfileName),
                    ProfileName = first.Trade.ProfileName,
                    WindowLabel = first.Window,
                    TradeCount = summary.TradeCount,
                    ProfitLockCount = summary.ProfitLockCount,
                    ProfitableExitCount = summary.ProfitLockCount + summary.CostCoveredBreakevenCount,
                    NetPnlQuote = summary.NetPnl,
                    NetPerTrade = summary.NetPerTrade
                };
            })
            .OrderBy(r => r.VariantLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.WindowLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<RangeExpansionV24CostSensitivityRow> BuildCostSensitivity(
        IReadOnlyList<SimulatedTrade> trades,
        decimal currentFeeRatePercent,
        decimal currentSpreadPercent)
    {
        var scenarios = RangeExpansionCostSensitivity.StandardScenarios
            .Select(s => string.Equals(s.Label, "current", StringComparison.OrdinalIgnoreCase)
                ? s with { FeeRatePercent = currentFeeRatePercent, SpreadPercent = currentSpreadPercent }
                : s)
            .ToArray();

        return trades
            .GroupBy(t => t.ProfileName, StringComparer.OrdinalIgnoreCase)
            .SelectMany(profileGroup =>
            {
                var variant = ExtractVariantLabel(profileGroup.Key);
                return scenarios.Select(scenario =>
                {
                    var nets = profileGroup
                        .Select(t => RangeExpansionCostSensitivity.RecalculateNetPnl(t, scenario))
                        .ToArray();
                    return new RangeExpansionV24CostSensitivityRow
                    {
                        VariantLabel = variant,
                        ProfileName = profileGroup.Key,
                        CostScenario = scenario.Label,
                        FeeRatePercent = scenario.FeeRatePercent,
                        SpreadPercent = scenario.SpreadPercent,
                        TradeCount = profileGroup.Count(),
                        NetPnlQuote = nets.Sum(),
                        NetPerTrade = nets.Length == 0 ? 0m : Math.Round(nets.Sum() / nets.Length, 8),
                        NetWinnerCount = nets.Count(n => n > 0m)
                    };
                });
            })
            .OrderBy(r => r.VariantLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CostScenario, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildResearchAnswers(
        IReadOnlyList<RangeExpansionV24ExitPolicyImpactRow> exitPolicyImpact,
        IReadOnlyList<RangeExpansionV24WindowRobustnessRow> windowRobustness,
        IReadOnlyList<RangeExpansionV24CostSensitivityRow> costSensitivity,
        IReadOnlyList<SimulatedTrade> trades)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var body80Current = exitPolicyImpact.FirstOrDefault(r => r.VariantLabel == Body80CurrentVariant);
        var meaningful = exitPolicyImpact.Where(r => r.MeaningfulSample && !r.OverfitWarning).ToArray();
        var bestMeaningful = meaningful.MaxBy(r => r.NetPnlQuote);

        var costCoverProfiles = exitPolicyImpact
            .Where(r => r.CostCoveredBreakevenCount > 0 || r.VariantLabel.Contains("costcover", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var bestCostCover = costCoverProfiles.MaxBy(r => r.CostCoveredBreakevenNetPnlQuote + r.NetPnlQuote);

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does fee-covered breakeven reduce HalfLock drag?",
            Answer = bestCostCover is null
                ? "No cost-cover profiles produced CostCoveredBreakeven exits."
                : $"{bestCostCover.VariantLabel}: HalfLock={bestCostCover.HalfLockBreakevenNetPnlQuote:F8} ({bestCostCover.HalfLockBreakevenCount}), CostCover={bestCostCover.CostCoveredBreakevenNetPnlQuote:F8} ({bestCostCover.CostCoveredBreakevenCount}), net={bestCostCover.NetPnlQuote:F8}.",
            Verdict = bestCostCover?.HalfLockBreakevenNetPnlQuote > body80Current?.HalfLockBreakevenNetPnlQuote
                ? "HalfLockDragWorse"
                : bestCostCover?.CostCoveredBreakevenNetPnlQuote >= 0m ? "CostCoverReducesDrag" : "CostCoverPartialImprovement",
            Details = new Dictionary<string, object?> { ["costCoverProfiles"] = costCoverProfiles.Take(5).ToArray(), ["body80Current"] = body80Current }
        });

        var noProgressProfiles = exitPolicyImpact
            .Where(r => r.NoProgressExitCount > 0 || r.VariantLabel.Contains("no-progress", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var bestNoProgress = noProgressProfiles.MaxBy(r => r.NetPnlQuote);

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does no-progress exit reduce TimeStop drag without cutting many ProfitLock exits?",
            Answer = bestNoProgress is null
                ? "No no-progress profiles in matrix."
                : $"{bestNoProgress.VariantLabel}: TimeStop={bestNoProgress.TimeStopNetPnlQuote:F8} ({bestNoProgress.TimeStopCount}), NoProgress={bestNoProgress.NoProgressExitNetPnlQuote:F8} ({bestNoProgress.NoProgressExitCount}), PL={bestNoProgress.ProfitLockCount}, net={bestNoProgress.NetPnlQuote:F8}.",
            Verdict = bestNoProgress?.TimeStopCount < body80Current?.TimeStopCount && bestNoProgress?.ProfitLockCount >= (body80Current?.ProfitLockCount ?? 0) * 0.7m
                ? "TimeStopCutPreservingPl"
                : bestNoProgress?.DeltaVsBody80Current > 0m ? "NetImprovesTradeoff" : "NoProgressDoesNotHelp",
            Details = new Dictionary<string, object?> { ["noProgressProfiles"] = noProgressProfiles.ToArray() }
        });

        var profitLockProfiles = exitPolicyImpact
            .Where(r => r.VariantLabel.Contains("profitlock", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does ProfitLock80/85 improve net under conservative costs?",
            Answer = profitLockProfiles.Length == 0
                ? "No ProfitLock80/85 profiles."
                : string.Join("; ", profitLockProfiles.Select(r =>
                    $"{r.VariantLabel} net={r.NetPnlQuote:F8}, PL={r.ProfitLockCount}, avgMfe={r.AvgProfitLockCapturedMfePercent:F2}%")),
            Verdict = profitLockProfiles.Any(r => r.MeaningfulSample && r.NetPnlQuote > (body80Current?.NetPnlQuote ?? Body80CurrentNetReference))
                ? "LowerLockImprovesNet"
                : "LowerLockDoesNotBeatBody80Current",
            Details = new Dictionary<string, object?> { ["profitLockProfiles"] = profitLockProfiles.ToArray() }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is body80 + improved exit policy near breakeven or positive with meaningful sample?",
            Answer = bestMeaningful is null
                ? "No profile met meaningful sample thresholds (>=50 trades and >=20 profitable exits)."
                : $"Best={bestMeaningful.VariantLabel} net={bestMeaningful.NetPnlQuote:F8}, trades={bestMeaningful.TradeCount}, deltaVsBody80={bestMeaningful.DeltaVsBody80Current:F8}.",
            Verdict = bestMeaningful?.NetPnlQuote >= 0m ? "PositiveMeaningful" : bestMeaningful?.NetPnlQuote >= -0.5m ? "NearBreakeven" : "StillNegative",
            Details = new Dictionary<string, object?> { ["best"] = bestMeaningful, ["top5"] = meaningful.OrderByDescending(r => r.NetPnlQuote).Take(5).ToArray() }
        });

        var profilesWithWindows = windowRobustness
            .GroupBy(r => r.ProfileName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Profile = g.Key,
                Variant = g.First().VariantLabel,
                Windows = g.OrderBy(x => x.WindowLabel).ToArray(),
                CollapsesAny = g.Any(w => w.NetPnlQuote < (body80Current?.NetPnlQuote ?? Body80CurrentNetReference))
            })
            .Where(x => x.Windows.Length >= 2)
            .ToArray();

        var robustProfiles = meaningful
            .Where(r => profilesWithWindows.Any(p =>
                string.Equals(p.Variant, r.VariantLabel, StringComparison.OrdinalIgnoreCase)
                && p.Windows.All(w => w.TradeCount >= 8)
                && !p.CollapsesAny))
            .ToArray();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does any result hold across 30d/60d/90d?",
            Answer = robustProfiles.Length == 0
                ? "No meaningful profile shows stable non-collapsing cross-window improvement."
                : string.Join("; ", robustProfiles.Take(5).Select(r =>
                {
                    var windows = profilesWithWindows.First(p => p.Variant == r.VariantLabel).Windows;
                    return $"{r.VariantLabel}: {string.Join(",", windows.Select(w => $"{w.WindowLabel}={w.NetPnlQuote:F4}"))}";
                })),
            Verdict = robustProfiles.Length > 0 ? "CrossWindowStable" : "SingleWindowFragile",
            Details = new Dictionary<string, object?> { ["profilesWithWindows"] = profilesWithWindows.Take(10).ToArray() }
        });

        var bestCurrent = meaningful.MaxBy(r => r.NetPnlQuote);
        var bestOptimistic = costSensitivity
            .Where(r => !string.Equals(r.CostScenario, "current", StringComparison.OrdinalIgnoreCase)
                        && meaningful.Any(m => string.Equals(m.ProfileName, r.ProfileName, StringComparison.OrdinalIgnoreCase)))
            .MaxBy(r => r.NetPnlQuote);

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "If still negative under current costs, is the remaining gap small enough to justify future lower-fee/Futures research, or should RangeExpansion V2 be parked?",
            Answer = bestMeaningful is null
                ? "No meaningful profile to evaluate."
                : $"Best current-cost {bestCurrent?.VariantLabel} net={bestCurrent?.NetPnlQuote:F8}; optimistic {bestOptimistic?.CostScenario} net={bestOptimistic?.NetPnlQuote:F8}.",
            Verdict = bestMeaningful?.NetPnlQuote >= 0m
                ? "TradeableUnderCurrentCosts"
                : bestMeaningful?.NetPnlQuote >= -0.5m && bestOptimistic?.NetPnlQuote > 0m
                    ? "PromisingButNotSpotLiveTradable"
                    : "ParkOrRequireMajorRework",
            Details = new Dictionary<string, object?>
            {
                ["body80Current"] = body80Current,
                ["bestMeaningful"] = bestMeaningful,
                ["bestOptimistic"] = bestOptimistic
            }
        });

        return answers;
    }

    private sealed record ProfileTradeSummary(
        int TradeCount,
        int NetWinnerCount,
        decimal NetPnl,
        decimal NetPerTrade,
        int ProfitLockCount,
        decimal ProfitLockNet,
        decimal AvgProfitLockCapturedMfePercent,
        int StopLossCount,
        decimal StopLossNet,
        int TimeStopCount,
        decimal TimeStopNet,
        int HalfLockCount,
        decimal HalfLockNet,
        int CostCoveredBreakevenCount,
        decimal CostCoveredBreakevenNet,
        int NoProgressExitCount,
        decimal NoProgressExitNet,
        int ReachedHalfLockBeforeNoProgressDeadlineCount);

    private static ProfileTradeSummary? SummarizeProfile(IReadOnlyList<SimulatedTrade> trades, string variantLabel)
    {
        var profileTrades = trades.Where(t =>
            ExtractVariantLabel(t.ProfileName).Equals(variantLabel, StringComparison.OrdinalIgnoreCase)).ToList();
        return profileTrades.Count == 0 ? null : SummarizeTrades(profileTrades);
    }

    private static ProfileTradeSummary SummarizeTrades(IReadOnlyList<SimulatedTrade> profileTrades)
    {
        var net = profileTrades.Sum(t => t.NetPnlQuote);
        var profitLockTrades = profileTrades
            .Where(t => string.Equals(t.ExitReason, "ProfitLock", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var avgCapturedMfe = profitLockTrades.Count == 0
            ? 0m
            : Math.Round(profitLockTrades.Where(t => t.CapturedMfePercent.HasValue).Select(t => t.CapturedMfePercent!.Value).DefaultIfEmpty(0m).Average(), 2);

        var noProgressTrades = profileTrades
            .Where(t => string.Equals(t.ExitReason, "NoProgressExit", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new ProfileTradeSummary(
            profileTrades.Count,
            profileTrades.Count(t => t.NetPnlQuote > 0m),
            net,
            profileTrades.Count == 0 ? 0m : Math.Round(net / profileTrades.Count, 8),
            CountExit(profileTrades, "ProfitLock"),
            SumExit(profileTrades, "ProfitLock"),
            avgCapturedMfe,
            CountExit(profileTrades, "StopLoss"),
            SumExit(profileTrades, "StopLoss"),
            CountExit(profileTrades, "TimeStop"),
            SumExit(profileTrades, "TimeStop"),
            CountExit(profileTrades, "HalfLockBreakeven"),
            SumExit(profileTrades, "HalfLockBreakeven"),
            CountExit(profileTrades, "CostCoveredBreakeven"),
            SumExit(profileTrades, "CostCoveredBreakeven"),
            noProgressTrades.Count,
            noProgressTrades.Sum(t => t.NetPnlQuote),
            profileTrades.Count(t => t.HalfLockReachedBeforeExit));
    }

    private static string ClassifyExitPolicyGroup(string variantLabel)
    {
        if (variantLabel == Body80CurrentVariant)
            return "reference";
        if (variantLabel.Contains("profitlock", StringComparison.OrdinalIgnoreCase))
            return "profitlock-threshold";
        if (variantLabel.Contains("costcover", StringComparison.OrdinalIgnoreCase))
            return "cost-cover";
        if (variantLabel.Contains("no-progress", StringComparison.OrdinalIgnoreCase))
            return "no-progress";
        if (variantLabel.Contains("timestop", StringComparison.OrdinalIgnoreCase))
            return "timestop";
        if (variantLabel.Contains("combo", StringComparison.OrdinalIgnoreCase))
            return "combined";
        return "other";
    }

    private static string ResolveProfitLockThreshold(IReadOnlyList<SimulatedTrade> trades)
    {
        var threshold = trades.Select(t => t.ProfitLockThresholdPercent).FirstOrDefault(t => t.HasValue);
        return threshold.HasValue ? $"{threshold.Value:0}%" : "90%";
    }

    private static string ExtractVariantLabel(string profileName)
    {
        const string prefix = "range-expansion-v24-bnb-";
        if (!profileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return profileName;

        var remainder = profileName[prefix.Length..];
        var markerIndex = remainder.IndexOf("-1m-", StringComparison.OrdinalIgnoreCase);
        return markerIndex >= 0 ? remainder[..markerIndex] : remainder;
    }

    private static string TradeKey(string profileName, TradingSymbol symbol, DateTime timeUtc)
        => $"{profileName}|{symbol}|{timeUtc:O}";

    private static int CountExit(IReadOnlyList<SimulatedTrade> trades, string exitReason)
        => trades.Count(t => string.Equals(t.ExitReason, exitReason, StringComparison.OrdinalIgnoreCase));

    private static decimal SumExit(IReadOnlyList<SimulatedTrade> trades, string exitReason)
        => trades.Where(t => string.Equals(t.ExitReason, exitReason, StringComparison.OrdinalIgnoreCase)).Sum(t => t.NetPnlQuote);
}
