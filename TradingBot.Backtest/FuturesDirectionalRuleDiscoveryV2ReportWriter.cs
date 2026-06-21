using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed class FuturesDirectionalRuleDiscoveryV2ReportWriter(string outputDirectory)
{
    public async Task WriteAsync(FuturesDirectionalRuleDiscoveryV2RunResult result, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        await WriteJsonAsync("futures-directional-v2-rule-candidates.json", result.Candidates, cancellationToken);
        await WriteJsonAsync("futures-directional-v2-trades.json", result.Trades, cancellationToken);
        await WriteJsonAsync("futures-directional-v2-split-performance.json", result.SplitPerformance, cancellationToken);
        await WriteJsonAsync("futures-directional-v2-window-robustness.json", result.WindowRobustness, cancellationToken);
        await WriteJsonAsync("futures-directional-v2-monthly-performance.json", result.MonthlyPerformance, cancellationToken);
        await WriteJsonAsync("futures-directional-v2-cost-sensitivity.json", result.CostSensitivity, cancellationToken);
        await WriteJsonAsync("futures-directional-v2-drawdown.json", result.Drawdown, cancellationToken);
        await WriteJsonAsync("futures-directional-v2-feature-importance-summary.json", result.FeatureImportance, cancellationToken);
        await WriteJsonAsync("futures-directional-v2-research-answers.json", result.Answers, cancellationToken);

        await WriteCandidatesCsvAsync(result.Candidates, cancellationToken);
        await WriteTradesCsvAsync(result.Trades, cancellationToken);
        await WriteSplitCsvAsync(result.SplitPerformance, cancellationToken);
        await WriteWindowCsvAsync(result.WindowRobustness, cancellationToken);
        await WriteMonthlyCsvAsync(result.MonthlyPerformance, cancellationToken);
        await WriteCostCsvAsync(result.CostSensitivity, cancellationToken);
        await WriteDrawdownCsvAsync(result.Drawdown, cancellationToken);
        await WriteFeatureImportanceCsvAsync(result.FeatureImportance, cancellationToken);
    }

    private async Task WriteJsonAsync<T>(string fileName, T payload, CancellationToken cancellationToken)
        => await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, fileName),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

    private async Task WriteCandidatesCsvAsync(IReadOnlyList<DiscoveryRuleCandidateRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,Symbol,Interval,Direction,RuleDescription,FeaturesUsed,FeatureCount,ConfigLabel,EntryMode,TargetPercent,StopPercent,MaxHoldMinutes,CooldownCandles,TotalTrades,TrainTrades,ValidationTrades,HoldoutTrades,TrainNet,ValidationNet,HoldoutNet,FullHistoryNet,PositiveMonths,TotalMonths,ProfitFactor,WinRate,MaxDrawdownQuote,TrainPositive,ValidationPositive,HoldoutPositive,AllSplitsPositive,FullHistoryPositive,StressPositive,StressPlusPositive,MonthlyConsistencyPass,TradeCountSufficient,OverfitWarning,UsesFutureInformation,ConfigVariantsTested,ConfigVariantsFullHistoryPositive,BestConfigLabel,BestConfigTrainNet,BestConfigValidationNet,BestConfigHoldoutNet,BestConfigFullHistoryNet,SelectionStage,Verdict");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(r.RuleName), Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), Csv(r.RuleDescription),
                Csv(r.FeaturesUsed), r.FeatureCount, Csv(r.ConfigLabel), Csv(r.EntryMode),
                r.TargetPercent, r.StopPercent, r.MaxHoldMinutes, r.CooldownCandles,
                r.TotalTrades, r.TrainTrades, r.ValidationTrades, r.HoldoutTrades,
                r.TrainNet, r.ValidationNet, r.HoldoutNet, r.FullHistoryNet,
                r.PositiveMonths, r.TotalMonths, r.ProfitFactor, r.WinRate, r.MaxDrawdownQuote,
                r.TrainPositive, r.ValidationPositive, r.HoldoutPositive, r.AllSplitsPositive, r.FullHistoryPositive,
                r.StressPositive, r.StressPlusPositive, r.MonthlyConsistencyPass, r.TradeCountSufficient,
                r.OverfitWarning, r.UsesFutureInformation, r.ConfigVariantsTested, r.ConfigVariantsFullHistoryPositive,
                Csv(r.BestConfigLabel), r.BestConfigTrainNet, r.BestConfigValidationNet, r.BestConfigHoldoutNet,
                r.BestConfigFullHistoryNet, Csv(r.SelectionStage), Csv(r.Verdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "futures-directional-v2-rule-candidates.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteTradesCsvAsync(IReadOnlyList<DiscoveryTradeRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,Symbol,Interval,Direction,EntryTimeUtc,ExitTimeUtc,Split,MonthKey,EntryPrice,ExitPrice,GrossPnlQuote,NetPnlQuote,ExitReason,DurationMinutes,AtrPercent,RangeWidthPercent,TrendSlopePercent,DistanceFromRecentHighPercent,DistanceFromRecentLowPercent,BtcReturn30mPercent,VolatilityRegime,SessionBucket,HourOfDayUtc");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(r.RuleName), Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction),
                r.EntryTimeUtc.ToString("o", CultureInfo.InvariantCulture), r.ExitTimeUtc.ToString("o", CultureInfo.InvariantCulture),
                Csv(r.Split), Csv(r.MonthKey), r.EntryPrice, r.ExitPrice, r.GrossPnlQuote, r.NetPnlQuote,
                Csv(r.ExitReason), r.DurationMinutes, r.AtrPercent, r.RangeWidthPercent, r.TrendSlopePercent,
                r.DistanceFromRecentHighPercent, r.DistanceFromRecentLowPercent, F(r.BtcReturn30mPercent),
                Csv(r.VolatilityRegime), Csv(r.SessionBucket), r.HourOfDayUtc));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "futures-directional-v2-trades.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteSplitCsvAsync(IReadOnlyList<DiscoverySplitPerformanceRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,Symbol,Interval,Direction,Split,CostScenarioLabel,TradeCount,WinCount,WinRate,NetPnlQuote,AvgNetPerTrade,MedianNetPerTrade,ProfitFactor,MaxDrawdownQuote,MaxConsecutiveLosses,WorstTradeNet,ProfitTargetRate,StopLossRate,TimeStopRate,AverageHoldMinutes,Positive");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(r.RuleName), Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), Csv(r.Split), Csv(r.CostScenarioLabel),
                r.TradeCount, r.WinCount, r.WinRate, r.NetPnlQuote, F(r.AvgNetPerTrade), F(r.MedianNetPerTrade),
                r.ProfitFactor, r.MaxDrawdownQuote, r.MaxConsecutiveLosses, r.WorstTradeNet,
                r.ProfitTargetRate, r.StopLossRate, r.TimeStopRate, r.AverageHoldMinutes, r.Positive));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "futures-directional-v2-split-performance.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteWindowCsvAsync(IReadOnlyList<DiscoveryWindowRobustnessRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,Symbol,Interval,Direction,WindowLabel,CostScenarioLabel,TradeCount,NetPnlQuote,WinRate,Positive");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(r.RuleName), Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), Csv(r.WindowLabel), Csv(r.CostScenarioLabel),
                r.TradeCount, r.NetPnlQuote, r.WinRate, r.Positive));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "futures-directional-v2-window-robustness.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteMonthlyCsvAsync(IReadOnlyList<DiscoveryMonthlyPerformanceRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,Symbol,Interval,Direction,MonthKey,CostScenarioLabel,TradeCount,NetPnlQuote,WinRate,Positive");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(r.RuleName), Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), Csv(r.MonthKey), Csv(r.CostScenarioLabel),
                r.TradeCount, r.NetPnlQuote, r.WinRate, r.Positive));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "futures-directional-v2-monthly-performance.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteCostCsvAsync(IReadOnlyList<DiscoveryCostSensitivityRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,Symbol,Interval,Direction,CostScenarioLabel,TradeCount,TrainNet,ValidationNet,HoldoutNet,FullHistoryNet,FullHistoryPositive,AllSplitsPositive");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(r.RuleName), Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), Csv(r.CostScenarioLabel),
                r.TradeCount, r.TrainNet, r.ValidationNet, r.HoldoutNet, r.FullHistoryNet, r.FullHistoryPositive, r.AllSplitsPositive));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "futures-directional-v2-cost-sensitivity.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteDrawdownCsvAsync(IReadOnlyList<DiscoveryDrawdownRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,Symbol,Interval,Direction,CostScenarioLabel,TotalTrades,MaxDrawdownQuote,MaxConsecutiveLosses,WorstTradeNet,BestTradeNet,ProfitFactor,AverageWin,AverageLoss,EquityFinalQuote,PositiveMonthsCount,TotalMonthsCount,WorstMonthNet,BestMonthNet");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(r.RuleName), Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), Csv(r.CostScenarioLabel),
                r.TotalTrades, r.MaxDrawdownQuote, r.MaxConsecutiveLosses, r.WorstTradeNet, r.BestTradeNet,
                r.ProfitFactor, F(r.AverageWin), F(r.AverageLoss), r.EquityFinalQuote,
                r.PositiveMonthsCount, r.TotalMonthsCount, r.WorstMonthNet, r.BestMonthNet));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "futures-directional-v2-drawdown.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteFeatureImportanceCsvAsync(IReadOnlyList<DiscoveryFeatureImportanceRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Feature,Direction,CandidateCount,TrainQualifiedCount,ValidationSurvivorCount,HoldoutPositiveCount,AvgTrainNet,AvgValidationNet,AvgHoldoutNet");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(r.Feature), Csv(r.Direction), r.CandidateCount, r.TrainQualifiedCount, r.ValidationSurvivorCount,
                r.HoldoutPositiveCount, F(r.AvgTrainNet), F(r.AvgValidationNet), F(r.AvgHoldoutNet)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "futures-directional-v2-feature-importance-summary.csv"), sb.ToString(), cancellationToken);
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
