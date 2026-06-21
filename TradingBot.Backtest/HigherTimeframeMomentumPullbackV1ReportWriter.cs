using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class HigherTimeframeMomentumPullbackV1Aggregator
{
    private const int RepeatableThreshold = 3;
    private const decimal ImpulseV1StopLossRateBaseline = 0.531m;

    public static IReadOnlyList<HigherTimeframeMomentumPullbackV1SummaryRow> BuildSummaries(
        string windowLabel,
        IReadOnlyList<HigherTimeframeMomentumPullbackV1CandidateRecord> candidates,
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
                return new HigherTimeframeMomentumPullbackV1SummaryRow
                {
                    WindowLabel = windowLabel,
                    Interval = first.Interval,
                    ProfileName = first.ProfileName,
                    Symbol = first.Symbol,
                    CandidateCount = g.Count(),
                    ExecutedCount = executed.Length,
                    BlockedCount = g.Count(x => !x.Executed),
                    EstimatedNetPnlQuote = profileTrades.Sum(t => t.NetPnlQuote),
                    TradesCount = profileTrades.Length,
                    NetWinnerCount = profileTrades.Count(t => t.NetPnlQuote > 0m),
                    AvgExpectedMovePercent = executed.Length == 0
                        ? null
                        : Math.Round(executed.Average(x => x.ExpectedMovePercent ?? 0m), 6),
                    RepeatabilityVerdict = executed.Length >= RepeatableThreshold
                        ? "RepeatableCandidates"
                        : executed.Length >= 1 ? "Sparse" : "NoCandidates"
                };
            })
            .OrderBy(x => x.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Symbol)
            .ToArray();
    }

    public static IReadOnlyList<HigherTimeframeMomentumPullbackV1ExitBreakdownRow> BuildExitBreakdown(IReadOnlyList<SimulatedTrade> trades)
    {
        return trades
            .GroupBy(t => NormalizeExitReason(t.ExitReason, t.ProfitLockThresholdPercent), StringComparer.OrdinalIgnoreCase)
            .Select(g => new HigherTimeframeMomentumPullbackV1ExitBreakdownRow
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

    public static IReadOnlyList<HigherTimeframeMomentumPullbackV1WindowRobustnessRow> BuildWindowRobustness(
        IReadOnlyList<HigherTimeframeMomentumPullbackV1SummaryRow> summaries)
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

                return new HigherTimeframeMomentumPullbackV1WindowRobustnessRow
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
        IReadOnlyList<HigherTimeframeMomentumPullbackV1CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<HigherTimeframeMomentumPullbackV1SummaryRow> summaries,
        IReadOnlyList<HigherTimeframeMomentumPullbackV1ExitBreakdownRow> exitBreakdown,
        IReadOnlyList<HigherTimeframeMomentumPullbackV1WindowRobustnessRow> windowRobustness)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var executed = candidates.Where(c => c.Executed).ToArray();
        var stopLoss = exitBreakdown.FirstOrDefault(r => string.Equals(r.ExitReason, "StopLoss", StringComparison.OrdinalIgnoreCase));
        var profitTarget = exitBreakdown.FirstOrDefault(r => string.Equals(r.ExitReason, "ProfitTarget", StringComparison.OrdinalIgnoreCase));
        var profitLock = exitBreakdown.FirstOrDefault(r => string.Equals(r.ExitReason, "ProfitLock", StringComparison.OrdinalIgnoreCase));
        var timeStop = exitBreakdown.FirstOrDefault(r => string.Equals(r.ExitReason, "TimeStop", StringComparison.OrdinalIgnoreCase));
        var totalTrades = trades.Count;
        var stopLossRate = totalTrades == 0 ? 0m : (decimal)(stopLoss?.Count ?? 0) / totalTrades;
        var medianExpectedMove = Median(executed.Select(c => c.ExpectedMovePercent));
        var medianRequiredGross = Median(candidates.Select(c => c.RequiredGrossMovePercent));
        var medianForwardMfe8h = Median(executed.Select(c => c.ForwardMfe8hPercent));
        var avgNetPerTrade = totalTrades == 0 ? 0m : trades.Sum(t => t.NetPnlQuote) / totalTrades;

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Do higher timeframes produce larger gross edge per trade?",
            Answer = $"Median expected move={medianExpectedMove:F4}%, median forward MFE 8h={medianForwardMfe8h:F4}%, avg net/trade={avgNetPerTrade:F8}. Impulse V1 1m baseline avg expected move ~0.35-0.50%.",
            Verdict = medianExpectedMove is >= 0.80m ? "LargerMovesThan1mFamilies" : "MovesStillMarginal",
            Details = new Dictionary<string, object?>
            {
                ["medianExpectedMovePercent"] = medianExpectedMove,
                ["medianForwardMfe8hPercent"] = medianForwardMfe8h
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are targets large enough to survive conservative Spot costs?",
            Answer = $"Median expected move={medianExpectedMove:F4}%, median required gross={medianRequiredGross:F4}%. Executed count={executed.Length}.",
            Verdict = medianExpectedMove.HasValue && medianRequiredGross.HasValue && medianExpectedMove.Value >= medianRequiredGross.Value
                ? "TargetsSurviveCosts" : "TargetsBelowCostFloor",
            Details = new Dictionary<string, object?>()
        });

        var bySymbolInterval = summaries
            .GroupBy(s => $"{s.Symbol}|{s.Interval}", StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Key = g.Key,
                NetPnl = g.Sum(x => x.EstimatedNetPnlQuote),
                Trades = g.Sum(x => x.TradesCount),
                AvgMove = g.Where(x => x.AvgExpectedMovePercent.HasValue).Select(x => x.AvgExpectedMovePercent!.Value).DefaultIfEmpty().Average()
            })
            .OrderByDescending(x => x.NetPnl)
            .ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which symbol/interval is least bad or positive?",
            Answer = string.Join("; ", bySymbolInterval.Select(x => $"{x.Key}: net={x.NetPnl:F8}, trades={x.Trades}, avgMove={x.AvgMove:F4}%")),
            Verdict = bySymbolInterval.FirstOrDefault()?.NetPnl > 0m ? "PositiveSliceFound" : "AllNegativeOrNearZero",
            Details = new Dictionary<string, object?> { ["bySymbolInterval"] = bySymbolInterval }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is stop-loss rate lower than 1m strategies?",
            Answer = $"StopLoss={stopLoss?.Count ?? 0}/{totalTrades} ({stopLossRate:P1}), net={stopLoss?.NetPnlQuote:F8}. Impulse V1 baseline stop-loss rate ~{ImpulseV1StopLossRateBaseline:P1}.",
            Verdict = stopLossRate < ImpulseV1StopLossRateBaseline ? "LowerStopLossRateThan1m" : "StopLossRateNotLower",
            Details = new Dictionary<string, object?> { ["stopLossRate"] = stopLossRate }
        });

        var nearBreakeven = windowRobustness.Where(w =>
            w.Window30dNetPnl + w.Window60dNetPnl + w.Window90dNetPnl > -1m
            && w.Window30dTrades + w.Window60dTrades + w.Window90dTrades >= RepeatableThreshold).ToArray();
        var positiveProfiles = windowRobustness.Where(w =>
            w.Window30dNetPnl + w.Window60dNetPnl + w.Window90dNetPnl > 0m
            && w.Window30dTrades + w.Window60dTrades + w.Window90dTrades >= RepeatableThreshold).ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does any entry-time subset survive 30d/60d/90d?",
            Answer = positiveProfiles.Length > 0
                ? $"{positiveProfiles.Length} profile(s) positive across windows; total net={trades.Sum(t => t.NetPnlQuote):F8}."
                : nearBreakeven.Length > 0
                    ? $"{nearBreakeven.Length} profile(s) near breakeven; total net={trades.Sum(t => t.NetPnlQuote):F8}."
                    : $"No robust positive subset; total net={trades.Sum(t => t.NetPnlQuote):F8}.",
            Verdict = positiveProfiles.Length > 0 ? "PositiveProfileSurvives" : nearBreakeven.Length > 0 ? "NearBreakevenOnly" : "NoRobustSubset",
            Details = new Dictionary<string, object?>
            {
                ["positiveProfiles"] = positiveProfiles.Select(w => w.ProfileName).ToArray(),
                ["nearBreakevenProfiles"] = nearBreakeven.Select(w => w.ProfileName).ToArray()
            }
        });

        var profitExits = (profitTarget?.Count ?? 0) + (profitLock?.Count ?? 0);
        var profitNet = (profitTarget?.NetPnlQuote ?? 0m) + (profitLock?.NetPnlQuote ?? 0m);
        var totalNet = trades.Sum(t => t.NetPnlQuote);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "If still negative, is Spot strategy research blocked by market edge or by execution costs?",
            Answer = $"Total net={totalNet:F8}, profit exits={profitExits} (net={profitNet:F8}), stop={stopLoss?.Count ?? 0}, time={timeStop?.Count ?? 0}. Median expected move={medianExpectedMove:F4}% vs required gross={medianRequiredGross:F4}%.",
            Verdict = medianExpectedMove.HasValue && medianRequiredGross.HasValue && medianExpectedMove.Value >= medianRequiredGross.Value && totalNet < 0m
                ? "MarketEdgeInsufficientDespiteLargeTargets"
                : totalNet < 0m && (stopLoss?.Count ?? 0) > profitExits
                    ? "StopDragDominates"
                    : totalNet < 0m
                        ? "CostDragDominates"
                        : "ViableEdge",
            Details = new Dictionary<string, object?>
            {
                ["exitBreakdown"] = exitBreakdown,
                ["totalNetPnl"] = totalNet
            }
        });

        return answers;
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
}

