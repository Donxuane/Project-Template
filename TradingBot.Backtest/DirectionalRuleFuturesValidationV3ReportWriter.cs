using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed class DirectionalRuleFuturesValidationV3ReportWriter(string outputDirectory)
{
    public async Task WriteAsync(
        IReadOnlyList<DirectionalRuleV3FocusedSummaryRow> summaries,
        IReadOnlyList<DirectionalRuleV3TradeRecord> trades,
        IReadOnlyList<DirectionalRuleV3WindowRobustnessRow> windowRobustness,
        IReadOnlyList<DirectionalRuleV3CostSensitivityRow> costSensitivity,
        IReadOnlyList<DirectionalRuleV3DrawdownRow> drawdown,
        IReadOnlyList<DirectionalRuleV3VariantComparisonRow> variantComparison,
        IReadOnlyList<DirectionalRuleV3ReportConsistencyRow> reportConsistency,
        IReadOnlyList<ReachabilityResearchAnswer> researchAnswers,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        await WriteJsonAsync("directional-rule-v3-focused-summary.json", summaries, cancellationToken);
        await WriteJsonAsync("directional-rule-v3-trades.json", trades, cancellationToken);
        await WriteJsonAsync("directional-rule-v3-window-robustness.json", windowRobustness, cancellationToken);
        await WriteJsonAsync("directional-rule-v3-cost-sensitivity.json", costSensitivity, cancellationToken);
        await WriteJsonAsync("directional-rule-v3-drawdown.json", drawdown, cancellationToken);
        await WriteJsonAsync("directional-rule-v3-variant-comparison.json", variantComparison, cancellationToken);
        await WriteJsonAsync("directional-rule-v3-report-consistency.json", reportConsistency, cancellationToken);
        await WriteJsonAsync("directional-rule-v3-research-answers.json", researchAnswers, cancellationToken);

        await WriteSummaryCsvAsync(summaries, cancellationToken);
        await WriteTradesCsvAsync(trades, cancellationToken);
        await WriteWindowRobustnessCsvAsync(windowRobustness, cancellationToken);
        await WriteCostSensitivityCsvAsync(costSensitivity, cancellationToken);
        await WriteDrawdownCsvAsync(drawdown, cancellationToken);
        await WriteVariantComparisonCsvAsync(variantComparison, cancellationToken);
        await WriteReportConsistencyCsvAsync(reportConsistency, cancellationToken);
    }

    private async Task WriteJsonAsync<T>(string fileName, T payload, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, fileName),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private async Task WriteSummaryCsvAsync(
        IReadOnlyList<DirectionalRuleV3FocusedSummaryRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileKey,VariantLabel,IsPrimaryCandidate,WindowLabel,EntryMode,OverlapPolicy,CooldownCandlesAfterExit,TargetPercent,StopPercent,MaxHoldMinutes,CostScenarioLabel,SignalCount,ExecutedTrades,SkippedOverlapSignals,SkippedCooldownSignals,GrossPnlQuote,NetPnlQuote,AvgNetPnlPerTrade,MedianNetPerTrade,WinRate,AverageWin,AverageLoss,ProfitFactor,AggregatePositive,AllWindowsPositive,HoldoutPositive,StressPositive,StressAllWindowsPositive,Verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.ProfileKey), Csv(row.VariantLabel), row.IsPrimaryCandidate, Csv(row.WindowLabel),
                Csv(row.EntryMode), Csv(row.OverlapPolicy), row.CooldownCandlesAfterExit,
                row.TargetPercent, row.StopPercent, row.MaxHoldMinutes, Csv(row.CostScenarioLabel),
                row.SignalCount, row.ExecutedTrades, row.SkippedOverlapSignals, row.SkippedCooldownSignals,
                row.GrossPnlQuote, row.NetPnlQuote,
                F(row.AvgNetPnlPerTrade), F(row.MedianNetPerTrade), row.WinRate, F(row.AverageWin), F(row.AverageLoss),
                F(row.ProfitFactor), row.AggregatePositive, row.AllWindowsPositive, row.HoldoutPositive,
                row.StressPositive, row.StressAllWindowsPositive, Csv(row.Verdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v3-focused-summary.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteTradesCsvAsync(
        IReadOnlyList<DirectionalRuleV3TradeRecord> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileKey,VariantLabel,IsPrimaryCandidate,RuleName,Direction,Symbol,Interval,WindowLabel,EntryMode,TargetPercent,StopPercent,MaxHoldMinutes,CooldownCandlesAfterExit,OverlapPolicy,CostScenarioLabel,EntryTimeUtc,ExitTimeUtc,EntryPrice,ExitPrice,ExitReason,GrossPnlQuote,NetPnlQuote,FeesQuote,SlippageQuote,FundingQuote,DistanceFromRecentHighPercent,AtrPercent,BtcReturn30mPercent,VolatilityRegime,TrendSlopePercent,MfePercent,MaePercent,DurationMinutes");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.ProfileKey), Csv(row.VariantLabel), row.IsPrimaryCandidate, Csv(row.RuleName), row.Direction,
                row.Symbol, Csv(row.Interval), Csv(row.WindowLabel), Csv(row.EntryMode),
                row.TargetPercent, row.StopPercent, row.MaxHoldMinutes, row.CooldownCandlesAfterExit,
                Csv(row.OverlapPolicy), Csv(row.CostScenarioLabel),
                row.EntryTimeUtc.ToString("O", CultureInfo.InvariantCulture),
                row.ExitTimeUtc.ToString("O", CultureInfo.InvariantCulture),
                row.EntryPrice, row.ExitPrice, Csv(row.ExitReason),
                row.GrossPnlQuote, row.NetPnlQuote, row.FeesQuote, row.SlippageQuote, row.FundingQuote,
                row.DistanceFromRecentHighPercent, row.AtrPercent,
                row.BtcReturn30mPercent?.ToString(CultureInfo.InvariantCulture) ?? "",
                Csv(row.VolatilityRegime), row.TrendSlopePercent,
                row.MfePercent?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.MaePercent?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.DurationMinutes));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v3-trades.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteWindowRobustnessCsvAsync(
        IReadOnlyList<DirectionalRuleV3WindowRobustnessRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileKey,VariantLabel,IsPrimaryCandidate,EntryMode,OverlapPolicy,CooldownCandlesAfterExit,TargetPercent,StopPercent,MaxHoldMinutes,CostScenarioLabel,Window30dTrades,Window60dTrades,Window90dTrades,Window120dTrades,Window180dTrades,Holdout30dTrades,TrainReferenceTrades,Window30dNetPnl,Window60dNetPnl,Window90dNetPnl,Window120dNetPnl,Window180dNetPnl,Holdout30dNetPnl,TrainReferenceNetPnl,AggregateNetPnl,AggregatePositive,AllWindowsPositive,HoldoutPositive,StressPositive,StressAllWindowsPositive,RobustnessVerdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.ProfileKey), Csv(row.VariantLabel), row.IsPrimaryCandidate, Csv(row.EntryMode),
                Csv(row.OverlapPolicy), row.CooldownCandlesAfterExit, row.TargetPercent, row.StopPercent,
                row.MaxHoldMinutes, Csv(row.CostScenarioLabel),
                row.Window30dTrades, row.Window60dTrades, row.Window90dTrades, row.Window120dTrades, row.Window180dTrades,
                row.Holdout30dTrades, row.TrainReferenceTrades,
                row.Window30dNetPnl, row.Window60dNetPnl, row.Window90dNetPnl, row.Window120dNetPnl, row.Window180dNetPnl,
                row.Holdout30dNetPnl, row.TrainReferenceNetPnl, row.AggregateNetPnl,
                row.AggregatePositive, row.AllWindowsPositive, row.HoldoutPositive,
                row.StressPositive, row.StressAllWindowsPositive, Csv(row.RobustnessVerdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v3-window-robustness.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteCostSensitivityCsvAsync(
        IReadOnlyList<DirectionalRuleV3CostSensitivityRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileKey,VariantLabel,IsPrimaryCandidate,EntryMode,OverlapPolicy,CooldownCandlesAfterExit,MaxHoldMinutes,TargetPercent,StopPercent,CostScenarioLabel,RoundTripCostPercent,ExtraAdverseSlippagePercentPerSide,TradeCount,NetPnlQuote,AvgNetPnlPerTrade,AggregatePositive,StressPositive,StressAllWindowsPositive,Verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.ProfileKey), Csv(row.VariantLabel), row.IsPrimaryCandidate, Csv(row.EntryMode),
                Csv(row.OverlapPolicy), row.CooldownCandlesAfterExit, row.MaxHoldMinutes,
                row.TargetPercent, row.StopPercent, Csv(row.CostScenarioLabel),
                row.RoundTripCostPercent, row.ExtraAdverseSlippagePercentPerSide,
                row.TradeCount, row.NetPnlQuote, F(row.AvgNetPnlPerTrade),
                row.AggregatePositive, row.StressPositive, row.StressAllWindowsPositive, Csv(row.Verdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v3-cost-sensitivity.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteDrawdownCsvAsync(
        IReadOnlyList<DirectionalRuleV3DrawdownRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileKey,VariantLabel,IsPrimaryCandidate,WindowLabel,EntryMode,OverlapPolicy,CooldownCandlesAfterExit,MaxHoldMinutes,CostScenarioLabel,TradeCount,MaxConsecutiveLosses,MaxDrawdownQuote,WorstWindowNet,WorstTradeNet,ProfitFactor,WinRate,AverageWin,AverageLoss,MedianNetPerTrade,LongestFlatPeriodDays,LargestGivebackFromPeak");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.ProfileKey), Csv(row.VariantLabel), row.IsPrimaryCandidate, Csv(row.WindowLabel),
                Csv(row.EntryMode), Csv(row.OverlapPolicy), row.CooldownCandlesAfterExit, row.MaxHoldMinutes,
                Csv(row.CostScenarioLabel), row.TradeCount, row.MaxConsecutiveLosses, row.MaxDrawdownQuote,
                row.WorstWindowNet, row.WorstTradeNet, F(row.ProfitFactor), row.WinRate,
                F(row.AverageWin), F(row.AverageLoss), F(row.MedianNetPerTrade),
                row.LongestFlatPeriodDays, row.LargestGivebackFromPeak));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v3-drawdown.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteVariantComparisonCsvAsync(
        IReadOnlyList<DirectionalRuleV3VariantComparisonRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileKey,VariantLabel,IsPrimaryCandidate,EntryMode,OverlapPolicy,CooldownCandlesAfterExit,MaxHoldMinutes,TargetPercent,StopPercent,ExecutedTrades,AggregateNetPnl,Holdout30dNetPnl,Window90dNetPnl,MaxDrawdownQuote,MaxConsecutiveLosses,AllRollingWindowsPositive,HoldoutPositive,StressModerateLatencyPositive,ComparisonVerdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.ProfileKey), Csv(row.VariantLabel), row.IsPrimaryCandidate, Csv(row.EntryMode),
                Csv(row.OverlapPolicy), row.CooldownCandlesAfterExit, row.MaxHoldMinutes,
                row.TargetPercent, row.StopPercent, row.ExecutedTrades, row.AggregateNetPnl,
                row.Holdout30dNetPnl, row.Window90dNetPnl, row.MaxDrawdownQuote, row.MaxConsecutiveLosses,
                row.AllRollingWindowsPositive, row.HoldoutPositive, row.StressModerateLatencyPositive,
                Csv(row.ComparisonVerdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v3-variant-comparison.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteReportConsistencyCsvAsync(
        IReadOnlyList<DirectionalRuleV3ReportConsistencyRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileKey,VariantLabel,WindowLabel,CostScenarioLabel,ReportedTradeCount,ActualTradeRowCount,CountMismatch,MissingWindowLabels,MissingCostScenarioLabels,MissingExitReasons");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.ProfileKey), Csv(row.VariantLabel), Csv(row.WindowLabel), Csv(row.CostScenarioLabel),
                row.ReportedTradeCount, row.ActualTradeRowCount, row.CountMismatch,
                Csv(row.MissingWindowLabels), Csv(row.MissingCostScenarioLabels), Csv(row.MissingExitReasons)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v3-report-consistency.csv"), sb.ToString(), cancellationToken);
    }

    private static string F(decimal? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
