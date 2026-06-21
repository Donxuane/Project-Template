using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public static class RangeExpansionV23ReportWriter
{
    public static async Task WriteAsync(
        string outputDirectory,
        RangeExpansionV23ExtendedDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        await WriteJsonAsync(outputDirectory, "range-expansion-v23-filter-impact.json", diagnostics.FilterImpact, cancellationToken);
        await WriteJsonAsync(outputDirectory, "range-expansion-v23-window-robustness.json", diagnostics.WindowRobustness, cancellationToken);
        await WriteJsonAsync(outputDirectory, "range-expansion-v23-cost-sensitivity.json", diagnostics.CostSensitivity, cancellationToken);
        await WriteJsonAsync(outputDirectory, "range-expansion-v23-research-answers.json", diagnostics.ResearchAnswers, cancellationToken);

        await WriteFilterImpactCsvAsync(outputDirectory, diagnostics.FilterImpact, cancellationToken);
        await WriteWindowRobustnessCsvAsync(outputDirectory, diagnostics.WindowRobustness, cancellationToken);
        await WriteCostSensitivityCsvAsync(outputDirectory, diagnostics.CostSensitivity, cancellationToken);

        await WriteJsonAsync(
            outputDirectory,
            "range-expansion-v23-fast-summary.json",
            diagnostics.FilterImpact,
            cancellationToken);
        await WriteFilterImpactCsvAsync(
            outputDirectory,
            diagnostics.FilterImpact,
            cancellationToken,
            fileName: "range-expansion-v23-fast-summary.csv");
    }

    private static async Task WriteJsonAsync<T>(string dir, string fileName, T value, CancellationToken ct)
    {
        await File.WriteAllTextAsync(
            Path.Combine(dir, fileName),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
            ct);
    }

    private static async Task WriteFilterImpactCsvAsync(
        string dir,
        IReadOnlyList<RangeExpansionV23FilterImpactRow> rows,
        CancellationToken ct,
        string fileName = "range-expansion-v23-filter-impact.csv")
    {
        var sb = new StringBuilder();
        sb.AppendLine("variantLabel,profileName,sweepGroup,candidateCount,tradeCount,netWinnerCount,netPnlQuote,netPerTrade,profitLockCount,profitLockNetPnlQuote,stopLossCount,stopLossNetPnlQuote,timeStopCount,timeStopNetPnlQuote,halfLockBreakevenCount,halfLockBreakevenNetPnlQuote,profitLockPreservationRate,stopLossReductionRate,timeStopReductionRate,deltaVsFailedBreakoutRef,overfitWarning,meaningfulSample");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.VariantLabel), Escape(row.ProfileName), Escape(row.SweepGroup),
                row.CandidateCount, row.TradeCount, row.NetWinnerCount,
                row.NetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.NetPerTrade.ToString(CultureInfo.InvariantCulture),
                row.ProfitLockCount, row.ProfitLockNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.StopLossCount, row.StopLossNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.TimeStopCount, row.TimeStopNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.HalfLockBreakevenCount, row.HalfLockBreakevenNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.ProfitLockPreservationRate.ToString(CultureInfo.InvariantCulture),
                row.StopLossReductionRate.ToString(CultureInfo.InvariantCulture),
                row.TimeStopReductionRate.ToString(CultureInfo.InvariantCulture),
                row.DeltaVsFailedBreakoutRef.ToString(CultureInfo.InvariantCulture),
                row.OverfitWarning, row.MeaningfulSample));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, fileName), sb.ToString(), ct);
    }

    private static async Task WriteWindowRobustnessCsvAsync(
        string dir,
        IReadOnlyList<RangeExpansionV23WindowRobustnessRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("variantLabel,profileName,windowLabel,tradeCount,profitLockCount,netPnlQuote,netPerTrade");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.VariantLabel), Escape(row.ProfileName), Escape(row.WindowLabel),
                row.TradeCount, row.ProfitLockCount,
                row.NetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.NetPerTrade.ToString(CultureInfo.InvariantCulture)));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "range-expansion-v23-window-robustness.csv"), sb.ToString(), ct);
    }

    private static async Task WriteCostSensitivityCsvAsync(
        string dir,
        IReadOnlyList<RangeExpansionV23CostSensitivityRow> rows,
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

        await File.WriteAllTextAsync(Path.Combine(dir, "range-expansion-v23-cost-sensitivity.csv"), sb.ToString(), ct);
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (value.Contains(',') || value.Contains('"'))
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        return value;
    }
}
