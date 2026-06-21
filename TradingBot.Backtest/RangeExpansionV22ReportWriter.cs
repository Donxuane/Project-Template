using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public static class RangeExpansionV22ReportWriter
{
    public static async Task WriteAsync(
        string outputDirectory,
        RangeExpansionV22ExtendedDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        await WriteJsonAsync(outputDirectory, "range-expansion-v22-fast-summary.json", diagnostics.FastSummary, cancellationToken);
        await WriteJsonAsync(outputDirectory, "range-expansion-v22-filter-impact.json", diagnostics.FilterImpact, cancellationToken);
        await WriteJsonAsync(outputDirectory, "range-expansion-v22-exit-breakdown.json", diagnostics.ExitBreakdown, cancellationToken);
        await WriteJsonAsync(outputDirectory, "range-expansion-v22-winner-loser-comparison.json", diagnostics.WinnerLoserComparison, cancellationToken);
        await WriteJsonAsync(outputDirectory, "range-expansion-v22-research-answers.json", diagnostics.ResearchAnswers, cancellationToken);

        await WriteFastSummaryCsvAsync(outputDirectory, diagnostics.FastSummary, cancellationToken);
        await WriteFilterImpactCsvAsync(outputDirectory, diagnostics.FilterImpact, cancellationToken);
        await WriteExitBreakdownCsvAsync(outputDirectory, diagnostics.ExitBreakdown, cancellationToken);
        await WriteWinnerLoserCsvAsync(outputDirectory, diagnostics.WinnerLoserComparison, cancellationToken);
    }

    private static async Task WriteJsonAsync<T>(string dir, string fileName, T value, CancellationToken ct)
    {
        await File.WriteAllTextAsync(
            Path.Combine(dir, fileName),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
            ct);
    }

    private static async Task WriteFastSummaryCsvAsync(
        string dir,
        IReadOnlyList<RangeExpansionV22FastSummaryRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("variantLabel,profileName,candidateCount,tradeCount,netWinnerCount,netPnlQuote,deltaVsBaselineHalfLock,netPerTrade,profitLockNetPnlQuote,stopLossNetPnlQuote,timeStopNetPnlQuote,halfLockBreakevenNetPnlQuote,feeAwareBreakevenNetPnlQuote,profitLockCount,stopLossCount,timeStopCount,timeStopGrossPositiveNetNegativeCount,timeStopGrossNegativeCount");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.VariantLabel), Escape(row.ProfileName),
                row.CandidateCount, row.TradeCount, row.NetWinnerCount,
                row.NetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.DeltaVsBaselineHalfLock.ToString(CultureInfo.InvariantCulture),
                row.NetPerTrade.ToString(CultureInfo.InvariantCulture),
                row.ProfitLockNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.StopLossNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.TimeStopNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.HalfLockBreakevenNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.FeeAwareBreakevenNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.ProfitLockCount, row.StopLossCount, row.TimeStopCount,
                row.TimeStopGrossPositiveNetNegativeCount, row.TimeStopGrossNegativeCount));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "range-expansion-v22-fast-summary.csv"), sb.ToString(), ct);
    }

    private static async Task WriteFilterImpactCsvAsync(
        string dir,
        IReadOnlyList<RangeExpansionV22FilterImpactRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("variantLabel,profileName,candidateCount,executedCount,blockedCount,inflationProxyBlocked,stopToLockBlocked,breakoutQualityBlocked,failedBreakoutBlocked,profitLockCount,stopLossCount,profitLockRetentionPercent,stopLossReductionPercent,netPnlQuote");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.VariantLabel), Escape(row.ProfileName),
                row.CandidateCount, row.ExecutedCount, row.BlockedCount,
                row.InflationProxyBlocked, row.StopToLockBlocked, row.BreakoutQualityBlocked, row.FailedBreakoutBlocked,
                row.ProfitLockCount, row.StopLossCount,
                row.ProfitLockRetentionPercent.ToString(CultureInfo.InvariantCulture),
                row.StopLossReductionPercent.ToString(CultureInfo.InvariantCulture),
                row.NetPnlQuote.ToString(CultureInfo.InvariantCulture)));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "range-expansion-v22-filter-impact.csv"), sb.ToString(), ct);
    }

    private static async Task WriteExitBreakdownCsvAsync(
        string dir,
        IReadOnlyList<RangeExpansionV22ExitBreakdownRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("variantLabel,profileName,exitBucket,count,netPnlQuote,grossPnlQuote");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.VariantLabel), Escape(row.ProfileName), Escape(row.ExitBucket),
                row.Count,
                row.NetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.GrossPnlQuote.ToString(CultureInfo.InvariantCulture)));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "range-expansion-v22-exit-breakdown.csv"), sb.ToString(), ct);
    }

    private static async Task WriteWinnerLoserCsvAsync(
        string dir,
        IReadOnlyList<RangeExpansionV22WinnerLoserComparisonRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("bucket,count,medianStopToLockRatio,medianRealizedMoveProxyPercent,inflationAtEntryRatePercent,medianGivebackAtEntryPercent,medianBreakoutBodyStrengthPercent,medianBreakoutCloseAboveRangePercent,medianAtrExpansionRatio,medianVolumeExpansionRatio,medianMfePercent,medianMaePercent,medianForwardMfe60Percent,medianForwardMae60Percent");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.Bucket), row.Count,
                FormatNullable(row.MedianStopToLockRatio),
                FormatNullable(row.MedianRealizedMoveProxyPercent),
                row.InflationAtEntryRatePercent.ToString(CultureInfo.InvariantCulture),
                FormatNullable(row.MedianGivebackAtEntryPercent),
                FormatNullable(row.MedianBreakoutBodyStrengthPercent),
                FormatNullable(row.MedianBreakoutCloseAboveRangePercent),
                FormatNullable(row.MedianAtrExpansionRatio),
                FormatNullable(row.MedianVolumeExpansionRatio),
                FormatNullable(row.MedianMfePercent),
                FormatNullable(row.MedianMaePercent),
                FormatNullable(row.MedianForwardMfe60Percent),
                FormatNullable(row.MedianForwardMae60Percent)));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "range-expansion-v22-winner-loser-comparison.csv"), sb.ToString(), ct);
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
