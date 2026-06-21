using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class MeanReversionRangeBounceV1Aggregator
{
    private const int RepeatableThreshold = 3;

    public static IReadOnlyList<MeanReversionRangeBounceV1SummaryRow> BuildSummaries(
        string windowLabel,
        IReadOnlyList<MeanReversionRangeBounceV1CandidateRecord> candidates,
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
                return new MeanReversionRangeBounceV1SummaryRow
                {
                    WindowLabel = windowLabel,
                    Interval = first.Interval,
                    ProfileName = first.ProfileName,
                    Symbol = first.Symbol,
                    CandidateCount = g.Count(),
                    ExecutedCount = executed.Length,
                    BlockedCount = g.Count(x => !x.Executed),
                    TargetReachableCount = g.Count(x => x.TargetReachableWithin60m),
                    EstimatedNetPnlQuote = profileTrades.Sum(t => t.NetPnlQuote),
                    TradesCount = profileTrades.Length,
                    NetWinnerCount = profileTrades.Count(t => t.NetPnlQuote > 0m),
                    AvgExpectedMovePercent = executed.Length == 0
                        ? null
                        : Math.Round(executed.Average(x => x.ExpectedMovePercent ?? 0m), 6),
                    RepeatabilityVerdict = g.Count(x => x.Executed) >= RepeatableThreshold
                        ? "RepeatableCandidates"
                        : g.Count(x => x.Executed) >= 1 ? "Sparse" : "NoCandidates"
                };
            })
            .OrderBy(x => x.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Symbol)
            .ToArray();
    }

    public static IReadOnlyList<MeanReversionRangeBounceV1ExitBreakdownRow> BuildExitBreakdown(IReadOnlyList<SimulatedTrade> trades)
    {
        return trades
            .GroupBy(t => NormalizeExitReason(t.ExitReason, t.ProfitLockThresholdPercent), StringComparer.OrdinalIgnoreCase)
            .Select(g => new MeanReversionRangeBounceV1ExitBreakdownRow
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

    public static IReadOnlyList<MeanReversionRangeBounceV1WindowRobustnessRow> BuildWindowRobustness(
        IReadOnlyList<MeanReversionRangeBounceV1SummaryRow> summaries)
    {
        return summaries
            .GroupBy(s => $"{s.ProfileName}|{s.Interval}|{s.Symbol}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var w30 = g.FirstOrDefault(x => x.WindowLabel == "30d");
                var w60 = g.FirstOrDefault(x => x.WindowLabel == "60d");
                var w90 = g.FirstOrDefault(x => x.WindowLabel == "90d");
                var totalNet = (w30?.EstimatedNetPnlQuote ?? 0m) + (w60?.EstimatedNetPnlQuote ?? 0m) + (w90?.EstimatedNetPnlQuote ?? 0m);
                var totalTrades = (w30?.TradesCount ?? 0) + (w60?.TradesCount ?? 0) + (w90?.TradesCount ?? 0);
                var positiveWindows = new[] { w30, w60, w90 }.Count(w => w is not null && w.EstimatedNetPnlQuote > 0m);

                return new MeanReversionRangeBounceV1WindowRobustnessRow
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
                    RobustnessVerdict = totalNet > 0m
                        ? "ProfitableAcrossWindows"
                        : totalNet > -1m && totalTrades >= RepeatableThreshold
                            ? "NearBreakeven"
                            : positiveWindows >= 2
                                ? "MixedWindows"
                                : "ConsistentlyNegative"
                };
            })
            .OrderBy(x => x.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Symbol)
            .ToArray();
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildResearchAnswers(
        IReadOnlyList<MeanReversionRangeBounceV1CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<MeanReversionRangeBounceV1SummaryRow> summaries,
        IReadOnlyList<MeanReversionRangeBounceV1ExitBreakdownRow> exitBreakdown,
        IReadOnlyList<MeanReversionRangeBounceV1WindowRobustnessRow> windowRobustness)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var stopLoss = exitBreakdown.FirstOrDefault(r => string.Equals(r.ExitReason, "StopLoss", StringComparison.OrdinalIgnoreCase));
        var profitTarget = exitBreakdown.FirstOrDefault(r => string.Equals(r.ExitReason, "ProfitTarget", StringComparison.OrdinalIgnoreCase));
        var profitLock = exitBreakdown.FirstOrDefault(r => string.Equals(r.ExitReason, "ProfitLock", StringComparison.OrdinalIgnoreCase));
        var timeStop = exitBreakdown.FirstOrDefault(r => string.Equals(r.ExitReason, "TimeStop", StringComparison.OrdinalIgnoreCase));
        var profitExits = (profitTarget?.Count ?? 0) + (profitLock?.Count ?? 0);
        var profitNet = (profitTarget?.NetPnlQuote ?? 0m) + (profitLock?.NetPnlQuote ?? 0m);

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does range-bounce produce better StopLoss/ProfitTarget balance than breakout/impulse?",
            Answer = $"StopLoss={stopLoss?.Count ?? 0} (net={stopLoss?.NetPnlQuote:F8}), ProfitTarget/Lock={profitExits} (net={profitNet:F8}). Impulse V1 baseline: StopLoss=1557/-167.57, ProfitLock=774/+27.21.",
            Verdict = profitExits > (stopLoss?.Count ?? 0) / 2 ? "BetterBalanceThanImpulse" : "StopLossStillDominant",
            Details = new Dictionary<string, object?> { ["exitBreakdown"] = exitBreakdown }
        });

        var byInterval = summaries
            .GroupBy(s => s.Interval, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Interval = g.Key, NetPnl = g.Sum(x => x.EstimatedNetPnlQuote), Trades = g.Sum(x => x.TradesCount), Reachable = g.Sum(x => x.TargetReachableCount) })
            .OrderByDescending(x => x.NetPnl)
            .ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which symbol/interval has the best range-bounce behavior?",
            Answer = string.Join("; ", byInterval.Select(x => $"{x.Interval}: net={x.NetPnl:F8}, trades={x.Trades}, reachable={x.Reachable}")),
            Verdict = byInterval.FirstOrDefault()?.Interval ?? "NoData",
            Details = new Dictionary<string, object?> { ["byInterval"] = byInterval }
        });

        var executed = candidates.Where(c => c.Executed).ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are targets large enough to survive conservative Spot costs?",
            Answer = $"Executed median expected move={Median(executed.Select(c => c.ExpectedMovePercent)):F4}%, median required gross={Median(candidates.Select(c => c.RequiredGrossMovePercent)):F4}%.",
            Verdict = Median(executed.Select(c => c.ExpectedMovePercent)) >= Median(candidates.Select(c => c.RequiredGrossMovePercent))
                ? "TargetsSurviveCosts" : "TargetsMarginal",
            Details = new Dictionary<string, object?>()
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are losses mostly breakdown failures or fee drag?",
            Answer = $"StopLoss={stopLoss?.Count ?? 0} (net={stopLoss?.NetPnlQuote:F8}), TimeStop={timeStop?.Count ?? 0} (net={timeStop?.NetPnlQuote:F8}), Profit exits={profitExits} (net={profitNet:F8}).",
            Verdict = (stopLoss?.Count ?? 0) >= (timeStop?.Count ?? 0) ? "BreakdownStopDominant" : "TimeStopDominant",
            Details = new Dictionary<string, object?> { ["exitBreakdown"] = exitBreakdown }
        });

        var midpoint = trades.Where(t => t.ProjectionMode?.Contains("Midpoint", StringComparison.OrdinalIgnoreCase) == true).Sum(t => t.NetPnlQuote);
        var rangeHigh = trades.Where(t => t.ProjectionMode?.Contains("RangeHigh", StringComparison.OrdinalIgnoreCase) == true).Sum(t => t.NetPnlQuote);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does midpoint target outperform range-high target?",
            Answer = $"Midpoint net={midpoint:F8}, RangeHigh net={rangeHigh:F8}.",
            Verdict = midpoint >= rangeHigh ? "MidpointBetterOrEqual" : "RangeHighBetter",
            Details = new Dictionary<string, object?> { ["midpointNet"] = midpoint, ["rangeHighNet"] = rangeHigh }
        });

        var nearBreakeven = windowRobustness.Where(w =>
            w.Window30dNetPnl + w.Window60dNetPnl + w.Window90dNetPnl > -1m
            && w.Window30dTrades + w.Window60dTrades + w.Window90dTrades >= RepeatableThreshold).ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is there any profile near breakeven or positive across 30d/60d/90d?",
            Answer = nearBreakeven.Length > 0
                ? $"{nearBreakeven.Length} profile(s) near breakeven; total net={trades.Sum(t => t.NetPnlQuote):F8}."
                : $"No near-breakeven profiles; total net={trades.Sum(t => t.NetPnlQuote):F8}.",
            Verdict = windowRobustness.Any(w => w.Window30dNetPnl + w.Window60dNetPnl + w.Window90dNetPnl > 0m)
                ? "PositiveProfileFound" : nearBreakeven.Length > 0 ? "NearBreakeven" : "AllNegative",
            Details = new Dictionary<string, object?> { ["profiles"] = nearBreakeven.Select(w => w.ProfileName).ToArray() }
        });

        var avgStop = Median(executed.Select(c => c.StopDistancePercent));
        var avgTarget = Median(executed.Select(c => c.ExpectedMovePercent));
        var avgRangeWidth = Median(executed.Select(c => (decimal?)c.RangeWidthPercent));
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "If negative, is the issue target size, stop size, or poor range detection?",
            Answer = $"Median target={avgTarget:F4}%, stop={avgStop:F4}%, range width={avgRangeWidth:F4}%, reward/risk={Median(executed.Select(c => c.RewardRisk)):F2}.",
            Verdict = avgStop is > 0m && avgTarget is > 0m && avgTarget < avgStop * 1.5m
                ? "TargetTooSmallVsStop"
                : avgRangeWidth is < 0.3m or > 1.2m ? "PoorRangeDetection" : "MixedFailureModes",
            Details = new Dictionary<string, object?>()
        });

        return answers;
    }

    public static object BuildReachabilityAnalysis(IReadOnlyList<MeanReversionRangeBounceV1CandidateRecord> candidates)
    {
        var executed = candidates.Where(c => c.Executed).ToArray();
        return new
        {
            totalCandidates = candidates.Count,
            executedCount = executed.Length,
            blockedCount = candidates.Count(c => !c.Executed),
            targetReachableExecuted = executed.Count(c => c.TargetReachableWithin60m),
            medianForwardMfe60Executed = Median(executed.Select(c => c.ForwardMfe60Percent)),
            medianTimeToTargetMinutes = MedianInt(executed.Select(c => c.TimeToTargetMinutes)),
            medianExpectedMovePercent = Median(executed.Select(c => c.ExpectedMovePercent))
        };
    }

    private static string NormalizeExitReason(string? exitReason, decimal? profitLockThreshold)
    {
        if (string.Equals(exitReason, "ProfitLock", StringComparison.OrdinalIgnoreCase)
            && profitLockThreshold is >= 99m)
        {
            return "ProfitTarget";
        }

        return exitReason ?? "Unknown";
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

public sealed class MeanReversionRangeBounceV1ReportWriter(string outputDirectory)
{
    public async Task WriteAsync(
        IReadOnlyList<MeanReversionRangeBounceV1SummaryRow> summaries,
        IReadOnlyList<MeanReversionRangeBounceV1CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<BlockedEntryRecord> blockedEntries,
        IReadOnlyList<ReachabilityResearchAnswer> researchAnswers,
        IReadOnlyList<MeanReversionRangeBounceV1ExitBreakdownRow> exitBreakdown,
        IReadOnlyList<MeanReversionRangeBounceV1WindowRobustnessRow> windowRobustness,
        object reachabilityAnalysis,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        var executed = candidates.Where(c => c.Executed).ToArray();
        var blocked = candidates.Where(c => !c.Executed).ToArray();

        await WriteJsonAsync("mean-reversion-range-summary.json", summaries, cancellationToken);
        await WriteJsonAsync("mean-reversion-range-trades.json", executed, cancellationToken);
        await WriteJsonAsync("mean-reversion-range-blocked-candidates.json", blocked, cancellationToken);
        await WriteJsonAsync("mean-reversion-range-reachability.json", reachabilityAnalysis, cancellationToken);
        await WriteJsonAsync("mean-reversion-range-exit-breakdown.json", exitBreakdown, cancellationToken);
        await WriteJsonAsync("mean-reversion-range-window-robustness.json", windowRobustness, cancellationToken);
        await WriteJsonAsync("mean-reversion-range-research-answers.json", researchAnswers, cancellationToken);

        await WriteSummaryCsvAsync("mean-reversion-range-summary.csv", summaries, cancellationToken);
        await WriteCandidatesCsvAsync("mean-reversion-range-trades.csv", executed, cancellationToken);
        await WriteCandidatesCsvAsync("mean-reversion-range-blocked-candidates.csv", blocked, cancellationToken);
        await WriteExitBreakdownCsvAsync("mean-reversion-range-exit-breakdown.csv", exitBreakdown, cancellationToken);
        await WriteWindowRobustnessCsvAsync("mean-reversion-range-window-robustness.csv", windowRobustness, cancellationToken);

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

    private async Task WriteSummaryCsvAsync(string fileName, IReadOnlyList<MeanReversionRangeBounceV1SummaryRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("windowLabel,interval,profileName,symbol,candidateCount,executedCount,blockedCount,targetReachableCount,estimatedNetPnlQuote,tradesCount,netWinnerCount,avgExpectedMovePercent,repeatabilityVerdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.WindowLabel, row.Interval, Escape(row.ProfileName), row.Symbol,
                row.CandidateCount, row.ExecutedCount, row.BlockedCount, row.TargetReachableCount,
                row.EstimatedNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.TradesCount, row.NetWinnerCount,
                FormatNullable(row.AvgExpectedMovePercent), row.RepeatabilityVerdict));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), ct);
    }

    private async Task WriteCandidatesCsvAsync(string fileName, IReadOnlyList<MeanReversionRangeBounceV1CandidateRecord> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("windowLabel,interval,profileName,symbol,timeUtc,executed,rejectionReason,rangeHigh,rangeLow,rangeMidpoint,rangeWidthPercent,distanceToRangeLowPercent,distanceToRangeHighPercent,entryRejectionCandleBodyPercent,entryRejectionWickPercent,closeBackInsideRange,trendSlopePercent,atrPercent,targetModelName,expectedMovePercent,requiredGrossMovePercent,stopDistancePercent,rewardRisk,forwardMfe60Percent,forwardMae60Percent,targetReachableWithin60m,timeToTargetMinutes,exitReason,netPnlQuote,isWinner");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.WindowLabel, row.Interval, Escape(row.ProfileName), row.Symbol,
                row.TimeUtc.ToString("O", CultureInfo.InvariantCulture), row.Executed, Escape(row.RejectionReason),
                row.RangeHigh.ToString(CultureInfo.InvariantCulture),
                row.RangeLow.ToString(CultureInfo.InvariantCulture),
                row.RangeMidpoint.ToString(CultureInfo.InvariantCulture),
                row.RangeWidthPercent.ToString(CultureInfo.InvariantCulture),
                row.DistanceToRangeLowPercent.ToString(CultureInfo.InvariantCulture),
                row.DistanceToRangeHighPercent.ToString(CultureInfo.InvariantCulture),
                row.EntryRejectionCandleBodyPercent.ToString(CultureInfo.InvariantCulture),
                row.EntryRejectionWickPercent.ToString(CultureInfo.InvariantCulture),
                row.CloseBackInsideRange,
                row.TrendSlopePercent.ToString(CultureInfo.InvariantCulture),
                row.AtrPercent.ToString(CultureInfo.InvariantCulture),
                Escape(row.TargetModelName),
                FormatNullable(row.ExpectedMovePercent),
                FormatNullable(row.RequiredGrossMovePercent),
                FormatNullable(row.StopDistancePercent),
                FormatNullable(row.RewardRisk),
                FormatNullable(row.ForwardMfe60Percent),
                FormatNullable(row.ForwardMae60Percent),
                row.TargetReachableWithin60m,
                FormatNullable(row.TimeToTargetMinutes),
                Escape(row.ExitReason),
                FormatNullable(row.NetPnlQuote),
                row.IsWinner));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), ct);
    }

    private async Task WriteExitBreakdownCsvAsync(string fileName, IReadOnlyList<MeanReversionRangeBounceV1ExitBreakdownRow> rows, CancellationToken ct)
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

    private async Task WriteWindowRobustnessCsvAsync(string fileName, IReadOnlyList<MeanReversionRangeBounceV1WindowRobustnessRow> rows, CancellationToken ct)
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
