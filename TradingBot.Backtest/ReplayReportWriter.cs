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
        sb.AppendLine("interval,profileName,symbols,tradesCount,wins,losses,winRatePercent,grossPnlQuote,estimatedNetPnlQuote,totalFeeAndSpreadEstimateQuote,averageWinQuote,averageLossQuote,maxConsecutiveLosses,averageTradeDurationMinutes,rawBuySignals,executedBuySignals,blockedBuySignals,strategyRejectedBuySignals,grossWinningTrades,grossWinRatePercent,netWinningTrades,netWinRatePercent,expectedTargetTouchTrades,expectedTargetTouchRatePercent,averageMfePercent,averageMaePercent,expectedTargetCounterfactualNetPnlQuote,expectedTargetCounterfactualDeltaQuote,profitCapture90TouchTrades,profitCapture95TouchTrades,profitCapture98TouchTrades,profitCapture90CounterfactualNetPnlQuote,profitCapture95CounterfactualNetPnlQuote,profitCapture98CounterfactualNetPnlQuote,profitCaptureDeltaVsOppositeSignalExitQuote,exitPolicyName,profitLockThresholdPercent,profitLockExitTrades,oppositeSignalExitTrades,breakevenExitTrades,trailingExitTrades,avgGivebackFromMfePercent,avgCapturedMfePercent,capturedMfeCalculationMode,avgCapturedMfeIncludingNegativeRatio,negativeCaptureTradeCount,netPnlByExitPolicy,enableLowVolatilityBreakoutEntry,enableNormalTrendPullbackContinuationOverride,normalTrendPullbackMinExpectedRewardRisk,enableNormalTrendMinDistanceToInvalidationFilter,normalTrendMinDistanceToInvalidationPercent,enableNormalTrendNearRecentHighRejection,normalTrendNearRecentHighRequiresRewardRisk,normalTrendNearRecentHighRequiresTrendStrengthPercent,enablePullbackOverrideHighVolatilityBlock,enableNormalTrendPullbackReclaimConfirmationFilter,normalTrendPullbackReclaimMode,enablePullbackFollowThroughV2,enablePullbackFollowThroughV3,pullbackV3MinResidualExpectedMovePercent,pullbackV3MinResidualNetMovePercent,pullbackV3MinResidualRewardRisk,pullbackV3RejectIfTargetAlreadyMostlyConsumed,pullbackV3MaxTargetConsumedPercent,breakoutLookbackCandles,breakoutBufferPercent,breakoutConfirmationCandles,minBreakoutSlopePercent,useConfirmedClosedCandlesForLowVolBreakout,blockedByReason,strategyRejectedByReason,symbolBreakdown,exitReasonBreakdown");
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
                row.StrategyRejectedBuySignals,
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
                row.ProfitCapture90TouchTrades,
                row.ProfitCapture95TouchTrades,
                row.ProfitCapture98TouchTrades,
                row.ProfitCapture90CounterfactualNetPnlQuote,
                row.ProfitCapture95CounterfactualNetPnlQuote,
                row.ProfitCapture98CounterfactualNetPnlQuote,
                row.ProfitCaptureDeltaVsOppositeSignalExitQuote,
                Escape(row.ExitPolicyName),
                ToCsvNullable(row.ProfitLockThresholdPercent),
                row.ProfitLockExitTrades,
                row.OppositeSignalExitTrades,
                row.BreakevenExitTrades,
                row.TrailingExitTrades,
                row.AvgGivebackFromMfePercent,
                row.AvgCapturedMfePercent,
                Escape(row.CapturedMfeCalculationMode),
                ToCsvNullable(row.AvgCapturedMfeIncludingNegativeRatio),
                row.NegativeCaptureTradeCount,
                Escape(FormatPnlBreakdown(row.NetPnlByExitPolicy)),
                row.EnableLowVolatilityBreakoutEntry,
                row.EnableNormalTrendPullbackContinuationOverride,
                row.NormalTrendPullbackMinExpectedRewardRisk,
                row.EnableNormalTrendMinDistanceToInvalidationFilter,
                row.NormalTrendMinDistanceToInvalidationPercent,
                row.EnableNormalTrendNearRecentHighRejection,
                row.NormalTrendNearRecentHighRequiresRewardRisk,
                ToCsvNullable(row.NormalTrendNearRecentHighRequiresTrendStrengthPercent),
                row.EnablePullbackOverrideHighVolatilityBlock,
                row.EnableNormalTrendPullbackReclaimConfirmationFilter,
                Escape(row.NormalTrendPullbackReclaimMode),
                row.EnablePullbackFollowThroughV2,
                row.EnablePullbackFollowThroughV3,
                row.PullbackV3MinResidualExpectedMovePercent,
                row.PullbackV3MinResidualNetMovePercent,
                row.PullbackV3MinResidualRewardRisk,
                row.PullbackV3RejectIfTargetAlreadyMostlyConsumed,
                row.PullbackV3MaxTargetConsumedPercent,
                row.BreakoutLookbackCandles,
                row.BreakoutBufferPercent,
                row.BreakoutConfirmationCandles,
                row.MinBreakoutSlopePercent,
                row.UseConfirmedClosedCandlesForLowVolBreakout,
                blockedByReason,
                Escape(FormatExitBreakdown(row.StrategyRejectedByReason)),
                symbolBreakdown,
                exitBreakdown));
        }

        await File.WriteAllTextAsync(path, sb.ToString(), cancellationToken);
    }

    private static async Task WriteTradesCsvAsync(string path, IReadOnlyList<SimulatedTrade> trades, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("interval,profileName,symbols,symbol,entryTimeUtc,entryPrice,exitTimeUtc,exitPrice,quantity,grossPnlQuote,estimatedNetPnlQuote,feeAndSpreadEstimateQuote,entryReason,exitReason,exitPolicyName,profitLockThresholdPercent,expectedMovePercent,expectedTargetPrice,expectedTargetSource,rewardRisk,consecutiveBullishCandles,currentCloseAboveRecentHigh,distanceToInvalidationPercent,previousCandleBearish,entryNearRecentHigh,shortMaSlopePercent,trendStrengthPercent,projectionMode,projectedExtension,wasGuarded,estimatedRoundTripCostPercent,estimatedNetMovePercent,maxFavorablePrice,maxAdversePrice,mfePercent,maePercent,givebackFromMfePercent,capturedMfePercent,touchedExpectedTarget,firstExpectedTargetTouchTimeUtc,counterfactualExitAtExpectedTargetNetPnlQuote,counterfactualDeltaVsActualNetPnlQuote,volatilityRegime,pullbackSetupDetected,pullbackReclaimConfirmed,pullbackFollowThroughConfirmed,pullbackRejectedReason,reclaimReferencePrice,followThroughReferencePrice,candlesWaitedAfterReclaim,residualExpectedMovePercent,residualEstimatedNetMovePercent,residualRewardRisk,distanceFromEntryToExpectedTargetPercent,profitCapture90Touched,profitCapture95Touched,profitCapture98Touched,profitCapture90CounterfactualNetPnlQuote,profitCapture95CounterfactualNetPnlQuote,profitCapture98CounterfactualNetPnlQuote,profitCaptureDeltaVsOppositeSignalExitQuote,durationMinutes");
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
                Escape(t.ExitPolicyName),
                ToCsvNullable(t.ProfitLockThresholdPercent),
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
                ToCsvNullable(t.GivebackFromMfePercent),
                ToCsvNullable(t.CapturedMfePercent),
                t.TouchedExpectedTarget,
                t.FirstExpectedTargetTouchTimeUtc?.ToString("O") ?? string.Empty,
                ToCsvNullable(t.CounterfactualExitAtExpectedTargetNetPnlQuote),
                ToCsvNullable(t.CounterfactualDeltaVsActualNetPnlQuote),
                Escape(t.VolatilityRegime),
                ToCsvNullable(t.PullbackSetupDetected),
                ToCsvNullable(t.PullbackReclaimConfirmed),
                ToCsvNullable(t.PullbackFollowThroughConfirmed),
                Escape(t.PullbackRejectedReason),
                ToCsvNullable(t.ReclaimReferencePrice),
                ToCsvNullable(t.FollowThroughReferencePrice),
                ToCsvNullable(t.CandlesWaitedAfterReclaim),
                ToCsvNullable(t.ResidualExpectedMovePercent),
                ToCsvNullable(t.ResidualEstimatedNetMovePercent),
                ToCsvNullable(t.ResidualRewardRisk),
                ToCsvNullable(t.DistanceFromEntryToExpectedTargetPercent),
                t.ProfitCapture90Touched,
                t.ProfitCapture95Touched,
                t.ProfitCapture98Touched,
                ToCsvNullable(t.ProfitCapture90CounterfactualNetPnlQuote),
                ToCsvNullable(t.ProfitCapture95CounterfactualNetPnlQuote),
                ToCsvNullable(t.ProfitCapture98CounterfactualNetPnlQuote),
                ToCsvNullable(t.ProfitCaptureDeltaVsOppositeSignalExitQuote),
                t.DurationMinutes));
        }

        await File.WriteAllTextAsync(path, sb.ToString(), cancellationToken);
    }

    private static async Task WriteBlockedEntriesCsvAsync(string path, IReadOnlyList<BlockedEntryRecord> blockedEntries, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("interval,profileName,symbols,symbol,timeUtc,rejectionLayer,reason,confidence,confidenceThreshold,expectedMovePercent,estimatedNetMovePercent,expectedTargetSource,signalReason,pullbackSetupDetected,pullbackReclaimConfirmed,pullbackFollowThroughConfirmed,pullbackRejectedReason,reclaimReferencePrice,followThroughReferencePrice,candlesWaitedAfterReclaim,residualExpectedMovePercent,residualEstimatedNetMovePercent,residualRewardRisk,distanceFromEntryToExpectedTargetPercent");
        foreach (var entry in blockedEntries)
        {
            sb.AppendLine(string.Join(",",
                entry.Interval,
                Escape(entry.ProfileName),
                Escape(entry.Symbols),
                entry.Symbol,
                entry.TimeUtc.ToString("O"),
                entry.RejectionLayer,
                Escape(entry.Reason),
                entry.Confidence,
                entry.ConfidenceThreshold,
                ToCsvNullable(entry.ExpectedMovePercent),
                ToCsvNullable(entry.EstimatedNetMovePercent),
                Escape(entry.ExpectedTargetSource),
                Escape(entry.SignalReason),
                ToCsvNullable(entry.PullbackSetupDetected),
                ToCsvNullable(entry.PullbackReclaimConfirmed),
                ToCsvNullable(entry.PullbackFollowThroughConfirmed),
                Escape(entry.PullbackRejectedReason),
                ToCsvNullable(entry.ReclaimReferencePrice),
                ToCsvNullable(entry.FollowThroughReferencePrice),
                ToCsvNullable(entry.CandlesWaitedAfterReclaim),
                ToCsvNullable(entry.ResidualExpectedMovePercent),
                ToCsvNullable(entry.ResidualEstimatedNetMovePercent),
                ToCsvNullable(entry.ResidualRewardRisk),
                ToCsvNullable(entry.DistanceFromEntryToExpectedTargetPercent)));
        }

        await File.WriteAllTextAsync(path, sb.ToString(), cancellationToken);
    }

    private static string FormatSymbolBreakdown(IReadOnlyDictionary<TradingSymbol, int> map)
        => string.Join("|", map.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));

    private static string FormatExitBreakdown(IReadOnlyDictionary<string, int> map)
        => string.Join("|", map.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => $"{kv.Key}:{kv.Value}"));

    private static string FormatPnlBreakdown(IReadOnlyDictionary<string, decimal> map)
        => string.Join("|", map.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => $"{kv.Key}:{kv.Value:F8}"));

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
