using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed class RobustnessReportWriter(string outputDirectory)
{
    public async Task WriteAsync(
        IReadOnlyList<RobustnessWindowDetailRow> windowDetails,
        IReadOnlyList<RobustnessSummaryRow> summaries,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        await WriteJsonAsync(Path.Combine(outputDirectory, "robustness-window-details.json"), windowDetails, cancellationToken);
        await WriteJsonAsync(Path.Combine(outputDirectory, "robustness-summary.json"), summaries, cancellationToken);
        await WriteWindowDetailsCsvAsync(Path.Combine(outputDirectory, "robustness-window-details.csv"), windowDetails, cancellationToken);
        await WriteSummaryCsvAsync(Path.Combine(outputDirectory, "robustness-summary.csv"), summaries, cancellationToken);
        await WriteIntervalComparisonAsync(summaries, cancellationToken);
    }

    private async Task WriteIntervalComparisonAsync(IReadOnlyList<RobustnessSummaryRow> summaries, CancellationToken cancellationToken)
    {
        var comparison = summaries
            .OrderBy(x => x.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Interval, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await WriteJsonAsync(Path.Combine(outputDirectory, "interval-comparison-summary.json"), comparison, cancellationToken);
        await WriteSummaryCsvAsync(Path.Combine(outputDirectory, "interval-comparison-summary.csv"), comparison, cancellationToken);
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static async Task WriteWindowDetailsCsvAsync(string path, IReadOnlyList<RobustnessWindowDetailRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("profileName,interval,windowLabel,windowStartUtc,windowEndUtc,tradesCount,estimatedNetPnlQuote,netPnlBySymbol,profitLockExitTrades,oppositeSignalExitTrades,avgMfePercent,avgMaePercent,avgGivebackFromMfePercent,avgCapturedMfePercent,capturedMfeCalculationMode,avgCapturedMfeIncludingNegativeRatio,negativeCaptureTradeCount,bnbPullbackGuardEnabled,bnbPullbackGuardBlockedSignals,bnbPullbackGuardBlockedByReason");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.ProfileName),
                row.Interval,
                Escape(row.WindowLabel),
                row.WindowStartUtc.ToString("O"),
                row.WindowEndUtc.ToString("O"),
                row.TradesCount,
                row.EstimatedNetPnlQuote,
                Escape(FormatPnlBreakdown(row.NetPnlBySymbol)),
                row.ProfitLockExitTrades,
                row.OppositeSignalExitTrades,
                row.AvgMfePercent,
                row.AvgMaePercent,
                row.AvgGivebackFromMfePercent,
                row.AvgCapturedMfePercent,
                Escape(row.CapturedMfeCalculationMode),
                ToCsvNullable(row.AvgCapturedMfeIncludingNegativeRatio),
                row.NegativeCaptureTradeCount,
                row.BnbPullbackGuardEnabled,
                row.BnbPullbackGuardBlockedSignals,
                Escape(FormatCountBreakdown(row.BnbPullbackGuardBlockedByReason))));
        }

        await File.WriteAllTextAsync(path, sb.ToString(), cancellationToken);
    }

    private static async Task WriteSummaryCsvAsync(string path, IReadOnlyList<RobustnessSummaryRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("profileName,interval,windowStartUtc,windowEndUtc,windowCount,tradesCount,estimatedNetPnlQuote,netPnlBySymbol,profitLockExitTrades,oppositeSignalExitTrades,avgMfePercent,avgMaePercent,avgGivebackFromMfePercent,avgCapturedMfePercent,capturedMfeCalculationMode,avgCapturedMfeIncludingNegativeRatio,negativeCaptureTradeCount,positiveWindowsCount,negativeWindowsCount,medianNetPnlPerTrade,minWindowNetPnl,oneTradeProfileWarning,bnbPullbackGuardEnabled,bnbPullbackGuardBlockedSignals,bnbPullbackGuardBlockedByReason");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.ProfileName),
                row.Interval,
                row.WindowStartUtc.ToString("O"),
                row.WindowEndUtc.ToString("O"),
                row.WindowCount,
                row.TradesCount,
                row.EstimatedNetPnlQuote,
                Escape(FormatPnlBreakdown(row.NetPnlBySymbol)),
                row.ProfitLockExitTrades,
                row.OppositeSignalExitTrades,
                row.AvgMfePercent,
                row.AvgMaePercent,
                row.AvgGivebackFromMfePercent,
                row.AvgCapturedMfePercent,
                Escape(row.CapturedMfeCalculationMode),
                ToCsvNullable(row.AvgCapturedMfeIncludingNegativeRatio),
                row.NegativeCaptureTradeCount,
                row.PositiveWindowsCount,
                row.NegativeWindowsCount,
                row.MedianNetPnlPerTrade,
                row.MinWindowNetPnl,
                row.OneTradeProfileWarning,
                row.BnbPullbackGuardEnabled,
                row.BnbPullbackGuardBlockedSignals,
                Escape(FormatCountBreakdown(row.BnbPullbackGuardBlockedByReason))));
        }

        await File.WriteAllTextAsync(path, sb.ToString(), cancellationToken);
    }

    private static string FormatCountBreakdown(IReadOnlyDictionary<string, int> map)
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
