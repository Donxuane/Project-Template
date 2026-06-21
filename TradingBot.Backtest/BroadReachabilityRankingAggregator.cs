using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class BroadReachabilityRankingAggregator
{
    public const int RepeatableReachableLock90MinimumCount = 5;
    public const decimal RepeatableReachableLock90MinimumRate = 0.25m;

    public static IReadOnlyList<SymbolIntervalReachabilityRankingRow> BuildRankings(
        IReadOnlyList<CandidateReachabilityRecord> candidates)
    {
        return candidates
            .GroupBy(c => $"{c.Symbol}|{c.Interval}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var count = g.Count();
                var reachableCount = g.Count(x => x.Lock90Reachable);
                var inflatedCount = g.Count(x => x.ExpectedMoveInflated);
                var row = new SymbolIntervalReachabilityRankingRow
                {
                    Symbol = g.First().Symbol,
                    Interval = g.First().Interval,
                    CandidateCount = count,
                    ReachableLock90Count = reachableCount,
                    ReachableLock90Rate = count == 0 ? 0m : Math.Round((decimal)reachableCount / count, 6),
                    MedianForwardMfe60Percent = Median(g.Select(x => x.ForwardMfe60Percent)),
                    MedianForwardMae60Percent = Median(g.Select(x => x.ForwardMae60Percent)),
                    MedianLock90DistancePercent = Median(g.Where(x => x.Lock90Reachable).Select(x => x.Lock90DistancePercent)),
                    InflationRate = count == 0 ? 0m : Math.Round((decimal)inflatedCount / count, 6),
                    ConfidenceFalseNegativeCount = g.Count(x => x.ConfidenceFalseNegativeCandidate)
                };
                return row with
                {
                    NetReachabilityScore = ComputeNetReachabilityScore(row),
                    RepeatabilityVerdict = ClassifyRepeatability(row)
                };
            })
            .OrderByDescending(x => x.NetReachabilityScore)
            .ThenByDescending(x => x.ReachableLock90Count)
            .ThenBy(x => x.Symbol)
            .ThenBy(x => x.Interval, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static decimal ComputeNetReachabilityScore(SymbolIntervalReachabilityRankingRow row)
    {
        if (row.CandidateCount == 0)
            return 0m;

        var maePenalty = row.MedianForwardMae60Percent.HasValue && row.MedianForwardMae60Percent.Value < -0.30m
            ? Math.Abs(row.MedianForwardMae60Percent.Value + 0.30m) * 30m
            : 0m;

        return Math.Round(
            row.ReachableLock90Count * 10m
            + row.ReachableLock90Rate * 100m
            - row.InflationRate * 50m
            + row.ConfidenceFalseNegativeCount * 2m
            - maePenalty,
            4);
    }

    public static string ClassifyRepeatability(SymbolIntervalReachabilityRankingRow row)
    {
        if (row.CandidateCount == 0)
            return "NoCandidates";
        if (row.ReachableLock90Count >= RepeatableReachableLock90MinimumCount
            && row.ReachableLock90Rate >= RepeatableReachableLock90MinimumRate)
        {
            return "Repeatable";
        }

        if (row.ReachableLock90Count >= 2)
            return "Sparse";
        if (row.ReachableLock90Count == 1)
            return "Isolated";
        return "None";
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildDiscoveryAnswers(
        IReadOnlyList<CandidateReachabilityRecord> candidates,
        IReadOnlyList<SymbolIntervalReachabilityRankingRow> rankings)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var best = rankings.FirstOrDefault();
        var repeatable = rankings.Where(r => r.RepeatabilityVerdict == "Repeatable").ToArray();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which symbol + interval has the highest repeatable lock90 reachability?",
            Answer = best is null || best.CandidateCount == 0
                ? "No candidates were collected."
                : repeatable.Length > 0
                    ? $"Best repeatable pair(s): {string.Join(", ", repeatable.Select(r => $"{r.Symbol}/{r.Interval} ({r.ReachableLock90Count} reachable, rate={FormatPct(r.ReachableLock90Rate)})"))}."
                    : $"No pair met repeatability threshold (>={RepeatableReachableLock90MinimumCount} reachable and rate>={FormatPct(RepeatableReachableLock90MinimumRate)}). Top pair: {best.Symbol}/{best.Interval} with {best.ReachableLock90Count} reachable (rate={FormatPct(best.ReachableLock90Rate)}).",
            Verdict = repeatable.Length > 0 ? "RepeatablePairFound" : best?.ReachableLock90Count >= 2 ? "SparseOnly" : "TooSparse",
            Details = new Dictionary<string, object?>
            {
                ["topSymbol"] = best?.Symbol.ToString(),
                ["topInterval"] = best?.Interval,
                ["topReachableLock90Count"] = best?.ReachableLock90Count,
                ["topReachableLock90Rate"] = best?.ReachableLock90Rate,
                ["repeatablePairCount"] = repeatable.Length
            }
        });

        var leastInflated = rankings
            .Where(r => r.CandidateCount > 0)
            .OrderBy(r => r.InflationRate)
            .ThenByDescending(r => r.ReachableLock90Count)
            .FirstOrDefault();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are there symbols where ExpectedMove is less inflated?",
            Answer = leastInflated is null
                ? "No candidate cohorts available."
                : $"Lowest inflation rate: {leastInflated.Symbol}/{leastInflated.Interval} at {FormatPct(leastInflated.InflationRate)} ({leastInflated.CandidateCount} candidates, {leastInflated.ReachableLock90Count} lock90 reachable).",
            Verdict = leastInflated is not null && leastInflated.InflationRate <= 0.40m && leastInflated.ReachableLock90Count >= RepeatableReachableLock90MinimumCount
                ? "LessInflatedAndRepeatable"
                : leastInflated is not null && leastInflated.InflationRate <= 0.50m
                    ? "LessInflatedButSparse"
                    : "BroadlyInflated",
            Details = new Dictionary<string, object?>
            {
                ["leastInflatedSymbol"] = leastInflated?.Symbol.ToString(),
                ["leastInflatedInterval"] = leastInflated?.Interval,
                ["leastInflatedRate"] = leastInflated?.InflationRate
            }
        });

        var falseNegatives = candidates.Where(c => c.ConfidenceFalseNegativeCandidate).ToArray();
        var falseNegativeGroups = falseNegatives
            .GroupBy(c => $"{c.Symbol}|{c.Interval}")
            .Select(g => new { Key = g.Key, Count = g.Count(), Days = g.Select(x => x.TimeUtc.Date).Distinct().Count() })
            .OrderByDescending(g => g.Count)
            .ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are confidence false negatives repeatable anywhere?",
            Answer = falseNegatives.Length == 0
                ? "No confidence false-negative candidates detected across scanned symbols."
                : $"Total false negatives: {falseNegatives.Length}. Top groups: {string.Join("; ", falseNegativeGroups.Take(3).Select(g => $"{g.Key} count={g.Count}, days={g.Days}"))}.",
            Verdict = falseNegatives.Length >= 5 && falseNegativeGroups.Any(g => g.Count >= 3)
                ? "RepeatableFalseNegatives"
                : falseNegatives.Length <= 2
                    ? "TooSparse"
                    : "SparseFalseNegatives",
            Details = new Dictionary<string, object?>
            {
                ["totalFalseNegativeCount"] = falseNegatives.Length,
                ["groupCount"] = falseNegativeGroups.Length
            }
        });

        var totalCandidates = candidates.Count;
        var totalReachable = candidates.Count(c => c.Lock90Reachable);
        var anyRepeatable = rankings.Any(r => r.RepeatabilityVerdict == "Repeatable");
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Do existing strategy candidates have enough reachable opportunities, or do we need a new strategy model entirely?",
            Answer = anyRepeatable
                ? $"Some symbol/interval pairs show repeatable lock90 reachability ({repeatable.Length} pair(s)). Existing strategy may be worth targeted follow-up on those pairs only."
                : $"Across {rankings.Count(r => r.CandidateCount > 0)} active symbol/interval pairs, only {totalReachable}/{totalCandidates} candidates reached lock90 within 60m. No pair met repeatability threshold.",
            Verdict = anyRepeatable ? "TargetedFollowUpPossible" : "ReplaceStrategyFamily",
            Details = new Dictionary<string, object?>
            {
                ["totalCandidates"] = totalCandidates,
                ["totalReachableLock90"] = totalReachable,
                ["repeatablePairCount"] = repeatable.Length,
                ["activePairCount"] = rankings.Count(r => r.CandidateCount > 0)
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

    private static string FormatPct(decimal value)
        => (value * 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";
}

public sealed class BroadReachabilityReportWriter(string outputDirectory)
{
    public async Task WriteAsync(
        IReadOnlyList<CandidateReachabilityRecord> candidates,
        IReadOnlyList<SymbolIntervalReachabilityRankingRow> rankings,
        IReadOnlyList<ReachabilityResearchAnswer> discoveryAnswers,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var falseNegatives = candidates.Where(c => c.ConfidenceFalseNegativeCandidate).ToArray();
        var inflatedTargets = candidates.Where(c => c.ExpectedMoveInflated).ToArray();

        await WriteJsonAsync(Path.Combine(outputDirectory, "symbol-interval-reachability-ranking.json"), rankings, cancellationToken);
        await WriteJsonAsync(Path.Combine(outputDirectory, "candidate-reachability-details.json"), candidates, cancellationToken);
        await WriteJsonAsync(Path.Combine(outputDirectory, "confidence-false-negatives.json"), falseNegatives, cancellationToken);
        await WriteJsonAsync(Path.Combine(outputDirectory, "inflated-target-candidates.json"), inflatedTargets, cancellationToken);
        await WriteJsonAsync(Path.Combine(outputDirectory, "broad-reachability-discovery-answers.json"), discoveryAnswers, cancellationToken);

        await WriteRankingCsvAsync(Path.Combine(outputDirectory, "symbol-interval-reachability-ranking.csv"), rankings, cancellationToken);
        await WriteDetailsCsvAsync(Path.Combine(outputDirectory, "candidate-reachability-details.csv"), candidates, cancellationToken);
        await WriteDetailsCsvAsync(Path.Combine(outputDirectory, "confidence-false-negatives.csv"), falseNegatives, cancellationToken);
        await WriteDetailsCsvAsync(Path.Combine(outputDirectory, "inflated-target-candidates.csv"), inflatedTargets, cancellationToken);
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static async Task WriteRankingCsvAsync(string path, IReadOnlyList<SymbolIntervalReachabilityRankingRow> rankings, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("symbol,interval,candidateCount,reachableLock90Count,reachableLock90Rate,medianForwardMfe60Percent,medianForwardMae60Percent,medianLock90DistancePercent,inflationRate,confidenceFalseNegativeCount,netReachabilityScore,repeatabilityVerdict");
        foreach (var row in rankings)
        {
            sb.AppendLine(string.Join(",",
                row.Symbol,
                row.Interval,
                row.CandidateCount,
                row.ReachableLock90Count,
                row.ReachableLock90Rate,
                ToCsv(row.MedianForwardMfe60Percent),
                ToCsv(row.MedianForwardMae60Percent),
                ToCsv(row.MedianLock90DistancePercent),
                row.InflationRate,
                row.ConfidenceFalseNegativeCount,
                row.NetReachabilityScore,
                row.RepeatabilityVerdict));
        }

        await File.WriteAllTextAsync(path, sb.ToString(), cancellationToken);
    }

    private static async Task WriteDetailsCsvAsync(string path, IReadOnlyList<CandidateReachabilityRecord> candidates, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("interval,profileName,symbols,symbol,timeUtc,signalReason,rejectionLayer,rejectionReason,executed,confidence,confidenceThreshold,expectedMovePercent,estimatedNetMovePercent,expectedTargetSource,rewardRisk,distanceToInvalidationPercent,trendStrengthPercent,shortMaSlopePercent,consecutiveBullishTrendCandles,entryNearRecentHigh,previousCandleBearish,volatilityRegime,entryPrice,forwardMfe15Percent,forwardMfe30Percent,forwardMfe60Percent,forwardMae15Percent,forwardMae30Percent,forwardMae60Percent,lock90DistancePercent,lock95DistancePercent,lock98DistancePercent,lock90ReachableWithin60m,lock95ReachableWithin60m,lock98ReachableWithin60m,timeToLock90Minutes,timeToLock95Minutes,timeToLock98Minutes,expectedMoveInflated,lock90Reachable,lock95Reachable,lock98Reachable,favorableButNetUntradable,confidenceFalseNegativeCandidate");
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
