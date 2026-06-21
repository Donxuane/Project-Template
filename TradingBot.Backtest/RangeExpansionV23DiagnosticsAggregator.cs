using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed record RangeExpansionV23FilterImpactRow
{
    public string VariantLabel { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string SweepGroup { get; init; } = string.Empty;
    public int CandidateCount { get; init; }
    public int TradeCount { get; init; }
    public int NetWinnerCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal NetPerTrade { get; init; }
    public int ProfitLockCount { get; init; }
    public decimal ProfitLockNetPnlQuote { get; init; }
    public int StopLossCount { get; init; }
    public decimal StopLossNetPnlQuote { get; init; }
    public int TimeStopCount { get; init; }
    public decimal TimeStopNetPnlQuote { get; init; }
    public int HalfLockBreakevenCount { get; init; }
    public decimal HalfLockBreakevenNetPnlQuote { get; init; }
    public decimal ProfitLockPreservationRate { get; init; }
    public decimal StopLossReductionRate { get; init; }
    public decimal TimeStopReductionRate { get; init; }
    public decimal DeltaVsFailedBreakoutRef { get; init; }
    public bool OverfitWarning { get; init; }
    public bool MeaningfulSample { get; init; }
}

public sealed record RangeExpansionV23WindowRobustnessRow
{
    public string VariantLabel { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string WindowLabel { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public int ProfitLockCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal NetPerTrade { get; init; }
}

public sealed record RangeExpansionV23CostSensitivityRow
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

public sealed record RangeExpansionV23ExtendedDiagnostics(
    IReadOnlyList<RangeExpansionV23FilterImpactRow> FilterImpact,
    IReadOnlyList<RangeExpansionV23WindowRobustnessRow> WindowRobustness,
    IReadOnlyList<RangeExpansionV23CostSensitivityRow> CostSensitivity,
    IReadOnlyList<ReachabilityResearchAnswer> ResearchAnswers);

public static class RangeExpansionV23DiagnosticsAggregator
{
    private const string BaselineVariant = "baseline-halflock";
    private const string FailedBreakoutRefVariant = "failed-breakout-ref-halflock";
    private const decimal FailedBreakoutRefNetReference = -3.464365411514985375000m;
    private const int MinMeaningfulTrades = 50;
    private const int MinMeaningfulProfitLock = 20;

    public static RangeExpansionV23ExtendedDiagnostics Build(
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades,
        decimal currentFeeRatePercent,
        decimal currentSpreadPercent)
    {
        var filterImpact = BuildFilterImpact(candidates, trades);
        var windowRobustness = BuildWindowRobustness(candidates, trades);
        var costSensitivity = BuildCostSensitivity(trades, currentFeeRatePercent, currentSpreadPercent);
        var researchAnswers = BuildResearchAnswers(filterImpact, windowRobustness, costSensitivity);

        return new RangeExpansionV23ExtendedDiagnostics(filterImpact, windowRobustness, costSensitivity, researchAnswers);
    }

    public static IReadOnlyList<RangeExpansionV23FilterImpactRow> BuildFilterImpact(
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades)
    {
        var baseline = SummarizeProfile(trades, BaselineVariant);
        var failedRef = SummarizeProfile(trades, FailedBreakoutRefVariant);
        var failedRefNet = failedRef?.NetPnl ?? FailedBreakoutRefNetReference;

        var profileNames = candidates.Select(c => c.ProfileName)
            .Concat(trades.Select(t => t.ProfileName))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return profileNames
            .Select(profileName =>
            {
                var profileCandidates = candidates.Where(c =>
                    string.Equals(c.ProfileName, profileName, StringComparison.OrdinalIgnoreCase)).ToArray();
                var profileTrades = trades.Where(t =>
                    string.Equals(t.ProfileName, profileName, StringComparison.OrdinalIgnoreCase)).ToArray();
                var variant = ExtractVariantLabel(profileName);
                var summary = SummarizeTrades(profileTrades);

                return new RangeExpansionV23FilterImpactRow
                {
                    VariantLabel = variant,
                    ProfileName = profileName,
                    SweepGroup = ClassifySweepGroup(variant),
                    CandidateCount = profileCandidates.Length,
                    TradeCount = summary.TradeCount,
                    NetWinnerCount = summary.NetWinnerCount,
                    NetPnlQuote = summary.NetPnl,
                    NetPerTrade = summary.NetPerTrade,
                    ProfitLockCount = summary.ProfitLockCount,
                    ProfitLockNetPnlQuote = summary.ProfitLockNet,
                    StopLossCount = summary.StopLossCount,
                    StopLossNetPnlQuote = summary.StopLossNet,
                    TimeStopCount = summary.TimeStopCount,
                    TimeStopNetPnlQuote = summary.TimeStopNet,
                    HalfLockBreakevenCount = summary.HalfLockCount,
                    HalfLockBreakevenNetPnlQuote = summary.HalfLockNet,
                    ProfitLockPreservationRate = baseline?.ProfitLockCount > 0
                        ? Math.Round(summary.ProfitLockCount * 100m / baseline!.ProfitLockCount, 2)
                        : 0m,
                    StopLossReductionRate = baseline?.StopLossCount > 0
                        ? Math.Round((baseline.StopLossCount - summary.StopLossCount) * 100m / baseline.StopLossCount, 2)
                        : 0m,
                    TimeStopReductionRate = baseline?.TimeStopCount > 0
                        ? Math.Round((baseline.TimeStopCount - summary.TimeStopCount) * 100m / baseline.TimeStopCount, 2)
                        : 0m,
                    DeltaVsFailedBreakoutRef = Math.Round(summary.NetPnl - failedRefNet, 8),
                    OverfitWarning = summary.TradeCount < MinMeaningfulTrades || summary.ProfitLockCount < MinMeaningfulProfitLock,
                    MeaningfulSample = summary.TradeCount >= MinMeaningfulTrades && summary.ProfitLockCount >= MinMeaningfulProfitLock
                };
            })
            .OrderBy(r => r.SweepGroup, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.VariantLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<RangeExpansionV23WindowRobustnessRow> BuildWindowRobustness(
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
                var profileTrades = g.Select(x => x.Trade).ToArray();
                return new RangeExpansionV23WindowRobustnessRow
                {
                    VariantLabel = ExtractVariantLabel(first.Trade.ProfileName),
                    ProfileName = first.Trade.ProfileName,
                    WindowLabel = first.Window,
                    TradeCount = profileTrades.Length,
                    ProfitLockCount = CountExit(profileTrades, "ProfitLock"),
                    NetPnlQuote = profileTrades.Sum(t => t.NetPnlQuote),
                    NetPerTrade = profileTrades.Length == 0 ? 0m : Math.Round(profileTrades.Sum(t => t.NetPnlQuote) / profileTrades.Length, 8)
                };
            })
            .OrderBy(r => r.VariantLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.WindowLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<RangeExpansionV23CostSensitivityRow> BuildCostSensitivity(
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
                    return new RangeExpansionV23CostSensitivityRow
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
        IReadOnlyList<RangeExpansionV23FilterImpactRow> filterImpact,
        IReadOnlyList<RangeExpansionV23WindowRobustnessRow> windowRobustness,
        IReadOnlyList<RangeExpansionV23CostSensitivityRow> costSensitivity)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var failedRef = filterImpact.FirstOrDefault(r => r.VariantLabel == FailedBreakoutRefVariant);
        var meaningful = filterImpact.Where(r => r.MeaningfulSample && !r.OverfitWarning).ToArray();
        var bestMeaningful = meaningful.MaxBy(r => r.NetPnlQuote);

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which failed-breakout threshold improves net most while preserving at least 50 trades?",
            Answer = bestMeaningful is null
                ? "No profile met meaningful sample thresholds (>=50 trades and >=20 ProfitLock exits)."
                : $"Best={bestMeaningful.VariantLabel} net={bestMeaningful.NetPnlQuote:F8}, trades={bestMeaningful.TradeCount}, PL={bestMeaningful.ProfitLockCount}, deltaVsRef={bestMeaningful.DeltaVsFailedBreakoutRef:F8}.",
            Verdict = bestMeaningful?.DeltaVsFailedBreakoutRef > 0m ? "ThresholdImprovesRef" : "NoThresholdBeatsRef",
            Details = new Dictionary<string, object?> { ["top5"] = meaningful.OrderByDescending(r => r.NetPnlQuote).Take(5).ToArray() }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Can any V2.3 profile get near breakeven with meaningful sample size?",
            Answer = bestMeaningful?.NetPnlQuote >= -1m
                ? $"{bestMeaningful.VariantLabel} net={bestMeaningful.NetPnlQuote:F8} with {bestMeaningful.TradeCount} trades."
                : bestMeaningful is null
                    ? "No meaningful sample profile."
                    : $"Best meaningful {bestMeaningful.VariantLabel} net={bestMeaningful.NetPnlQuote:F8}; still clearly negative.",
            Verdict = bestMeaningful?.NetPnlQuote >= 0m ? "NearPositive" : bestMeaningful?.NetPnlQuote >= -1m ? "NearBreakeven" : "StillNegative",
            Details = new Dictionary<string, object?> { ["best"] = bestMeaningful }
        });

        var profilesWithWindows = windowRobustness
            .GroupBy(r => r.ProfileName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Profile = g.Key,
                Variant = g.First().VariantLabel,
                Windows = g.OrderBy(x => x.WindowLabel).ToArray(),
                ImprovesAll = g.All(w => w.NetPnlQuote > (failedRef?.NetPnlQuote ?? FailedBreakoutRefNetReference) / 3m),
                CollapsesAny = g.Any(w => w.NetPnlQuote < (failedRef?.NetPnlQuote ?? FailedBreakoutRefNetReference))
            })
            .Where(x => x.Windows.Length >= 2)
            .ToArray();

        var robustProfiles = meaningful
            .Where(r => profilesWithWindows.Any(p =>
                string.Equals(p.Variant, r.VariantLabel, StringComparison.OrdinalIgnoreCase)
                && p.Windows.All(w => w.TradeCount >= 10)))
            .ToArray();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does improvement hold across 30d/60d/90d or only one window?",
            Answer = robustProfiles.Length == 0
                ? "No meaningful profile shows stable cross-window improvement."
                : string.Join("; ", robustProfiles.Take(5).Select(r =>
                {
                    var windows = profilesWithWindows.First(p => p.Variant == r.VariantLabel).Windows;
                    return $"{r.VariantLabel}: {string.Join(",", windows.Select(w => $"{w.WindowLabel}={w.NetPnlQuote:F4}"))}";
                })),
            Verdict = robustProfiles.Any(r => profilesWithWindows.First(p => p.Variant == r.VariantLabel).Windows.All(w => w.NetPnlQuote > r.NetPnlQuote / 2m))
                ? "CrossWindowStable"
                : "SingleWindowFragile",
            Details = new Dictionary<string, object?> { ["profilesWithWindows"] = profilesWithWindows.Take(10).ToArray() }
        });

        var refRow = bestMeaningful ?? failedRef;
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is the remaining loss mostly StopLoss, TimeStop, or HalfLockBreakeven?",
            Answer = refRow is null
                ? "No reference row."
                : $"StopLoss={refRow.StopLossNetPnlQuote:F8} ({refRow.StopLossCount}), TimeStop={refRow.TimeStopNetPnlQuote:F8} ({refRow.TimeStopCount}), HalfLock={refRow.HalfLockBreakevenNetPnlQuote:F8} ({refRow.HalfLockBreakevenCount}), ProfitLock={refRow.ProfitLockNetPnlQuote:F8}.",
            Verdict = Math.Abs(refRow.StopLossNetPnlQuote) >= Math.Abs(refRow.TimeStopNetPnlQuote)
                ? "StopLossDominatesDrag"
                : "TimeStopDominatesDrag",
            Details = new Dictionary<string, object?> { ["row"] = refRow }
        });

        var currentCost = costSensitivity.Where(r => r.CostScenario == "current").ToArray();
        var optimistic = costSensitivity.Where(r => r.CostScenario == RangeExpansionCostSensitivity.Optimistic.Label).ToArray();
        var bestCurrent = currentCost.MaxBy(r => r.NetPnlQuote);
        var bestOptimistic = optimistic.MaxBy(r => r.NetPnlQuote);

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does lower-cost sensitivity make this strategy viable, or is edge still too weak?",
            Answer = bestCurrent is null || bestOptimistic is null
                ? "No cost sensitivity rows."
                : $"Best current-cost {bestCurrent.VariantLabel} net={bestCurrent.NetPnlQuote:F8}; optimistic fee/spread net={bestOptimistic.NetPnlQuote:F8}.",
            Verdict = bestOptimistic.NetPnlQuote >= 0m ? "CostsAreMainBlocker" : "EdgeStillTooWeak",
            Details = new Dictionary<string, object?> { ["bestCurrent"] = bestCurrent, ["bestOptimistic"] = bestOptimistic }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Should RangeExpansion V2 continue, or be marked promising-but-not-tradeable?",
            Answer = bestMeaningful?.NetPnlQuote >= 0m
                ? "V2.3 found a meaningful near-breakeven profile under current costs; continue narrow research."
                : bestMeaningful?.NetPnlQuote > (failedRef?.NetPnlQuote ?? FailedBreakoutRefNetReference)
                    ? "V2.3 improved failed-breakout filter but remains clearly negative under conservative Spot costs."
                    : "V2.3 did not beat failed-breakout reference meaningfully.",
            Verdict = bestMeaningful?.NetPnlQuote >= 0m ? "ContinueResearch" : "PromisingButNotTradeable",
            Details = new Dictionary<string, object?> { ["failedRef"] = failedRef, ["bestMeaningful"] = bestMeaningful }
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
        int StopLossCount,
        decimal StopLossNet,
        int TimeStopCount,
        decimal TimeStopNet,
        int HalfLockCount,
        decimal HalfLockNet);

    private static ProfileTradeSummary? SummarizeProfile(IReadOnlyList<SimulatedTrade> trades, string variantLabel)
    {
        var profileTrades = trades.Where(t =>
            ExtractVariantLabel(t.ProfileName).Equals(variantLabel, StringComparison.OrdinalIgnoreCase)).ToList();
        return profileTrades.Count == 0 ? null : SummarizeTrades(profileTrades);
    }

    private static ProfileTradeSummary SummarizeTrades(IReadOnlyList<SimulatedTrade> profileTrades)
    {
        var net = profileTrades.Sum(t => t.NetPnlQuote);
        return new ProfileTradeSummary(
            profileTrades.Count,
            profileTrades.Count(t => t.NetPnlQuote > 0m),
            net,
            profileTrades.Count == 0 ? 0m : Math.Round(net / profileTrades.Count, 8),
            CountExit(profileTrades, "ProfitLock"),
            SumExit(profileTrades, "ProfitLock"),
            CountExit(profileTrades, "StopLoss"),
            SumExit(profileTrades, "StopLoss"),
            CountExit(profileTrades, "TimeStop"),
            SumExit(profileTrades, "TimeStop"),
            CountExit(profileTrades, "HalfLockBreakeven"),
            SumExit(profileTrades, "HalfLockBreakeven"));
    }

    private static string ClassifySweepGroup(string variantLabel)
    {
        if (variantLabel == BaselineVariant)
            return "reference";
        if (variantLabel == FailedBreakoutRefVariant)
            return "reference";
        if (variantLabel.StartsWith("sweep-giveback-", StringComparison.OrdinalIgnoreCase))
            return "giveback-sweep";
        if (variantLabel.StartsWith("sweep-follow-", StringComparison.OrdinalIgnoreCase))
            return "follow-sweep";
        if (variantLabel.StartsWith("sweep-body-", StringComparison.OrdinalIgnoreCase))
            return "body-sweep";
        if (variantLabel.StartsWith("combo-", StringComparison.OrdinalIgnoreCase))
            return "combined";
        return "other";
    }

    private static string ExtractVariantLabel(string profileName)
    {
        const string prefix = "range-expansion-v23-bnb-";
        if (profileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return profileName[prefix.Length..].Replace("-1m-profitlock-90", "", StringComparison.OrdinalIgnoreCase);
        return profileName;
    }

    private static string TradeKey(string profileName, TradingSymbol symbol, DateTime timeUtc)
        => $"{profileName}|{symbol}|{timeUtc:O}";

    private static int CountExit(IReadOnlyList<SimulatedTrade> trades, string exitReason)
        => trades.Count(t => string.Equals(t.ExitReason, exitReason, StringComparison.OrdinalIgnoreCase));

    private static decimal SumExit(IReadOnlyList<SimulatedTrade> trades, string exitReason)
        => trades.Where(t => string.Equals(t.ExitReason, exitReason, StringComparison.OrdinalIgnoreCase)).Sum(t => t.NetPnlQuote);
}
