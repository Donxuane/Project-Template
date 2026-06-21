using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public static class RangeExpansionV24ReportWriter
{
    public static async Task WriteAsync(
        string outputDirectory,
        RangeExpansionV24ExtendedDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        await WriteJsonAsync(outputDirectory, "range-expansion-v24-exit-policy-impact.json", diagnostics.ExitPolicyImpact, cancellationToken);
        await WriteJsonAsync(outputDirectory, "range-expansion-v24-window-robustness.json", diagnostics.WindowRobustness, cancellationToken);
        await WriteJsonAsync(outputDirectory, "range-expansion-v24-cost-sensitivity.json", diagnostics.CostSensitivity, cancellationToken);
        await WriteJsonAsync(outputDirectory, "range-expansion-v24-research-answers.json", diagnostics.ResearchAnswers, cancellationToken);

        await WriteExitPolicyImpactCsvAsync(outputDirectory, diagnostics.ExitPolicyImpact, cancellationToken);
        await WriteWindowRobustnessCsvAsync(outputDirectory, diagnostics.WindowRobustness, cancellationToken);
        await WriteCostSensitivityCsvAsync(outputDirectory, diagnostics.CostSensitivity, cancellationToken);

        await WriteJsonAsync(
            outputDirectory,
            "range-expansion-v24-fast-summary.json",
            diagnostics.ExitPolicyImpact,
            cancellationToken);
        await WriteExitPolicyImpactCsvAsync(
            outputDirectory,
            diagnostics.ExitPolicyImpact,
            cancellationToken,
            fileName: "range-expansion-v24-fast-summary.csv");
    }

    private static async Task WriteJsonAsync<T>(string dir, string fileName, T value, CancellationToken ct)
    {
        await File.WriteAllTextAsync(
            Path.Combine(dir, fileName),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
            ct);
    }

    private static async Task WriteExitPolicyImpactCsvAsync(
        string dir,
        IReadOnlyList<RangeExpansionV24ExitPolicyImpactRow> rows,
        CancellationToken ct,
        string fileName = "range-expansion-v24-exit-policy-impact.csv")
    {
        var sb = new StringBuilder();
        sb.AppendLine("variantLabel,profileName,exitPolicyGroup,profitLockThreshold,candidateCount,tradeCount,netWinnerCount,netPnlQuote,netPerTrade,profitLockCount,profitLockNetPnlQuote,avgProfitLockCapturedMfePercent,stopLossCount,stopLossNetPnlQuote,timeStopCount,timeStopNetPnlQuote,halfLockBreakevenCount,halfLockBreakevenNetPnlQuote,costCoveredBreakevenCount,costCoveredBreakevenNetPnlQuote,noProgressExitCount,noProgressExitNetPnlQuote,reachedHalfLockBeforeNoProgressDeadlineCount,profitableExitCount,profitLockPreservationRateVsBody80Current,timeStopReductionRateVsBody80Current,halfLockReductionRateVsBody80Current,deltaVsBody80Current,overfitWarning,meaningfulSample");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.VariantLabel), Escape(row.ProfileName), Escape(row.ExitPolicyGroup), Escape(row.ProfitLockThreshold),
                row.CandidateCount, row.TradeCount, row.NetWinnerCount,
                row.NetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.NetPerTrade.ToString(CultureInfo.InvariantCulture),
                row.ProfitLockCount, row.ProfitLockNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.AvgProfitLockCapturedMfePercent.ToString(CultureInfo.InvariantCulture),
                row.StopLossCount, row.StopLossNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.TimeStopCount, row.TimeStopNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.HalfLockBreakevenCount, row.HalfLockBreakevenNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.CostCoveredBreakevenCount, row.CostCoveredBreakevenNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.NoProgressExitCount, row.NoProgressExitNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.ReachedHalfLockBeforeNoProgressDeadlineCount, row.ProfitableExitCount,
                row.ProfitLockPreservationRateVsBody80Current.ToString(CultureInfo.InvariantCulture),
                row.TimeStopReductionRateVsBody80Current.ToString(CultureInfo.InvariantCulture),
                row.HalfLockReductionRateVsBody80Current.ToString(CultureInfo.InvariantCulture),
                row.DeltaVsBody80Current.ToString(CultureInfo.InvariantCulture),
                row.OverfitWarning, row.MeaningfulSample));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, fileName), sb.ToString(), ct);
    }

    private static async Task WriteWindowRobustnessCsvAsync(
        string dir,
        IReadOnlyList<RangeExpansionV24WindowRobustnessRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("variantLabel,profileName,windowLabel,tradeCount,profitLockCount,profitableExitCount,netPnlQuote,netPerTrade");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.VariantLabel), Escape(row.ProfileName), Escape(row.WindowLabel),
                row.TradeCount, row.ProfitLockCount, row.ProfitableExitCount,
                row.NetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.NetPerTrade.ToString(CultureInfo.InvariantCulture)));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "range-expansion-v24-window-robustness.csv"), sb.ToString(), ct);
    }

    private static async Task WriteCostSensitivityCsvAsync(
        string dir,
        IReadOnlyList<RangeExpansionV24CostSensitivityRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("variantLabel,profileName,costScenario,feeRatePercent,spreadPercent,tradeCount,netPnlQuote,netPerTrade,netWinnerCount");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.VariantLabel), Escape(row.ProfileName), Escape(row.CostScenario),
                row.FeeRatePercent.ToString(CultureInfo.InvariantCulture),
                row.SpreadPercent.ToString(CultureInfo.InvariantCulture),
                row.TradeCount,
                row.NetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.NetPerTrade.ToString(CultureInfo.InvariantCulture),
                row.NetWinnerCount));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "range-expansion-v24-cost-sensitivity.csv"), sb.ToString(), ct);
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"'))
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        return value;
    }
}
