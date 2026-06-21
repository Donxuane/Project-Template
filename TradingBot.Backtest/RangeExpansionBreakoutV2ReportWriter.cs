using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class RangeExpansionBreakoutV2Aggregator
{
    private const int RepeatableThreshold = 3;

    public static IReadOnlyList<RangeExpansionV2SummaryRow> BuildSummaries(
        string windowLabel,
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates,
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
                return new RangeExpansionV2SummaryRow
                {
                    WindowLabel = windowLabel,
                    Interval = first.Interval,
                    ProfileName = first.ProfileName,
                    Symbol = first.Symbol,
                    CandidateCount = g.Count(),
                    ExecutedCount = g.Count(x => x.Executed),
                    BlockedCount = g.Count(x => !x.Executed),
                    Lock90MeetsRequiredGrossCount = g.Count(x => x.Lock90MeetsRequiredGross),
                    Lock90ReachableCount = g.Count(x => x.Lock90ReachableWithin60m),
                    EstimatedNetPnlQuote = profileTrades.Sum(t => t.NetPnlQuote),
                    TradesCount = profileTrades.Length,
                    NetWinnerCount = profileTrades.Count(t => t.NetPnlQuote > 0m),
                    RepeatabilityVerdict = g.Count(x => x.Lock90MeetsRequiredGross) >= RepeatableThreshold
                        ? "RepeatableCostAware"
                        : g.Count(x => x.Executed) >= 1 ? "Sparse" : "NoCandidates"
                };
            })
            .OrderBy(x => x.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Symbol)
            .ToArray();
    }

    public static IReadOnlyList<RangeExpansionV2ExitBreakdownRow> BuildExitBreakdown(IReadOnlyList<SimulatedTrade> trades)
    {
        return trades
            .GroupBy(t => t.ExitReason ?? "Unknown", StringComparer.OrdinalIgnoreCase)
            .Select(g => new RangeExpansionV2ExitBreakdownRow
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

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildResearchAnswers(
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<RangeExpansionV2SummaryRow> summaries,
        IReadOnlyList<RangeExpansionV2ExitBreakdownRow> exitBreakdown)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var costAwareCandidates = candidates.Where(c => c.Lock90MeetsRequiredGross).ToArray();
        var repeatable = summaries
            .GroupBy(s => $"{s.ProfileName}|{s.Symbol}", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count(x => x.Lock90MeetsRequiredGrossCount >= RepeatableThreshold) >= 2)
            .ToArray();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Can any symbol produce repeated candidates with lock90 >= required gross?",
            Answer = repeatable.Length > 0
                ? $"{repeatable.Length} profile/symbol pair(s) show repeated cost-aware candidates across windows."
                : $"Cost-aware candidates={costAwareCandidates.Length}; executed={candidates.Count(c => c.Executed)}.",
            Verdict = repeatable.Length > 0 ? "RepeatableCostAwareCandidates" : costAwareCandidates.Length > 0 ? "SparseCostAware" : "NoCostAwareCandidates",
            Details = new Dictionary<string, object?> { ["repeatablePairs"] = repeatable.Select(g => g.Key).ToArray() }
        });

        var reachable = candidates.Where(c => c.Lock90ReachableWithin60m).ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Are larger-move candidates still reachable within 60m?",
            Answer = $"Lock90-reachable within 60m={reachable.Length}/{candidates.Count}; executed reachable={reachable.Count(c => c.Executed)}.",
            Verdict = reachable.Length >= 10 ? "ReachableLargerMoves" : reachable.Length > 0 ? "SparseReachability" : "NotReachable",
            Details = new Dictionary<string, object?> { ["medianForwardMfe60"] = Median(reachable.Select(c => c.ForwardMfe60Percent)) }
        });

        var endOfData = exitBreakdown.FirstOrDefault(r => string.Equals(r.ExitReason, "EndOfData", StringComparison.OrdinalIgnoreCase));
        var stopLoss = exitBreakdown.FirstOrDefault(r => string.Equals(r.ExitReason, "StopLoss", StringComparison.OrdinalIgnoreCase));
        var timeStop = exitBreakdown.FirstOrDefault(r => string.Equals(r.ExitReason, "TimeStop", StringComparison.OrdinalIgnoreCase));
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does adding stop/time exit prevent huge EndOfData losses?",
            Answer = $"EndOfData={endOfData?.Count ?? 0} (net={endOfData?.NetPnlQuote:F8}), StopLoss={stopLoss?.Count ?? 0}, TimeStop={timeStop?.Count ?? 0}.",
            Verdict = (endOfData?.Count ?? 0) <= 2 ? "EndOfDataControlled" : "EndOfDataStillMaterial",
            Details = new Dictionary<string, object?> { ["exitBreakdown"] = exitBreakdown }
        });

        var byTarget = candidates
            .Where(c => c.Executed)
            .GroupBy(c => c.TargetModelName ?? "Unknown", StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                TargetModel = g.Key,
                Count = g.Count(),
                Inflated = g.Count(c => c.ExpectedMoveInflated),
                NetTradable = g.Count(c => c.Lock90MeetsRequiredGross),
                MedianExpectedMove = Median(g.Select(c => c.ExpectedMovePercent))
            })
            .OrderByDescending(x => x.NetTradable)
            .ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which target model is least inflated but still net-tradable?",
            Answer = byTarget.Length == 0
                ? "No executed candidates."
                : string.Join("; ", byTarget.Select(t => $"{t.TargetModel}: exec={t.Count}, netTradable={t.NetTradable}, inflated={t.Inflated}")),
            Verdict = byTarget.FirstOrDefault()?.TargetModel ?? "NoData",
            Details = new Dictionary<string, object?> { ["byTargetModel"] = byTarget }
        });

        var ethNet = trades.Where(t => t.Symbol == TradingSymbol.ETHUSDT).Sum(t => t.NetPnlQuote);
        var bnbNet = trades.Where(t => t.Symbol == TradingSymbol.BNBUSDT).Sum(t => t.NetPnlQuote);
        var solNet = trades.Where(t => t.Symbol == TradingSymbol.SOLUSDT).Sum(t => t.NetPnlQuote);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is BNB still less bad than ETH/SOL?",
            Answer = $"ETH net={ethNet:F8}, BNB net={bnbNet:F8}, SOL net={solNet:F8}.",
            Verdict = bnbNet >= ethNet && bnbNet >= solNet ? "BNBLeastBad" : ethNet >= bnbNet && ethNet >= solNet ? "ETHLeastBad" : "SOLMixed",
            Details = new Dictionary<string, object?> { ["ethNet"] = ethNet, ["bnbNet"] = bnbNet, ["solNet"] = solNet }
        });

        var netWinners = trades.Count(t => t.NetPnlQuote > 0m);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does V2 produce any net winners after cost?",
            Answer = $"Trades={trades.Count}, netWinners={netWinners}, netPnl={trades.Sum(t => t.NetPnlQuote):F8}.",
            Verdict = netWinners > 0 ? "NetWinnersFound" : trades.Count == 0 ? "NoTradesUnderCostAwareFloor" : "AllNetLosers",
            Details = new Dictionary<string, object?> { ["netWinners"] = netWinners }
        });

        var viable = trades.Count > 0 && netWinners > 0 && (endOfData?.Count ?? 0) <= trades.Count / 10;
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is RangeExpansion viable under current Spot fee assumptions?",
            Answer = viable
                ? "V2 shows cost-aware trades with net winners; continue V2 target tuning."
                : trades.Count == 0
                    ? "Cost-aware larger-move setup produced zero trades; family not viable without bigger moves or lower costs."
                    : "V2 executes but all net losers; RangeExpansion not viable under current fee assumptions.",
            Verdict = trades.Count == 0 ? "NotViableNoTrades" : netWinners > 0 ? "PromisingContinueResearch" : "NotViableAllLosers",
            Details = new Dictionary<string, object?> { ["tradeCount"] = trades.Count, ["netWinners"] = netWinners }
        });

        return answers;
    }

    public static object BuildCostAnalysis(IReadOnlyList<RangeExpansionV2CandidateRecord> candidates)
    {
        var blocked = candidates.Where(c => !c.Executed).ToArray();
        var executed = candidates.Where(c => c.Executed).ToArray();
        return new
        {
            totalCandidates = candidates.Count,
            executedCount = executed.Length,
            blockedCount = blocked.Length,
            lockBelowRequiredGrossBlocked = blocked.Count(c =>
                c.RejectionReason?.Contains("LockBelowRequiredGross", StringComparison.OrdinalIgnoreCase) == true),
            medianLock90DistancePercent = Median(candidates.Select(c => c.Lock90DistancePercent)),
            medianRequiredGrossMovePercent = Median(candidates.Select(c => c.RequiredGrossMovePercent)),
            medianLock90NetProfitPercent = Median(candidates.Select(c => c.Lock90NetProfitPercent)),
            executedMedianLock90Net = Median(executed.Select(c => c.Lock90NetProfitPercent))
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
}

public sealed class RangeExpansionBreakoutV2ReportWriter(string outputDirectory)
{
    public async Task WriteAsync(
        IReadOnlyList<RangeExpansionV2SummaryRow> summaries,
        IReadOnlyList<RangeExpansionV2CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<BlockedEntryRecord> blockedEntries,
        IReadOnlyList<ReachabilityResearchAnswer> researchAnswers,
        IReadOnlyList<RangeExpansionV2ExitBreakdownRow> exitBreakdown,
        object costAnalysis,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        var executed = candidates.Where(c => c.Executed).ToArray();
        var blocked = candidates.Where(c => !c.Executed).ToArray();

        await WriteJsonAsync("range-expansion-v2-summary.json", summaries, cancellationToken);
        await WriteJsonAsync("range-expansion-v2-trades.json", executed, cancellationToken);
        await WriteJsonAsync("range-expansion-v2-blocked-candidates.json", blocked, cancellationToken);
        await WriteJsonAsync("range-expansion-v2-cost-analysis.json", costAnalysis, cancellationToken);
        await WriteJsonAsync("range-expansion-v2-exit-breakdown.json", exitBreakdown, cancellationToken);
        await WriteJsonAsync("range-expansion-v2-research-answers.json", researchAnswers, cancellationToken);

        await WriteSummaryCsvAsync("range-expansion-v2-summary.csv", summaries, cancellationToken);
        await WriteCandidatesCsvAsync("range-expansion-v2-trades.csv", executed, cancellationToken);
        await WriteCandidatesCsvAsync("range-expansion-v2-blocked-candidates.csv", blocked, cancellationToken);
        await WriteExitBreakdownCsvAsync("range-expansion-v2-exit-breakdown.csv", exitBreakdown, cancellationToken);
        await WriteCostAnalysisCsvAsync("range-expansion-v2-cost-analysis.csv", costAnalysis, cancellationToken);

        var standardWriter = new ReplayReportWriter(outputDirectory);
        await standardWriter.WriteAsync([], trades, blockedEntries, [], cancellationToken);
    }

    public async Task WriteExtendedDiagnosticsAsync(
        RangeExpansionV2ExtendedDiagnostics extended,
        bool includeV21Outputs,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        await WriteJsonAsync("range-expansion-v2-exit-outcome-comparison.json", extended.ExitOutcomeComparison, cancellationToken);
        await WriteJsonAsync("range-expansion-v2-failure-timing.json", extended.FailureTiming, cancellationToken);
        await WriteJsonAsync("range-expansion-v2-symbol-exit-breakdown.json", extended.SymbolExitBreakdown, cancellationToken);
        await WriteExitOutcomeComparisonCsvAsync("range-expansion-v2-exit-outcome-comparison.csv", extended.ExitOutcomeComparison, cancellationToken);
        await WriteFailureTimingCsvAsync("range-expansion-v2-failure-timing.csv", extended.FailureTiming, cancellationToken);
        await WriteSymbolExitBreakdownCsvAsync("range-expansion-v2-symbol-exit-breakdown.csv", extended.SymbolExitBreakdown, cancellationToken);

        if (!includeV21Outputs)
            return;

        await WriteJsonAsync("range-expansion-v21-fast-summary.json", extended.V21FastSummary, cancellationToken);
        await WriteJsonAsync("range-expansion-v21-research-answers.json", extended.V21ResearchAnswers, cancellationToken);
        await WriteV21FastSummaryCsvAsync("range-expansion-v21-fast-summary.csv", extended.V21FastSummary, cancellationToken);
    }

    private async Task WriteExitOutcomeComparisonCsvAsync(
        string fileName,
        IReadOnlyList<RangeExpansionV2OutcomeComparisonRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("bucket,count,netPnlQuote,medianRangeWidthPercent,medianBreakoutBodyStrengthPercent,medianBreakoutCloseAboveRangePercent,medianBreakoutCandleRangePercent,medianAtrPercent,medianAtrExpansionRatio,medianVolumeExpansionRatio,medianExpectedMovePercent,medianLock90DistancePercent,medianLock90NetProfitPercent,medianForwardMfe60Percent,medianForwardMae60Percent,medianMfePercent,medianMaePercent,medianDurationMinutes,medianTimeToMaxFavorableMinutes,medianTimeToStopMinutes,inflationRatePercent");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.Bucket), row.Count, row.NetPnlQuote.ToString(CultureInfo.InvariantCulture),
                FormatNullable(row.MedianRangeWidthPercent),
                FormatNullable(row.MedianBreakoutBodyStrengthPercent),
                FormatNullable(row.MedianBreakoutCloseAboveRangePercent),
                FormatNullable(row.MedianBreakoutCandleRangePercent),
                FormatNullable(row.MedianAtrPercent),
                FormatNullable(row.MedianAtrExpansionRatio),
                FormatNullable(row.MedianVolumeExpansionRatio),
                FormatNullable(row.MedianExpectedMovePercent),
                FormatNullable(row.MedianLock90DistancePercent),
                FormatNullable(row.MedianLock90NetProfitPercent),
                FormatNullable(row.MedianForwardMfe60Percent),
                FormatNullable(row.MedianForwardMae60Percent),
                FormatNullable(row.MedianMfePercent),
                FormatNullable(row.MedianMaePercent),
                FormatNullable(row.MedianDurationMinutes),
                FormatNullable(row.MedianTimeToMaxFavorableMinutes),
                FormatNullable(row.MedianTimeToStopMinutes),
                row.InflationRatePercent.ToString(CultureInfo.InvariantCulture)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), ct);
    }

    private async Task WriteFailureTimingCsvAsync(
        string fileName,
        IReadOnlyList<RangeExpansionV2FailureTimingRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("bucket,count,medianMfeBeforeStopPercent,medianMaeBeforeProfitLockPercent,medianTimeToMfeMinutes,medianTimeToMaeMinutes,didReachHalfLockBeforeStopCount,didReachHalfLockBeforeTimeStopCount,medianTimeStopExitMovePercent,timeStopNearBreakevenCount,timeStopGrossPositiveCount,timeStopNetNegativeOnlyDueToFeesCount");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.Bucket), row.Count,
                FormatNullable(row.MedianMfeBeforeStopPercent),
                FormatNullable(row.MedianMaeBeforeProfitLockPercent),
                FormatNullable(row.MedianTimeToMfeMinutes),
                FormatNullable(row.MedianTimeToMaeMinutes),
                row.DidReachHalfLockBeforeStopCount,
                row.DidReachHalfLockBeforeTimeStopCount,
                FormatNullable(row.MedianTimeStopExitMovePercent),
                row.TimeStopNearBreakevenCount,
                row.TimeStopGrossPositiveCount,
                row.TimeStopNetNegativeOnlyDueToFeesCount));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), ct);
    }

    private async Task WriteSymbolExitBreakdownCsvAsync(
        string fileName,
        IReadOnlyList<RangeExpansionV2SymbolExitRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("symbol,interval,tradeCount,profitLockRatePercent,stopLossRatePercent,timeStopRatePercent,profitLockNetPnlQuote,stopLossNetPnlQuote,timeStopNetPnlQuote,totalNetPnlQuote,medianMfePercent,medianMaePercent,medianLock90DistancePercent,medianForwardMfe60Percent,medianForwardMae60Percent");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.Symbol, row.Interval, row.TradeCount,
                row.ProfitLockRatePercent.ToString(CultureInfo.InvariantCulture),
                row.StopLossRatePercent.ToString(CultureInfo.InvariantCulture),
                row.TimeStopRatePercent.ToString(CultureInfo.InvariantCulture),
                row.ProfitLockNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.StopLossNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.TimeStopNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.TotalNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                FormatNullable(row.MedianMfePercent),
                FormatNullable(row.MedianMaePercent),
                FormatNullable(row.MedianLock90DistancePercent),
                FormatNullable(row.MedianForwardMfe60Percent),
                FormatNullable(row.MedianForwardMae60Percent)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), ct);
    }

    private async Task WriteV21FastSummaryCsvAsync(
        string fileName,
        IReadOnlyList<RangeExpansionV21FastSummaryRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("variantLabel,profileName,candidateCount,tradeCount,netWinnerCount,netPnlQuote,profitLockNetPnlQuote,stopLossNetPnlQuote,timeStopNetPnlQuote,profitLockCount,stopLossCount,timeStopCount,endOfDataCount");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.VariantLabel), Escape(row.ProfileName),
                row.CandidateCount, row.TradeCount, row.NetWinnerCount,
                row.NetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.ProfitLockNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.StopLossNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.TimeStopNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.ProfitLockCount, row.StopLossCount, row.TimeStopCount, row.EndOfDataCount));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), ct);
    }

    private async Task WriteJsonAsync<T>(string fileName, T value, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, fileName),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private async Task WriteSummaryCsvAsync(string fileName, IReadOnlyList<RangeExpansionV2SummaryRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("windowLabel,interval,profileName,symbol,candidateCount,executedCount,blockedCount,lock90MeetsRequiredGrossCount,lock90ReachableCount,estimatedNetPnlQuote,tradesCount,netWinnerCount,repeatabilityVerdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.WindowLabel, row.Interval, Escape(row.ProfileName), row.Symbol,
                row.CandidateCount, row.ExecutedCount, row.BlockedCount,
                row.Lock90MeetsRequiredGrossCount, row.Lock90ReachableCount,
                row.EstimatedNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.TradesCount, row.NetWinnerCount, row.RepeatabilityVerdict));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), ct);
    }

    private async Task WriteCandidatesCsvAsync(string fileName, IReadOnlyList<RangeExpansionV2CandidateRecord> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("windowLabel,interval,profileName,symbol,timeUtc,executed,rejectionReason,rangeWidthPercent,breakoutBodyStrengthPercent,breakoutCloseAboveRangePercent,atrPercent,atrExpansionRatio,volumeExpansionRatio,targetModelName,expectedMovePercent,lock90DistancePercent,estimatedRoundTripCostPercent,requiredNetProfitPercent,requiredGrossMovePercent,lock90NetProfitPercent,lock90ReachableWithin60m,lock90MeetsRequiredGross,forwardMfe60Percent,expectedMoveInflated,netPnlQuote,grossPnlQuote,exitReason,durationMinutes,isWinner");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.WindowLabel, row.Interval, Escape(row.ProfileName), row.Symbol,
                row.TimeUtc.ToString("O", CultureInfo.InvariantCulture), row.Executed, Escape(row.RejectionReason),
                row.RangeWidthPercent.ToString(CultureInfo.InvariantCulture),
                FormatNullable(row.BreakoutBodyStrengthPercent),
                FormatNullable(row.BreakoutCloseAboveRangePercent),
                row.AtrPercent.ToString(CultureInfo.InvariantCulture),
                row.AtrExpansionRatio.ToString(CultureInfo.InvariantCulture),
                row.VolumeExpansionRatio.ToString(CultureInfo.InvariantCulture),
                Escape(row.TargetModelName),
                FormatNullable(row.ExpectedMovePercent),
                FormatNullable(row.Lock90DistancePercent),
                FormatNullable(row.EstimatedRoundTripCostPercent),
                FormatNullable(row.RequiredNetProfitPercent),
                FormatNullable(row.RequiredGrossMovePercent),
                FormatNullable(row.Lock90NetProfitPercent),
                row.Lock90ReachableWithin60m, row.Lock90MeetsRequiredGross,
                FormatNullable(row.ForwardMfe60Percent), row.ExpectedMoveInflated,
                FormatNullable(row.NetPnlQuote), FormatNullable(row.GrossPnlQuote),
                Escape(row.ExitReason), row.DurationMinutes.ToString(CultureInfo.InvariantCulture),
                row.IsWinner));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), ct);
    }

    private async Task WriteExitBreakdownCsvAsync(string fileName, IReadOnlyList<RangeExpansionV2ExitBreakdownRow> rows, CancellationToken ct)
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

    private async Task WriteCostAnalysisCsvAsync(string fileName, object costAnalysis, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(costAnalysis);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), json, ct);
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
