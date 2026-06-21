using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed class FuturesMarketDataExpansionV1ReportWriter(string outputDirectory)
{
    public async Task WriteAsync(FuturesMarketDataExpansionV1RunResult result, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        await Json("futures-data-availability.json", result.Availability, cancellationToken);
        await Json("futures-data-quality.json", result.Quality, cancellationToken);
        await Json("futures-flow-feature-summary.json", result.FlowFeatureSummary, cancellationToken);
        await Json("futures-flow-rule-candidates.json", result.RuleCandidates, cancellationToken);
        await Json("futures-flow-split-performance.json", result.SplitPerformance, cancellationToken);
        await Json("futures-flow-research-answers.json", result.Answers, cancellationToken);

        await AvailabilityCsv(result.Availability, cancellationToken);
        await QualityCsv(result.Quality, cancellationToken);
        await FeatureSummaryCsv(result.FlowFeatureSummary, cancellationToken);
        await CandidatesCsv(result.RuleCandidates, cancellationToken);
        await SplitCsv(result.SplitPerformance, cancellationToken);
    }

    private async Task Json<T>(string fileName, T payload, CancellationToken ct)
        => await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), ct);

    private async Task AvailabilityCsv(IReadOnlyList<FuturesDataAvailabilityRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,SourceKey,DisplayName,Endpoint,Granularity,AvailabilityClass,BootstrapSupported,LocalFilePresent,LocalRecordCount,LocalStartUtc,LocalEndUtc,LocalSpanDays,Supports365dStudy,Notes");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Csv(r.SourceKey), Csv(r.DisplayName), Csv(r.Endpoint), Csv(r.Granularity),
                Csv(r.AvailabilityClass), r.BootstrapSupported, r.LocalFilePresent, r.LocalRecordCount, Dt(r.LocalStartUtc), Dt(r.LocalEndUtc),
                r.LocalSpanDays, r.Supports365dStudy, Csv(r.Notes)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "futures-data-availability.csv"), sb.ToString(), ct);
    }

    private async Task QualityCsv(IReadOnlyList<FuturesDataQualityRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,SourceKey,RecordCount,DuplicateTimestampCount,GapCount,ExpectedCadenceMinutes,StartUtc,EndUtc,SpanDays,CoveragePercent,TimestampsSorted,AlignedWithCandles,AlignmentSampleCount,AlignmentMatchedCount,FieldAvailability,Verdict");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Csv(r.SourceKey), r.RecordCount, r.DuplicateTimestampCount, r.GapCount,
                r.ExpectedCadenceMinutes, Dt(r.StartUtc), Dt(r.EndUtc), r.SpanDays, r.CoveragePercent, r.TimestampsSorted,
                r.AlignedWithCandles, r.AlignmentSampleCount, r.AlignmentMatchedCount, Csv(r.FieldAvailability), Csv(r.Verdict)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "futures-data-quality.csv"), sb.ToString(), ct);
    }

    private async Task FeatureSummaryCsv(IReadOnlyList<FuturesFlowFeatureSummaryRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Feature,Symbol,Interval,SourceKey,SampleCount,NonNullCount,NonNullPercent,Min,Median,Max,Mean,StdDev,Supports365dStudy");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Feature), Csv(r.Symbol), Csv(r.Interval), Csv(r.SourceKey), r.SampleCount, r.NonNullCount,
                r.NonNullPercent, F(r.Min), F(r.Median), F(r.Max), F(r.Mean), F(r.StdDev), r.Supports365dStudy));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "futures-flow-feature-summary.csv"), sb.ToString(), ct);
    }

    private async Task CandidatesCsv(IReadOnlyList<FuturesFlowRuleCandidateRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,Symbol,Interval,Direction,RuleDescription,FeaturesUsed,UsesFlowFeature,FeatureCount,TotalTrades,TrainTrades,ValidationTrades,HoldoutTrades,TrainNet,ValidationNet,HoldoutNet,FullHistoryNet,WinRate,ProfitFactor,TrainPositive,ValidationPositive,HoldoutPositive,AllSplitsPositive,TradeCountSufficient,OverfitWarning,UsesFutureInformation,SelectionStage,Verdict");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.RuleName), Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), Csv(r.RuleDescription),
                Csv(r.FeaturesUsed), r.UsesFlowFeature, r.FeatureCount, r.TotalTrades, r.TrainTrades, r.ValidationTrades, r.HoldoutTrades,
                r.TrainNet, r.ValidationNet, r.HoldoutNet, r.FullHistoryNet, r.WinRate, r.ProfitFactor, r.TrainPositive, r.ValidationPositive,
                r.HoldoutPositive, r.AllSplitsPositive, r.TradeCountSufficient, r.OverfitWarning, r.UsesFutureInformation,
                Csv(r.SelectionStage), Csv(r.Verdict)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "futures-flow-rule-candidates.csv"), sb.ToString(), ct);
    }

    private async Task SplitCsv(IReadOnlyList<FuturesFlowSplitPerformanceRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,Symbol,Interval,Direction,Split,CostScenarioLabel,TradeCount,WinCount,WinRate,NetPnlQuote,AvgNetPerTrade,Positive");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.RuleName), Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), Csv(r.Split), Csv(r.CostScenarioLabel),
                r.TradeCount, r.WinCount, r.WinRate, r.NetPnlQuote, F(r.AvgNetPerTrade), r.Positive));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "futures-flow-split-performance.csv"), sb.ToString(), ct);
    }

    private static string Dt(DateTime? value) => value?.ToString("o", CultureInfo.InvariantCulture) ?? "";
    private static string F(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
