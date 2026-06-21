using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed class NoPaidDataAdaptiveActivationV1ReportWriter(string outputDirectory)
{
    public async Task WriteAsync(NoPaidDataAdaptiveActivationV1RunResult result, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        await Json("adaptive-activation-summary.json", result.Summary, cancellationToken);
        await Json("adaptive-activation-trades.json", result.Trades, cancellationToken);
        await Json("adaptive-activation-periods.json", result.Periods, cancellationToken);
        await Json("adaptive-activation-window-performance.json", result.WindowPerformance, cancellationToken);
        await Json("adaptive-activation-cost-sensitivity.json", result.CostSensitivity, cancellationToken);
        await Json("adaptive-activation-drawdown.json", result.Drawdown, cancellationToken);
        await Json("adaptive-activation-research-answers.json", result.Answers, cancellationToken);

        await SummaryCsv(result.Summary, cancellationToken);
        await TradesCsv(result.Trades, cancellationToken);
        await PeriodsCsv(result.Periods, cancellationToken);
        await WindowCsv(result.WindowPerformance, cancellationToken);
        await CostCsv(result.CostSensitivity, cancellationToken);
        await DrawdownCsv(result.Drawdown, cancellationToken);
    }

    private async Task Json<T>(string fileName, T payload, CancellationToken ct)
        => await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), ct);

    private async Task SummaryCsv(IReadOnlyList<AdaptiveActivationSummaryRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ActivationRuleName,ConditionType,Description,CheckpointFrequencyDays,LookbackDays,ActivationPeriodDays,MinLookbackTrades,CostScenario,TotalTrades,BaselineTrades,TradeRetentionRate,Full365NetPnl,BaselineFull365NetPnl,Full365Delta,OlderNetPnl,BaselineOlderNetPnl,OlderDelta,Recent90dNetPnl,BaselineRecent90dNetPnl,Recent90dDelta,PositivePeriodsCount,TotalPeriodsCount,PositivePeriodRate,MaxDrawdownQuote,MaxConsecutiveLosses,WinRate,ProfitFactor,MeetsMinTrades,Full365NearBreakeven,OlderLossReduced,Recent90dPositive,PositivePeriodsMajority,PassesSuccessCriteria,Verdict");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.ActivationRuleName), Csv(r.ConditionType), Csv(r.Description), r.CheckpointFrequencyDays, r.LookbackDays, r.ActivationPeriodDays, r.MinLookbackTrades, Csv(r.CostScenario), r.TotalTrades, r.BaselineTrades, r.TradeRetentionRate, r.Full365NetPnl, r.BaselineFull365NetPnl, r.Full365Delta, r.OlderNetPnl, r.BaselineOlderNetPnl, r.OlderDelta, r.Recent90dNetPnl, r.BaselineRecent90dNetPnl, r.Recent90dDelta, r.PositivePeriodsCount, r.TotalPeriodsCount, r.PositivePeriodRate, r.MaxDrawdownQuote, r.MaxConsecutiveLosses, r.WinRate, r.ProfitFactor, r.MeetsMinTrades, r.Full365NearBreakeven, r.OlderLossReduced, r.Recent90dPositive, r.PositivePeriodsMajority, r.PassesSuccessCriteria, Csv(r.Verdict)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "adaptive-activation-summary.csv"), sb.ToString(), ct);
    }

    private async Task TradesCsv(IReadOnlyList<AdaptiveActivationTradeRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ActivationRuleName,EntryTimeUtc,ExitTimeUtc,NetPnlQuote,IsWinner,ExitReason,CostScenario,ActivationStartUtc,ActivationEndUtc,InOlder,InRecent90d,MonthKey");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.ActivationRuleName), Dt(r.EntryTimeUtc), Dt(r.ExitTimeUtc), r.NetPnlQuote, r.IsWinner, Csv(r.ExitReason), Csv(r.CostScenario), Dt(r.ActivationStartUtc), Dt(r.ActivationEndUtc), r.InOlder, r.InRecent90d, Csv(r.MonthKey)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "adaptive-activation-trades.csv"), sb.ToString(), ct);
    }

    private async Task PeriodsCsv(IReadOnlyList<AdaptiveActivationPeriodRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ActivationRuleName,LookbackDays,ActivationPeriodDays,CheckpointFrequencyDays,CheckpointUtc,ActivationStartUtc,ActivationEndUtc,LookbackTradeCount,LookbackNetPnl,LookbackProfitFactor,LookbackWinRate,Activated,DeactivationReason,TradesDuringActivation,NetPnlDuringActivation,CostScenario");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.ActivationRuleName), r.LookbackDays, r.ActivationPeriodDays, r.CheckpointFrequencyDays, Dt(r.CheckpointUtc), Dt(r.ActivationStartUtc), Dt(r.ActivationEndUtc), r.LookbackTradeCount, r.LookbackNetPnl, r.LookbackProfitFactor, r.LookbackWinRate, r.Activated, Csv(r.DeactivationReason), r.TradesDuringActivation, r.NetPnlDuringActivation, Csv(r.CostScenario)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "adaptive-activation-periods.csv"), sb.ToString(), ct);
    }

    private async Task WindowCsv(IReadOnlyList<AdaptiveActivationWindowPerformanceRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ActivationRuleName,WindowLabel,CostScenario,TradeCount,NetPnlQuote,BaselineNetPnlQuote,Delta,Positive");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.ActivationRuleName), Csv(r.WindowLabel), Csv(r.CostScenario), r.TradeCount, r.NetPnlQuote, r.BaselineNetPnlQuote, r.Delta, r.Positive));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "adaptive-activation-window-performance.csv"), sb.ToString(), ct);
    }

    private async Task CostCsv(IReadOnlyList<AdaptiveActivationCostSensitivityRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ActivationRuleName,CostScenario,TradeCount,Full365NetPnl,OlderNetPnl,Recent90dNetPnl,Full365Positive,SurvivesModerateSlippage,SurvivesStress");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.ActivationRuleName), Csv(r.CostScenario), r.TradeCount, r.Full365NetPnl, r.OlderNetPnl, r.Recent90dNetPnl, r.Full365Positive, r.SurvivesModerateSlippage, r.SurvivesStress));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "adaptive-activation-cost-sensitivity.csv"), sb.ToString(), ct);
    }

    private async Task DrawdownCsv(IReadOnlyList<AdaptiveActivationDrawdownRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ActivationRuleName,CostScenario,MaxDrawdownQuote,MaxConsecutiveLosses,WorstTradeNet,MaxDrawdownPeakUtc,MaxDrawdownTroughUtc");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.ActivationRuleName), Csv(r.CostScenario), r.MaxDrawdownQuote, r.MaxConsecutiveLosses, r.WorstTradeNet, Dt(r.MaxDrawdownPeakUtc), Dt(r.MaxDrawdownTroughUtc)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "adaptive-activation-drawdown.csv"), sb.ToString(), ct);
    }

    private static string Dt(DateTime? value) => value?.ToString("o", CultureInfo.InvariantCulture) ?? "";
    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
