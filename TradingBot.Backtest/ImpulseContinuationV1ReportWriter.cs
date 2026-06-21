using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class ImpulseContinuationV1Aggregator
{
    private const int RepeatableThreshold = 3;

    public static IReadOnlyList<ImpulseContinuationV1SummaryRow> BuildSummaries(
        string windowLabel,
        IReadOnlyList<ImpulseContinuationV1CandidateRecord> candidates,
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
                var executed = g.Where(x => x.Executed).ToArray();
                return new ImpulseContinuationV1SummaryRow
                {
                    WindowLabel = windowLabel,
                    Interval = first.Interval,
                    ProfileName = first.ProfileName,
                    Symbol = first.Symbol,
                    CandidateCount = g.Count(),
                    ExecutedCount = executed.Length,
                    BlockedCount = g.Count(x => !x.Executed),
                    Lock90MeetsRequiredGrossCount = g.Count(x => x.Lock90MeetsRequiredGross),
                    Lock90ReachableCount = g.Count(x => x.Lock90ReachableWithin60m),
                    EstimatedNetPnlQuote = profileTrades.Sum(t => t.NetPnlQuote),
                    TradesCount = profileTrades.Length,
                    NetWinnerCount = profileTrades.Count(t => t.NetPnlQuote > 0m),
                    AvgExpectedMovePercent = executed.Length == 0
                        ? null
                        : Math.Round(executed.Average(x => x.ExpectedMovePercent ?? 0m), 6),
                    RepeatabilityVerdict = g.Count(x => x.Lock90MeetsRequiredGross) >= RepeatableThreshold
                        ? "RepeatableCostAware"
                        : g.Count(x => x.Executed) >= 1 ? "Sparse" : "NoCandidates"
                };
            })
            .OrderBy(x => x.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Symbol)
            .ToArray();
    }

    public static IReadOnlyList<ImpulseContinuationV1ExitBreakdownRow> BuildExitBreakdown(IReadOnlyList<SimulatedTrade> trades)
    {
        return trades
            .GroupBy(t => t.ExitReason ?? "Unknown", StringComparer.OrdinalIgnoreCase)
            .Select(g => new ImpulseContinuationV1ExitBreakdownRow
            {
                ExitReason = g.Key,
                Count = g.Count(),
                NetPnlQuote = g.Sum(t => t.NetPnlQuote),
                GrossPnlQuote = g.Sum(t => t.GrossPnlQuote),
                AvgDurationMinutes = !g.Any() ? null : Math.Round(g.Average(t => t.DurationMinutes), 2)
            })
            .OrderByDescending(r => r.Count)
            .ToArray();
    }

    public static IReadOnlyList<ImpulseContinuationV1WindowRobustnessRow> BuildWindowRobustness(
        IReadOnlyList<ImpulseContinuationV1SummaryRow> summaries,
        IReadOnlyList<SimulatedTrade> trades)
    {
        return summaries
            .GroupBy(s => $"{s.ProfileName}|{s.Interval}|{s.Symbol}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var w30 = g.FirstOrDefault(x => x.WindowLabel == "30d");
                var w60 = g.FirstOrDefault(x => x.WindowLabel == "60d");
                var w90 = g.FirstOrDefault(x => x.WindowLabel == "90d");
                var profileTrades = trades.Where(t =>
                    string.Equals(t.ProfileName, first.ProfileName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(t.Interval, first.Interval, StringComparison.OrdinalIgnoreCase)
                    && t.Symbol == first.Symbol).ToArray();

                var hasRepeatable = new[] { w30, w60, w90 }.Count(w =>
                    w is not null && w.Lock90MeetsRequiredGrossCount >= RepeatableThreshold) >= 2;
                var netPositive = new[] { w30, w60, w90 }.Count(w =>
                    w is not null && w.EstimatedNetPnlQuote > 0m);

                return new ImpulseContinuationV1WindowRobustnessRow
                {
                    ProfileName = first.ProfileName,
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    Window30dCandidates = w30?.CandidateCount ?? 0,
                    Window60dCandidates = w60?.CandidateCount ?? 0,
                    Window90dCandidates = w90?.CandidateCount ?? 0,
                    Window30dTrades = w30?.TradesCount ?? 0,
                    Window60dTrades = w60?.TradesCount ?? 0,
                    Window90dTrades = w90?.TradesCount ?? 0,
                    Window30dNetPnl = w30?.EstimatedNetPnlQuote ?? 0m,
                    Window60dNetPnl = w60?.EstimatedNetPnlQuote ?? 0m,
                    Window90dNetPnl = w90?.EstimatedNetPnlQuote ?? 0m,
                    RobustnessVerdict = hasRepeatable
                        ? netPositive > 0 ? "RepeatableAndProfitable" : "RepeatableNotProfitable"
                        : profileTrades.Length >= RepeatableThreshold ? "SparseAcrossWindows" : "InsufficientSample"
                };
            })
            .OrderBy(x => x.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Symbol)
            .ToArray();
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildResearchAnswers(
        IReadOnlyList<ImpulseContinuationV1CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<ImpulseContinuationV1SummaryRow> summaries,
        IReadOnlyList<ImpulseContinuationV1ExitBreakdownRow> exitBreakdown,
        IReadOnlyList<ImpulseContinuationV1WindowRobustnessRow> windowRobustness)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var costAwareCandidates = candidates.Where(c => c.Lock90MeetsRequiredGross).ToArray();
        var repeatable = summaries
            .GroupBy(s => $"{s.ProfileName}|{s.Symbol}", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count(x => x.Lock90MeetsRequiredGrossCount >= RepeatableThreshold) >= 2)
            .ToArray();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does any symbol/interval produce repeated cost-aware impulse candidates?",
            Answer = repeatable.Length > 0
                ? $"{repeatable.Length} profile/symbol pair(s) show repeated cost-aware impulse candidates across windows."
                : $"Cost-aware candidates={costAwareCandidates.Length}; executed={candidates.Count(c => c.Executed)}.",
            Verdict = repeatable.Length > 0 ? "RepeatableCostAwareCandidates" : costAwareCandidates.Length > 0 ? "SparseCostAware" : "NoCostAwareCandidates",
            Details = new Dictionary<string, object?> { ["repeatablePairs"] = repeatable.Select(g => g.Key).ToArray() }
        });

        var executed = candidates.Where(c => c.Executed).ToArray();
        var medianLock90 = Median(executed.Select(c => c.Lock90DistancePercent));
        var medianCost = Median(candidates.Select(c => c.EstimatedRoundTripCostPercent));
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are impulse targets large enough to survive conservative Spot fees?",
            Answer = $"Executed median lock90={medianLock90:F4}%, median round-trip cost={medianCost:F4}%; cost-aware pass rate={costAwareCandidates.Length}/{candidates.Count}.",
            Verdict = medianLock90.HasValue && medianCost.HasValue && medianLock90.Value >= medianCost.Value + 0.10m
                ? "TargetsSurviveSpotCosts"
                : costAwareCandidates.Length > 0 ? "MarginalTargetSize" : "TargetsTooSmall",
            Details = new Dictionary<string, object?> { ["medianLock90"] = medianLock90, ["medianCost"] = medianCost }
        });

        var byInterval = summaries
            .GroupBy(s => s.Interval, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Interval = g.Key,
                NetPnl = g.Sum(x => x.EstimatedNetPnlQuote),
                Reachable = g.Sum(x => x.Lock90ReachableCount),
                Trades = g.Sum(x => x.TradesCount)
            })
            .OrderByDescending(x => x.NetPnl)
            .ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which interval has the best net PnL and best reachability?",
            Answer = byInterval.Length == 0
                ? "No summary data."
                : string.Join("; ", byInterval.Select(x => $"{x.Interval}: net={x.NetPnl:F8}, reachable={x.Reachable}, trades={x.Trades}")),
            Verdict = byInterval.FirstOrDefault()?.Interval ?? "NoData",
            Details = new Dictionary<string, object?> { ["byInterval"] = byInterval }
        });

        var stopLoss = exitBreakdown.FirstOrDefault(r => string.Equals(r.ExitReason, "StopLoss", StringComparison.OrdinalIgnoreCase));
        var timeStop = exitBreakdown.FirstOrDefault(r => string.Equals(r.ExitReason, "TimeStop", StringComparison.OrdinalIgnoreCase));
        var profitLock = exitBreakdown.FirstOrDefault(r => string.Equals(r.ExitReason, "ProfitLock", StringComparison.OrdinalIgnoreCase));
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are losers mostly stop-loss, time-stop, or fee drag?",
            Answer = $"StopLoss={stopLoss?.Count ?? 0} (net={stopLoss?.NetPnlQuote:F8}), TimeStop={timeStop?.Count ?? 0} (net={timeStop?.NetPnlQuote:F8}), ProfitLock={profitLock?.Count ?? 0} (net={profitLock?.NetPnlQuote:F8}).",
            Verdict = (stopLoss?.Count ?? 0) >= (timeStop?.Count ?? 0) ? "StopLossDominant" : "TimeStopDominant",
            Details = new Dictionary<string, object?> { ["exitBreakdown"] = exitBreakdown }
        });

        var impulseAvgMove = executed.Length == 0 ? (decimal?)null : executed.Average(c => c.ExpectedMovePercent ?? 0m);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does impulse continuation produce bigger average gross move than RangeExpansion V2?",
            Answer = impulseAvgMove.HasValue
                ? $"ImpulseContinuationV1 executed avg expected move={impulseAvgMove.Value:F4}% (V2 baseline ~0.8-1.2% depending on profile)."
                : "No executed trades to compare.",
            Verdict = impulseAvgMove is > 1.0m ? "LargerMovesThanV2Typical" : impulseAvgMove.HasValue ? "SimilarOrSmallerThanV2" : "NoData",
            Details = new Dictionary<string, object?> { ["avgExpectedMovePercent"] = impulseAvgMove }
        });

        var nearBreakeven = windowRobustness.Where(w =>
            w.Window30dNetPnl + w.Window60dNetPnl + w.Window90dNetPnl >= -0.5m
            && w.Window30dTrades + w.Window60dTrades + w.Window90dTrades >= RepeatableThreshold).ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is there any profile with positive or near-breakeven net PnL across 30d/60d/90d?",
            Answer = nearBreakeven.Length > 0
                ? $"{nearBreakeven.Length} profile(s) near breakeven or positive across windows."
                : $"All profiles negative; total net PnL={trades.Sum(t => t.NetPnlQuote):F8}.",
            Verdict = nearBreakeven.Any(w => w.Window30dNetPnl + w.Window60dNetPnl + w.Window90dNetPnl > 0m)
                ? "PositiveProfileFound"
                : nearBreakeven.Length > 0 ? "NearBreakeven" : "AllNegative",
            Details = new Dictionary<string, object?> { ["profiles"] = nearBreakeven.Select(w => w.ProfileName).ToArray() }
        });

        var grossEdge = executed.Length > 0 && executed.Average(c => c.ExpectedMovePercent ?? 0m) > (medianCost ?? 0m) + 0.15m;
        var netWinners = trades.Count(t => t.NetPnlQuote > 0m);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "If not positive, is the gross edge large enough to justify future lower-fee/futures research?",
            Answer = grossEdge
                ? $"Gross edge appears meaningful (avg move={impulseAvgMove:F4}%); net winners={netWinners}/{trades.Count} under Spot costs."
                : trades.Count == 0
                    ? "No trades; insufficient gross edge detected."
                    : "Gross edge weak under current assumptions.",
            Verdict = grossEdge && trades.Count > 0 ? "WorthLowerFeeResearch" : trades.Count == 0 ? "NotViableNoTrades" : "NotViableWeakEdge",
            Details = new Dictionary<string, object?> { ["netWinners"] = netWinners, ["tradeCount"] = trades.Count }
        });

        return answers;
    }

    public static object BuildReachabilityAnalysis(IReadOnlyList<ImpulseContinuationV1CandidateRecord> candidates)
    {
        var executed = candidates.Where(c => c.Executed).ToArray();
        var blocked = candidates.Where(c => !c.Executed).ToArray();
        var reachableBlocked = blocked.Where(c => c.Lock90ReachableWithin60m).ToArray();

        return new
        {
            totalCandidates = candidates.Count,
            executedCount = executed.Length,
            blockedCount = blocked.Length,
            lock90ReachableExecuted = executed.Count(c => c.Lock90ReachableWithin60m),
            lock90ReachableBlocked = reachableBlocked.Length,
            medianForwardMfe60Executed = Median(executed.Select(c => c.ForwardMfe60Percent)),
            medianForwardMfe60BlockedReachable = Median(reachableBlocked.Select(c => c.ForwardMfe60Percent)),
            medianTimeToLock90Minutes = MedianInt(executed.Select(c => c.TimeToLock90Minutes))
        };
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

    private static decimal? MedianInt(IEnumerable<int?> values)
    {
        var samples = values.Where(v => v.HasValue).Select(v => (decimal)v!.Value).OrderBy(v => v).ToArray();
        if (samples.Length == 0)
            return null;
        var mid = samples.Length / 2;
        return samples.Length % 2 == 0
            ? Math.Round((samples[mid - 1] + samples[mid]) / 2m, 2)
            : Math.Round(samples[mid], 2);
    }
}

