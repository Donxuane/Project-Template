using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed class NoPaidDataShortWindowFlowResearchV1CrossSymbolReportWriter(string outputDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task WriteAsync(NoPaidDataShortWindowFlowResearchV1CrossSymbolRunResult result, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDirectory);

        await Json("cross-symbol-v1-data-coverage.json", result.Coverage, ct);
        await Json("cross-symbol-v1-summary.json", result.Summary, ct);
        await Json("cross-symbol-v1-leaderboard.json", result.Leaderboard, ct);
        await Json("cross-symbol-v1-trades.json", result.Trades, ct);
        await Json("cross-symbol-v1-periods.json", result.Periods, ct);
        await Json("cross-symbol-v1-cost-sensitivity.json", result.CostSensitivity, ct);
        await Json("cross-symbol-v1-research-answers.json", result.Answers, ct);

        await CoverageCsv(result.Coverage, ct);
        await SummaryCsv(result.Summary, ct);
        await LeaderboardCsv(result.Leaderboard, ct);
        await TradesCsv(result.Trades, ct);
        await PeriodsCsv(result.Periods, ct);
        await CostCsv(result.CostSensitivity, ct);
    }

    private async Task Json<T>(string fileName, T payload, CancellationToken ct)
        => await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, fileName), JsonSerializer.Serialize(payload, JsonOptions), ct);

    private async Task CoverageCsv(IReadOnlyList<MultiSymbolDataCoverageRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Interval,CandleDataPresent,CandleStartUtc,CandleEndUtc,CandleSpanDays,OiCoverageDays,TakerCoverageDays,GlobalLongShortCoverageDays,TopLongShortCoverageDays,FundingSpanDays,MarkIndexSpanDays,FlowStartUtc,FlowEndUtc,UsableWindowDays,EligibleForShortWindowResearch,Notes");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Csv(r.Interval), r.CandleDataPresent, Dt(r.CandleStartUtc), Dt(r.CandleEndUtc), r.CandleSpanDays, r.OiCoverageDays, r.TakerCoverageDays, r.GlobalLongShortCoverageDays, r.TopLongShortCoverageDays, r.FundingSpanDays, r.MarkIndexSpanDays, Dt(r.FlowStartUtc), Dt(r.FlowEndUtc), r.UsableWindowDays, r.EligibleForShortWindowResearch, Csv(r.Notes)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "cross-symbol-v1-data-coverage.csv"), sb.ToString(), ct);
    }

    private async Task SummaryCsv(IReadOnlyList<CrossSymbolSummaryRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Interval,Direction,TargetPercent,StopPercent,HoldHours,SignalCount,BaselineTrades,BaselineNet,BaselineWinRate,BaselineProfitFactor,BestActivationRule,BestActivationNet,BestActivationTrades,ConfigsEvaluated,ConfigsNetPositive,CostScenario");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), r.TargetPercent, r.StopPercent, r.HoldHours, r.SignalCount, r.BaselineTrades, r.BaselineNet, r.BaselineWinRate, r.BaselineProfitFactor, Csv(r.BestActivationRule), r.BestActivationNet, r.BestActivationTrades, r.ConfigsEvaluated, r.ConfigsNetPositive, Csv(r.CostScenario)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "cross-symbol-v1-summary.csv"), sb.ToString(), ct);
    }

    private async Task LeaderboardCsv(IReadOnlyList<CrossSymbolLeaderboardRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Interval,Direction,TargetPercent,StopPercent,HoldHours,ActivationRule,TradeCount,NetPnl,WinRate,ProfitFactor,MaxDrawdown,MaxConsecutiveLosses,PositiveActivatedPeriodsPercent,ModerateLatencyNet,StressPlusNet,SparseWarning,OverfitWarning,SingleClusterWarning,Recommendation,SuggestedFrozenProfileName,Notes");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), r.TargetPercent, r.StopPercent, r.HoldHours, Csv(r.ActivationRule), r.TradeCount, r.NetPnl, r.WinRate, r.ProfitFactor, r.MaxDrawdown, r.MaxConsecutiveLosses, r.PositiveActivatedPeriodsPercent, r.ModerateLatencyNet, r.StressPlusNet, r.SparseWarning, r.OverfitWarning, r.SingleClusterWarning, Csv(r.Recommendation), Csv(r.SuggestedFrozenProfileName), Csv(r.Notes)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "cross-symbol-v1-leaderboard.csv"), sb.ToString(), ct);
    }

    private async Task TradesCsv(IReadOnlyList<CrossSymbolTradeRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Interval,Direction,TargetPercent,StopPercent,ActivationRule,EntryTimeUtc,ExitTimeUtc,NetPnlQuote,IsWinner,ExitReason,CostScenario");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), r.TargetPercent, r.StopPercent, Csv(r.ActivationRule), Dt(r.EntryTimeUtc), Dt(r.ExitTimeUtc), r.NetPnlQuote, r.IsWinner, Csv(r.ExitReason), Csv(r.CostScenario)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "cross-symbol-v1-trades.csv"), sb.ToString(), ct);
    }

    private async Task PeriodsCsv(IReadOnlyList<CrossSymbolPeriodRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Interval,Direction,TargetPercent,StopPercent,ActivationRule,CheckpointUtc,ActivationStartUtc,ActivationEndUtc,LookbackTradeCount,LookbackNetPnl,LookbackProfitFactor,PerfPass,FlowDataAvailable,FlowPass,Activated,SkipReason,TradesInActivationWindow,NetInActivationWindow,CostScenario");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), r.TargetPercent, r.StopPercent, Csv(r.ActivationRule), Dt(r.CheckpointUtc), Dt(r.ActivationStartUtc), Dt(r.ActivationEndUtc), r.LookbackTradeCount, r.LookbackNetPnl, r.LookbackProfitFactor, r.PerfPass, r.FlowDataAvailable, r.FlowPass, r.Activated, Csv(r.SkipReason), r.TradesInActivationWindow, r.NetInActivationWindow, Csv(r.CostScenario)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "cross-symbol-v1-periods.csv"), sb.ToString(), ct);
    }

    private async Task CostCsv(IReadOnlyList<CrossSymbolCostSensitivityRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Interval,Direction,TargetPercent,StopPercent,ActivationRule,CostScenario,TradeCount,NetPnlQuote,WinRate,ProfitFactor,NetPositive");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), r.TargetPercent, r.StopPercent, Csv(r.ActivationRule), Csv(r.CostScenario), r.TradeCount, r.NetPnlQuote, r.WinRate, r.ProfitFactor, r.NetPositive));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "cross-symbol-v1-cost-sensitivity.csv"), sb.ToString(), ct);
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
