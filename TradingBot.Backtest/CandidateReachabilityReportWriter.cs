using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class CandidateReachabilityAggregator
{
    public static IReadOnlyList<CandidateReachabilitySummaryRow> BuildSummaries(
        IReadOnlyList<CandidateReachabilityRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades)
    {
        return candidates
            .GroupBy(c => $"{c.ProfileName}|{c.Interval}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var profileTrades = trades.Where(t =>
                    string.Equals(t.ProfileName, g.First().ProfileName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(t.Interval, g.First().Interval, StringComparison.OrdinalIgnoreCase)).ToArray();
                var reachableLock90 = g.Where(x => x.Lock90Reachable).Select(x => x.Lock90DistancePercent).Where(x => x.HasValue).Select(x => x!.Value).OrderBy(x => x).ToArray();
                return new CandidateReachabilitySummaryRow
                {
                    Interval = g.First().Interval,
                    ProfileName = g.First().ProfileName,
                    CandidateCount = g.Count(),
                    ExecutedCount = g.Count(x => x.Executed),
                    ConfidenceBlockedCount = g.Count(x => string.Equals(x.RejectionReason, BacktestEntryGuard.ConfidenceBelowThreshold, StringComparison.OrdinalIgnoreCase)),
                    ConfidenceFalseNegativeCount = g.Count(x => x.ConfidenceFalseNegativeCandidate),
                    ExpectedMoveInflatedCount = g.Count(x => x.ExpectedMoveInflated),
                    Lock90ReachableCount = g.Count(x => x.Lock90Reachable),
                    Lock90ReachableButBlockedCount = g.Count(x => x.Lock90Reachable && !x.Executed),
                    FavorableButNetUntradableCount = g.Count(x => x.FavorableButNetUntradable),
                    MedianExpectedMovePercent = Median(g.Select(x => x.ExpectedMovePercent)),
                    MedianForwardMfe60Percent = Median(g.Select(x => x.ForwardMfe60Percent)),
                    MedianLock90DistancePercent = Median(g.Select(x => x.Lock90DistancePercent)),
                    MedianReachableLock90DistancePercent = Median(reachableLock90),
                    WinnerCount = profileTrades.Count(t => t.NetPnlQuote > 0m),
                    LoserCount = profileTrades.Count(t => t.NetPnlQuote <= 0m),
                    EstimatedNetPnlQuote = profileTrades.Sum(t => t.NetPnlQuote)
                };
            })
            .OrderBy(x => x.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Interval, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildResearchAnswers(
        IReadOnlyList<CandidateReachabilityRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var mar9FalseNegatives = candidates.Where(c =>
            c.ConfidenceFalseNegativeCandidate
            && c.TimeUtc >= new DateTime(2026, 3, 9, 8, 5, 0, DateTimeKind.Utc)
            && c.TimeUtc <= new DateTime(2026, 3, 9, 8, 8, 0, DateTimeKind.Utc)).ToArray();
        var allFalseNegatives = candidates.Where(c => c.ConfidenceFalseNegativeCandidate).ToArray();
        var mar9DistinctDays = allFalseNegatives
            .Select(c => c.TimeUtc.Date)
            .Distinct()
            .Count();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are Mar 9 BNB 1m confidence false negatives repeatable or isolated?",
            Answer = mar9FalseNegatives.Length > 0
                ? $"Found {mar9FalseNegatives.Length} Mar 9 1m confidence false-negative candidate(s) and {allFalseNegatives.Length} total across all intervals/profiles."
                : "No Mar 9 1m confidence false-negative candidates found in this run.",
            Verdict = allFalseNegatives.Length <= 2 ? "TooSparse" : mar9DistinctDays >= 2 ? "RepeatablePattern" : "Isolated",
            Details = new Dictionary<string, object?>
            {
                ["mar9Count"] = mar9FalseNegatives.Length,
                ["totalFalseNegativeCount"] = allFalseNegatives.Length,
                ["distinctFalseNegativeDays"] = mar9DistinctDays
            }
        });

        var badFalseNegatives = allFalseNegatives.Count(c =>
            c.ForwardMae60Percent.HasValue && c.ForwardMae60Percent.Value < -0.20m);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are confidence false negatives mostly safe or do they include many bad setups?",
            Answer = allFalseNegatives.Length == 0
                ? "No confidence false-negative candidates detected."
                : $"{allFalseNegatives.Length - badFalseNegatives} of {allFalseNegatives.Length} had forward MAE60 >= -0.20%.",
            Verdict = badFalseNegatives > allFalseNegatives.Length / 2 ? "MostlyUnsafe" : allFalseNegatives.Length <= 2 ? "TooSparse" : "MostlySafe",
            Details = new Dictionary<string, object?>
            {
                ["totalFalseNegativeCount"] = allFalseNegatives.Length,
                ["badFalseNegativeCount"] = badFalseNegatives
            }
        });

        var reachable = candidates.Where(c => c.Lock90Reachable).ToArray();
        var inflated = candidates.Where(c => c.ExpectedMoveInflated && !c.Executed).ToArray();
        var reachableMedianEm = Median(reachable.Select(c => c.ExpectedMovePercent));
        var inflatedMedianEm = Median(inflated.Select(c => c.ExpectedMovePercent));
        var reachableMedianInvalidation = Median(reachable.Select(c => c.DistanceToInvalidationPercent));
        var inflatedMedianInvalidation = Median(inflated.Select(c => c.DistanceToInvalidationPercent));

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which fields separate reachable-lock candidates from inflated-target failures?",
            Answer = reachable.Length == 0 || inflated.Length == 0
                ? "Insufficient paired samples to compare reachable vs inflated cohorts."
                : $"Reachable median EM={FormatNullable(reachableMedianEm)}%, invalidation={FormatNullable(reachableMedianInvalidation)}%. Inflated blocked median EM={FormatNullable(inflatedMedianEm)}%, invalidation={FormatNullable(inflatedMedianInvalidation)}%.",
            Verdict = reachable.Length >= 3 && inflated.Length >= 3 ? "SeparatorsObserved" : "InsufficientData",
            Details = new Dictionary<string, object?>
            {
                ["reachableCount"] = reachable.Length,
                ["inflatedBlockedCount"] = inflated.Length,
                ["reachableMedianExpectedMovePercent"] = reachableMedianEm,
                ["inflatedMedianExpectedMovePercent"] = inflatedMedianEm,
                ["reachableMedianDistanceToInvalidationPercent"] = reachableMedianInvalidation,
                ["inflatedMedianDistanceToInvalidationPercent"] = inflatedMedianInvalidation
            }
        });

        foreach (var interval in new[] { "1m", "3m", "5m" })
        {
            var intervalReachable = candidates.Where(c => c.Interval == interval && c.Lock90Reachable).ToArray();
            var medianLock90 = Median(intervalReachable.Select(c => c.Lock90DistancePercent));
            answers.Add(new ReachabilityResearchAnswer
            {
                Question = $"Is there a reachable target size for BNB {interval} that appears repeatedly?",
                Answer = intervalReachable.Length == 0
                    ? $"No lock90-reachable candidates on {interval} in this window."
                    : $"{intervalReachable.Length} lock90-reachable candidate(s); median lock90 distance={FormatNullable(medianLock90)}%.",
                Verdict = intervalReachable.Length >= 3 ? "RepeatedReachableSize" : "TooSparse",
                Details = new Dictionary<string, object?>
                {
                    ["interval"] = interval,
                    ["reachableCount"] = intervalReachable.Length,
                    ["medianLock90DistancePercent"] = medianLock90
                }
            });
        }

        var relaxedProfile = candidates.Where(c => c.ProfileName.Contains("confidence-relaxed", StringComparison.OrdinalIgnoreCase)).ToArray();
        var relaxedWinners = trades.Where(t =>
            t.ProfileName.Contains("confidence-relaxed", StringComparison.OrdinalIgnoreCase) && t.NetPnlQuote > 0m).ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does a confidence-adjusted but reachability-gated profile produce more than one winner across 90d?",
            Answer = relaxedProfile.Length == 0
                ? "Experimental confidence-relaxed profile was not run or produced no candidates."
                : $"Relaxed profile: {relaxedWinners.Length} winner(s), {trades.Count(t => t.ProfileName.Contains("confidence-relaxed", StringComparison.OrdinalIgnoreCase))} total trade(s).",
            Verdict = relaxedWinners.Length >= 2 ? "MultipleWinners" : relaxedWinners.Length == 1 ? "SingleWinnerOverfit" : relaxedProfile.Length == 0 ? "NotRun" : "NoWinners",
            Details = new Dictionary<string, object?>
            {
                ["relaxedCandidateCount"] = relaxedProfile.Length,
                ["relaxedWinnerCount"] = relaxedWinners.Length,
                ["relaxedTradeCount"] = trades.Count(t => t.ProfileName.Contains("confidence-relaxed", StringComparison.OrdinalIgnoreCase))
            }
        });

        return answers;
    }

    private static decimal? Median(IEnumerable<decimal?> values)
    {
        var sorted = values.Where(v => v.HasValue).Select(v => v!.Value).OrderBy(v => v).ToArray();
        if (sorted.Length == 0)
            return null;
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2m;
    }

    private static decimal? Median(IEnumerable<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0)
            return null;
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2m;
    }

    private static string FormatNullable(decimal? value)
        => value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "n/a";
}