public sealed class ImpulseContinuationV1ReportWriter(string outputDirectory)
{
    public async Task WriteAsync(
        IReadOnlyList<ImpulseContinuationV1SummaryRow> summaries,
        IReadOnlyList<ImpulseContinuationV1CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<BlockedEntryRecord> blockedEntries,
        IReadOnlyList<ReachabilityResearchAnswer> researchAnswers,
        IReadOnlyList<ImpulseContinuationV1ExitBreakdownRow> exitBreakdown,
        IReadOnlyList<ImpulseContinuationV1WindowRobustnessRow> windowRobustness,
        object reachabilityAnalysis,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        var executed = candidates.Where(c => c.Executed).ToArray();
        var blocked = candidates.Where(c => !c.Executed).ToArray();

        await WriteJsonAsync("impulse-continuation-summary.json", summaries, cancellationToken);
        await WriteJsonAsync("impulse-continuation-trades.json", executed, cancellationToken);
        await WriteJsonAsync("impulse-continuation-blocked-candidates.json", blocked, cancellationToken);
        await WriteJsonAsync("impulse-continuation-reachability.json", reachabilityAnalysis, cancellationToken);
        await WriteJsonAsync("impulse-continuation-exit-breakdown.json", exitBreakdown, cancellationToken);
        await WriteJsonAsync("impulse-continuation-window-robustness.json", windowRobustness, cancellationToken);
        await WriteJsonAsync("impulse-continuation-research-answers.json", researchAnswers, cancellationToken);

        await WriteSummaryCsvAsync("impulse-continuation-summary.csv", summaries, cancellationToken);
        await WriteCandidatesCsvAsync("impulse-continuation-trades.csv", executed, cancellationToken);
        await WriteCandidatesCsvAsync("impulse-continuation-blocked-candidates.csv", blocked, cancellationToken);
        await WriteExitBreakdownCsvAsync("impulse-continuation-exit-breakdown.csv", exitBreakdown, cancellationToken);
        await WriteWindowRobustnessCsvAsync("impulse-continuation-window-robustness.csv", windowRobustness, cancellationToken);

        var standardWriter = new ReplayReportWriter(outputDirectory);
        await standardWriter.WriteAsync([], trades, blockedEntries, [], cancellationToken);
    }

