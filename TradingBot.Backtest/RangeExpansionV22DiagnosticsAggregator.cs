using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed record RangeExpansionV22FastSummaryRow
{
    public string VariantLabel { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public int CandidateCount { get; init; }
    public int TradeCount { get; init; }
    public int NetWinnerCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal ProfitLockNetPnlQuote { get; init; }
    public decimal StopLossNetPnlQuote { get; init; }
    public decimal TimeStopNetPnlQuote { get; init; }
    public decimal HalfLockBreakevenNetPnlQuote { get; init; }
    public decimal FeeAwareBreakevenNetPnlQuote { get; init; }
    public int ProfitLockCount { get; init; }
    public int StopLossCount { get; init; }
    public int TimeStopCount { get; init; }
    public int TimeStopGrossPositiveNetNegativeCount { get; init; }
    public int TimeStopGrossNegativeCount { get; init; }
    public decimal NetPerTrade { get; init; }
    public decimal DeltaVsBaselineHalfLock { get; init; }
}

public sealed record RangeExpansionV22FilterImpactRow
{
    public string ProfileName { get; init; } = string.Empty;
    public string VariantLabel { get; init; } = string.Empty;
    public int CandidateCount { get; init; }
    public int ExecutedCount { get; init; }
    public int BlockedCount { get; init; }
    public int InflationProxyBlocked { get; init; }
    public int StopToLockBlocked { get; init; }
    public int BreakoutQualityBlocked { get; init; }
    public int FailedBreakoutBlocked { get; init; }
    public int ProfitLockCount { get; init; }
    public int StopLossCount { get; init; }
    public decimal ProfitLockRetentionPercent { get; init; }
    public decimal StopLossReductionPercent { get; init; }
    public decimal NetPnlQuote { get; init; }
}

public sealed record RangeExpansionV22ExitBreakdownRow
{
    public string ProfileName { get; init; } = string.Empty;
    public string VariantLabel { get; init; } = string.Empty;
    public string ExitBucket { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal GrossPnlQuote { get; init; }
}

public sealed record RangeExpansionV22WinnerLoserComparisonRow
{
    public string Bucket { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal? MedianStopToLockRatio { get; init; }
    public decimal? MedianRealizedMoveProxyPercent { get; init; }
    public decimal InflationAtEntryRatePercent { get; init; }
    public decimal? MedianGivebackAtEntryPercent { get; init; }
    public decimal? MedianBreakoutBodyStrengthPercent { get; init; }
    public decimal? MedianBreakoutCloseAboveRangePercent { get; init; }
    public decimal? MedianAtrExpansionRatio { get; init; }
    public decimal? MedianVolumeExpansionRatio { get; init; }
    public decimal? MedianMfePercent { get; init; }
    public decimal? MedianMaePercent { get; init; }
    public decimal? MedianForwardMfe60Percent { get; init; }
    public decimal? MedianForwardMae60Percent { get; init; }
}

public sealed record RangeExpansionV22ExtendedDiagnostics(
    IReadOnlyList<RangeExpansionV22FastSummaryRow> FastSummary,
    IReadOnlyList<RangeExpansionV22FilterImpactRow> FilterImpact,
    IReadOnlyList<RangeExpansionV22ExitBreakdownRow> ExitBreakdown,
    IReadOnlyList<RangeExpansionV22WinnerLoserComparisonRow> WinnerLoserComparison,
    IReadOnlyList<ReachabilityResearchAnswer> ResearchAnswers);

public static class RangeExpansionV22DiagnosticsAggregator
{
    private const string BaselineVariant = "baseline-halflock";
    private const decimal BaselineHalfLockNetReference = -6.385210126301847375000m;

    public static RangeExpansionV22ExtendedDiagnostics Build(
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades)
    {
        var baseline = BuildFastSummary(candidates, trades);
        var baselineRow = baseline.FirstOrDefault(r => r.VariantLabel == BaselineVariant);
        var baselineNet = baselineRow?.NetPnlQuote ?? BaselineHalfLockNetReference;
        var baselinePl = baselineRow?.ProfitLockCount ?? 0;
        var baselineSl = baselineRow?.StopLossCount ?? 0;

        var fastSummary = baseline
            .Select(r => r with { DeltaVsBaselineHalfLock = Math.Round(r.NetPnlQuote - baselineNet, 8) })
            .ToArray();

        return new RangeExpansionV22ExtendedDiagnostics(
            fastSummary,
            BuildFilterImpact(candidates, trades, baselinePl, baselineSl, baselineNet),
            BuildExitBreakdown(trades),
            BuildWinnerLoserComparison(candidates),
            BuildResearchAnswers(fastSummary, candidates, trades, baselineNet, baselinePl, baselineSl));
    }

    public static IReadOnlyList<RangeExpansionV22FastSummaryRow> BuildFastSummary(
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades)
    {
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
                var timeStopTrades = profileTrades.Where(t =>
                    string.Equals(t.ExitReason, "TimeStop", StringComparison.OrdinalIgnoreCase)).ToArray();

                return new RangeExpansionV22FastSummaryRow
                {
                    VariantLabel = variant,
                    ProfileName = profileName,
                    CandidateCount = profileCandidates.Length,
                    TradeCount = profileTrades.Length,
                    NetWinnerCount = profileTrades.Count(t => t.NetPnlQuote > 0m),
                    NetPnlQuote = profileTrades.Sum(t => t.NetPnlQuote),
                    ProfitLockNetPnlQuote = SumByExit(profileTrades, "ProfitLock"),
                    StopLossNetPnlQuote = SumByExit(profileTrades, "StopLoss"),
                    TimeStopNetPnlQuote = SumByExit(profileTrades, "TimeStop"),
                    HalfLockBreakevenNetPnlQuote = SumByExit(profileTrades, "HalfLockBreakeven"),
                    FeeAwareBreakevenNetPnlQuote = SumByExit(profileTrades, "FeeAwareBreakeven"),
                    ProfitLockCount = CountByExit(profileTrades, "ProfitLock"),
                    StopLossCount = CountByExit(profileTrades, "StopLoss"),
                    TimeStopCount = CountByExit(profileTrades, "TimeStop"),
                    TimeStopGrossPositiveNetNegativeCount = timeStopTrades.Count(t => t.GrossPnlQuote > 0m && t.NetPnlQuote <= 0m),
                    TimeStopGrossNegativeCount = timeStopTrades.Count(t => t.GrossPnlQuote <= 0m),
                    NetPerTrade = profileTrades.Length == 0 ? 0m : Math.Round(profileTrades.Sum(t => t.NetPnlQuote) / profileTrades.Length, 8),
                    DeltaVsBaselineHalfLock = 0m
                };
            })
            .OrderBy(r => r.VariantLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<RangeExpansionV22FilterImpactRow> BuildFilterImpact(
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades,
        int baselineProfitLockCount,
        int baselineStopLossCount,
        decimal baselineNet)
    {
        return candidates
            .GroupBy(c => c.ProfileName, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var rows = g.ToArray();
                var profileTrades = trades.Where(t =>
                    string.Equals(t.ProfileName, g.Key, StringComparison.OrdinalIgnoreCase)).ToArray();
                var plCount = profileTrades.Count(t => string.Equals(t.ExitReason, "ProfitLock", StringComparison.OrdinalIgnoreCase));
                var slCount = profileTrades.Count(t => string.Equals(t.ExitReason, "StopLoss", StringComparison.OrdinalIgnoreCase));

                return new RangeExpansionV22FilterImpactRow
                {
                    ProfileName = g.Key,
                    VariantLabel = ExtractVariantLabel(g.Key),
                    CandidateCount = rows.Length,
                    ExecutedCount = rows.Count(c => c.Executed),
                    BlockedCount = rows.Count(c => !c.Executed),
                    InflationProxyBlocked = CountBlocked(rows, RangeExpansionBreakoutV2Model.InflationProxyRejected),
                    StopToLockBlocked = CountBlocked(rows, RangeExpansionBreakoutV2Model.StopToLockRatioExceeded),
                    BreakoutQualityBlocked = CountBlocked(rows, RangeExpansionBreakoutV2Model.BreakoutQualityRejected),
                    FailedBreakoutBlocked = CountBlocked(rows, RangeExpansionBreakoutV2Model.FailedBreakoutWeaknessRejected),
                    ProfitLockCount = plCount,
                    StopLossCount = slCount,
                    ProfitLockRetentionPercent = baselineProfitLockCount == 0 ? 0m : Math.Round(plCount * 100m / baselineProfitLockCount, 2),
                    StopLossReductionPercent = baselineStopLossCount == 0 ? 0m : Math.Round((baselineStopLossCount - slCount) * 100m / baselineStopLossCount, 2),
                    NetPnlQuote = profileTrades.Sum(t => t.NetPnlQuote)
                };
            })
            .OrderBy(r => r.VariantLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<RangeExpansionV22ExitBreakdownRow> BuildExitBreakdown(
        IReadOnlyList<SimulatedTrade> trades)
    {
        return trades
            .GroupBy(t => t.ProfileName, StringComparer.OrdinalIgnoreCase)
            .SelectMany(profileGroup =>
            {
                var variant = ExtractVariantLabel(profileGroup.Key);
                return profileGroup
                    .GroupBy(t => ClassifyExitBucket(t), StringComparer.OrdinalIgnoreCase)
                    .Select(g => new RangeExpansionV22ExitBreakdownRow
                    {
                        ProfileName = profileGroup.Key,
                        VariantLabel = variant,
                        ExitBucket = g.Key,
                        Count = g.Count(),
                        NetPnlQuote = g.Sum(t => t.NetPnlQuote),
                        GrossPnlQuote = g.Sum(t => t.GrossPnlQuote)
                    });
            })
            .OrderBy(r => r.VariantLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ExitBucket, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<RangeExpansionV22WinnerLoserComparisonRow> BuildWinnerLoserComparison(
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates)
    {
        var executed = candidates.Where(c => c.Executed).ToArray();
        var enriched = executed.Select(RangeExpansionV2DiagnosticsAggregator.EnrichFailureTiming).ToArray();
        var buckets = new[]
        {
            ("ProfitLockWinners", enriched.Where(c => c.OutcomeBucket == "ProfitLockWinners").ToArray()),
            ("StopLossLosers", enriched.Where(c => c.OutcomeBucket is "StopLossLosers" or "StopLossEarlyFailures").ToArray()),
            ("TimeStopGrossPositiveNetNegative", enriched.Where(c => c.OutcomeBucket == "TimeStopGrossPositiveNetNegative").ToArray()),
            ("TimeStopGrossNegative", enriched.Where(c => c.OutcomeBucket == "TimeStopGrossNegative").ToArray())
        };

        return buckets
            .Where(b => b.Item2.Length > 0)
            .Select(b => new RangeExpansionV22WinnerLoserComparisonRow
            {
                Bucket = b.Item1,
                Count = b.Item2.Length,
                MedianStopToLockRatio = Median(b.Item2.Select(c => c.StopToLockRatio)),
                MedianRealizedMoveProxyPercent = Median(b.Item2.Select(c => c.RealizedMoveProxyPercent)),
                InflationAtEntryRatePercent = b.Item2.Length == 0 ? 0m : Math.Round(b.Item2.Count(c => c.ExpectedMoveInflatedAtEntry) * 100m / b.Item2.Length, 2),
                MedianGivebackAtEntryPercent = Median(b.Item2.Select(c => c.GivebackAtEntryPercent)),
                MedianBreakoutBodyStrengthPercent = Median(b.Item2.Select(c => c.BreakoutBodyStrengthPercent)),
                MedianBreakoutCloseAboveRangePercent = Median(b.Item2.Select(c => c.BreakoutCloseAboveRangePercent)),
                MedianAtrExpansionRatio = Median(b.Item2.Select(c => (decimal?)c.AtrExpansionRatio)),
                MedianVolumeExpansionRatio = Median(b.Item2.Select(c => (decimal?)c.VolumeExpansionRatio)),
                MedianMfePercent = Median(b.Item2.Select(c => c.MfePercent)),
                MedianMaePercent = Median(b.Item2.Select(c => c.MaePercent)),
                MedianForwardMfe60Percent = Median(b.Item2.Select(c => c.ForwardMfe60Percent)),
                MedianForwardMae60Percent = Median(b.Item2.Select(c => c.ForwardMae60Percent))
            })
            .ToArray();
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildResearchAnswers(
        IReadOnlyList<RangeExpansionV22FastSummaryRow> fastSummary,
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades,
        decimal baselineNet,
        int baselinePl,
        int baselineSl)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var baseline = fastSummary.FirstOrDefault(r => r.VariantLabel == BaselineVariant);
        var comparison = BuildWinnerLoserComparison(candidates);
        var pl = comparison.FirstOrDefault(r => r.Bucket == "ProfitLockWinners");
        var sl = comparison.FirstOrDefault(r => r.Bucket == "StopLossLosers");

        var filterProfiles = fastSummary
            .Where(r => r.VariantLabel.Contains("filter", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.DeltaVsBaselineHalfLock)
            .ToArray();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which filter removes the most StopLoss losers while preserving ProfitLock winners?",
            Answer = filterProfiles.Length == 0
                ? "No filter profiles in run."
                : string.Join("; ", filterProfiles.Select(r =>
                    $"{r.VariantLabel}: net={r.NetPnlQuote:F8}, PL={r.ProfitLockCount}, SL={r.StopLossCount}, delta={r.DeltaVsBaselineHalfLock:F8}")),
            Verdict = filterProfiles.FirstOrDefault()?.StopLossCount < baselineSl && filterProfiles.FirstOrDefault()?.ProfitLockCount >= baselinePl * 0.7m
                ? "FilterPreservesProfitLock"
                : "FilterTradeoff",
            Details = new Dictionary<string, object?> { ["baselinePl"] = baselinePl, ["baselineSl"] = baselineSl, ["filters"] = filterProfiles }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does StopToLockRatio separate winners from StopLoss losers?",
            Answer = pl is null || sl is null
                ? "Insufficient executed bucket data."
                : $"ProfitLock median StopToLock={pl.MedianStopToLockRatio:F4}, StopLoss median={sl.MedianStopToLockRatio:F4}.",
            Verdict = pl.MedianStopToLockRatio.HasValue && sl.MedianStopToLockRatio.HasValue
                && sl.MedianStopToLockRatio.Value - pl.MedianStopToLockRatio.Value >= 0.15m
                ? "StopToLockSeparates"
                : "StopToLockWeakSeparator",
            Details = new Dictionary<string, object?> { ["profitLock"] = pl, ["stopLoss"] = sl }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does inflation proxy reduce the inflated StopLoss bucket?",
            Answer = sl is null
                ? "No StopLoss bucket."
                : $"StopLoss inflation-at-entry rate={sl.InflationAtEntryRatePercent:F2}%, ProfitLock={pl?.InflationAtEntryRatePercent:F2}%.",
            Verdict = sl.InflationAtEntryRatePercent > (pl?.InflationAtEntryRatePercent ?? 0m) + 10m
                ? "InflationProxyTargetsStopLoss"
                : "InflationProxyWeak",
            Details = new Dictionary<string, object?> { ["inflationFilter"] = filterProfiles.FirstOrDefault(r => r.VariantLabel == "inflation-filter-halflock") }
        });

        var best = fastSummary.MaxBy(r => r.NetPnlQuote);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does HalfLockBreakeven + filters improve BNB 1m materially beyond -6.39?",
            Answer = best is null
                ? "No profiles."
                : $"Best={best.VariantLabel} net={best.NetPnlQuote:F8} (delta={best.DeltaVsBaselineHalfLock:F8}), trades={best.TradeCount}, PL={best.ProfitLockCount}.",
            Verdict = best?.DeltaVsBaselineHalfLock >= 0.5m && best.TradeCount >= 80 ? "MaterialImprovement" : "MarginalOrNoImprovement",
            Details = new Dictionary<string, object?> { ["best"] = best, ["baselineNet"] = baselineNet }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Can any V2.2 variant get near breakeven over 30/60/90d?",
            Answer = best?.NetPnlQuote >= -1m
                ? $"Best variant {best.VariantLabel} net={best.NetPnlQuote:F8} is near breakeven."
                : $"Best variant {best?.VariantLabel} net={best?.NetPnlQuote:F8}; still far from breakeven.",
            Verdict = best?.NetPnlQuote >= 0m ? "NearPositive" : best?.NetPnlQuote >= -2m ? "NearBreakeven" : "StillNegative",
            Details = new Dictionary<string, object?> { ["best"] = best }
        });

        var feeVariants = fastSummary.Where(r =>
            r.VariantLabel.Contains("fee", StringComparison.OrdinalIgnoreCase)
            || r.VariantLabel.Contains("breakeven-0.10", StringComparison.OrdinalIgnoreCase)).ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are TimeStop gross-positive/net-negative trades salvageable with fee-aware breakeven?",
            Answer = feeVariants.Length == 0
                ? "No fee-aware variants."
                : string.Join("; ", feeVariants.Select(r =>
                    $"{r.VariantLabel}: net={r.NetPnlQuote:F8}, TS fee-drag={r.TimeStopGrossPositiveNetNegativeCount}, FeeAware={r.FeeAwareBreakevenNetPnlQuote:F8}")),
            Verdict = feeVariants.Any(r => r.DeltaVsBaselineHalfLock > 0m) ? "FeeAwareHelps" : "FeeAwareMixed",
            Details = new Dictionary<string, object?> { ["baselineTsFeeDrag"] = baseline?.TimeStopGrossPositiveNetNegativeCount, ["variants"] = feeVariants }
        });

        var meaningful = fastSummary.Where(r => r.TradeCount >= 80).ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is RangeExpansionV2 tradeable under current Spot fee assumptions after V2.2?",
            Answer = meaningful.Length == 0
                ? "No meaningful sample sizes."
                : $"Best meaningful variant {meaningful.MaxBy(r => r.NetPnlQuote)?.VariantLabel} net={meaningful.MaxBy(r => r.NetPnlQuote)?.NetPnlQuote:F8}.",
            Verdict = meaningful.Any(r => r.NetPnlQuote >= 0m) ? "PotentiallyTradeable" : "PromisingButNotTradeable",
            Details = new Dictionary<string, object?> { ["meaningfulVariants"] = meaningful }
        });

        return answers;
    }

    private static string ClassifyExitBucket(SimulatedTrade trade)
    {
        if (string.Equals(trade.ExitReason, "TimeStop", StringComparison.OrdinalIgnoreCase))
        {
            if (trade.GrossPnlQuote > 0m && trade.NetPnlQuote <= 0m)
                return "TimeStopGrossPositiveNetNegative";
            if (trade.GrossPnlQuote <= 0m)
                return "TimeStopGrossNegative";
        }

        return trade.ExitReason ?? "Unknown";
    }

    private static int CountBlocked(IReadOnlyList<RangeExpansionV2CandidateRecord> rows, string reasonPrefix)
        => rows.Count(c => !c.Executed && c.RejectionReason?.Contains(reasonPrefix, StringComparison.OrdinalIgnoreCase) == true);

    private static decimal SumByExit(IReadOnlyList<SimulatedTrade> trades, string exitReason)
        => trades.Where(t => string.Equals(t.ExitReason, exitReason, StringComparison.OrdinalIgnoreCase)).Sum(t => t.NetPnlQuote);

    private static int CountByExit(IReadOnlyList<SimulatedTrade> trades, string exitReason)
        => trades.Count(t => string.Equals(t.ExitReason, exitReason, StringComparison.OrdinalIgnoreCase));

    private static string ExtractVariantLabel(string profileName)
    {
        const string prefix = "range-expansion-v22-bnb-";
        if (profileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return profileName[prefix.Length..].Replace("-1m-profitlock-90", "", StringComparison.OrdinalIgnoreCase);
        return profileName;
    }

    private static decimal? Median(IEnumerable<decimal?> values)
    {
        var samples = values.Where(v => v.HasValue).Select(v => v!.Value).OrderBy(v => v).ToArray();
        if (samples.Length == 0)
            return null;
        var mid = samples.Length / 2;
        return samples.Length % 2 == 0
            ? Math.Round((samples[mid - 1] + samples[mid]) / 2m, 6)
            : Math.Round(samples[mid], 6);
    }
}
