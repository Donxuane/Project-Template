using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed record RangeExpansionV2OutcomeComparisonRow
{
    public string Bucket { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal? MedianRangeWidthPercent { get; init; }
    public decimal? MedianBreakoutBodyStrengthPercent { get; init; }
    public decimal? MedianBreakoutCloseAboveRangePercent { get; init; }
    public decimal? MedianBreakoutCandleRangePercent { get; init; }
    public decimal? MedianAtrPercent { get; init; }
    public decimal? MedianAtrExpansionRatio { get; init; }
    public decimal? MedianVolumeExpansionRatio { get; init; }
    public decimal? MedianExpectedMovePercent { get; init; }
    public decimal? MedianLock90DistancePercent { get; init; }
    public decimal? MedianLock90NetProfitPercent { get; init; }
    public decimal? MedianForwardMfe60Percent { get; init; }
    public decimal? MedianForwardMae60Percent { get; init; }
    public decimal? MedianMfePercent { get; init; }
    public decimal? MedianMaePercent { get; init; }
    public decimal? MedianDurationMinutes { get; init; }
    public decimal? MedianTimeToMaxFavorableMinutes { get; init; }
    public decimal? MedianTimeToStopMinutes { get; init; }
    public decimal InflationRatePercent { get; init; }
}

public sealed record RangeExpansionV2FailureTimingRow
{
    public string Bucket { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal? MedianMfeBeforeStopPercent { get; init; }
    public decimal? MedianMaeBeforeProfitLockPercent { get; init; }
    public decimal? MedianTimeToMfeMinutes { get; init; }
    public decimal? MedianTimeToMaeMinutes { get; init; }
    public int DidReachHalfLockBeforeStopCount { get; init; }
    public int DidReachHalfLockBeforeTimeStopCount { get; init; }
    public decimal? MedianTimeStopExitMovePercent { get; init; }
    public int TimeStopNearBreakevenCount { get; init; }
    public int TimeStopGrossPositiveCount { get; init; }
    public int TimeStopNetNegativeOnlyDueToFeesCount { get; init; }
}

public sealed record RangeExpansionV2SymbolExitRow
{
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "1m";
    public int TradeCount { get; init; }
    public decimal ProfitLockRatePercent { get; init; }
    public decimal StopLossRatePercent { get; init; }
    public decimal TimeStopRatePercent { get; init; }
    public decimal ProfitLockNetPnlQuote { get; init; }
    public decimal StopLossNetPnlQuote { get; init; }
    public decimal TimeStopNetPnlQuote { get; init; }
    public decimal TotalNetPnlQuote { get; init; }
    public decimal? MedianMfePercent { get; init; }
    public decimal? MedianMaePercent { get; init; }
    public decimal? MedianLock90DistancePercent { get; init; }
    public decimal? MedianForwardMfe60Percent { get; init; }
    public decimal? MedianForwardMae60Percent { get; init; }
}

public sealed record RangeExpansionV21FastSummaryRow
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
    public int ProfitLockCount { get; init; }
    public int StopLossCount { get; init; }
    public int TimeStopCount { get; init; }
    public int EndOfDataCount { get; init; }
}

public sealed record RangeExpansionV2ExtendedDiagnostics(
    IReadOnlyList<RangeExpansionV2OutcomeComparisonRow> ExitOutcomeComparison,
    IReadOnlyList<RangeExpansionV2FailureTimingRow> FailureTiming,
    IReadOnlyList<RangeExpansionV2SymbolExitRow> SymbolExitBreakdown,
    IReadOnlyList<RangeExpansionV21FastSummaryRow> V21FastSummary,
    IReadOnlyList<ReachabilityResearchAnswer> V21ResearchAnswers);

public static class RangeExpansionV2DiagnosticsAggregator
{
    private const decimal HalfLockFraction = 0.45m;
    private const decimal NearBreakevenGrossThresholdPercent = 0.05m;
    private const decimal EarlyStopLossMinutes = 30m;

    public static IReadOnlyList<RangeExpansionV2CandidateRecord> EnrichExecutedCandidates(
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates)
    {
        return candidates
            .Where(c => c.Executed)
            .Select(EnrichFailureTiming)
            .ToArray();
    }

    public static RangeExpansionV2CandidateRecord EnrichFailureTiming(RangeExpansionV2CandidateRecord c)
    {
        var halfLock = c.Lock90DistancePercent.HasValue
            ? c.Lock90DistancePercent.Value * HalfLockFraction
            : (decimal?)null;
        var mfe = c.MfePercent;
        var didReachHalfLock = halfLock.HasValue && mfe.HasValue && mfe.Value >= halfLock.Value;
        var isTimeStop = string.Equals(c.ExitReason, "TimeStop", StringComparison.OrdinalIgnoreCase);
        var isStopLoss = string.Equals(c.ExitReason, "StopLoss", StringComparison.OrdinalIgnoreCase);
        var isProfitLock = string.Equals(c.ExitReason, "ProfitLock", StringComparison.OrdinalIgnoreCase);
        var grossPositive = c.GrossPnlQuote is > 0m;
        var netNegative = c.NetPnlQuote is <= 0m;
        var exitMove = ComputeExitMovePercent(c);

        return c with
        {
            HalfLockDistancePercent = halfLock,
            DidReachHalfLockBeforeStop = isStopLoss && didReachHalfLock,
            DidReachHalfLockBeforeTimeStop = isTimeStop && didReachHalfLock,
            MfeBeforeStopPercent = isStopLoss ? mfe : c.MfeBeforeStopPercent,
            MaeBeforeProfitLockPercent = isProfitLock ? c.MaePercent : c.MaeBeforeProfitLockPercent,
            TimeToMaxFavorableMinutes = ResolveTimeToMaxFavorable(c),
            TimeToStopMinutes = isStopLoss || isTimeStop ? (int?)Math.Round(c.DurationMinutes) : c.TimeToStopMinutes,
            TimeToMfeMinutes = ResolveTimeToMfe(c),
            TimeToMaeMinutes = ResolveTimeToMae(c),
            TimeStopExitMovePercent = isTimeStop ? exitMove : c.TimeStopExitMovePercent,
            TimeStopWasNearBreakeven = isTimeStop && exitMove is >= -NearBreakevenGrossThresholdPercent and <= NearBreakevenGrossThresholdPercent,
            TimeStopWasGrossPositive = isTimeStop && grossPositive,
            TimeStopWasNetNegativeOnlyDueToFees = isTimeStop && grossPositive && netNegative,
            OutcomeBucket = ClassifyOutcomeBucket(c)
        };
    }

    private static decimal? ComputeExitMovePercent(RangeExpansionV2CandidateRecord c)
    {
        if (c.EntryPrice <= 0m || !c.GrossPnlQuote.HasValue)
            return null;
        const decimal qty = 0.001m;
        return Math.Round(c.GrossPnlQuote.Value / (c.EntryPrice * qty) * 100m, 6);
    }

    private static int? ResolveTimeToMaxFavorable(RangeExpansionV2CandidateRecord c)
    {
        if (c.TimeToLock90Minutes.HasValue
            && c.MfePercent.HasValue
            && c.Lock90DistancePercent.HasValue
            && c.MfePercent.Value >= c.Lock90DistancePercent.Value * 0.9m)
            return c.TimeToLock90Minutes;
        if (c.ForwardMfe15Percent.HasValue && c.MfePercent.HasValue
            && Math.Abs(c.ForwardMfe15Percent.Value - c.MfePercent.Value) < 0.02m)
            return 15;
        if (c.ForwardMfe30Percent.HasValue && c.MfePercent.HasValue
            && Math.Abs(c.ForwardMfe30Percent.Value - c.MfePercent.Value) < 0.02m)
            return 30;
        return c.DurationMinutes > 0 ? (int?)Math.Round(c.DurationMinutes) : null;
    }

    private static int? ResolveTimeToMfe(RangeExpansionV2CandidateRecord c)
    {
        if (c.TimeToLock90Minutes.HasValue
            && c.MfePercent.HasValue
            && c.Lock90DistancePercent.HasValue
            && c.MfePercent.Value >= c.Lock90DistancePercent.Value * 0.5m)
            return c.TimeToLock90Minutes;
        return ResolveTimeToMaxFavorable(c);
    }

    private static int? ResolveTimeToMae(RangeExpansionV2CandidateRecord c)
    {
        if (c.ForwardMae15Percent.HasValue && c.MaePercent.HasValue
            && Math.Abs(c.ForwardMae15Percent.Value - c.MaePercent.Value) < 0.03m)
            return 15;
        if (c.DurationMinutes > 0 && c.MaePercent is < -0.05m)
            return (int?)Math.Round(c.DurationMinutes);
        return null;
    }

    public static string ClassifyOutcomeBucket(RangeExpansionV2CandidateRecord c)
    {
        if (!c.Executed || string.IsNullOrWhiteSpace(c.ExitReason))
            return "Unknown";

        if (string.Equals(c.ExitReason, "ProfitLock", StringComparison.OrdinalIgnoreCase) && c.NetPnlQuote is > 0m)
            return "ProfitLockWinners";
        if (string.Equals(c.ExitReason, "TimeStop", StringComparison.OrdinalIgnoreCase))
        {
            if (c.GrossPnlQuote is > 0m && c.NetPnlQuote is <= 0m)
                return "TimeStopGrossPositiveNetNegative";
            if (c.GrossPnlQuote is <= 0m)
                return "TimeStopGrossNegative";
            return "TimeStopLosers";
        }
        if (string.Equals(c.ExitReason, "StopLoss", StringComparison.OrdinalIgnoreCase))
        {
            return c.DurationMinutes < EarlyStopLossMinutes
                ? "StopLossEarlyFailures"
                : "StopLossLosers";
        }

        return c.ExitReason;
    }

    public static IReadOnlyList<RangeExpansionV2OutcomeComparisonRow> BuildExitOutcomeComparison(
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates)
    {
        var executed = EnrichExecutedCandidates(candidates);
        var buckets = new[]
        {
            "ProfitLockWinners",
            "TimeStopLosers",
            "StopLossLosers",
            "TimeStopGrossPositiveNetNegative",
            "TimeStopGrossNegative",
            "StopLossEarlyFailures"
        };

        return buckets
            .Select(bucket => BuildOutcomeRow(bucket, executed.Where(c => c.OutcomeBucket == bucket).ToArray()))
            .Where(r => r.Count > 0)
            .ToArray();
    }

    private static RangeExpansionV2OutcomeComparisonRow BuildOutcomeRow(
        string bucket,
        IReadOnlyList<RangeExpansionV2CandidateRecord> rows)
        => new()
        {
            Bucket = bucket,
            Count = rows.Count,
            NetPnlQuote = rows.Sum(r => r.NetPnlQuote ?? 0m),
            MedianRangeWidthPercent = Median(rows.Select(r => (decimal?)r.RangeWidthPercent)),
            MedianBreakoutBodyStrengthPercent = Median(rows.Select(r => r.BreakoutBodyStrengthPercent)),
            MedianBreakoutCloseAboveRangePercent = Median(rows.Select(r => r.BreakoutCloseAboveRangePercent)),
            MedianBreakoutCandleRangePercent = Median(rows.Select(r => r.BreakoutCandleRangePercent)),
            MedianAtrPercent = Median(rows.Select(r => (decimal?)r.AtrPercent)),
            MedianAtrExpansionRatio = Median(rows.Select(r => (decimal?)r.AtrExpansionRatio)),
            MedianVolumeExpansionRatio = Median(rows.Select(r => (decimal?)r.VolumeExpansionRatio)),
            MedianExpectedMovePercent = Median(rows.Select(r => r.ExpectedMovePercent)),
            MedianLock90DistancePercent = Median(rows.Select(r => r.Lock90DistancePercent)),
            MedianLock90NetProfitPercent = Median(rows.Select(r => r.Lock90NetProfitPercent)),
            MedianForwardMfe60Percent = Median(rows.Select(r => r.ForwardMfe60Percent)),
            MedianForwardMae60Percent = Median(rows.Select(r => r.ForwardMae60Percent)),
            MedianMfePercent = Median(rows.Select(r => r.MfePercent)),
            MedianMaePercent = Median(rows.Select(r => r.MaePercent)),
            MedianDurationMinutes = Median(rows.Select(r => (decimal?)r.DurationMinutes)),
            MedianTimeToMaxFavorableMinutes = Median(rows.Select(r => r.TimeToMaxFavorableMinutes.HasValue ? (decimal?)r.TimeToMaxFavorableMinutes.Value : null)),
            MedianTimeToStopMinutes = Median(rows.Select(r => r.TimeToStopMinutes.HasValue ? (decimal?)r.TimeToStopMinutes.Value : null)),
            InflationRatePercent = rows.Count == 0 ? 0m : Math.Round(rows.Count(r => r.ExpectedMoveInflated) * 100m / rows.Count, 2)
        };

    public static IReadOnlyList<RangeExpansionV2FailureTimingRow> BuildFailureTiming(
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates)
    {
        var executed = EnrichExecutedCandidates(candidates);
        var buckets = new[]
        {
            ("StopLossLosers", executed.Where(c => string.Equals(c.ExitReason, "StopLoss", StringComparison.OrdinalIgnoreCase)).ToArray()),
            ("TimeStopLosers", executed.Where(c => string.Equals(c.ExitReason, "TimeStop", StringComparison.OrdinalIgnoreCase)).ToArray()),
            ("ProfitLockWinners", executed.Where(c => c.OutcomeBucket == "ProfitLockWinners").ToArray())
        };

        return buckets
            .Where(b => b.Item2.Length > 0)
            .Select(b => new RangeExpansionV2FailureTimingRow
            {
                Bucket = b.Item1,
                Count = b.Item2.Length,
                MedianMfeBeforeStopPercent = Median(b.Item2.Select(c => c.MfeBeforeStopPercent)),
                MedianMaeBeforeProfitLockPercent = Median(b.Item2.Select(c => c.MaeBeforeProfitLockPercent)),
                MedianTimeToMfeMinutes = Median(b.Item2.Select(c => c.TimeToMfeMinutes.HasValue ? (decimal?)c.TimeToMfeMinutes.Value : null)),
                MedianTimeToMaeMinutes = Median(b.Item2.Select(c => c.TimeToMaeMinutes.HasValue ? (decimal?)c.TimeToMaeMinutes.Value : null)),
                DidReachHalfLockBeforeStopCount = b.Item2.Count(c => c.DidReachHalfLockBeforeStop),
                DidReachHalfLockBeforeTimeStopCount = b.Item2.Count(c => c.DidReachHalfLockBeforeTimeStop),
                MedianTimeStopExitMovePercent = Median(b.Item2.Select(c => c.TimeStopExitMovePercent)),
                TimeStopNearBreakevenCount = b.Item2.Count(c => c.TimeStopWasNearBreakeven),
                TimeStopGrossPositiveCount = b.Item2.Count(c => c.TimeStopWasGrossPositive),
                TimeStopNetNegativeOnlyDueToFeesCount = b.Item2.Count(c => c.TimeStopWasNetNegativeOnlyDueToFees)
            })
            .ToArray();
    }

    public static IReadOnlyList<RangeExpansionV2SymbolExitRow> BuildSymbolExitBreakdown(
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades)
    {
        var symbols = new[] { TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT };
        return symbols
            .Select(symbol =>
            {
                var symbolTrades = trades.Where(t => t.Symbol == symbol && t.Interval == "1m").ToArray();
                var symbolCandidates = candidates.Where(c => c.Symbol == symbol && c.Executed).ToArray();
                if (symbolTrades.Length == 0 && symbolCandidates.Length == 0)
                    return null;

                var total = symbolTrades.Length;
                var profitLock = symbolTrades.Where(t => string.Equals(t.ExitReason, "ProfitLock", StringComparison.OrdinalIgnoreCase)).ToArray();
                var stopLoss = symbolTrades.Where(t => string.Equals(t.ExitReason, "StopLoss", StringComparison.OrdinalIgnoreCase)).ToArray();
                var timeStop = symbolTrades.Where(t => string.Equals(t.ExitReason, "TimeStop", StringComparison.OrdinalIgnoreCase)).ToArray();

                return new RangeExpansionV2SymbolExitRow
                {
                    Symbol = symbol,
                    Interval = "1m",
                    TradeCount = total,
                    ProfitLockRatePercent = total == 0 ? 0m : Math.Round(profitLock.Length * 100m / total, 2),
                    StopLossRatePercent = total == 0 ? 0m : Math.Round(stopLoss.Length * 100m / total, 2),
                    TimeStopRatePercent = total == 0 ? 0m : Math.Round(timeStop.Length * 100m / total, 2),
                    ProfitLockNetPnlQuote = profitLock.Sum(t => t.NetPnlQuote),
                    StopLossNetPnlQuote = stopLoss.Sum(t => t.NetPnlQuote),
                    TimeStopNetPnlQuote = timeStop.Sum(t => t.NetPnlQuote),
                    TotalNetPnlQuote = symbolTrades.Sum(t => t.NetPnlQuote),
                    MedianMfePercent = Median(symbolCandidates.Select(c => c.MfePercent)),
                    MedianMaePercent = Median(symbolCandidates.Select(c => c.MaePercent)),
                    MedianLock90DistancePercent = Median(symbolCandidates.Select(c => c.Lock90DistancePercent)),
                    MedianForwardMfe60Percent = Median(symbolCandidates.Select(c => c.ForwardMfe60Percent)),
                    MedianForwardMae60Percent = Median(symbolCandidates.Select(c => c.ForwardMae60Percent))
                };
            })
            .Where(r => r is not null)
            .Cast<RangeExpansionV2SymbolExitRow>()
            .ToArray();
    }

    public static IReadOnlyList<RangeExpansionV21FastSummaryRow> BuildV21FastSummary(
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades)
    {
        var profileNames = candidates
            .Select(c => c.ProfileName)
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
                return new RangeExpansionV21FastSummaryRow
                {
                    VariantLabel = variant,
                    ProfileName = profileName,
                    CandidateCount = profileCandidates.Length,
                    TradeCount = profileTrades.Length,
                    NetWinnerCount = profileTrades.Count(t => t.NetPnlQuote > 0m),
                    NetPnlQuote = profileTrades.Sum(t => t.NetPnlQuote),
                    ProfitLockNetPnlQuote = profileTrades.Where(t => t.ExitReason == "ProfitLock").Sum(t => t.NetPnlQuote),
                    StopLossNetPnlQuote = profileTrades.Where(t => t.ExitReason == "StopLoss").Sum(t => t.NetPnlQuote),
                    TimeStopNetPnlQuote = profileTrades.Where(t => t.ExitReason == "TimeStop").Sum(t => t.NetPnlQuote),
                    ProfitLockCount = profileTrades.Count(t => t.ExitReason == "ProfitLock"),
                    StopLossCount = profileTrades.Count(t => t.ExitReason == "StopLoss"),
                    TimeStopCount = profileTrades.Count(t => t.ExitReason == "TimeStop"),
                    EndOfDataCount = profileTrades.Count(t => t.ExitReason == "EndOfData")
                };
            })
            .OrderBy(r => r.VariantLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildV21ResearchAnswers(
        IReadOnlyList<RangeExpansionV2OutcomeComparisonRow> outcomeComparison,
        IReadOnlyList<RangeExpansionV2FailureTimingRow> failureTiming,
        IReadOnlyList<RangeExpansionV21FastSummaryRow> fastSummary,
        IReadOnlyList<RangeExpansionV2SymbolExitRow> symbolBreakdown)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var profitLock = outcomeComparison.FirstOrDefault(r => r.Bucket == "ProfitLockWinners");
        var stopLoss = outcomeComparison.FirstOrDefault(r => r.Bucket == "StopLossLosers");
        var timeStopFee = outcomeComparison.FirstOrDefault(r => r.Bucket == "TimeStopGrossPositiveNetNegative");
        var timeStopFail = outcomeComparison.FirstOrDefault(r => r.Bucket == "TimeStopGrossNegative");

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are StopLoss losers structurally identifiable at entry?",
            Answer = stopLoss is null
                ? "No StopLoss loser bucket."
                : $"StopLoss losers={stopLoss.Count}; median body={stopLoss.MedianBreakoutBodyStrengthPercent:F2}, median lock90Net={stopLoss.MedianLock90NetProfitPercent:F4}, median rangeWidth={stopLoss.MedianRangeWidthPercent:F4}.",
            Verdict = profitLock is not null && stopLoss is not null
                && (profitLock.MedianBreakoutBodyStrengthPercent ?? 0) - (stopLoss.MedianBreakoutBodyStrengthPercent ?? 0) >= 5m
                ? "EntrySeparatorsExist"
                : "WeakEntrySeparator",
            Details = new Dictionary<string, object?> { ["profitLock"] = profitLock, ["stopLoss"] = stopLoss }
        });

        var timeStopTiming = failureTiming.FirstOrDefault(r => r.Bucket == "TimeStopLosers");
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are TimeStop losers just fee drag or genuinely failed breakouts?",
            Answer = $"TimeStop gross+ net-={(timeStopFee?.Count ?? 0)}; gross-={(timeStopFail?.Count ?? 0)}; fee-only={(timeStopTiming?.TimeStopNetNegativeOnlyDueToFeesCount ?? 0)}.",
            Verdict = (timeStopFail?.Count ?? 0) > (timeStopFee?.Count ?? 0) ? "FailedBreakoutsDominant" : "FeeDragMaterial",
            Details = new Dictionary<string, object?> { ["timeStopTiming"] = timeStopTiming }
        });

        var baseline = fastSummary.FirstOrDefault(r => r.VariantLabel == "baseline");
        var shorter = fastSummary.Where(r => r.VariantLabel.Contains("timestop", StringComparison.OrdinalIgnoreCase)).ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does shorter TimeStop reduce bleed or cut future winners?",
            Answer = shorter.Length == 0
                ? "No TimeStop variant profiles in this run."
                : string.Join("; ", shorter.Select(r => $"{r.VariantLabel}: net={r.NetPnlQuote:F8}, winners={r.NetWinnerCount}, PL={r.ProfitLockCount}")),
            Verdict = shorter.Length > 0 && shorter.MaxBy(r => r.NetPnlQuote)?.NetPnlQuote > (baseline?.NetPnlQuote ?? decimal.MinValue)
                ? "ShorterTimeStopHelps"
                : "ShorterTimeStopMixed",
            Details = new Dictionary<string, object?> { ["baseline"] = baseline, ["variants"] = shorter }
        });

        var stopVariants = fastSummary.Where(r => r.VariantLabel.Contains("stop", StringComparison.OrdinalIgnoreCase)).ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does RangeMidpoint or BelowRangeHigh stop improve net PnL versus RangeLow?",
            Answer = stopVariants.Length == 0
                ? "No structural stop variants in run."
                : string.Join("; ", stopVariants.Select(r => $"{r.VariantLabel}: net={r.NetPnlQuote:F8}, SL={r.StopLossCount}")),
            Verdict = stopVariants.Length > 0 && stopVariants.MaxBy(r => r.NetPnlQuote)?.NetPnlQuote > (baseline?.NetPnlQuote ?? decimal.MinValue)
                ? "AlternativeStopImproves"
                : "RangeLowBaselineBest",
            Details = new Dictionary<string, object?> { ["variants"] = stopVariants }
        });

        var beVariants = fastSummary.Where(r => r.VariantLabel.Contains("breakeven", StringComparison.OrdinalIgnoreCase) || r.VariantLabel.Contains("halflock", StringComparison.OrdinalIgnoreCase)).ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does breakeven protection reduce TimeStop/StopLoss losses without killing ProfitLock wins?",
            Answer = beVariants.Length == 0
                ? "No breakeven variants in run."
                : string.Join("; ", beVariants.Select(r => $"{r.VariantLabel}: net={r.NetPnlQuote:F8}, PL={r.ProfitLockCount}, winners={r.NetWinnerCount}")),
            Verdict = beVariants.Any(r => r.NetPnlQuote > (baseline?.NetPnlQuote ?? decimal.MinValue) && r.ProfitLockCount >= (baseline?.ProfitLockCount ?? 0) * 0.8m)
                ? "BreakevenProtectionHelps"
                : "BreakevenProtectionMixed",
            Details = new Dictionary<string, object?> { ["variants"] = beVariants }
        });

        var bnb = symbolBreakdown.FirstOrDefault(r => r.Symbol == TradingSymbol.BNBUSDT);
        var bestBnb = fastSummary.Where(r => r.ProfileName.Contains("BNB", StringComparison.OrdinalIgnoreCase)).MaxBy(r => r.NetPnlQuote);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is BNB 1m improvable to near breakeven or positive?",
            Answer = bnb is null
                ? "No BNB symbol breakdown."
                : $"BNB aggregate net={bnb.TotalNetPnlQuote:F8}; best variant {bestBnb?.VariantLabel} net={bestBnb?.NetPnlQuote:F8}.",
            Verdict = bestBnb?.NetPnlQuote >= 0m ? "BNBNearPositive" : bestBnb?.NetPnlQuote > (bnb?.TotalNetPnlQuote ?? decimal.MinValue) * 0.5m ? "BNBImprovable" : "BNBStillNegative",
            Details = new Dictionary<string, object?> { ["bnb"] = bnb, ["bestVariant"] = bestBnb }
        });

        return answers;
    }

    public static RangeExpansionV2ExtendedDiagnostics BuildExtended(
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades,
        bool includeV21Summary)
    {
        var outcome = BuildExitOutcomeComparison(candidates);
        var timing = BuildFailureTiming(candidates);
        var symbol = BuildSymbolExitBreakdown(candidates, trades);
        var v21Summary = includeV21Summary ? BuildV21FastSummary(candidates, trades) : [];
        var v21Answers = includeV21Summary ? BuildV21ResearchAnswers(outcome, timing, v21Summary, symbol) : [];

        return new RangeExpansionV2ExtendedDiagnostics(outcome, timing, symbol, v21Summary, v21Answers);
    }

    private static string ExtractVariantLabel(string profileName)
    {
        const string prefix = "range-expansion-v21-bnb-";
        if (profileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return profileName[prefix.Length..].Replace("-1m-profitlock-90", "", StringComparison.OrdinalIgnoreCase);
        if (profileName.Contains("range-expansion-v2-BNB", StringComparison.OrdinalIgnoreCase))
            return "v2-bnb-baseline";
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
