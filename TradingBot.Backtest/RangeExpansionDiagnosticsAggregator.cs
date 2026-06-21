using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed record RangeExpansionPnlDecompositionRow
{
    public string Dimension { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string? WindowLabel { get; init; }
    public int TradeCount { get; init; }
    public int CandidateCount { get; init; }
    public int ExecutedCount { get; init; }
    public int BlockedCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal WinRatePercent { get; init; }
    public decimal? AvgNetPnlQuote { get; init; }
}

public sealed record RangeExpansionTradeQualityBucketRow
{
    public string Bucket { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal WinRatePercent { get; init; }
    public decimal? AvgMfePercent { get; init; }
    public decimal? AvgMaePercent { get; init; }
    public decimal? AvgGivebackFromMfePercent { get; init; }
    public decimal? AvgCapturedMfePercent { get; init; }
    public decimal? AvgForwardMfe60Percent { get; init; }
    public decimal? AvgForwardMae60Percent { get; init; }
    public decimal? AvgTimeToLock90Minutes { get; init; }
    public decimal? AvgDurationMinutes { get; init; }
    public int Lock90ReachedBeforeMaeThresholdCount { get; init; }
    public int ProfitLockExitCount { get; init; }
    public int OppositeSignalExitCount { get; init; }
}

public sealed record RangeExpansionWinnerLoserComparisonRow
{
    public string Bucket { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal? AvgRangeWidthPercent { get; init; }
    public decimal? AvgDistanceFromBreakoutPercent { get; init; }
    public decimal? AvgLock90DistancePercent { get; init; }
    public decimal? AvgForwardMfe60Percent { get; init; }
    public decimal? AvgForwardMae60Percent { get; init; }
    public decimal? AvgTimeToLock90Minutes { get; init; }
    public decimal? AvgTrendStrengthPercent { get; init; }
    public decimal? AvgShortMaSlopePercent { get; init; }
    public decimal? AvgCandidateAgeCandles { get; init; }
    public decimal? AvgBreakoutCloseAboveRangePercent { get; init; }
    public decimal? AvgBreakoutBodyStrengthPercent { get; init; }
    public decimal? AvgNetPnlQuote { get; init; }
}

public sealed record RangeExpansionBlockedReachableRow
{
    public string? RejectionReason { get; init; }
    public int BlockedCount { get; init; }
    public int BlockedReachableCount { get; init; }
    public decimal BlockedReachableRate { get; init; }
    public int Lock90ReachableAndNetProfitableCount { get; init; }
    public decimal Lock90ReachableAndNetProfitableRate { get; init; }
    public decimal? MedianLock90NetProfitPercent { get; init; }
    public decimal? MedianForwardMfe60Percent { get; init; }
    public decimal? MedianForwardMae60Percent { get; init; }
    public int CouldRelaxSafelyCount { get; init; }
}

public sealed record RangeExpansionBlockedReachableSummary
{
    public int BlockedReachableCount { get; init; }
    public int Lock90ReachableAndNetProfitableCount { get; init; }
    public IReadOnlyList<RangeExpansionBlockedReachableRow> ByReason { get; init; } = [];
    public decimal? MedianForwardMfe60Percent { get; init; }
    public decimal? MedianForwardMae60Percent { get; init; }
    public decimal? MedianLock90NetProfitPercent { get; init; }
    public int CouldRelaxSafelyCandidateCount { get; init; }
}

public sealed record RangeExpansionExecutedTradeCostRow
{
    public string SymbolIntervalKey { get; init; } = string.Empty;
    public int ExecutedCount { get; init; }
    public int ProfitLockGrossPositiveNetNegativeCount { get; init; }
    public int ExpectedMovePassedLock90BelowRequiredNetCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal? MedianLock90NetProfitPercent { get; init; }
    public decimal? MedianLock90DistancePercent { get; init; }
}

public sealed record RangeExpansionExecutedTradeCostAnalysis
{
    public int ProfitLockGrossPositiveNetNegativeCount { get; init; }
    public int ExpectedMovePassedLock90BelowRequiredNetCount { get; init; }
    public IReadOnlyList<RangeExpansionExecutedTradeCostRow> BySymbolInterval { get; init; } = [];
}

public sealed record RangeExpansionReportingConsistencyReport
{
    public int NullExitReasonCount { get; init; }
    public int UnknownExitReasonCount { get; init; }
    public int OrphanExecutedCandidateCount { get; init; }
    public int ExcludedFromPnlAggregatesCount { get; init; }
    public int ProfitLockExitMissingIsProfitLockFlagCount { get; init; }
    public int ZeroDurationNonSameCandleCount { get; init; }
    public int EndOfDataExitCount { get; init; }
    public decimal EndOfDataNetPnlQuote { get; init; }
    public decimal EndOfDataGrossPnlQuote { get; init; }
    public IReadOnlyList<RangeExpansionEndOfDataLossRow> LargestEndOfDataLosses { get; init; } = [];
}

public sealed record RangeExpansionEndOfDataLossRow
{
    public string ProfileName { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = string.Empty;
    public DateTime TimeUtc { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal DurationMinutes { get; init; }
    public decimal? MaePercent { get; init; }
}

public sealed record RangeExpansionTargetFloorComparisonRow
{
    public string TargetFloorMode { get; init; } = string.Empty;
    public int CandidateCount { get; init; }
    public int ExecutedCount { get; init; }
    public int BlockedCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public int NetWinnerCount { get; init; }
}

public sealed record RangeExpansionDiagnosticsBundle(
    IReadOnlyList<RangeExpansionPnlDecompositionRow> PnlDecomposition,
    IReadOnlyList<RangeExpansionTradeQualityBucketRow> TradeQualityBuckets,
    IReadOnlyList<RangeExpansionWinnerLoserComparisonRow> WinnerLoserComparison,
    RangeExpansionBlockedReachableSummary BlockedReachable,
    RangeExpansionExecutedTradeCostAnalysis ExecutedTradeCostAnalysis,
    RangeExpansionReportingConsistencyReport ReportingConsistency,
    IReadOnlyList<RangeExpansionTargetFloorComparisonRow> TargetFloorComparison,
    IReadOnlyList<ReachabilityResearchAnswer> DiagnosticAnswers,
    bool SeparatorDetected,
    string TradeabilityVerdict,
    RangeExpansionV2Recommendation? V2Recommendation);

public sealed record RangeExpansionV2Recommendation(
    bool ShouldCreateV2Profiles,
    string Rationale,
    IReadOnlyDictionary<string, string> SuggestedConfigOverrides);

public static class RangeExpansionDiagnosticsAggregator
{
    private const decimal MaeSafetyThresholdPercent = -0.20m;
    private const decimal SeparatorMinDeltaPercent = 0.03m;

    public static RangeExpansionDiagnosticsBundle Build(
        IReadOnlyList<RangeExpansionCandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades)
    {
        var executed = EnrichExecutedCandidates(candidates, trades);
        var pnl = BuildPnlDecomposition(executed, trades, candidates);
        var buckets = BuildTradeQualityBuckets(executed);
        var comparison = BuildWinnerLoserComparison(executed);
        var blockedReachable = BuildBlockedReachableSummary(candidates);
        var executedCost = BuildExecutedTradeCostAnalysis(executed);
        var reportingConsistency = BuildReportingConsistencyReport(candidates, trades);
        var targetFloorComparison = BuildTargetFloorComparison(candidates, trades);
        var separator = DetectSeparator(comparison);
        var tradeability = separator ? "FilterableLosses" : "CandidateRichNotTradeableYet";
        var v2 = BuildV2Recommendation(separator, comparison, blockedReachable);
        var answers = BuildDiagnosticAnswers(
            executed,
            trades,
            candidates,
            pnl,
            buckets,
            comparison,
            blockedReachable,
            executedCost,
            targetFloorComparison,
            reportingConsistency,
            separator,
            tradeability,
            v2);

        return new RangeExpansionDiagnosticsBundle(
            pnl,
            buckets,
            comparison,
            blockedReachable,
            executedCost,
            reportingConsistency,
            targetFloorComparison,
            answers,
            separator,
            tradeability,
            v2);
    }

    public static IReadOnlyList<RangeExpansionCandidateRecord> EnrichExecutedCandidates(
        IReadOnlyList<RangeExpansionCandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades)
    {
        var tradeLookup = trades
            .GroupBy(t => $"{t.ProfileName}|{t.Interval}|{t.Symbol}|{t.EntryTimeUtc:O}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return candidates
            .Where(c => c.Executed)
            .Select(c =>
            {
                var key = $"{c.ProfileName}|{c.Interval}|{c.Symbol}|{c.TimeUtc:O}";
                if (!tradeLookup.TryGetValue(key, out var trade))
                    return c with { IsWinner = c.NetPnlQuote > 0m };

                var maeBeforeTarget = c.TimeToLock90Minutes.HasValue
                    ? c.ForwardMae15Percent
                    : c.ForwardMae60Percent;
                var exitReason = string.IsNullOrWhiteSpace(trade.ExitReason) ? "UnknownExit" : trade.ExitReason;
                var excluded = c.ExcludedFromPnlAggregates
                    || string.Equals(exitReason, "UnknownExit", StringComparison.OrdinalIgnoreCase);
                var isProfitLock = string.Equals(exitReason, "ProfitLock", StringComparison.OrdinalIgnoreCase);

                return c with
                {
                    NetPnlQuote = trade.NetPnlQuote,
                    GrossPnlQuote = trade.GrossPnlQuote,
                    FeeAndSpreadEstimateQuote = trade.FeeAndSpreadEstimateQuote,
                    ExitReason = exitReason,
                    ExitPolicyName = trade.ExitPolicyName,
                    ProfitLockThresholdPercent = trade.ProfitLockThresholdPercent,
                    MfePercent = trade.MfePercent,
                    MaePercent = trade.MaePercent,
                    GivebackFromMfePercent = trade.GivebackFromMfePercent,
                    CapturedMfePercent = trade.CapturedMfePercent,
                    DurationMinutes = trade.DurationMinutes,
                    VolatilityRegime = trade.VolatilityRegime ?? c.VolatilityRegime,
                    TrendStrengthPercent = trade.TrendStrengthPercent ?? c.TrendStrengthPercent,
                    ShortMaSlopePercent = trade.ShortMaSlopePercent ?? c.ShortMaSlopePercent,
                    Lock90ReachedBeforeMaeThreshold = c.TimeToLock90Minutes.HasValue
                        && (!c.ForwardMae15Percent.HasValue || c.ForwardMae15Percent.Value >= MaeSafetyThresholdPercent),
                    MaeBeforeTargetPercent = maeBeforeTarget,
                    EntryToMaxFavorableMinutes = c.TimeToLock90Minutes,
                    IsWinner = trade.NetPnlQuote > 0m,
                    IsProfitLockExit = isProfitLock,
                    IsOppositeSignalExit = string.Equals(exitReason, "OppositeSignal", StringComparison.OrdinalIgnoreCase),
                    ExcludedFromPnlAggregates = excluded,
                    ReportingConsistencyFlag = excluded ? "MissingExitReason" : c.ReportingConsistencyFlag
                };
            })
            .ToArray();
    }

    public static IReadOnlyList<RangeExpansionPnlDecompositionRow> BuildPnlDecomposition(
        IReadOnlyList<RangeExpansionCandidateRecord> executedCandidates,
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<RangeExpansionCandidateRecord> allCandidates)
    {
        var rows = new List<RangeExpansionPnlDecompositionRow>();
        rows.AddRange(GroupTradePnl("Symbol", trades, allCandidates, t => t.Symbol.ToString()));
        rows.AddRange(GroupTradePnl("Interval", trades, allCandidates, t => t.Interval));
        rows.AddRange(GroupTradePnl("ProfitLockThreshold", trades, allCandidates, t => (t.ProfitLockThresholdPercent ?? 0m).ToString("0")));
        rows.AddRange(GroupTradePnl("ExitReason", trades, allCandidates, t => t.ExitReason));

        rows.AddRange(GroupCandidatePnl("TargetModelName", trades, executedCandidates, c => c.TargetModelName ?? "Unknown"));
        rows.AddRange(GroupCandidatePnl("RejectionReason", trades, allCandidates.Where(c => !c.Executed).ToArray(), c => c.RejectionReason ?? "Unknown"));
        rows.AddRange(GroupCandidatePnl("VolatilityRegime", trades, executedCandidates, c => c.VolatilityRegime ?? "Unknown"));

        foreach (var window in allCandidates.Select(c => c.WindowLabel).Where(w => !string.IsNullOrWhiteSpace(w)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var windowCandidates = allCandidates.Where(c => string.Equals(c.WindowLabel, window, StringComparison.OrdinalIgnoreCase)).ToArray();
            var windowTrades = MatchTradesToCandidates(trades, windowCandidates.Where(c => c.Executed).ToArray());
            var windowExecuted = executedCandidates.Where(c => string.Equals(c.WindowLabel, window, StringComparison.OrdinalIgnoreCase)).ToArray();

            rows.AddRange(GroupTradePnl("Window", windowTrades, windowCandidates, _ => window, window));
            rows.AddRange(GroupTradePnl("Window+Symbol", windowTrades, windowCandidates, t => $"{window}|{t.Symbol}", window));
            rows.AddRange(GroupTradePnl("Window+Interval", windowTrades, windowCandidates, t => $"{window}|{t.Interval}", window));
            rows.AddRange(GroupTradePnl("Window+ProfitLock", windowTrades, windowCandidates, t => $"{window}|{t.ProfitLockThresholdPercent:0}", window));
            rows.AddRange(GroupCandidatePnl("Window+TargetModel", windowTrades, windowExecuted, c => $"{window}|{c.TargetModelName ?? "Unknown"}", window));
        }

        return rows;
    }

    private static IReadOnlyList<SimulatedTrade> MatchTradesToCandidates(
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<RangeExpansionCandidateRecord> executedCandidates)
    {
        var keys = executedCandidates
            .Where(c => !c.ExcludedFromPnlAggregates)
            .Select(c => $"{c.ProfileName}|{c.Interval}|{c.Symbol}|{c.TimeUtc:O}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return trades.Where(t => keys.Contains($"{t.ProfileName}|{t.Interval}|{t.Symbol}|{t.EntryTimeUtc:O}")).ToArray();
    }

    private static IEnumerable<RangeExpansionPnlDecompositionRow> GroupTradePnl(
        string dimension,
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<RangeExpansionCandidateRecord> candidates,
        Func<SimulatedTrade, string> keySelector,
        string? windowLabel = null)
    {
        return trades
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var groupCandidates = candidates.Where(c =>
                    g.Any(t => string.Equals(c.ProfileName, t.ProfileName, StringComparison.OrdinalIgnoreCase)
                               && string.Equals(c.Interval, t.Interval, StringComparison.OrdinalIgnoreCase)
                               && c.Symbol == t.Symbol
                               && c.TimeUtc == t.EntryTimeUtc)).ToArray();
                return BuildPnlRow(dimension, g.Key, windowLabel, g.ToArray(), groupCandidates);
            });
    }

    private static IEnumerable<RangeExpansionPnlDecompositionRow> GroupCandidatePnl(
        string dimension,
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<RangeExpansionCandidateRecord> candidates,
        Func<RangeExpansionCandidateRecord, string> keySelector,
        string? windowLabel = null)
    {
        return candidates
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var executed = g.Where(c => c.Executed).ToArray();
                var groupTrades = MatchTradesToCandidates(trades, executed);
                return BuildPnlRow(dimension, g.Key, windowLabel, groupTrades, g.ToArray());
            });
    }

    private static RangeExpansionPnlDecompositionRow BuildPnlRow(
        string dimension,
        string key,
        string? windowLabel,
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<RangeExpansionCandidateRecord> candidates)
    {
        var wins = trades.Count(t => t.NetPnlQuote > 0m);
        var includedCandidates = candidates.Where(c => !c.ExcludedFromPnlAggregates).ToArray();
        return new RangeExpansionPnlDecompositionRow
        {
            Dimension = dimension,
            Key = key,
            WindowLabel = windowLabel,
            TradeCount = trades.Count,
            CandidateCount = includedCandidates.Length,
            ExecutedCount = includedCandidates.Count(c => c.Executed),
            BlockedCount = includedCandidates.Count(c => !c.Executed),
            NetPnlQuote = trades.Sum(t => t.NetPnlQuote),
            GrossPnlQuote = trades.Sum(t => t.GrossPnlQuote),
            WinRatePercent = trades.Count == 0 ? 0m : Math.Round(wins * 100m / trades.Count, 2),
            AvgNetPnlQuote = trades.Count == 0 ? null : Math.Round(trades.Average(t => t.NetPnlQuote), 8)
        };
    }

    public static IReadOnlyList<RangeExpansionTradeQualityBucketRow> BuildTradeQualityBuckets(
        IReadOnlyList<RangeExpansionCandidateRecord> executed)
    {
        var buckets = new List<RangeExpansionTradeQualityBucketRow>
        {
            BuildQualityBucket("Winners", executed.Where(c => c.IsWinner == true).ToArray()),
            BuildQualityBucket("Losers", executed.Where(c => c.IsWinner == false).ToArray()),
            BuildQualityBucket("ProfitLockExit", executed.Where(c => c.IsProfitLockExit).ToArray()),
            BuildQualityBucket("EndOfDataExit", executed.Where(c => string.Equals(c.ExitReason, "EndOfData", StringComparison.OrdinalIgnoreCase)).ToArray()),
            BuildQualityBucket("BreakevenExit", executed.Where(c => string.Equals(c.ExitReason, "Breakeven", StringComparison.OrdinalIgnoreCase)).ToArray()),
            BuildQualityBucket("TrailingStopExit", executed.Where(c => string.Equals(c.ExitReason, "TrailingStop", StringComparison.OrdinalIgnoreCase)).ToArray()),
            BuildQualityBucket("Lock90ReachedBeforeMaeThreshold", executed.Where(c => c.Lock90ReachedBeforeMaeThreshold).ToArray()),
            BuildQualityBucket("HighMaeBeforeTarget", executed.Where(c => c.MaeBeforeTargetPercent is < -0.20m).ToArray()),
            BuildQualityBucket("LateEntry", executed.Where(c => c.DistanceFromBreakoutPercent is > 0.10m).ToArray()),
            BuildQualityBucket("FalseBreakoutProxy", executed.Where(c => c is { ForwardMfe60Percent: < 0.05m, ForwardMae60Percent: < -0.15m }).ToArray())
        };

        return buckets.Where(b => b.Count > 0).ToArray();
    }

    private static RangeExpansionTradeQualityBucketRow BuildQualityBucket(
        string bucket,
        IReadOnlyList<RangeExpansionCandidateRecord> rows)
    {
        var wins = rows.Count(r => r.IsWinner == true);
        return new RangeExpansionTradeQualityBucketRow
        {
            Bucket = bucket,
            Count = rows.Count,
            NetPnlQuote = rows.Sum(r => r.NetPnlQuote ?? 0m),
            WinRatePercent = rows.Count == 0 ? 0m : Math.Round(wins * 100m / rows.Count, 2),
            AvgMfePercent = Average(rows.Select(r => r.MfePercent)),
            AvgMaePercent = Average(rows.Select(r => r.MaePercent)),
            AvgGivebackFromMfePercent = Average(rows.Select(r => r.GivebackFromMfePercent)),
            AvgCapturedMfePercent = Average(rows.Select(r => r.CapturedMfePercent)),
            AvgForwardMfe60Percent = Average(rows.Select(r => r.ForwardMfe60Percent)),
            AvgForwardMae60Percent = Average(rows.Select(r => r.ForwardMae60Percent)),
            AvgTimeToLock90Minutes = Average(rows.Select(r => r.TimeToLock90Minutes.HasValue ? (decimal?)r.TimeToLock90Minutes.Value : null)),
            AvgDurationMinutes = Average(rows.Select(r => (decimal?)r.DurationMinutes)),
            Lock90ReachedBeforeMaeThresholdCount = rows.Count(r => r.Lock90ReachedBeforeMaeThreshold),
            ProfitLockExitCount = rows.Count(r => r.IsProfitLockExit),
            OppositeSignalExitCount = rows.Count(r => r.IsOppositeSignalExit)
        };
    }

    public static IReadOnlyList<RangeExpansionWinnerLoserComparisonRow> BuildWinnerLoserComparison(
        IReadOnlyList<RangeExpansionCandidateRecord> executed)
    {
        return
        [
            BuildComparisonRow("Winners", executed.Where(c => c.IsWinner == true).ToArray()),
            BuildComparisonRow("Losers", executed.Where(c => c.IsWinner == false).ToArray())
        ];
    }

    private static RangeExpansionWinnerLoserComparisonRow BuildComparisonRow(
        string bucket,
        IReadOnlyList<RangeExpansionCandidateRecord> rows)
        => new()
        {
            Bucket = bucket,
            Count = rows.Count,
            AvgRangeWidthPercent = Average(rows.Select(r => (decimal?)r.RangeWidthPercent)),
            AvgDistanceFromBreakoutPercent = Average(rows.Select(r => r.DistanceFromBreakoutPercent)),
            AvgLock90DistancePercent = Average(rows.Select(r => r.Lock90DistancePercent)),
            AvgForwardMfe60Percent = Average(rows.Select(r => r.ForwardMfe60Percent)),
            AvgForwardMae60Percent = Average(rows.Select(r => r.ForwardMae60Percent)),
            AvgTimeToLock90Minutes = Average(rows.Select(r => r.TimeToLock90Minutes.HasValue ? (decimal?)r.TimeToLock90Minutes.Value : null)),
            AvgTrendStrengthPercent = Average(rows.Select(r => r.TrendStrengthPercent)),
            AvgShortMaSlopePercent = Average(rows.Select(r => r.ShortMaSlopePercent)),
            AvgCandidateAgeCandles = Average(rows.Select(r => (decimal?)r.CandidateAgeCandles)),
            AvgBreakoutCloseAboveRangePercent = Average(rows.Select(r => r.BreakoutCloseAboveRangePercent)),
            AvgBreakoutBodyStrengthPercent = Average(rows.Select(r => r.BreakoutBodyStrengthPercent)),
            AvgNetPnlQuote = Average(rows.Select(r => r.NetPnlQuote))
        };

    public static RangeExpansionBlockedReachableSummary BuildBlockedReachableSummary(
        IReadOnlyList<RangeExpansionCandidateRecord> candidates)
    {
        var blocked = candidates.Where(c => !c.Executed).ToArray();
        var blockedReachable = blocked.Where(c => c.Lock90ReachableWithin60m).ToArray();
        var blockedReachableNetProfitable = blocked.Where(c => c.Lock90ReachableAndNetProfitableWithin60m).ToArray();
        var byReason = blocked
            .GroupBy(c => NormalizeTargetTooSmallReason(c.RejectionReason), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var reachable = g.Where(c => c.Lock90ReachableWithin60m).ToArray();
                var reachableNetProfitable = g.Where(c => c.Lock90ReachableAndNetProfitableWithin60m).ToArray();
                var safeRelax = reachableNetProfitable.Count(c =>
                    c.ForwardMae60Percent is null or >= MaeSafetyThresholdPercent
                    && c is { ExpectedMoveInflated: false });
                return new RangeExpansionBlockedReachableRow
                {
                    RejectionReason = g.Key,
                    BlockedCount = g.Count(),
                    BlockedReachableCount = reachable.Length,
                    BlockedReachableRate = g.Count() == 0 ? 0m : Math.Round(reachable.Length * 100m / g.Count(), 2),
                    Lock90ReachableAndNetProfitableCount = reachableNetProfitable.Length,
                    Lock90ReachableAndNetProfitableRate = g.Count() == 0 ? 0m : Math.Round(reachableNetProfitable.Length * 100m / g.Count(), 2),
                    MedianLock90NetProfitPercent = Median(reachable.Select(c => c.Lock90NetProfitPercent)),
                    MedianForwardMfe60Percent = Median(reachable.Select(c => c.ForwardMfe60Percent)),
                    MedianForwardMae60Percent = Median(reachable.Select(c => c.ForwardMae60Percent)),
                    CouldRelaxSafelyCount = safeRelax
                };
            })
            .OrderByDescending(r => r.BlockedReachableCount)
            .ToArray();

        return new RangeExpansionBlockedReachableSummary
        {
            BlockedReachableCount = blockedReachable.Length,
            Lock90ReachableAndNetProfitableCount = blockedReachableNetProfitable.Length,
            ByReason = byReason,
            MedianForwardMfe60Percent = Median(blockedReachable.Select(c => c.ForwardMfe60Percent)),
            MedianForwardMae60Percent = Median(blockedReachable.Select(c => c.ForwardMae60Percent)),
            MedianLock90NetProfitPercent = Median(blockedReachable.Select(c => c.Lock90NetProfitPercent)),
            CouldRelaxSafelyCandidateCount = byReason.Sum(r => r.CouldRelaxSafelyCount)
        };
    }

    public static RangeExpansionExecutedTradeCostAnalysis BuildExecutedTradeCostAnalysis(
        IReadOnlyList<RangeExpansionCandidateRecord> executed)
    {
        var included = executed.Where(c => !c.ExcludedFromPnlAggregates).ToArray();
        var profitLockGrossPositiveNetNegative = included.Count(c =>
            c.IsProfitLockExit
            && c.GrossPnlQuote is > 0m
            && c.NetPnlQuote is <= 0m);
        var expectedMovePassedLockBelowNet = included.Count(c =>
            c.ExpectedMovePercent is >= 0.10m
            && c.Lock90NetProfitPercent.HasValue
            && c.RequiredNetProfitPercent.HasValue
            && c.Lock90NetProfitPercent.Value < c.RequiredNetProfitPercent.Value);

        var bySymbolInterval = included
            .GroupBy(c => $"{c.Symbol}|{c.Interval}", StringComparer.OrdinalIgnoreCase)
            .Select(g => new RangeExpansionExecutedTradeCostRow
            {
                SymbolIntervalKey = g.Key,
                ExecutedCount = g.Count(),
                ProfitLockGrossPositiveNetNegativeCount = g.Count(c =>
                    c.IsProfitLockExit && c.GrossPnlQuote is > 0m && c.NetPnlQuote is <= 0m),
                ExpectedMovePassedLock90BelowRequiredNetCount = g.Count(c =>
                    c.ExpectedMovePercent is >= 0.10m
                    && c.Lock90NetProfitPercent.HasValue
                    && c.RequiredNetProfitPercent.HasValue
                    && c.Lock90NetProfitPercent.Value < c.RequiredNetProfitPercent.Value),
                NetPnlQuote = g.Sum(c => c.NetPnlQuote ?? 0m),
                MedianLock90NetProfitPercent = Median(g.Select(c => c.Lock90NetProfitPercent)),
                MedianLock90DistancePercent = Median(g.Select(c => c.Lock90DistancePercent))
            })
            .OrderBy(r => r.SymbolIntervalKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new RangeExpansionExecutedTradeCostAnalysis
        {
            ProfitLockGrossPositiveNetNegativeCount = profitLockGrossPositiveNetNegative,
            ExpectedMovePassedLock90BelowRequiredNetCount = expectedMovePassedLockBelowNet,
            BySymbolInterval = bySymbolInterval
        };
    }

    public static RangeExpansionReportingConsistencyReport BuildReportingConsistencyReport(
        IReadOnlyList<RangeExpansionCandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades)
    {
        var executed = candidates.Where(c => c.Executed).ToArray();
        var tradeLookup = trades
            .GroupBy(t => $"{t.ProfileName}|{t.Interval}|{t.Symbol}|{t.EntryTimeUtc:O}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var nullExitReason = executed.Count(c => string.IsNullOrWhiteSpace(c.ExitReason));
        var unknownExitReason = executed.Count(c =>
            string.Equals(c.ExitReason, "UnknownExit", StringComparison.OrdinalIgnoreCase));
        var orphanCount = executed.Count(c => c.OrphanExecutedCandidate);
        var excludedCount = executed.Count(c => c.ExcludedFromPnlAggregates);
        var profitLockFlagMismatch = executed.Count(c =>
            string.Equals(c.ExitReason, "ProfitLock", StringComparison.OrdinalIgnoreCase) && !c.IsProfitLockExit);
        var zeroDuration = executed.Count(c =>
        {
            var key = $"{c.ProfileName}|{c.Interval}|{c.Symbol}|{c.TimeUtc:O}";
            if (!tradeLookup.TryGetValue(key, out var trade))
                return false;
            return trade.DurationMinutes <= 0m && trade.ExitTimeUtc > trade.EntryTimeUtc;
        });

        var endOfData = executed
            .Where(c => string.Equals(c.ExitReason, "EndOfData", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var largestEndOfData = endOfData
            .OrderBy(c => c.NetPnlQuote ?? 0m)
            .Take(10)
            .Select(c => new RangeExpansionEndOfDataLossRow
            {
                ProfileName = c.ProfileName,
                Symbol = c.Symbol,
                Interval = c.Interval,
                TimeUtc = c.TimeUtc,
                NetPnlQuote = c.NetPnlQuote ?? 0m,
                GrossPnlQuote = c.GrossPnlQuote ?? 0m,
                DurationMinutes = c.DurationMinutes,
                MaePercent = c.MaePercent
            })
            .ToArray();

        return new RangeExpansionReportingConsistencyReport
        {
            NullExitReasonCount = nullExitReason,
            UnknownExitReasonCount = unknownExitReason,
            OrphanExecutedCandidateCount = orphanCount,
            ExcludedFromPnlAggregatesCount = excludedCount,
            ProfitLockExitMissingIsProfitLockFlagCount = profitLockFlagMismatch,
            ZeroDurationNonSameCandleCount = zeroDuration,
            EndOfDataExitCount = endOfData.Length,
            EndOfDataNetPnlQuote = endOfData.Sum(c => c.NetPnlQuote ?? 0m),
            EndOfDataGrossPnlQuote = endOfData.Sum(c => c.GrossPnlQuote ?? 0m),
            LargestEndOfDataLosses = largestEndOfData
        };
    }

    public static IReadOnlyList<RangeExpansionTargetFloorComparisonRow> BuildTargetFloorComparison(
        IReadOnlyList<RangeExpansionCandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades)
    {
        var modes = new[] { "current", "relaxed", "costaware" };
        var rows = new List<RangeExpansionTargetFloorComparisonRow>();
        foreach (var mode in modes)
        {
            var modeCandidates = candidates
                .Where(c => c.ProfileName.Contains($"-targetfloor-{mode}-", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (modeCandidates.Length == 0)
                continue;

            var modeTrades = MatchTradesToCandidates(trades, modeCandidates.Where(c => c.Executed).ToArray());
            rows.Add(new RangeExpansionTargetFloorComparisonRow
            {
                TargetFloorMode = mode,
                CandidateCount = modeCandidates.Length,
                ExecutedCount = modeCandidates.Count(c => c.Executed),
                BlockedCount = modeCandidates.Count(c => !c.Executed),
                NetPnlQuote = modeTrades.Sum(t => t.NetPnlQuote),
                GrossPnlQuote = modeTrades.Sum(t => t.GrossPnlQuote),
                NetWinnerCount = modeTrades.Count(t => t.NetPnlQuote > 0m)
            });
        }

        return rows;
    }

    private static string NormalizeTargetTooSmallReason(string? rejectionReason)
    {
        if (string.IsNullOrWhiteSpace(rejectionReason))
            return "Unknown";

        if (string.Equals(rejectionReason, RangeExpansionBreakoutV1Model.TargetTooSmall, StringComparison.OrdinalIgnoreCase))
            return RangeExpansionBreakoutV1Model.TargetTooSmallAndFeeUntradable;

        return rejectionReason;
    }

    public static bool DetectSeparator(IReadOnlyList<RangeExpansionWinnerLoserComparisonRow> comparison)
    {
        var winners = comparison.FirstOrDefault(c => c.Bucket == "Winners");
        var losers = comparison.FirstOrDefault(c => c.Bucket == "Losers");
        if (winners is null || losers is null || winners.Count < 5 || losers.Count < 5)
            return false;

        var deltas = new[]
        {
            Math.Abs((winners.AvgRangeWidthPercent ?? 0m) - (losers.AvgRangeWidthPercent ?? 0m)),
            Math.Abs((winners.AvgDistanceFromBreakoutPercent ?? 0m) - (losers.AvgDistanceFromBreakoutPercent ?? 0m)),
            Math.Abs((winners.AvgForwardMae60Percent ?? 0m) - (losers.AvgForwardMae60Percent ?? 0m)),
            Math.Abs((winners.AvgBreakoutBodyStrengthPercent ?? 0m) - (losers.AvgBreakoutBodyStrengthPercent ?? 0m))
        };

        return deltas.Count(d => d >= SeparatorMinDeltaPercent) >= 2;
    }

    public static RangeExpansionV2Recommendation BuildV2Recommendation(
        bool separatorDetected,
        IReadOnlyList<RangeExpansionWinnerLoserComparisonRow> comparison,
        RangeExpansionBlockedReachableSummary blockedReachable)
    {
        if (!separatorDetected)
        {
            return new RangeExpansionV2Recommendation(
                false,
                "No clear winner/loser separator on range width, chase distance, MAE, or body strength.",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        var losers = comparison.First(c => c.Bucket == "Losers");
        var topBlockedReason = blockedReachable.ByReason
            .OrderByDescending(r => r.BlockedReachableCount)
            .FirstOrDefault();

        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:Enabled"] = "true",
            ["Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:RequireFollowThroughCloseAboveBreakoutHigh"] = "true",
            ["Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:RequireBreakoutBodyStrengthPercent"] = "40",
            ["Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:MaxBreakoutCandleRangeToAtrRatio"] = "1.50",
            ["Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:MaxMaeRiskProxyPercent"] = "0.35",
            ["Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:TighterAntiChaseCapPercent"] = "0.12"
        };

        if (losers.AvgDistanceFromBreakoutPercent is > 0.08m)
            overrides["Backtest:RangeExpansionBreakoutV1:MaxDistanceFromBreakoutPercent"] = "0.10";

        if (topBlockedReason?.RejectionReason?.Contains("FollowThrough", StringComparison.OrdinalIgnoreCase) == true
            && topBlockedReason.BlockedReachableRate > 20m)
        {
            overrides["Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:RequireFollowThroughCloseAboveBreakoutHigh"] = "false";
        }

        return new RangeExpansionV2Recommendation(
            true,
            "Winner/loser separator detected; V2 applies body-strength, tighter anti-chase, and MAE-risk proxy filters.",
            overrides);
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildDiagnosticAnswers(
        IReadOnlyList<RangeExpansionCandidateRecord> executed,
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<RangeExpansionCandidateRecord> candidates,
        IReadOnlyList<RangeExpansionPnlDecompositionRow> pnl,
        IReadOnlyList<RangeExpansionTradeQualityBucketRow> buckets,
        IReadOnlyList<RangeExpansionWinnerLoserComparisonRow> comparison,
        RangeExpansionBlockedReachableSummary blockedReachable,
        RangeExpansionExecutedTradeCostAnalysis executedCost,
        IReadOnlyList<RangeExpansionTargetFloorComparisonRow> targetFloorComparison,
        RangeExpansionReportingConsistencyReport reportingConsistency,
        bool separatorDetected,
        string tradeabilityVerdict,
        RangeExpansionV2Recommendation v2)
    {
        var answers = new List<ReachabilityResearchAnswer>();

        var best = pnl
            .Where(r => r.Dimension == "Window+Symbol" && r.TradeCount > 0)
            .OrderByDescending(r => r.NetPnlQuote)
            .FirstOrDefault();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which symbol/interval/profitlock has the least negative or best net result?",
            Answer = best is null
                ? "No executed trades available for ranking."
                : $"Best observed: {best.Key} with net PnL {best.NetPnlQuote:F8} quote over window {best.WindowLabel}.",
            Verdict = best?.NetPnlQuote >= 0m ? "PositiveBucketFound" : "AllNegative",
            Details = new Dictionary<string, object?>
            {
                ["bestKey"] = best?.Key,
                ["bestNetPnl"] = best?.NetPnlQuote,
                ["topFive"] = pnl.Where(r => r.Dimension == "Window+Symbol" && r.TradeCount > 0)
                    .OrderByDescending(r => r.NetPnlQuote).Take(5).Select(r => new { r.Key, r.NetPnlQuote, r.WindowLabel }).ToArray()
            }
        });

        var loserBuckets = buckets.Where(b => b.Bucket is "FalseBreakoutProxy" or "HighMaeBeforeTarget" or "LateEntry").ToArray();
        var dominant = loserBuckets.OrderByDescending(b => b.Count).FirstOrDefault();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are losers mostly false breakouts, late entries, or high-MAE-before-target trades?",
            Answer = dominant is null
                ? "Insufficient executed loser bucket data."
                : $"Dominant loser pattern: {dominant.Bucket} ({dominant.Count} trades, net {dominant.NetPnlQuote:F8}).",
            Verdict = dominant?.Bucket ?? "Unknown",
            Details = loserBuckets.ToDictionary(b => b.Bucket, b => (object?)new { b.Count, b.NetPnlQuote, b.AvgForwardMae60Percent })
        });

        var antiChaseBlocked = blockedReachable.ByReason
            .Where(r => r.RejectionReason?.Contains("AntiChase", StringComparison.OrdinalIgnoreCase) == true
                        || r.RejectionReason?.Contains("FollowThrough", StringComparison.OrdinalIgnoreCase) == true)
            .Sum(r => r.BlockedReachableCount);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are many lock90-reachable candidates blocked by anti-chase/follow-through?",
            Answer = $"Blocked-reachable total={blockedReachable.BlockedReachableCount}; anti-chase/follow-through reachable={antiChaseBlocked}.",
            Verdict = antiChaseBlocked >= blockedReachable.BlockedReachableCount / 2 ? "FiltersRemoveManyGood" : "FiltersMostlyAppropriate",
            Details = new Dictionary<string, object?> { ["antiChaseFollowThroughReachable"] = antiChaseBlocked, ["blockedReachableTotal"] = blockedReachable.BlockedReachableCount }
        });

        var topReason = blockedReachable.ByReason.FirstOrDefault();
        var targetTooSmallRows = blockedReachable.ByReason
            .Where(r => r.RejectionReason?.Contains("TargetTooSmall", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
        var targetTooSmallNetTradable = targetTooSmallRows.Sum(r => r.Lock90ReachableAndNetProfitableCount);
        var targetTooSmallReachable = targetTooSmallRows.Sum(r => r.BlockedReachableCount);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does one filter/rejection reason remove too many good candidates?",
            Answer = topReason is null
                ? "No blocked candidates to analyze."
                : $"Top reason {topReason.RejectionReason}: blocked={topReason.BlockedCount}, lock90-reachable={topReason.BlockedReachableCount} ({topReason.BlockedReachableRate}%), net-profitable-reachable={topReason.Lock90ReachableAndNetProfitableCount} ({topReason.Lock90ReachableAndNetProfitableRate}%).",
            Verdict = topReason is { Lock90ReachableAndNetProfitableRate: > 10m } ? "NetTradableBlockedExists" : "ReachableNotNetTradable",
            Details = new Dictionary<string, object?>
            {
                ["topReason"] = topReason?.RejectionReason,
                ["topReachableRate"] = topReason?.BlockedReachableRate,
                ["topNetProfitableReachableRate"] = topReason?.Lock90ReachableAndNetProfitableRate
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are TargetTooSmall blocked candidates actually net-tradable after costs?",
            Answer = targetTooSmallRows.Length == 0
                ? "No TargetTooSmall-class rejections observed."
                : $"TargetTooSmall-class blocked={targetTooSmallRows.Sum(r => r.BlockedCount)}, lock90-reachable={targetTooSmallReachable}, net-profitable-reachable={targetTooSmallNetTradable}.",
            Verdict = targetTooSmallNetTradable >= 25 ? "MeaningfulNetTradableBlocked" : "DoNotRelaxTargetTooSmall",
            Details = new Dictionary<string, object?>
            {
                ["targetTooSmallNetTradableCount"] = targetTooSmallNetTradable,
                ["targetTooSmallReachableCount"] = targetTooSmallReachable,
                ["byReason"] = targetTooSmallRows.Select(r => new
                {
                    r.RejectionReason,
                    r.BlockedCount,
                    r.Lock90ReachableAndNetProfitableCount,
                    r.MedianLock90NetProfitPercent
                }).ToArray()
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are executed ProfitLock exits losing because lock distance is below fee/spread?",
            Answer = $"ProfitLock gross-positive/net-negative={executedCost.ProfitLockGrossPositiveNetNegativeCount}; expected-move-passed but lock90Net below requiredNet={executedCost.ExpectedMovePassedLock90BelowRequiredNetCount}.",
            Verdict = executedCost.ProfitLockGrossPositiveNetNegativeCount > 0 || executedCost.ExpectedMovePassedLock90BelowRequiredNetCount > 0
                ? "LockDistanceBelowCostLikely"
                : "NotPrimaryLossDriver",
            Details = new Dictionary<string, object?>
            {
                ["bySymbolInterval"] = executedCost.BySymbolInterval
            }
        });

        var ethRow = executedCost.BySymbolInterval.FirstOrDefault(r => r.SymbolIntervalKey.StartsWith("ETHUSDT|1m", StringComparison.OrdinalIgnoreCase));
        var bnbRow = executedCost.BySymbolInterval.FirstOrDefault(r => r.SymbolIntervalKey.StartsWith("BNBUSDT|1m", StringComparison.OrdinalIgnoreCase));
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is BNB negative because of bad selection or because targets are too small after cost?",
            Answer = ethRow is null || bnbRow is null
                ? "Insufficient ETH/BNB 1m executed trade cost rows."
                : $"BNB net={bnbRow.NetPnlQuote:F8}, medianLock90Net={bnbRow.MedianLock90NetProfitPercent:F4}%, lockBelowRequiredNet={bnbRow.ExpectedMovePassedLock90BelowRequiredNetCount}; ETH net={ethRow.NetPnlQuote:F8}, medianLock90Net={ethRow.MedianLock90NetProfitPercent:F4}%.",
            Verdict = bnbRow?.ExpectedMovePassedLock90BelowRequiredNetCount > ethRow?.ExpectedMovePassedLock90BelowRequiredNetCount
                ? "TargetsTooSmallAfterCost"
                : "BadSelectionOrMaeDominance",
            Details = new Dictionary<string, object?> { ["eth"] = ethRow, ["bnb"] = bnbRow }
        });

        var bestFloor = targetFloorComparison.OrderByDescending(r => r.NetPnlQuote).FirstOrDefault();
        var currentFloor = targetFloorComparison.FirstOrDefault(r => r.TargetFloorMode == "current");
        var costAwareFloor = targetFloorComparison.FirstOrDefault(r => r.TargetFloorMode == "costaware");
        var costAwareVerdict = targetFloorComparison.Count == 0
            ? "NoTargetFloorExperiment"
            : costAwareFloor is { ExecutedCount: 0 }
                ? "CostAwareFloorBlocksAllTrades"
                : costAwareFloor?.NetPnlQuote >= (currentFloor?.NetPnlQuote ?? decimal.MinValue)
                    ? "CostAwareFloorImprovesPnl"
                    : bestFloor?.NetPnlQuote >= 0m
                        ? "PositiveWithBestFloor"
                        : "AllFloorsNegative";
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does a cost-aware target floor improve net PnL?",
            Answer = targetFloorComparison.Count == 0
                ? "Target floor experiment profiles not present in this run."
                : string.Join("; ", targetFloorComparison.Select(r => $"{r.TargetFloorMode}: trades={r.ExecutedCount}, net={r.NetPnlQuote:F8}, winners={r.NetWinnerCount}")),
            Verdict = costAwareVerdict,
            Details = new Dictionary<string, object?> { ["bestFloor"] = bestFloor?.TargetFloorMode, ["comparison"] = targetFloorComparison }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Should V2 filters be skipped until a net-profitable target model exists?",
            Answer = separatorDetected
                ? "Separator exists but cost model still shows fee-adjusted edge missing; defer V2."
                : "No winner/loser separator and cost-adjusted targets remain weak; skip V2 until target model is net-profitable.",
            Verdict = v2.ShouldCreateV2Profiles && blockedReachable.Lock90ReachableAndNetProfitableCount >= 25
                ? "ConsiderV2AfterTargetModel"
                : "SkipV2UntilTargetModel",
            Details = new Dictionary<string, object?>
            {
                ["v2Recommended"] = v2.ShouldCreateV2Profiles,
                ["netProfitableReachableBlocked"] = blockedReachable.Lock90ReachableAndNetProfitableCount
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are EndOfData losses real simulation losses or reporting artifacts?",
            Answer = $"EndOfData exits={reportingConsistency.EndOfDataExitCount}, net={reportingConsistency.EndOfDataNetPnlQuote:F8}, orphanCandidates={reportingConsistency.OrphanExecutedCandidateCount}, excludedFromPnl={reportingConsistency.ExcludedFromPnlAggregatesCount}.",
            Verdict = reportingConsistency.OrphanExecutedCandidateCount > 0 || reportingConsistency.UnknownExitReasonCount > 0
                ? "ReportingArtifactsPresent"
                : reportingConsistency.EndOfDataExitCount > 0
                    ? "RealEndOfDataForceCloseLosses"
                    : "NoEndOfDataIssue",
            Details = new Dictionary<string, object?> { ["largestEndOfDataLosses"] = reportingConsistency.LargestEndOfDataLosses }
        });

        var eth1m = pnl.FirstOrDefault(r => r.Dimension == "Symbol" && r.Key == TradingSymbol.ETHUSDT.ToString());
        var bnb1m = pnl.FirstOrDefault(r => r.Dimension == "Symbol" && r.Key == TradingSymbol.BNBUSDT.ToString());
        var ethBetter = (eth1m?.NetPnlQuote ?? decimal.MinValue) >= (bnb1m?.NetPnlQuote ?? decimal.MinValue);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is ETH 1m or BNB 1m the better research target?",
            Answer = $"ETH net={eth1m?.NetPnlQuote:F8}, BNB net={bnb1m?.NetPnlQuote:F8}.",
            Verdict = ethBetter ? "PreferETH" : "PreferBNB",
            Details = new Dictionary<string, object?> { ["ethNetPnl"] = eth1m?.NetPnlQuote, ["bnbNetPnl"] = bnb1m?.NetPnlQuote }
        });

        var lock90 = pnl.Where(r => r.Dimension == "ProfitLockThreshold" && r.Key == "90").Sum(r => r.NetPnlQuote);
        var lock95 = pnl.Where(r => r.Dimension == "ProfitLockThreshold" && r.Key == "95").Sum(r => r.NetPnlQuote);
        var lock98 = pnl.Where(r => r.Dimension == "ProfitLockThreshold" && r.Key == "98").Sum(r => r.NetPnlQuote);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does lock90 outperform 95/98 by reducing missed exits?",
            Answer = $"Net PnL totals: lock90={lock90:F8}, lock95={lock95:F8}, lock98={lock98:F8}.",
            Verdict = lock90 >= lock95 && lock90 >= lock98 ? "Lock90Best" : lock95 >= lock98 ? "Lock95Best" : "Lock98Best",
            Details = new Dictionary<string, object?> { ["lock90"] = lock90, ["lock95"] = lock95, ["lock98"] = lock98 }
        });

        var intervalRows = pnl.Where(r => r.Dimension == "Interval").ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are 3m/5m worse because of target distance or because of fewer candidates?",
            Answer = string.Join("; ", intervalRows.Select(r => $"{r.Key}: trades={r.TradeCount}, candidates={r.CandidateCount}, net={r.NetPnlQuote:F8}")),
            Verdict = intervalRows.FirstOrDefault(r => r.Key == "1m")?.TradeCount > (intervalRows.FirstOrDefault(r => r.Key == "3m")?.TradeCount ?? 0)
                ? "FewerCandidatesOnHigherIntervals" : "Mixed",
            Details = intervalRows.ToDictionary(r => r.Key, r => (object?)new { r.TradeCount, r.CandidateCount, r.NetPnlQuote })
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is RangeExpansionV1 candidate-rich but tradeable yet?",
            Answer = separatorDetected
                ? "Losses show filterable structure; proceed with V2 backtest-only profiles."
                : "Candidate volume/reachability exist but winners/losers overlap; not tradeable yet.",
            Verdict = tradeabilityVerdict,
            Details = new Dictionary<string, object?>
            {
                ["separatorDetected"] = separatorDetected,
                ["v2Recommended"] = v2.ShouldCreateV2Profiles,
                ["couldRelaxSafely"] = blockedReachable.CouldRelaxSafelyCandidateCount
            }
        });

        return answers;
    }

    private static decimal? Average(IEnumerable<decimal?> values)
    {
        var samples = values.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        return samples.Length == 0 ? null : Math.Round(samples.Average(), 6);
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
