using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed class DirectionalRuleFuturesRegimeConditionalV2ReportWriter(string outputDirectory)
{
    public async Task WriteAsync(
        IReadOnlyList<RegimeConditionalSummaryRow> summary,
        IReadOnlyList<RegimeConditionalCostSensitivityRow> costSensitivity,
        IReadOnlyList<RegimeConditionalFilterImpactRow> filterImpact,
        IReadOnlyList<RegimeConditionalTradeRow> trades,
        IReadOnlyList<RegimeConditionalSummaryRow> monthlySource,
        IReadOnlyList<ReachabilityResearchAnswer> answers,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        await WriteJsonAsync("directional-rule-v32-regime-conditional-summary.json", summary, cancellationToken);
        await WriteJsonAsync("directional-rule-v32-trades.json", trades, cancellationToken);
        await WriteJsonAsync("directional-rule-v32-monthly-performance.json", BuildMonthlyRows(monthlySource), cancellationToken);
        await WriteJsonAsync("directional-rule-v32-cost-sensitivity.json", costSensitivity, cancellationToken);
        await WriteJsonAsync("directional-rule-v32-filter-impact.json", filterImpact, cancellationToken);
        await WriteJsonAsync("directional-rule-v32-research-answers.json", answers, cancellationToken);

        await WriteSummaryCsvAsync(summary, cancellationToken);
        await WriteTradesCsvAsync(trades, cancellationToken);
        await WriteMonthlyCsvAsync(monthlySource, cancellationToken);
        await WriteCostSensitivityCsvAsync(costSensitivity, cancellationToken);
        await WriteFilterImpactCsvAsync(filterImpact, cancellationToken);
    }

    private static IReadOnlyList<object> BuildMonthlyRows(IReadOnlyList<RegimeConditionalSummaryRow> summary)
        => summary
            .SelectMany(s => s.MonthlyNetPnl.Select(m => (object)new
            {
                s.FilterName,
                s.FilterGroup,
                s.CostScenarioLabel,
                m.MonthKey,
                m.TradeCount,
                m.NetPnlQuote,
                m.Positive
            }))
            .ToArray();

    private async Task WriteJsonAsync<T>(string fileName, T payload, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, fileName),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private async Task WriteSummaryCsvAsync(IReadOnlyList<RegimeConditionalSummaryRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FilterName,FilterGroup,FilterDescription,CostScenarioLabel,TradeCount,TradeCountOlder,TradeCountRecent,OlderNetPnl,Recent30dNetPnl,Recent60dNetPnl,Recent90dNetPnl,Full365NetPnl,TrainReferenceNetPnl,Holdout30dNetPnl,OlderAvgNetPerTrade,RecentAvgNetPerTrade,PositiveMonthsCount,TotalMonthsCount,SparseWarning,OlderViable,RecentViable,BothPeriodsViable,Full365Positive,MonthlyConsistencyImproved,PassesAllCriteria,Verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.FilterName), Csv(row.FilterGroup), Csv(row.FilterDescription), Csv(row.CostScenarioLabel),
                row.TradeCount, row.TradeCountOlder, row.TradeCountRecent,
                row.OlderNetPnl, row.Recent30dNetPnl, row.Recent60dNetPnl, row.Recent90dNetPnl,
                row.Full365NetPnl, row.TrainReferenceNetPnl, row.Holdout30dNetPnl,
                F(row.OlderAvgNetPerTrade), F(row.RecentAvgNetPerTrade),
                row.PositiveMonthsCount, row.TotalMonthsCount,
                row.SparseWarning, row.OlderViable, row.RecentViable, row.BothPeriodsViable,
                row.Full365Positive, row.MonthlyConsistencyImproved, row.PassesAllCriteria, Csv(row.Verdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v32-regime-conditional-summary.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteTradesCsvAsync(IReadOnlyList<RegimeConditionalTradeRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("EntryTimeUtc,ExitTimeUtc,NetPnlQuote,IsWinner,ExitReason,BtcReturn30mPercent,BtcReturn60mPercent,AtrPercent,TrendSlopePercent,DistanceFromRecentHighPercent,DistanceFromRecentLowPercent,RangeWidthPercent,VolatilityRegime,BtcTrendRegime,SessionBucket,HourOfDayUtc,DayOfWeek,MonthKey,InRecent30d,InRecent60d,InRecent90d,InOlder,InTrainReference,InHoldout30d");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.EntryTimeUtc.ToString("o", CultureInfo.InvariantCulture),
                row.ExitTimeUtc.ToString("o", CultureInfo.InvariantCulture),
                row.NetPnlQuote, row.IsWinner, Csv(row.ExitReason),
                F(row.BtcReturn30mPercent), F(row.BtcReturn60mPercent),
                row.AtrPercent, row.TrendSlopePercent,
                row.DistanceFromRecentHighPercent, row.DistanceFromRecentLowPercent, row.RangeWidthPercent,
                Csv(row.VolatilityRegime), Csv(row.BtcTrendRegime), Csv(row.SessionBucket),
                row.HourOfDayUtc, Csv(row.DayOfWeek), Csv(row.MonthKey),
                row.InRecent30d, row.InRecent60d, row.InRecent90d, row.InOlder, row.InTrainReference, row.InHoldout30d));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v32-trades.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteMonthlyCsvAsync(IReadOnlyList<RegimeConditionalSummaryRow> summary, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FilterName,FilterGroup,CostScenarioLabel,MonthKey,TradeCount,NetPnlQuote,Positive");
        foreach (var row in summary)
        {
            foreach (var month in row.MonthlyNetPnl)
            {
                sb.AppendLine(string.Join(",",
                    Csv(row.FilterName), Csv(row.FilterGroup), Csv(row.CostScenarioLabel),
                    Csv(month.MonthKey), month.TradeCount, month.NetPnlQuote, month.Positive));
            }
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v32-monthly-performance.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteCostSensitivityCsvAsync(IReadOnlyList<RegimeConditionalCostSensitivityRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FilterName,FilterGroup,CostScenarioLabel,TradeCount,TradeCountOlder,TradeCountRecent,OlderNetPnl,Recent90dNetPnl,Full365NetPnl,OlderViable,RecentViable,Full365Positive,SurvivesScenario");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.FilterName), Csv(row.FilterGroup), Csv(row.CostScenarioLabel),
                row.TradeCount, row.TradeCountOlder, row.TradeCountRecent,
                row.OlderNetPnl, row.Recent90dNetPnl, row.Full365NetPnl,
                row.OlderViable, row.RecentViable, row.Full365Positive, row.SurvivesScenario));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v32-cost-sensitivity.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteFilterImpactCsvAsync(IReadOnlyList<RegimeConditionalFilterImpactRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FilterName,FilterGroup,FilterDescription,BaselineTrades,FilteredTrades,TradeRetentionRate,BaselineFull365NetPnl,FilteredFull365NetPnl,Full365Delta,BaselineOlderNetPnl,FilteredOlderNetPnl,OlderDelta,BaselineRecent90dNetPnl,FilteredRecent90dNetPnl,Recent90dDelta,BaselinePositiveMonths,FilteredPositiveMonths,TotalMonths");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.FilterName), Csv(row.FilterGroup), Csv(row.FilterDescription),
                row.BaselineTrades, row.FilteredTrades, row.TradeRetentionRate,
                row.BaselineFull365NetPnl, row.FilteredFull365NetPnl, row.Full365Delta,
                row.BaselineOlderNetPnl, row.FilteredOlderNetPnl, row.OlderDelta,
                row.BaselineRecent90dNetPnl, row.FilteredRecent90dNetPnl, row.Recent90dDelta,
                row.BaselinePositiveMonths, row.FilteredPositiveMonths, row.TotalMonths));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v32-filter-impact.csv"), sb.ToString(), cancellationToken);
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
