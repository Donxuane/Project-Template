using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public static class RangeExpansionV2FeasibilityReportWriter
{
    public static async Task WriteAsync(
        string outputDirectory,
        RangeExpansionV2FeasibilityExtendedDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        await WriteJsonAsync(outputDirectory, "range-expansion-v2-feasibility-summary.json", diagnostics.Summary, cancellationToken);
        await WriteJsonAsync(outputDirectory, "range-expansion-v2-cost-surface.json", diagnostics.CostSurface, cancellationToken);
        await WriteJsonAsync(outputDirectory, "range-expansion-v2-break-even-cost-analysis.json", diagnostics.BreakEvenAnalysis, cancellationToken);
        await WriteJsonAsync(outputDirectory, "range-expansion-v2-feasibility-answers.json", diagnostics.ResearchAnswers, cancellationToken);

        await WriteSummaryCsvAsync(outputDirectory, diagnostics.Summary, cancellationToken);
        await WriteCostSurfaceCsvAsync(outputDirectory, diagnostics.CostSurface, cancellationToken);
        await WriteBreakEvenCsvAsync(outputDirectory, diagnostics.BreakEvenAnalysis, cancellationToken);
    }

    private static async Task WriteJsonAsync<T>(string dir, string fileName, T value, CancellationToken ct)
    {
        await File.WriteAllTextAsync(
            Path.Combine(dir, fileName),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
            ct);
    }

    private static async Task WriteSummaryCsvAsync(
        string dir,
        IReadOnlyList<RangeExpansionV2FeasibilitySummaryRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("variantLabel,profileName,symbol,tradeCount,grossPnlQuote,currentCostNetPnlQuote,currentCostNetPerTrade,currentCostNetWinnerCount,breakEvenRoundTripCostPercent,maxRealisticScenarioNetPnlQuote,maxRealisticScenarioLabel,futuresSimModerateNetPnlQuote,positiveUnderRealisticLowerCost,positiveOnlyUnderUnrealisticCost");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.VariantLabel), Escape(row.ProfileName), Escape(row.Symbol),
                row.TradeCount,
                row.GrossPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.CurrentCostNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.CurrentCostNetPerTrade.ToString(CultureInfo.InvariantCulture),
                row.CurrentCostNetWinnerCount,
                row.BreakEvenRoundTripCostPercent.ToString(CultureInfo.InvariantCulture),
                row.MaxRealisticScenarioNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                Escape(row.MaxRealisticScenarioLabel),
                row.FuturesSimModerateNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.PositiveUnderRealisticLowerCost,
                row.PositiveOnlyUnderUnrealisticCost));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "range-expansion-v2-feasibility-summary.csv"), sb.ToString(), ct);
    }

    private static async Task WriteCostSurfaceCsvAsync(
        string dir,
        IReadOnlyList<RangeExpansionV2CostSurfaceRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("variantLabel,profileName,scenarioLabel,marketMode,feeRatePercent,spreadPercent,slippagePercent,fundingRatePercentPerHour,roundTripCostPercent,tradeCount,netPnlQuote,netPerTrade,netWinnerCount,isProfitable");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.VariantLabel), Escape(row.ProfileName), Escape(row.ScenarioLabel), Escape(row.MarketMode),
                row.FeeRatePercent.ToString(CultureInfo.InvariantCulture),
                row.SpreadPercent.ToString(CultureInfo.InvariantCulture),
                row.SlippagePercent.ToString(CultureInfo.InvariantCulture),
                row.FundingRatePercentPerHour.ToString(CultureInfo.InvariantCulture),
                row.RoundTripCostPercent.ToString(CultureInfo.InvariantCulture),
                row.TradeCount,
                row.NetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.NetPerTrade.ToString(CultureInfo.InvariantCulture),
                row.NetWinnerCount,
                row.IsProfitable));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "range-expansion-v2-cost-surface.csv"), sb.ToString(), ct);
    }

    private static async Task WriteBreakEvenCsvAsync(
        string dir,
        IReadOnlyList<RangeExpansionV2BreakEvenCostRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("variantLabel,profileName,tradeCount,grossPnlQuote,currentRoundTripCostPercent,currentCostNetPnlQuote,breakEvenRoundTripCostPercent,headroomToBreakEvenPercent,slip002NetAtLowFee,slip005NetAtLowFee");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.VariantLabel), Escape(row.ProfileName),
                row.TradeCount,
                row.GrossPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.CurrentRoundTripCostPercent.ToString(CultureInfo.InvariantCulture),
                row.CurrentCostNetPnlQuote.ToString(CultureInfo.InvariantCulture),
                row.BreakEvenRoundTripCostPercent.ToString(CultureInfo.InvariantCulture),
                row.HeadroomToBreakEvenPercent.ToString(CultureInfo.InvariantCulture),
                row.Slip002NetAtLowFee.ToString(CultureInfo.InvariantCulture),
                row.Slip005NetAtLowFee.ToString(CultureInfo.InvariantCulture)));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "range-expansion-v2-break-even-cost-analysis.csv"), sb.ToString(), ct);
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"'))
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        return value;
    }
}
