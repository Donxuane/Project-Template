using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed class LongShortFuturesFeasibilityStudyV1ReportWriter(string outputDirectory)
{
    public async Task WriteAsync(
        IReadOnlyList<LongShortFuturesFeasibilitySummaryRow> summary,
        IReadOnlyList<LongShortSymbolIntervalRankingRow> symbolIntervalRanking,
        IReadOnlyList<LongShortRegimeRankingRow> regimeRanking,
        IReadOnlyList<LongShortTargetStopMatrixRow> targetStopMatrix,
        IReadOnlyList<LongShortCostSensitivityRow> costSensitivity,
        IReadOnlyList<LongShortEntryTimeRuleRow> entryTimeRules,
        IReadOnlyList<ReachabilityResearchAnswer> researchAnswers,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        await WriteJsonAsync("long-short-feasibility-summary.json", summary, cancellationToken);
        await WriteJsonAsync("long-short-symbol-interval-ranking.json", symbolIntervalRanking, cancellationToken);
        await WriteJsonAsync("long-short-regime-ranking.json", regimeRanking, cancellationToken);
        await WriteJsonAsync("long-short-target-stop-matrix.json", targetStopMatrix, cancellationToken);
        await WriteJsonAsync("long-short-cost-sensitivity.json", costSensitivity, cancellationToken);
        await WriteJsonAsync("long-short-entry-time-rule-discovery.json", entryTimeRules, cancellationToken);
        await WriteJsonAsync("long-short-research-answers.json", researchAnswers, cancellationToken);

        await WriteSummaryCsvAsync(summary, cancellationToken);
        await WriteSymbolIntervalCsvAsync(symbolIntervalRanking, cancellationToken);
        await WriteRegimeCsvAsync(regimeRanking, cancellationToken);
        await WriteTargetMatrixCsvAsync(targetStopMatrix, cancellationToken);
        await WriteCostSensitivityCsvAsync(costSensitivity, cancellationToken);
    }

    private async Task WriteJsonAsync<T>(string fileName, T value, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, fileName),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private async Task WriteSummaryCsvAsync(IReadOnlyList<LongShortFuturesFeasibilitySummaryRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("windowLabel,symbol,interval,tradeMode,costScenarioLabel,sampleCount,medianExpectedNetPercent,target050BeforeStop050Rate,edgeScore,verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.WindowLabel, row.Symbol, row.Interval, row.TradeMode, row.CostScenarioLabel,
                row.SampleCount,
                row.MedianExpectedNetPercent.ToString(CultureInfo.InvariantCulture),
                row.Target050BeforeStop050Rate.ToString(CultureInfo.InvariantCulture),
                row.EdgeScore.ToString(CultureInfo.InvariantCulture),
                row.Verdict));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "long-short-feasibility-summary.csv"), sb.ToString(), ct);
    }

    private async Task WriteSymbolIntervalCsvAsync(IReadOnlyList<LongShortSymbolIntervalRankingRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("rank,windowLabel,symbol,interval,direction,costScenarioLabel,sampleCount,medianExpectedNetPercent,target050BeforeStop050Rate,edgeScore,verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.Rank, row.WindowLabel, row.Symbol, row.Interval, row.Direction, row.CostScenarioLabel,
                row.SampleCount,
                row.MedianExpectedNetPercent.ToString(CultureInfo.InvariantCulture),
                row.Target050BeforeStop050Rate.ToString(CultureInfo.InvariantCulture),
                row.EdgeScore.ToString(CultureInfo.InvariantCulture),
                row.Verdict));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "long-short-symbol-interval-ranking.csv"), sb.ToString(), ct);
    }

    private async Task WriteRegimeCsvAsync(IReadOnlyList<LongShortRegimeRankingRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("rank,windowLabel,bucketType,bucketLabel,symbol,interval,direction,costScenarioLabel,sampleCount,medianExpectedNetPercent,target050BeforeStop050Rate,edgeScore,verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.Rank, row.WindowLabel, Escape(row.BucketType), Escape(row.BucketLabel),
                row.Symbol, row.Interval, row.Direction, row.CostScenarioLabel,
                row.SampleCount,
                row.MedianExpectedNetPercent.ToString(CultureInfo.InvariantCulture),
                row.Target050BeforeStop050Rate.ToString(CultureInfo.InvariantCulture),
                row.EdgeScore.ToString(CultureInfo.InvariantCulture),
                row.Verdict));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "long-short-regime-ranking.csv"), sb.ToString(), ct);
    }

    private async Task WriteTargetMatrixCsvAsync(IReadOnlyList<LongShortTargetStopMatrixRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("windowLabel,symbol,interval,direction,costScenarioLabel,targetPercent,stopPercent,forwardHorizonMinutes,sampleCount,targetBeforeStopCount,stopBeforeTargetCount,targetBeforeStopRate,medianExpectedNetPercent");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.WindowLabel, row.Symbol, row.Interval, row.Direction, row.CostScenarioLabel,
                row.TargetPercent.ToString(CultureInfo.InvariantCulture),
                row.StopPercent.ToString(CultureInfo.InvariantCulture),
                row.ForwardHorizonMinutes,
                row.SampleCount, row.TargetBeforeStopCount, row.StopBeforeTargetCount,
                row.TargetBeforeStopRate.ToString(CultureInfo.InvariantCulture),
                row.MedianExpectedNetPercent.ToString(CultureInfo.InvariantCulture)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "long-short-target-stop-matrix.csv"), sb.ToString(), ct);
    }

    private async Task WriteCostSensitivityCsvAsync(IReadOnlyList<LongShortCostSensitivityRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("costScenarioLabel,tradeMode,direction,roundTripCostPercent,fundingRatePercentPerHour,sampleCount,medianExpectedNetPercent,target050BeforeStop050Rate,verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.CostScenarioLabel, row.TradeMode, row.Direction,
                row.RoundTripCostPercent.ToString(CultureInfo.InvariantCulture),
                row.FundingRatePercentPerHour.ToString(CultureInfo.InvariantCulture),
                row.SampleCount,
                row.MedianExpectedNetPercent.ToString(CultureInfo.InvariantCulture),
                row.Target050BeforeStop050Rate.ToString(CultureInfo.InvariantCulture),
                row.Verdict));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "long-short-cost-sensitivity.csv"), sb.ToString(), ct);
    }

    private static string Escape(string value)
        => value.Contains(',') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
