using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed class DirectionalRuleFuturesRegimeDriftAnalysisV1ReportWriter(string outputDirectory)
{
    public async Task WriteAsync(
        IReadOnlyList<RegimeDriftSummaryRow> summary,
        IReadOnlyList<RegimeDriftFeatureComparisonRow> featureComparison,
        IReadOnlyList<RegimeDriftMonthlyPerformanceRow> monthly,
        IReadOnlyList<RegimeDriftEntryTimeRuleRow> entryTimeRules,
        IReadOnlyList<RegimeDriftOutcomeRuleRow> outcomeRules,
        IReadOnlyList<ReachabilityResearchAnswer> answers,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        await WriteJsonAsync("directional-rule-v31-regime-drift-summary.json", summary, cancellationToken);
        await WriteJsonAsync("directional-rule-v31-recent-vs-older-feature-comparison.json", featureComparison, cancellationToken);
        await WriteJsonAsync("directional-rule-v31-monthly-performance.json", monthly, cancellationToken);
        await WriteJsonAsync("directional-rule-v31-entry-time-rule-discovery.json", entryTimeRules, cancellationToken);
        await WriteJsonAsync("directional-rule-v31-diagnostic-outcome-rules.json", outcomeRules, cancellationToken);
        await WriteJsonAsync("directional-rule-v31-regime-drift-answers.json", answers, cancellationToken);

        await WriteSummaryCsvAsync(summary, cancellationToken);
        await WriteFeatureComparisonCsvAsync(featureComparison, cancellationToken);
        await WriteMonthlyCsvAsync(monthly, cancellationToken);
        await WriteEntryTimeRulesCsvAsync(entryTimeRules, cancellationToken);
        await WriteOutcomeRulesCsvAsync(outcomeRules, cancellationToken);
    }

    private async Task WriteJsonAsync<T>(string fileName, T payload, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, fileName),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private async Task WriteSummaryCsvAsync(IReadOnlyList<RegimeDriftSummaryRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PeriodLabel,CostScenarioLabel,TradeCount,WinCount,WinRate,NetPnlQuote,AvgNetPerTrade,MedianNetPerTrade,TradeCountSufficient,PeriodPositive,Verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.PeriodLabel), Csv(row.CostScenarioLabel), row.TradeCount, row.WinCount, row.WinRate,
                row.NetPnlQuote, F(row.AvgNetPerTrade), F(row.MedianNetPerTrade),
                row.TradeCountSufficient, row.PeriodPositive, Csv(row.Verdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v31-regime-drift-summary.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteFeatureComparisonCsvAsync(
        IReadOnlyList<RegimeDriftFeatureComparisonRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ComparisonGroup,TradeCount,AvgDistanceFromRecentHighPercent,AvgDistanceFromRecentLowPercent,AvgRangeWidthPercent,AvgAtrPercent,AvgTrendSlopePercent,AvgBtcReturn30mPercent,AvgBtcReturn60mPercent,AvgNetPnlQuote,AvgMfePercent,AvgMaePercent,TopVolatilityRegime,TopSessionBucket,TopBtcTrendRegime,TopHourOfDayUtc");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.ComparisonGroup), row.TradeCount,
                F(row.AvgDistanceFromRecentHighPercent), F(row.AvgDistanceFromRecentLowPercent),
                F(row.AvgRangeWidthPercent), F(row.AvgAtrPercent), F(row.AvgTrendSlopePercent),
                F(row.AvgBtcReturn30mPercent), F(row.AvgBtcReturn60mPercent), F(row.AvgNetPnlQuote),
                F(row.AvgMfePercent), F(row.AvgMaePercent),
                Csv(row.TopVolatilityRegime), Csv(row.TopSessionBucket), Csv(row.TopBtcTrendRegime),
                row.TopHourOfDayUtc));
        }

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "directional-rule-v31-recent-vs-older-feature-comparison.csv"),
            sb.ToString(),
            cancellationToken);
    }

    private async Task WriteMonthlyCsvAsync(IReadOnlyList<RegimeDriftMonthlyPerformanceRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("MonthKey,CostScenarioLabel,TradeCount,WinCount,WinRate,NetPnlQuote,AvgNetPerTrade,MonthPositive");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.MonthKey), Csv(row.CostScenarioLabel), row.TradeCount, row.WinCount,
                row.WinRate, row.NetPnlQuote, F(row.AvgNetPerTrade), row.MonthPositive));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v31-monthly-performance.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteEntryTimeRulesCsvAsync(IReadOnlyList<RegimeDriftEntryTimeRuleRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,TrainPeriod,TestPeriod,RuleDescription,FeaturesUsed,TrainSamples,TestSamples,TrainNetPnlQuote,TestNetPnlQuote,TrainMedianNetPerTrade,TestMedianNetPerTrade,TrainWinRate,TestWinRate,TrainPositive,TestPositive,BothPeriodsPositive,SparseWarning,Verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.RuleName), Csv(row.TrainPeriod), Csv(row.TestPeriod), Csv(row.RuleDescription),
                Csv(row.FeaturesUsed), row.TrainSamples, row.TestSamples,
                row.TrainNetPnlQuote, row.TestNetPnlQuote, F(row.TrainMedianNetPerTrade), F(row.TestMedianNetPerTrade),
                row.TrainWinRate, row.TestWinRate, row.TrainPositive, row.TestPositive,
                row.BothPeriodsPositive, row.SparseWarning, Csv(row.Verdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v31-entry-time-rule-discovery.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteOutcomeRulesCsvAsync(IReadOnlyList<RegimeDriftOutcomeRuleRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,RuleDescription,TrainPeriod,TestPeriod,CostScenarioLabel,BaselineTrades,FilteredTrades,BaselineNetPnlQuote,FilteredNetPnlQuote,BaselineTestNetPnlQuote,FilteredTestNetPnlQuote,RemovesOlderLosers,KeepsRecentWinners,SurvivesBothPeriods,Verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.RuleName), Csv(row.RuleDescription), Csv(row.TrainPeriod), Csv(row.TestPeriod),
                Csv(row.CostScenarioLabel), row.BaselineTrades, row.FilteredTrades,
                row.BaselineNetPnlQuote, row.FilteredNetPnlQuote, row.BaselineTestNetPnlQuote, row.FilteredTestNetPnlQuote,
                row.RemovesOlderLosers, row.KeepsRecentWinners, row.SurvivesBothPeriods, Csv(row.Verdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v31-diagnostic-outcome-rules.csv"), sb.ToString(), cancellationToken);
    }

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