    private async Task WriteJsonAsync<T>(string fileName, T value, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, fileName),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private async Task WriteSummaryCsvAsync(string fileName, IReadOnlyList<ImpulseContinuationV1SummaryRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("windowLabel,interval,profileName,symbol,candidateCount,executedCount,blockedCount,lock90MeetsRequiredGrossCount,lock90ReachableCount,estimatedNetPnlQuote,tradesCount,netWinnerCount,avgExpectedMovePercent,repeatabilityVerdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.WindowLabel, row.Interval, Escape(row.ProfileName), row.Symbol,
                row.CandidateCount, row.ExecutedCount, row.BlockedCount,
                row.Lock90MeetsRequiredGrossCount, row.Lock90ReachableCount,
                row.EstimatedNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.TradesCount, row.NetWinnerCount,
                FormatNullable(row.AvgExpectedMovePercent),
                row.RepeatabilityVerdict));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), ct);
    }

    private async Task WriteCandidatesCsvAsync(string fileName, IReadOnlyList<ImpulseContinuationV1CandidateRecord> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("windowLabel,interval,profileName,symbol,timeUtc,executed,rejectionReason,impulseBodyStrengthPercent,impulseRangePercent,impulseRangeVsAverage,volumeExpansionRatio,closeNearHighPercent,followThroughConfirmed,followThroughCloseStrengthPercent,entryDistanceFromImpulseHighPercent,expectedMovePercent,lock90DistancePercent,estimatedRoundTripCostPercent,requiredNetProfitPercent,requiredGrossMovePercent,lock90NetProfitPercent,targetModelName,targetWasCapped,stopDistancePercent,stopToLockRatio,forwardMfe15Percent,forwardMfe30Percent,forwardMfe60Percent,forwardMae15Percent,forwardMae30Percent,forwardMae60Percent,lock90ReachableWithin60m,timeToLock90Minutes,exitReason,netPnlQuote,grossPnlQuote,durationMinutes,isWinner");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.WindowLabel, row.Interval, Escape(row.ProfileName), row.Symbol,
                row.TimeUtc.ToString("O", CultureInfo.InvariantCulture), row.Executed, Escape(row.RejectionReason),
                row.ImpulseBodyStrengthPercent.ToString(CultureInfo.InvariantCulture),
                row.ImpulseRangePercent.ToString(CultureInfo.InvariantCulture),
                row.ImpulseRangeVsAverage.ToString(CultureInfo.InvariantCulture),
                row.VolumeExpansionRatio.ToString(CultureInfo.InvariantCulture),
                row.CloseNearHighPercent.ToString(CultureInfo.InvariantCulture),
                row.FollowThroughConfirmed,
                FormatNullable(row.FollowThroughCloseStrengthPercent),
                FormatNullable(row.EntryDistanceFromImpulseHighPercent),
                FormatNullable(row.ExpectedMovePercent),
                FormatNullable(row.Lock90DistancePercent),
                FormatNullable(row.EstimatedRoundTripCostPercent),
                FormatNullable(row.RequiredNetProfitPercent),
                FormatNullable(row.RequiredGrossMovePercent),
                FormatNullable(row.Lock90NetProfitPercent),
                Escape(row.TargetModelName),
                row.TargetWasCapped,
                FormatNullable(row.StopDistancePercent),
                FormatNullable(row.StopToLockRatio),
                FormatNullable(row.ForwardMfe15Percent),
                FormatNullable(row.ForwardMfe30Percent),
                FormatNullable(row.ForwardMfe60Percent),
                FormatNullable(row.ForwardMae15Percent),
                FormatNullable(row.ForwardMae30Percent),
                FormatNullable(row.ForwardMae60Percent),
                row.Lock90ReachableWithin60m,
                FormatNullable(row.TimeToLock90Minutes),
                Escape(row.ExitReason),
                FormatNullable(row.NetPnlQuote),
                FormatNullable(row.GrossPnlQuote),
                row.DurationMinutes.ToString(CultureInfo.InvariantCulture),
                row.IsWinner));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), ct);
    }

    private async Task WriteExitBreakdownCsvAsync(string fileName, IReadOnlyList<ImpulseContinuationV1ExitBreakdownRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("exitReason,count,netPnlQuote,grossPnlQuote,avgDurationMinutes");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.ExitReason, row.Count,
                row.NetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.GrossPnlQuote.ToString(CultureInfo.InvariantCulture),
                FormatNullable(row.AvgDurationMinutes)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), ct);
    }

    private async Task WriteWindowRobustnessCsvAsync(string fileName, IReadOnlyList<ImpulseContinuationV1WindowRobustnessRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("profileName,symbol,interval,window30dCandidates,window60dCandidates,window90dCandidates,window30dTrades,window60dTrades,window90dTrades,window30dNetPnl,window60dNetPnl,window90dNetPnl,robustnessVerdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.ProfileName), row.Symbol, row.Interval,
                row.Window30dCandidates, row.Window60dCandidates, row.Window90dCandidates,
                row.Window30dTrades, row.Window60dTrades, row.Window90dTrades,
                row.Window30dNetPnl.ToString(CultureInfo.InvariantCulture),
                row.Window60dNetPnl.ToString(CultureInfo.InvariantCulture),
                row.Window90dNetPnl.ToString(CultureInfo.InvariantCulture),
                row.RobustnessVerdict));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), ct);
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

    private static string FormatNullable(int? value)
        => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
}
