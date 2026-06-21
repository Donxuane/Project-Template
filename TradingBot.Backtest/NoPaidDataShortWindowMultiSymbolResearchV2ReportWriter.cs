using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed class NoPaidDataShortWindowMultiSymbolResearchV2ReportWriter(string outputDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task WriteAsync(NoPaidDataShortWindowMultiSymbolResearchV2RunResult result, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDirectory);

        await Json("multisymbol-data-coverage.json", result.Coverage, ct);
        await Json("multisymbol-base-rule-summary.json", result.BaseRuleSummary, ct);
        await Json("multisymbol-split-validation-summary.json", result.SplitValidation, ct);
        await Json("multisymbol-candidate-trades.json", result.CandidateTrades, ct);
        await Json("multisymbol-activation-periods.json", result.ActivationPeriods, ct);
        await Json("multisymbol-cost-sensitivity.json", result.CostSensitivity, ct);
        await Json("multisymbol-leaderboard.json", result.Leaderboard, ct);
        await Json("multisymbol-watchlist-candidates.json", result.Watchlist, ct);
        await Json("multisymbol-research-answers.json", result.Answers, ct);

        await CoverageCsv(result.Coverage, ct);
        await BaseRuleCsv(result.BaseRuleSummary, ct);
        await SplitCsv(result.SplitValidation, ct);
        await TradesCsv(result.CandidateTrades, ct);
        await PeriodsCsv(result.ActivationPeriods, ct);
        await CostCsv(result.CostSensitivity, ct);
        await LeaderboardCsv(result.Leaderboard, ct);
        await WatchlistCsv(result.Watchlist, ct);
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
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "multisymbol-data-coverage.csv"), sb.ToString(), ct);
    }

    private async Task BaseRuleCsv(IReadOnlyList<MultiSymbolBaseRuleSummaryRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Interval,Direction,RuleFamily,TargetPercent,StopPercent,MaxHoldMinutes,CooldownCandles,SignalCount,TradeCount,NetPnlQuote,WinRate,ProfitFactor,MaxDrawdownQuote,MaxConsecutiveLosses,DiscoveryNet,ValidationNet,HoldoutNet,DiscoveryTrades,ValidationTrades,HoldoutTrades,CostScenario");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), Csv(r.RuleFamily), r.TargetPercent, r.StopPercent, r.MaxHoldMinutes, r.CooldownCandles, r.SignalCount, r.TradeCount, r.NetPnlQuote, r.WinRate, r.ProfitFactor, r.MaxDrawdownQuote, r.MaxConsecutiveLosses, r.DiscoveryNet, r.ValidationNet, r.HoldoutNet, r.DiscoveryTrades, r.ValidationTrades, r.HoldoutTrades, Csv(r.CostScenario)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "multisymbol-base-rule-summary.csv"), sb.ToString(), ct);
    }

    private async Task SplitCsv(IReadOnlyList<MultiSymbolSplitValidationRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SplitScheme,Segment,SegmentStartUtc,SegmentEndUtc,Symbol,Interval,Direction,RuleFamily,TradeCount,NetPnlQuote,WinRate,ProfitFactor,SelectedInDiscovery,Notes");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.SplitScheme), Csv(r.Segment), Dt(r.SegmentStartUtc), Dt(r.SegmentEndUtc), Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), Csv(r.RuleFamily), r.TradeCount, r.NetPnlQuote, r.WinRate, r.ProfitFactor, r.SelectedInDiscovery, Csv(r.Notes)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "multisymbol-split-validation-summary.csv"), sb.ToString(), ct);
    }

    private async Task TradesCsv(IReadOnlyList<MultiSymbolCandidateTradeRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Interval,Direction,RuleFamily,ActivationRule,EntryTimeUtc,ExitTimeUtc,NetPnlQuote,IsWinner,ExitReason,Segment,CostScenario");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), Csv(r.RuleFamily), Csv(r.ActivationRule), Dt(r.EntryTimeUtc), Dt(r.ExitTimeUtc), r.NetPnlQuote, r.IsWinner, Csv(r.ExitReason), Csv(r.Segment), Csv(r.CostScenario)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "multisymbol-candidate-trades.csv"), sb.ToString(), ct);
    }

    private async Task PeriodsCsv(IReadOnlyList<MultiSymbolActivationPeriodRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Interval,Direction,RuleFamily,ActivationRule,CheckpointUtc,ActivationStartUtc,ActivationEndUtc,LookbackTradeCount,LookbackNetPnl,GateDataAvailable,GatePass,Activated,SkipReason,TradesInActivationWindow,NetInActivationWindow,CostScenario");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), Csv(r.RuleFamily), Csv(r.ActivationRule), Dt(r.CheckpointUtc), Dt(r.ActivationStartUtc), Dt(r.ActivationEndUtc), r.LookbackTradeCount, r.LookbackNetPnl, r.GateDataAvailable, r.GatePass, r.Activated, Csv(r.SkipReason), r.TradesInActivationWindow, r.NetInActivationWindow, Csv(r.CostScenario)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "multisymbol-activation-periods.csv"), sb.ToString(), ct);
    }

    private async Task CostCsv(IReadOnlyList<MultiSymbolCostSensitivityRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Interval,Direction,RuleFamily,ActivationRule,CostScenario,FullWindowTrades,FullWindowNet,ValidationHoldoutTrades,ValidationHoldoutNet,ValidationHoldoutNetPositive");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), Csv(r.RuleFamily), Csv(r.ActivationRule), Csv(r.CostScenario), r.FullWindowTrades, r.FullWindowNet, r.ValidationHoldoutTrades, r.ValidationHoldoutNet, r.ValidationHoldoutNetPositive));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "multisymbol-cost-sensitivity.csv"), sb.ToString(), ct);
    }

    private async Task LeaderboardCsv(IReadOnlyList<MultiSymbolLeaderboardRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Interval,Direction,RuleFamily,ActivationRule,DiscoveryNet,ValidationNet,HoldoutNet,FullWindowNet,TradeCount,ValidationTradeCount,HoldoutTradeCount,WinRate,ProfitFactor,MaxDrawdown,MaxConsecutiveLosses,PositiveActivatedPeriodsPercent,BestCostScenarioNet,ModerateLatencyNet,StressPlusNet,OverfitWarning,SparseWarning,SingleClusterWarning,Recommendation,SuggestedFrozenProfileName,Notes");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), Csv(r.RuleFamily), Csv(r.ActivationRule), r.DiscoveryNet, r.ValidationNet, r.HoldoutNet, r.FullWindowNet, r.TradeCount, r.ValidationTradeCount, r.HoldoutTradeCount, r.WinRate, r.ProfitFactor, r.MaxDrawdown, r.MaxConsecutiveLosses, r.PositiveActivatedPeriodsPercent, r.BestCostScenarioNet, r.ModerateLatencyNet, r.StressPlusNet, r.OverfitWarning, r.SparseWarning, r.SingleClusterWarning, Csv(r.Recommendation), Csv(r.SuggestedFrozenProfileName), Csv(r.Notes)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "multisymbol-leaderboard.csv"), sb.ToString(), ct);
    }

    private async Task WatchlistCsv(IReadOnlyList<MultiSymbolWatchlistCandidateRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Interval,Direction,RuleFamily,ActivationRule,DiscoveryNet,ValidationNet,HoldoutNet,ValidationHoldoutTradeCount,RequiredTradeCount,MissingTradeCount,CostScenarioResults,OverfitWarning,SparseWarning,SingleClusterWarning,Recommendation,NextRerunCondition,ExplicitlyTracked,Notes");
        foreach (var r in rows)
        {
            var costSummary = string.Join("; ", r.CostScenarioResults
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => $"{kv.Key}={kv.Value.ToString("F2", CultureInfo.InvariantCulture)}"));
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), Csv(r.RuleFamily), Csv(r.ActivationRule), r.DiscoveryNet, r.ValidationNet, r.HoldoutNet, r.ValidationHoldoutTradeCount, r.RequiredTradeCount, r.MissingTradeCount, Csv(costSummary), r.OverfitWarning, r.SparseWarning, r.SingleClusterWarning, Csv(r.Recommendation), Csv(r.NextRerunCondition), r.ExplicitlyTracked, Csv(r.Notes)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "multisymbol-watchlist-candidates.csv"), sb.ToString(), ct);
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
