using System.Text;
using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class ReplayReportWriter(string outputDirectory)
{
    public async Task WriteAsync(
        IReadOnlyList<ReplaySummaryRow> summaries,
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<BlockedEntryRecord> blockedEntries,
        IReadOnlyList<DataQualityIssue> issues,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        await WriteJsonAsync(Path.Combine(outputDirectory, "summary.json"), summaries, cancellationToken);
        await WriteJsonAsync(Path.Combine(outputDirectory, "trades.json"), trades, cancellationToken);
        await WriteJsonAsync(Path.Combine(outputDirectory, "blocked-entries.json"), blockedEntries, cancellationToken);
        await WriteJsonAsync(Path.Combine(outputDirectory, "data-quality.json"), issues, cancellationToken);
        await WriteSummaryCsvAsync(Path.Combine(outputDirectory, "summary.csv"), summaries, cancellationToken);
        await WriteTradesCsvAsync(Path.Combine(outputDirectory, "trades.csv"), trades, cancellationToken);
        await WriteBlockedEntriesCsvAsync(Path.Combine(outputDirectory, "blocked-entries.csv"), blockedEntries, cancellationToken);
    }

    public async Task WriteCrossIntervalSummaryAsync(IReadOnlyList<ReplaySummaryRow> summaries, CancellationToken cancellationToken)
    {
        await WriteJsonAsync(Path.Combine(outputDirectory, "cross-interval-summary.json"), summaries, cancellationToken);
        await WriteSummaryCsvAsync(Path.Combine(outputDirectory, "cross-interval-summary.csv"), summaries, cancellationToken);
    }

    public async Task WriteAggregationDiagnosticsAsync(IReadOnlyList<AggregationDiagnosticsRecord> diagnostics, CancellationToken cancellationToken)
    {
        await WriteJsonAsync(Path.Combine(outputDirectory, "aggregation-diagnostics.json"), diagnostics, cancellationToken);
        var sb = new StringBuilder();
        sb.AppendLine("interval,sourceInterval,targetInterval,symbol,inputCandleCount,outputCandleCount,droppedIncompleteFinalBucketCount,inheritedGapCount");
        foreach (var d in diagnostics)
        {
            sb.AppendLine(string.Join(",",
                d.Interval,
                d.SourceInterval,
                d.TargetInterval,
                d.Symbol,
                d.InputCandleCount,
                d.OutputCandleCount,
                d.DroppedIncompleteFinalBucketCount,
                d.InheritedGapCount));
        }
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "aggregation-diagnostics.csv"), sb.ToString(), cancellationToken);
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static async Task WriteSummaryCsvAsync(string path, IReadOnlyList<ReplaySummaryRow> summaries, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("interval,profileName,symbols,tradesCount,wins,losses,winRatePercent,grossPnlQuote,estimatedNetPnlQuote,totalFeeAndSpreadEstimateQuote,averageWinQuote,averageLossQuote,maxConsecutiveLosses,averageTradeDurationMinutes,rawBuySignals,executedBuySignals,blockedBuySignals,grossWinningTrades,grossWinRatePercent,netWinningTrades,netWinRatePercent,expectedTargetTouchTrades,expectedTargetTouchRatePercent,averageMfePercent,averageMaePercent,expectedTargetCounterfactualNetPnlQuote,expectedTargetCounterfactualDeltaQuote,enableLowVolatilityBreakoutEntry,breakoutLookbackCandles,breakoutBufferPercent,breakoutConfirmationCandles,minBreakoutSlopePercent,useConfirmedClosedCandlesForLowVolBreakout,blockedByReason,symbolBreakdown,exitReasonBreakdown");
        foreach (var row in summaries)
        {
            var symbols = Escape(row.Symbols);
            var blockedByReason = Escape(FormatExitBreakdown(row.BlockedByReason));
            var symbolBreakdown = Escape(FormatSymbolBreakdown(row.SymbolBreakdown));
            var exitBreakdown = Escape(FormatExitBreakdown(row.ExitReasonBreakdown));
            sb.AppendLine(string.Join(",",
                row.Interval,
                Escape(row.ProfileName),
                symbols,
                row.TradesCount,
                row.Wins,
                row.Losses,
                row.WinRatePercent,
                row.GrossPnlQuote,
                row.EstimatedNetPnlQuote,
                row.TotalFeeAndSpreadEstimateQuote,
                row.AverageWinQuote,
                row.AverageLossQuote,
                row.MaxConsecutiveLosses,
                row.AverageTradeDurationMinutes,
                row.RawBuySignals,
                row.ExecutedBuySignals,
                row.BlockedBuySignals,
                row.GrossWinningTrades,
                row.GrossWinRatePercent,
                row.NetWinningTrades,
                row.NetWinRatePercent,
                row.ExpectedTargetTouchTrades,
                row.ExpectedTargetTouchRatePercent,
                row.AverageMfePercent,
                row.AverageMaePercent,
                row.ExpectedTargetCounterfactualNetPnlQuote,
                row.ExpectedTargetCounterfactualDeltaQuote,
                row.EnableLowVolatilityBreakoutEntry,
                row.BreakoutLookbackCandles,
                row.BreakoutBufferPercent,
                row.BreakoutConfirmationCandles,
                row.MinBreakoutSlopePercent,
                row.UseConfirmedClosedCandlesForLowVolBreakout,
                blockedByReason,
                symbolBreakdown,
                exitBreakdown));
        }

        await File.WriteAllTextAsync(path, sb.ToString(), cancellationToken);
    }

    private static async Task WriteTradesCsvAsync(string path, IReadOnlyList<SimulatedTrade> trades, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("interval,profileName,symbols,symbol,entryTimeUtc,entryPrice,exitTimeUtc,exitPrice,quantity,grossPnlQuote,estimatedNetPnlQuote,feeAndSpreadEstimateQuote,entryReason,exitReason,expectedMovePercent,expectedTargetPrice,expectedTargetSource,rewardRisk,consecutiveBullishCandles,currentCloseAboveRecentHigh,distanceToInvalidationPercent,previousCandleBearish,entryNearRecentHigh,shortMaSlopePercent,trendStrengthPercent,projectionMode,projectedExtension,wasGuarded,estimatedRoundTripCostPercent,estimatedNetMovePercent,maxFavorablePrice,maxAdversePrice,mfePercent,maePercent,touchedExpectedTarget,firstExpectedTargetTouchTimeUtc,counterfactualExitAtExpectedTargetNetPnlQuote,counterfactualDeltaVsActualNetPnlQuote,volatilityRegime,durationMinutes");
        foreach (var t in trades)
        {
            sb.AppendLine(string.Join(",",
                t.Interval,
                Escape(t.ProfileName),
                Escape(t.Symbols),
                t.Symbol,
                t.EntryTimeUtc.ToString("O"),
                t.EntryPrice,
                t.ExitTimeUtc.ToString("O"),
                t.ExitPrice,
                t.Quantity,
                t.GrossPnlQuote,
                t.NetPnlQuote,
                t.FeeAndSpreadEstimateQuote,
                Escape(t.EntryReason),
                Escape(t.ExitReason),
                ToCsvNullable(t.ExpectedMovePercent),
                ToCsvNullable(t.ExpectedTargetPrice),
                Escape(t.ExpectedTargetSource),
                ToCsvNullable(t.RewardRisk),
                ToCsvNullable(t.ConsecutiveBullishTrendCandles),
                ToCsvNullable(t.CurrentCloseAboveRecentHigh),
                ToCsvNullable(t.DistanceToInvalidationPercent),
                ToCsvNullable(t.PreviousCandleBearish),
                ToCsvNullable(t.EntryNearRecentHigh),
                ToCsvNullable(t.ShortMaSlopePercent),
                ToCsvNullable(t.TrendStrengthPercent),
                Escape(t.ProjectionMode),
                ToCsvNullable(t.ProjectedExtension),
                t.WasGuarded.ToString(),
                ToCsvNullable(t.EstimatedRoundTripCostPercent),
                ToCsvNullable(t.EstimatedNetMovePercent),
                ToCsvNullable(t.MaxFavorablePrice),
                ToCsvNullable(t.MaxAdversePrice),
                ToCsvNullable(t.MfePercent),
                ToCsvNullable(t.MaePercent),
                t.TouchedExpectedTarget,
                t.FirstExpectedTargetTouchTimeUtc?.ToString("O") ?? string.Empty,
                ToCsvNullable(t.CounterfactualExitAtExpectedTargetNetPnlQuote),
                ToCsvNullable(t.CounterfactualDeltaVsActualNetPnlQuote),
                Escape(t.VolatilityRegime),
                t.DurationMinutes));
        }

        await File.WriteAllTextAsync(path, sb.ToString(), cancellationToken);
    }

    private static async Task WriteBlockedEntriesCsvAsync(string path, IReadOnlyList<BlockedEntryRecord> blockedEntries, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("interval,profileName,symbols,symbol,timeUtc,reason,confidence,confidenceThreshold,expectedMovePercent,estimatedNetMovePercent,expectedTargetSource,signalReason");
        foreach (var entry in blockedEntries)
        {
            sb.AppendLine(string.Join(",",
                entry.Interval,
                Escape(entry.ProfileName),
                Escape(entry.Symbols),
                entry.Symbol,
                entry.TimeUtc.ToString("O"),
                Escape(entry.Reason),
                entry.Confidence,
                entry.ConfidenceThreshold,
                ToCsvNullable(entry.ExpectedMovePercent),
                ToCsvNullable(entry.EstimatedNetMovePercent),
                Escape(entry.ExpectedTargetSource),
                Escape(entry.SignalReason)));
        }

        await File.WriteAllTextAsync(path, sb.ToString(), cancellationToken);
    }

    private static string FormatSymbolBreakdown(IReadOnlyDictionary<TradingSymbol, int> map)
        => string.Join("|", map.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));

    private static string FormatExitBreakdown(IReadOnlyDictionary<string, int> map)
        => string.Join("|", map.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => $"{kv.Key}:{kv.Value}"));

    private static string ToCsvNullable<T>(T? value) where T : struct
        => value?.ToString() ?? string.Empty;

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