public sealed class HigherTimeframeMomentumPullbackV1ReportWriter(string outputDirectory)
{
    public async Task WriteAsync(
        IReadOnlyList<HigherTimeframeMomentumPullbackV1SummaryRow> summaries,
        IReadOnlyList<HigherTimeframeMomentumPullbackV1CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<BlockedEntryRecord> blockedEntries,
        IReadOnlyList<ReachabilityResearchAnswer> researchAnswers,
        IReadOnlyList<HigherTimeframeMomentumPullbackV1ExitBreakdownRow> exitBreakdown,
        IReadOnlyList<HigherTimeframeMomentumPullbackV1WindowRobustnessRow> windowRobustness,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        var executed = candidates.Where(c => c.Executed).ToArray();
        var blocked = candidates.Where(c => !c.Executed).ToArray();

        await WriteJsonAsync("htf-momentum-summary.json", summaries, cancellationToken);
        await WriteJsonAsync("htf-momentum-trades.json", executed, cancellationToken);
        await WriteJsonAsync("htf-momentum-blocked-candidates.json", blocked, cancellationToken);
        await WriteJsonAsync("htf-momentum-exit-breakdown.json", exitBreakdown, cancellationToken);
        await WriteJsonAsync("htf-momentum-window-robustness.json", windowRobustness, cancellationToken);
        await WriteJsonAsync("htf-momentum-research-answers.json", researchAnswers, cancellationToken);

        await WriteSummaryCsvAsync("htf-momentum-summary.csv", summaries, cancellationToken);
        await WriteCandidatesCsvAsync("htf-momentum-trades.csv", executed, cancellationToken);
        await WriteCandidatesCsvAsync("htf-momentum-blocked-candidates.csv", blocked, cancellationToken);
        await WriteExitBreakdownCsvAsync("htf-momentum-exit-breakdown.csv", exitBreakdown, cancellationToken);
        await WriteWindowRobustnessCsvAsync("htf-momentum-window-robustness.csv", windowRobustness, cancellationToken);

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

    private async Task WriteSummaryCsvAsync(string fileName, IReadOnlyList<HigherTimeframeMomentumPullbackV1SummaryRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("windowLabel,interval,profileName,symbol,candidateCount,executedCount,blockedCount,estimatedNetPnlQuote,tradesCount,netWinnerCount,avgExpectedMovePercent,repeatabilityVerdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.WindowLabel, row.Interval, Escape(row.ProfileName), row.Symbol,
                row.CandidateCount, row.ExecutedCount, row.BlockedCount,
                row.EstimatedNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.TradesCount, row.NetWinnerCount,
                FormatNullable(row.AvgExpectedMovePercent), row.RepeatabilityVerdict));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), ct);
    }

    private async Task WriteCandidatesCsvAsync(string fileName, IReadOnlyList<HigherTimeframeMomentumPullbackV1CandidateRecord> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("windowLabel,interval,profileName,symbol,timeUtc,executed,rejectionReason,trendSlopePercent,trendStrengthPercent,pullbackDepthPercent,distanceToMaPercent,reclaimConfirmed,expectedMovePercent,requiredGrossMovePercent,stopDistancePercent,rewardRisk,targetModelName,forwardMfe4hPercent,forwardMfe8hPercent,forwardMfe12hPercent,forwardMae4hPercent,forwardMae8hPercent,forwardMae12hPercent,exitReason,netPnlQuote,mfePercent,maePercent,isWinner");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.WindowLabel, row.Interval, Escape(row.ProfileName), row.Symbol,
                row.TimeUtc.ToString("O", CultureInfo.InvariantCulture), row.Executed, Escape(row.RejectionReason),
                row.TrendSlopePercent.ToString(CultureInfo.InvariantCulture),
                row.TrendStrengthPercent.ToString(CultureInfo.InvariantCulture),
                row.PullbackDepthPercent.ToString(CultureInfo.InvariantCulture),
                FormatNullable(row.DistanceToMaPercent),
                row.ReclaimConfirmed,
                FormatNullable(row.ExpectedMovePercent),
                FormatNullable(row.RequiredGrossMovePercent),
                FormatNullable(row.StopDistancePercent),
                FormatNullable(row.RewardRisk),
                Escape(row.TargetModelName),
                FormatNullable(row.ForwardMfe4hPercent),
                FormatNullable(row.ForwardMfe8hPercent),
                FormatNullable(row.ForwardMfe12hPercent),
                FormatNullable(row.ForwardMae4hPercent),
                FormatNullable(row.ForwardMae8hPercent),
                FormatNullable(row.ForwardMae12hPercent),
                Escape(row.ExitReason),
                FormatNullable(row.NetPnlQuote),
                FormatNullable(row.MfePercent),
                FormatNullable(row.MaePercent),
                row.IsWinner));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), ct);
    }

    private async Task WriteExitBreakdownCsvAsync(string fileName, IReadOnlyList<HigherTimeframeMomentumPullbackV1ExitBreakdownRow> rows, CancellationToken ct)
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

    private async Task WriteWindowRobustnessCsvAsync(string fileName, IReadOnlyList<HigherTimeframeMomentumPullbackV1WindowRobustnessRow> rows, CancellationToken ct)
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
}
