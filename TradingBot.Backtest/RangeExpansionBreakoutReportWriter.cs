using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public static class RangeExpansionBreakoutAggregator
{
    private const int RepeatableLock90Threshold = 3;

    public static IReadOnlyList<RangeExpansionSummaryRow> BuildSummaries(
        string windowLabel,
        IReadOnlyList<RangeExpansionCandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades)
    {
        return candidates
            .GroupBy(c => $"{c.ProfileName}|{c.Interval}|{c.Symbol}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var profileTrades = trades.Where(t =>
                    string.Equals(t.ProfileName, first.ProfileName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(t.Interval, first.Interval, StringComparison.OrdinalIgnoreCase)
                    && t.Symbol == first.Symbol).ToArray();
                var lock90Reachable = g.Count(x => x.Lock90ReachableWithin60m);
                var lock90Executed = g.Count(x => x.Executed && x.Lock90ReachableWithin60m);
                var candidateCount = g.Count();
                return new RangeExpansionSummaryRow
                {
                    WindowLabel = windowLabel,
                    Interval = first.Interval,
                    ProfileName = first.ProfileName,
                    Symbol = first.Symbol,
                    CandidateCount = candidateCount,
                    ExecutedCount = g.Count(x => x.Executed),
                    BlockedCount = g.Count(x => !x.Executed),
                    Lock90ReachableCount = lock90Reachable,
                    Lock90ReachableExecutedCount = lock90Executed,
                    Lock90ReachableRate = candidateCount == 0 ? 0m : Math.Round(lock90Reachable * 100m / candidateCount, 2),
                    InflationRate = candidateCount == 0 ? 0m : Math.Round(g.Count(x => x.ExpectedMoveInflated) * 100m / candidateCount, 2),
                    MedianExpectedMovePercent = Median(g.Select(x => x.ExpectedMovePercent)),
                    MedianForwardMfe60Percent = Median(g.Select(x => x.ForwardMfe60Percent)),
                    EstimatedNetPnlQuote = profileTrades.Sum(t => t.NetPnlQuote),
                    TradesCount = profileTrades.Length,
                    RepeatabilityVerdict = ClassifyRepeatability(lock90Reachable, candidateCount)
                };
            })
            .OrderBy(x => x.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Interval, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Symbol)
            .ToArray();
    }

    public static IReadOnlyList<RangeExpansionRobustnessRow> BuildRobustnessSummaries(
        IReadOnlyList<RangeExpansionSummaryRow> windowSummaries)
    {
        return windowSummaries
            .GroupBy(x => $"{x.ProfileName}|{x.Symbol}|{x.Interval}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var lock90ByWindow = g.ToDictionary(x => x.WindowLabel, x => x.Lock90ReachableCount, StringComparer.OrdinalIgnoreCase);
                var pnlByWindow = g.ToDictionary(x => x.WindowLabel, x => x.EstimatedNetPnlQuote, StringComparer.OrdinalIgnoreCase);
                var totalLock90 = g.Sum(x => x.Lock90ReachableCount);
                var totalCandidates = g.Sum(x => x.CandidateCount);
                var windowsWithRepeatable = g.Count(x => x.Lock90ReachableCount >= RepeatableLock90Threshold);
                var verdict = windowsWithRepeatable >= 2 && totalLock90 >= RepeatableLock90Threshold * 2
                    ? "RepeatableLock90"
                    : totalCandidates <= 2
                        ? "TooSparse"
                        : "NonRepeatable";

                return new RangeExpansionRobustnessRow
                {
                    ProfileName = first.ProfileName,
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    TotalCandidates = totalCandidates,
                    TotalLock90Reachable = totalLock90,
                    TotalNetPnlQuote = g.Sum(x => x.EstimatedNetPnlQuote),
                    Lock90ReachableByWindow = lock90ByWindow,
                    NetPnlByWindow = pnlByWindow,
                    FamilyVerdict = verdict
                };
            })
            .OrderBy(x => x.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Interval, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Symbol)
            .ToArray();
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildResearchAnswers(
        IReadOnlyList<RangeExpansionCandidateRecord> candidates,
        IReadOnlyList<RangeExpansionRobustnessRow> robustnessSummaries)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var totalCandidates = candidates.Count;
        var reachableLock90 = candidates.Count(c => c.Lock90ReachableWithin60m);
        var executed = candidates.Count(c => c.Executed);
        var inflated = candidates.Count(c => c.ExpectedMoveInflated);
        var repeatablePairs = robustnessSummaries
            .Where(r => string.Equals(r.FamilyVerdict, "RepeatableLock90", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does RangeExpansionBreakoutV1 produce more than isolated trades?",
            Answer = $"Candidates={totalCandidates}, executed={executed}, blocked={totalCandidates - executed}.",
            Verdict = totalCandidates <= 2 ? "TooSparse" : totalCandidates >= 10 ? "AdequateVolume" : "LowVolume",
            Details = new Dictionary<string, object?>
            {
                ["candidateCount"] = totalCandidates,
                ["executedCount"] = executed
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does any symbol/interval show repeated reachable lock90 candidates across 30d/60d/90d?",
            Answer = repeatablePairs.Length > 0
                ? $"Found {repeatablePairs.Length} profile(s) with repeatable lock90 reachability."
                : "No symbol/interval pair met repeatability threshold across windows.",
            Verdict = repeatablePairs.Length > 0 ? "RepeatablePattern" : "FailedRepeatability",
            Details = new Dictionary<string, object?>
            {
                ["repeatableProfileCount"] = repeatablePairs.Length,
                ["repeatableProfiles"] = repeatablePairs.Select(p => p.ProfileName).Distinct().ToArray()
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are expected moves inflated versus realized forward MFE60?",
            Answer = inflated == 0
                ? "No inflated-target candidates detected at 1.25x MFE60 threshold."
                : $"{inflated} candidate(s) had ExpectedMove > 1.25x ForwardMfe60.",
            Verdict = totalCandidates == 0 ? "NoData" : inflated * 100m / totalCandidates <= 20m ? "AcceptableInflation" : "InflatedTargets",
            Details = new Dictionary<string, object?>
            {
                ["inflatedCount"] = inflated,
                ["inflationRate"] = totalCandidates == 0 ? 0m : Math.Round(inflated * 100m / totalCandidates, 2)
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Should RangeExpansionBreakoutV1 continue or move to another entry model?",
            Answer = repeatablePairs.Length > 0
                ? "At least one pair shows repeatable reachable lock90 candidates; continue tuning within this family."
                : "No pair produced repeated reachable lock90 candidates; mark RangeExpansionBreakoutV1 as failed and try another model.",
            Verdict = repeatablePairs.Length > 0 ? "ContinueFamily" : "FamilyFailed",
            Details = new Dictionary<string, object?>
            {
                ["reachableLock90Count"] = reachableLock90,
                ["repeatablePairCount"] = repeatablePairs.Length
            }
        });

        return answers;
    }

    private static string ClassifyRepeatability(int lock90ReachableCount, int candidateCount)
    {
        if (candidateCount == 0)
            return "NoCandidates";
        if (lock90ReachableCount >= RepeatableLock90Threshold)
            return "RepeatableLock90";
        if (lock90ReachableCount >= 1)
            return "SparseReachable";
        return "NoReachableLock90";
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

public sealed class RangeExpansionBreakoutReportWriter(string outputDirectory)
{
    public async Task WriteAsync(
        IReadOnlyList<RangeExpansionSummaryRow> summaries,
        IReadOnlyList<RangeExpansionCandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<BlockedEntryRecord> blockedEntries,
        IReadOnlyList<RangeExpansionRobustnessRow> robustnessSummaries,
        IReadOnlyList<ReachabilityResearchAnswer> researchAnswers,
        CancellationToken cancellationToken,
        RangeExpansionDiagnosticsBundle? diagnostics = null)
    {
        Directory.CreateDirectory(outputDirectory);

        var executedCandidates = candidates.Where(c => c.Executed).ToArray();
        var blockedCandidates = candidates.Where(c => !c.Executed).ToArray();
        var reachabilityDetails = candidates.ToArray();

        await WriteJsonAsync("range-expansion-summary.json", summaries, cancellationToken);
        await WriteJsonAsync("range-expansion-trades.json", MergeTradeDetails(executedCandidates, trades), cancellationToken);
        await WriteJsonAsync("range-expansion-blocked-candidates.json", blockedCandidates, cancellationToken);
        await WriteJsonAsync("range-expansion-reachability-details.json", reachabilityDetails, cancellationToken);
        await WriteJsonAsync("range-expansion-robustness-summary.json", robustnessSummaries, cancellationToken);
        await WriteJsonAsync("range-expansion-research-answers.json", researchAnswers, cancellationToken);

        if (diagnostics is not null)
        {
            await WriteJsonAsync("range-expansion-pnl-decomposition.json", diagnostics.PnlDecomposition, cancellationToken);
            await WriteJsonAsync("range-expansion-trade-quality-buckets.json", diagnostics.TradeQualityBuckets, cancellationToken);
            await WriteJsonAsync("range-expansion-winner-loser-comparison.json", diagnostics.WinnerLoserComparison, cancellationToken);
            await WriteJsonAsync("range-expansion-blocked-reachable-analysis.json", diagnostics.BlockedReachable, cancellationToken);
            await WriteJsonAsync("range-expansion-executed-trade-cost-analysis.json", diagnostics.ExecutedTradeCostAnalysis, cancellationToken);
            await WriteJsonAsync("range-expansion-reporting-consistency.json", diagnostics.ReportingConsistency, cancellationToken);
            await WriteJsonAsync("range-expansion-target-floor-comparison.json", diagnostics.TargetFloorComparison, cancellationToken);
            await WriteJsonAsync("range-expansion-v2-recommendation.json", diagnostics.V2Recommendation, cancellationToken);
            await WriteJsonAsync("range-expansion-diagnostic-answers.json", diagnostics.DiagnosticAnswers, cancellationToken);
            await WritePnlDecompositionCsvAsync("range-expansion-pnl-decomposition.csv", diagnostics.PnlDecomposition, cancellationToken);
            await WriteTradeQualityCsvAsync("range-expansion-trade-quality-buckets.csv", diagnostics.TradeQualityBuckets, cancellationToken);
            await WriteWinnerLoserCsvAsync("range-expansion-winner-loser-comparison.csv", diagnostics.WinnerLoserComparison, cancellationToken);
            await WriteBlockedReachableCsvAsync("range-expansion-blocked-reachable-analysis.csv", diagnostics.BlockedReachable, cancellationToken);
            await WriteExecutedTradeCostCsvAsync("range-expansion-executed-trade-cost-analysis.csv", diagnostics.ExecutedTradeCostAnalysis, cancellationToken);
            await WriteReportingConsistencyCsvAsync("range-expansion-reporting-consistency.csv", diagnostics.ReportingConsistency, cancellationToken);
            await WriteTargetFloorComparisonCsvAsync("range-expansion-target-floor-comparison.csv", diagnostics.TargetFloorComparison, cancellationToken);
        }

        await WriteCandidatesCsvAsync("range-expansion-summary.csv", summaries, cancellationToken);
        await WriteCandidateRecordsCsvAsync("range-expansion-trades.csv", MergeTradeDetails(executedCandidates, trades), cancellationToken);
        await WriteCandidateRecordsCsvAsync("range-expansion-blocked-candidates.csv", blockedCandidates, cancellationToken);
        await WriteCandidateRecordsCsvAsync("range-expansion-reachability-details.csv", reachabilityDetails, cancellationToken);
        await WriteRobustnessCsvAsync("range-expansion-robustness-summary.csv", robustnessSummaries, cancellationToken);

        var standardWriter = new ReplayReportWriter(outputDirectory);
        await standardWriter.WriteAsync([], trades, blockedEntries, [], cancellationToken);
    }

    private static IReadOnlyList<RangeExpansionCandidateRecord> MergeTradeDetails(
        IReadOnlyList<RangeExpansionCandidateRecord> executedCandidates,
        IReadOnlyList<SimulatedTrade> trades)
    {
        var tradeLookup = trades
            .GroupBy(t => $"{t.ProfileName}|{t.Interval}|{t.Symbol}|{t.EntryTimeUtc:O}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return executedCandidates
            .Select(candidate =>
            {
                var key = $"{candidate.ProfileName}|{candidate.Interval}|{candidate.Symbol}|{candidate.TimeUtc:O}";
                if (!tradeLookup.TryGetValue(key, out var trade))
                    return candidate;

                return candidate with
                {
                    NetPnlQuote = trade.NetPnlQuote,
                    GrossPnlQuote = trade.GrossPnlQuote,
                    FeeAndSpreadEstimateQuote = trade.FeeAndSpreadEstimateQuote,
                    ExitReason = string.IsNullOrWhiteSpace(trade.ExitReason) ? "UnknownExit" : trade.ExitReason,
                    ExitPolicyName = trade.ExitPolicyName,
                    ProfitLockThresholdPercent = trade.ProfitLockThresholdPercent,
                    MfePercent = trade.MfePercent,
                    MaePercent = trade.MaePercent,
                    GivebackFromMfePercent = trade.GivebackFromMfePercent,
                    CapturedMfePercent = trade.CapturedMfePercent,
                    DurationMinutes = trade.DurationMinutes,
                    IsProfitLockExit = string.Equals(trade.ExitReason, "ProfitLock", StringComparison.OrdinalIgnoreCase),
                    IsWinner = trade.NetPnlQuote > 0m
                };
            })
            .ToArray();
    }

    private async Task WriteJsonAsync<T>(string fileName, T value, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, fileName),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private async Task WriteCandidatesCsvAsync(
        string fileName,
        IReadOnlyList<RangeExpansionSummaryRow> summaries,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("windowLabel,interval,profileName,symbol,candidateCount,executedCount,blockedCount,lock90ReachableCount,lock90ReachableExecutedCount,lock90ReachableRate,inflationRate,medianExpectedMovePercent,medianForwardMfe60Percent,estimatedNetPnlQuote,tradesCount,repeatabilityVerdict");
        foreach (var row in summaries)
        {
            sb.AppendLine(string.Join(",",
                row.WindowLabel,
                row.Interval,
                Escape(row.ProfileName),
                row.Symbol,
                row.CandidateCount,
                row.ExecutedCount,
                row.BlockedCount,
                row.Lock90ReachableCount,
                row.Lock90ReachableExecutedCount,
                row.Lock90ReachableRate.ToString(CultureInfo.InvariantCulture),
                row.InflationRate.ToString(CultureInfo.InvariantCulture),
                FormatNullable(row.MedianExpectedMovePercent),
                FormatNullable(row.MedianForwardMfe60Percent),
                row.EstimatedNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.TradesCount,
                row.RepeatabilityVerdict));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private async Task WriteCandidateRecordsCsvAsync(
        string fileName,
        IReadOnlyList<RangeExpansionCandidateRecord> records,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("interval,profileName,symbols,symbol,timeUtc,executed,rejectionLayer,rejectionReason,entryPrice,rangeHigh,rangeLow,rangeWidthPercent,breakoutBufferPercent,breakoutClose,breakoutConfirmed,followThroughConfirmed,distanceFromBreakoutPercent,atrPercent,targetModelName,expectedMovePercent,lock90DistancePercent,lock95DistancePercent,lock98DistancePercent,targetWasCapped,capReason,maxAllowedLockDistancePercent,forwardMfe15Percent,forwardMfe30Percent,forwardMfe60Percent,forwardMae15Percent,forwardMae30Percent,forwardMae60Percent,lock90ReachableWithin60m,lock95ReachableWithin60m,lock98ReachableWithin60m,timeToLock90Minutes,timeToLock95Minutes,timeToLock98Minutes,expectedMoveInflated,estimatedRoundTripCostPercent,requiredNetProfitPercent,requiredGrossProfitPercent,lock90NetProfitPercent,lock95NetProfitPercent,lock98NetProfitPercent,lock90ReachableAndNetProfitableWithin60m,lock95ReachableAndNetProfitableWithin60m,lock98ReachableAndNetProfitableWithin60m,forwardMfe60NetTradable,netPnlQuote,grossPnlQuote,feeAndSpreadEstimateQuote,exitReason,exitPolicyName,profitLockThresholdPercent,isProfitLockExit,durationMinutes,excludedFromPnlAggregates,reportingConsistencyFlag");
        foreach (var row in records)
        {
            sb.AppendLine(string.Join(",",
                row.Interval,
                Escape(row.ProfileName),
                Escape(row.Symbols),
                row.Symbol,
                row.TimeUtc.ToString("O", CultureInfo.InvariantCulture),
                row.Executed,
                row.RejectionLayer,
                Escape(row.RejectionReason),
                row.EntryPrice.ToString(CultureInfo.InvariantCulture),
                row.RangeHigh.ToString(CultureInfo.InvariantCulture),
                row.RangeLow.ToString(CultureInfo.InvariantCulture),
                row.RangeWidthPercent.ToString(CultureInfo.InvariantCulture),
                row.BreakoutBufferPercent.ToString(CultureInfo.InvariantCulture),
                row.BreakoutClose.ToString(CultureInfo.InvariantCulture),
                row.BreakoutConfirmed,
                row.FollowThroughConfirmed,
                FormatNullable(row.DistanceFromBreakoutPercent),
                row.AtrPercent.ToString(CultureInfo.InvariantCulture),
                Escape(row.TargetModelName),
                FormatNullable(row.ExpectedMovePercent),
                FormatNullable(row.Lock90DistancePercent),
                FormatNullable(row.Lock95DistancePercent),
                FormatNullable(row.Lock98DistancePercent),
                row.TargetWasCapped,
                Escape(row.CapReason),
                FormatNullable(row.MaxAllowedLockDistancePercent),
                FormatNullable(row.ForwardMfe15Percent),
                FormatNullable(row.ForwardMfe30Percent),
                FormatNullable(row.ForwardMfe60Percent),
                FormatNullable(row.ForwardMae15Percent),
                FormatNullable(row.ForwardMae30Percent),
                FormatNullable(row.ForwardMae60Percent),
                row.Lock90ReachableWithin60m,
                row.Lock95ReachableWithin60m,
                row.Lock98ReachableWithin60m,
                FormatNullableInt(row.TimeToLock90Minutes),
                FormatNullableInt(row.TimeToLock95Minutes),
                FormatNullableInt(row.TimeToLock98Minutes),
                row.ExpectedMoveInflated,
                FormatNullable(row.EstimatedRoundTripCostPercent),
                FormatNullable(row.RequiredNetProfitPercent),
                FormatNullable(row.RequiredGrossProfitPercent),
                FormatNullable(row.Lock90NetProfitPercent),
                FormatNullable(row.Lock95NetProfitPercent),
                FormatNullable(row.Lock98NetProfitPercent),
                row.Lock90ReachableAndNetProfitableWithin60m,
                row.Lock95ReachableAndNetProfitableWithin60m,
                row.Lock98ReachableAndNetProfitableWithin60m,
                row.ForwardMfe60NetTradable,
                FormatNullable(row.NetPnlQuote),
                FormatNullable(row.GrossPnlQuote),
                FormatNullable(row.FeeAndSpreadEstimateQuote),
                Escape(row.ExitReason),
                Escape(row.ExitPolicyName),
                FormatNullable(row.ProfitLockThresholdPercent),
                row.IsProfitLockExit,
                row.DurationMinutes.ToString(CultureInfo.InvariantCulture),
                row.ExcludedFromPnlAggregates,
                Escape(row.ReportingConsistencyFlag)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private async Task WriteRobustnessCsvAsync(
        string fileName,
        IReadOnlyList<RangeExpansionRobustnessRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("profileName,symbol,interval,totalCandidates,totalLock90Reachable,totalNetPnlQuote,lock90ReachableByWindow,netPnlByWindow,familyVerdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.ProfileName),
                row.Symbol,
                row.Interval,
                row.TotalCandidates,
                row.TotalLock90Reachable,
                row.TotalNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                Escape(string.Join("|", row.Lock90ReachableByWindow.Select(kv => $"{kv.Key}={kv.Value}"))),
                Escape(string.Join("|", row.NetPnlByWindow.Select(kv => $"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}"))),
                row.FamilyVerdict));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (value.Contains(',') || value.Contains('"'))
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        return value;
    }

    private static string FormatNullable(decimal? value)
        => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private static string FormatNullableInt(int? value)
        => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private async Task WritePnlDecompositionCsvAsync(
        string fileName,
        IReadOnlyList<RangeExpansionPnlDecompositionRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("dimension,key,windowLabel,tradeCount,candidateCount,executedCount,blockedCount,netPnlQuote,grossPnlQuote,winRatePercent,avgNetPnlQuote");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.Dimension,
                Escape(row.Key),
                Escape(row.WindowLabel),
                row.TradeCount,
                row.CandidateCount,
                row.ExecutedCount,
                row.BlockedCount,
                row.NetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.GrossPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.WinRatePercent.ToString(CultureInfo.InvariantCulture),
                FormatNullable(row.AvgNetPnlQuote)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private async Task WriteTradeQualityCsvAsync(
        string fileName,
        IReadOnlyList<RangeExpansionTradeQualityBucketRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("bucket,count,netPnlQuote,winRatePercent,avgMfePercent,avgMaePercent,avgGivebackFromMfePercent,avgCapturedMfePercent,avgForwardMfe60Percent,avgForwardMae60Percent,avgTimeToLock90Minutes,avgDurationMinutes,lock90ReachedBeforeMaeThresholdCount,profitLockExitCount,oppositeSignalExitCount");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.Bucket,
                row.Count,
                row.NetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.WinRatePercent.ToString(CultureInfo.InvariantCulture),
                FormatNullable(row.AvgMfePercent),
                FormatNullable(row.AvgMaePercent),
                FormatNullable(row.AvgGivebackFromMfePercent),
                FormatNullable(row.AvgCapturedMfePercent),
                FormatNullable(row.AvgForwardMfe60Percent),
                FormatNullable(row.AvgForwardMae60Percent),
                FormatNullable(row.AvgTimeToLock90Minutes),
                FormatNullable(row.AvgDurationMinutes),
                row.Lock90ReachedBeforeMaeThresholdCount,
                row.ProfitLockExitCount,
                row.OppositeSignalExitCount));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private async Task WriteWinnerLoserCsvAsync(
        string fileName,
        IReadOnlyList<RangeExpansionWinnerLoserComparisonRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("bucket,count,avgRangeWidthPercent,avgDistanceFromBreakoutPercent,avgLock90DistancePercent,avgForwardMfe60Percent,avgForwardMae60Percent,avgTimeToLock90Minutes,avgTrendStrengthPercent,avgShortMaSlopePercent,avgCandidateAgeCandles,avgBreakoutCloseAboveRangePercent,avgBreakoutBodyStrengthPercent,avgNetPnlQuote");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.Bucket,
                row.Count,
                FormatNullable(row.AvgRangeWidthPercent),
                FormatNullable(row.AvgDistanceFromBreakoutPercent),
                FormatNullable(row.AvgLock90DistancePercent),
                FormatNullable(row.AvgForwardMfe60Percent),
                FormatNullable(row.AvgForwardMae60Percent),
                FormatNullable(row.AvgTimeToLock90Minutes),
                FormatNullable(row.AvgTrendStrengthPercent),
                FormatNullable(row.AvgShortMaSlopePercent),
                FormatNullable(row.AvgCandidateAgeCandles),
                FormatNullable(row.AvgBreakoutCloseAboveRangePercent),
                FormatNullable(row.AvgBreakoutBodyStrengthPercent),
                FormatNullable(row.AvgNetPnlQuote)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private async Task WriteBlockedReachableCsvAsync(
        string fileName,
        RangeExpansionBlockedReachableSummary summary,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("rejectionReason,blockedCount,blockedReachableCount,blockedReachableRate,lock90ReachableAndNetProfitableCount,lock90ReachableAndNetProfitableRate,medianLock90NetProfitPercent,medianForwardMfe60Percent,medianForwardMae60Percent,couldRelaxSafelyCount");
        foreach (var row in summary.ByReason)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.RejectionReason),
                row.BlockedCount,
                row.BlockedReachableCount,
                row.BlockedReachableRate.ToString(CultureInfo.InvariantCulture),
                row.Lock90ReachableAndNetProfitableCount,
                row.Lock90ReachableAndNetProfitableRate.ToString(CultureInfo.InvariantCulture),
                FormatNullable(row.MedianLock90NetProfitPercent),
                FormatNullable(row.MedianForwardMfe60Percent),
                FormatNullable(row.MedianForwardMae60Percent),
                row.CouldRelaxSafelyCount));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private async Task WriteExecutedTradeCostCsvAsync(
        string fileName,
        RangeExpansionExecutedTradeCostAnalysis analysis,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("symbolIntervalKey,executedCount,profitLockGrossPositiveNetNegativeCount,expectedMovePassedLock90BelowRequiredNetCount,netPnlQuote,medianLock90NetProfitPercent,medianLock90DistancePercent");
        foreach (var row in analysis.BySymbolInterval)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.SymbolIntervalKey),
                row.ExecutedCount,
                row.ProfitLockGrossPositiveNetNegativeCount,
                row.ExpectedMovePassedLock90BelowRequiredNetCount,
                row.NetPnlQuote.ToString(CultureInfo.InvariantCulture),
                FormatNullable(row.MedianLock90NetProfitPercent),
                FormatNullable(row.MedianLock90DistancePercent)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private async Task WriteReportingConsistencyCsvAsync(
        string fileName,
        RangeExpansionReportingConsistencyReport report,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("metric,value");
        sb.AppendLine($"nullExitReasonCount,{report.NullExitReasonCount}");
        sb.AppendLine($"unknownExitReasonCount,{report.UnknownExitReasonCount}");
        sb.AppendLine($"orphanExecutedCandidateCount,{report.OrphanExecutedCandidateCount}");
        sb.AppendLine($"excludedFromPnlAggregatesCount,{report.ExcludedFromPnlAggregatesCount}");
        sb.AppendLine($"profitLockExitMissingIsProfitLockFlagCount,{report.ProfitLockExitMissingIsProfitLockFlagCount}");
        sb.AppendLine($"zeroDurationNonSameCandleCount,{report.ZeroDurationNonSameCandleCount}");
        sb.AppendLine($"endOfDataExitCount,{report.EndOfDataExitCount}");
        sb.AppendLine($"endOfDataNetPnlQuote,{report.EndOfDataNetPnlQuote.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"endOfDataGrossPnlQuote,{report.EndOfDataGrossPnlQuote.ToString(CultureInfo.InvariantCulture)}");
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private async Task WriteTargetFloorComparisonCsvAsync(
        string fileName,
        IReadOnlyList<RangeExpansionTargetFloorComparisonRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("targetFloorMode,candidateCount,executedCount,blockedCount,netPnlQuote,grossPnlQuote,netWinnerCount");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.TargetFloorMode,
                row.CandidateCount,
                row.ExecutedCount,
                row.BlockedCount,
                row.NetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.GrossPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.NetWinnerCount));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }
}