public sealed class CandidateReachabilityReportWriter(string outputDirectory)
{
    public async Task WriteAsync(
        IReadOnlyList<CandidateReachabilityRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var summaries = CandidateReachabilityAggregator.BuildSummaries(candidates, trades);
        var answers = CandidateReachabilityAggregator.BuildResearchAnswers(candidates, trades);
        var falseNegatives = candidates.Where(c => c.ConfidenceFalseNegativeCandidate).ToArray();
        var inflatedTargets = candidates.Where(c => c.ExpectedMoveInflated).ToArray();

        await WriteJsonAsync(Path.Combine(outputDirectory, "candidate-reachability-details.json"), candidates, cancellationToken);
        await WriteJsonAsync(Path.Combine(outputDirectory, "candidate-reachability-summary.json"), summaries, cancellationToken);
        await WriteJsonAsync(Path.Combine(outputDirectory, "confidence-false-negatives.json"), falseNegatives, cancellationToken);
        await WriteJsonAsync(Path.Combine(outputDirectory, "inflated-target-candidates.json"), inflatedTargets, cancellationToken);
        await WriteJsonAsync(Path.Combine(outputDirectory, "reachability-research-answers.json"), answers, cancellationToken);

        await WriteDetailsCsvAsync(Path.Combine(outputDirectory, "candidate-reachability-details.csv"), candidates, cancellationToken);
        await WriteSummaryCsvAsync(Path.Combine(outputDirectory, "candidate-reachability-summary.csv"), summaries, cancellationToken);
        await WriteDetailsCsvAsync(Path.Combine(outputDirectory, "confidence-false-negatives.csv"), falseNegatives, cancellationToken);
        await WriteDetailsCsvAsync(Path.Combine(outputDirectory, "inflated-target-candidates.csv"), inflatedTargets, cancellationToken);
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static async Task WriteSummaryCsvAsync(string path, IReadOnlyList<CandidateReachabilitySummaryRow> summaries, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("interval,profileName,candidateCount,executedCount,confidenceBlockedCount,confidenceFalseNegativeCount,expectedMoveInflatedCount,lock90ReachableCount,lock90ReachableButBlockedCount,favorableButNetUntradableCount,medianExpectedMovePercent,medianForwardMfe60Percent,medianLock90DistancePercent,medianReachableLock90DistancePercent,winnerCount,loserCount,estimatedNetPnlQuote");
        foreach (var row in summaries)
        {
            sb.AppendLine(string.Join(",",
                row.Interval,
                Escape(row.ProfileName),
                row.CandidateCount,
                row.ExecutedCount,
                row.ConfidenceBlockedCount,
                row.ConfidenceFalseNegativeCount,
                row.ExpectedMoveInflatedCount,
                row.Lock90ReachableCount,
                row.Lock90ReachableButBlockedCount,
                row.FavorableButNetUntradableCount,
                ToCsv(row.MedianExpectedMovePercent),
                ToCsv(row.MedianForwardMfe60Percent),
                ToCsv(row.MedianLock90DistancePercent),
                ToCsv(row.MedianReachableLock90DistancePercent),
                row.WinnerCount,
                row.LoserCount,
                row.EstimatedNetPnlQuote));
        }

        await File.WriteAllTextAsync(path, sb.ToString(), cancellationToken);
    }

    private static async Task WriteDetailsCsvAsync(string path, IReadOnlyList<CandidateReachabilityRecord> candidates, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("interval,profileName,symbols,symbol,timeUtc,signalReason,rejectionLayer,rejectionReason,executed,confidence,confidenceThreshold,expectedMovePercent,estimatedNetMovePercent,expectedTargetPrice,expectedTargetSource,rewardRisk,distanceToInvalidationPercent,trendStrengthPercent,shortMaSlopePercent,consecutiveBullishTrendCandles,entryNearRecentHigh,previousCandleBearish,volatilityRegime,entryPrice,forwardMfe15Percent,forwardMfe30Percent,forwardMfe60Percent,forwardMae15Percent,forwardMae30Percent,forwardMae60Percent,lock90DistancePercent,lock95DistancePercent,lock98DistancePercent,lock90ReachableWithin60m,lock95ReachableWithin60m,lock98ReachableWithin60m,timeToLock90Minutes,timeToLock95Minutes,timeToLock98Minutes,expectedMoveInflated,lock90Reachable,lock95Reachable,lock98Reachable,favorableButNetUntradable,confidenceFalseNegativeCandidate");
        foreach (var c in candidates)
        {
            sb.AppendLine(string.Join(",",
                c.Interval,
                Escape(c.ProfileName),
                Escape(c.Symbols),
                c.Symbol,
                c.TimeUtc.ToString("O"),
                Escape(c.SignalReason),
                c.RejectionLayer,
                Escape(c.RejectionReason),
                c.Executed,
                c.Confidence,
                c.ConfidenceThreshold,
                ToCsv(c.ExpectedMovePercent),
                ToCsv(c.EstimatedNetMovePercent),
                ToCsv(c.ExpectedTargetPrice),
                Escape(c.ExpectedTargetSource),
                ToCsv(c.RewardRisk),
                ToCsv(c.DistanceToInvalidationPercent),
                ToCsv(c.TrendStrengthPercent),
                ToCsv(c.ShortMaSlopePercent),
                ToCsv(c.ConsecutiveBullishTrendCandles),
                ToCsv(c.EntryNearRecentHigh),
                ToCsv(c.PreviousCandleBearish),
                Escape(c.VolatilityRegime),
                c.EntryPrice,
                ToCsv(c.ForwardMfe15Percent),
                ToCsv(c.ForwardMfe30Percent),
                ToCsv(c.ForwardMfe60Percent),
                ToCsv(c.ForwardMae15Percent),
                ToCsv(c.ForwardMae30Percent),
                ToCsv(c.ForwardMae60Percent),
                ToCsv(c.Lock90DistancePercent),
                ToCsv(c.Lock95DistancePercent),
                ToCsv(c.Lock98DistancePercent),
                c.Lock90ReachableWithin60m,
                c.Lock95ReachableWithin60m,
                c.Lock98ReachableWithin60m,
                ToCsv(c.TimeToLock90Minutes),
                ToCsv(c.TimeToLock95Minutes),
                ToCsv(c.TimeToLock98Minutes),
                c.ExpectedMoveInflated,
                c.Lock90Reachable,
                c.Lock95Reachable,
                c.Lock98Reachable,
                c.FavorableButNetUntradable,
                c.ConfidenceFalseNegativeCandidate));
        }

        await File.WriteAllTextAsync(path, sb.ToString(), cancellationToken);
    }

    private static string ToCsv<T>(T? value) where T : struct => value?.ToString() ?? string.Empty;
    private static string ToCsv(int? value) => value?.ToString() ?? string.Empty;
    private static string ToCsv(bool? value) => value?.ToString() ?? string.Empty;

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
