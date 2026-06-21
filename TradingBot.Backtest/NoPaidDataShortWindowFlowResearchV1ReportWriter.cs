using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed class NoPaidDataShortWindowFlowResearchV1ReportWriter(string outputDirectory)
{
    public async Task WriteAsync(NoPaidDataShortWindowFlowResearchV1RunResult result, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        await Json("no-paid-short-window-data-availability.json", result.Availability, cancellationToken);
        await Json("no-paid-short-window-feature-sample.json", result.FeatureSamples, cancellationToken);
        await Json("no-paid-short-window-activation-summary.json", result.Summary, cancellationToken);
        await Json("no-paid-short-window-trades.json", result.Trades, cancellationToken);
        await Json("no-paid-short-window-periods.json", result.Periods, cancellationToken);
        await Json("no-paid-short-window-cost-sensitivity.json", result.CostSensitivity, cancellationToken);
        await Json("no-paid-short-window-research-answers.json", result.Answers, cancellationToken);

        await AvailabilityCsv(result.Availability, cancellationToken);
        await FeatureSampleCsv(result.FeatureSamples, cancellationToken);
        await SummaryCsv(result.Summary, cancellationToken);
        await TradesCsv(result.Trades, cancellationToken);
        await PeriodsCsv(result.Periods, cancellationToken);
        await CostCsv(result.CostSensitivity, cancellationToken);
    }

    private async Task Json<T>(string fileName, T payload, CancellationToken ct)
        => await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), ct);

    private async Task AvailabilityCsv(IReadOnlyList<ShortWindowDataAvailabilityRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Provider,Symbol,SourceKey,DisplayName,Endpoint,IntervalOptions,RequestedInterval,MaxLookbackDocumented,MaxLookbackDaysObserved,RateLimitNotes,SymbolsSupported,LocalFilePresent,LocalRecordCount,LocalStartUtc,LocalEndUtc,LocalSpanDays,UsefulFor7d,UsefulFor14d,UsefulFor30d,UsefulFor365d,ProbeStatus,Notes");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Provider), Csv(r.Symbol), Csv(r.SourceKey), Csv(r.DisplayName), Csv(r.Endpoint), Csv(r.IntervalOptions), Csv(r.RequestedInterval), Csv(r.MaxLookbackDocumented), N(r.MaxLookbackDaysObserved), Csv(r.RateLimitNotes), Csv(r.SymbolsSupported), r.LocalFilePresent, r.LocalRecordCount, Dt(r.LocalStartUtc), Dt(r.LocalEndUtc), r.LocalSpanDays, r.UsefulFor7d, r.UsefulFor14d, r.UsefulFor30d, r.UsefulFor365d, Csv(r.ProbeStatus), Csv(r.Notes)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "no-paid-short-window-data-availability.csv"), sb.ToString(), ct);
    }

    private async Task FeatureSampleCsv(IReadOnlyList<ShortWindowFeatureSampleRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,TimestampUtc,OiChange5mPercent,OiChange15mPercent,OiChange30mPercent,OiChange60mPercent,OiZScoreRecent,TakerBuySellImbalance,TakerImbalance1h,GlobalLongShortRatio,GlobalLongShortRatioChange1hPercent,GlobalLongShortZScore,TopLongShortRatio,TopLongShortZScore,FundingRate,FundingZScore,MarkIndexDivergencePercent,BtcReturn30mPercent,BtcReturn60mPercent,BtcTrendSlopePercentPerHour,VolatilityRegime,AtrPercent,DistanceFromRecentHighPercent,DistanceFromRecentLowPercent");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Dt(r.TimestampUtc), N(r.OiChange5mPercent), N(r.OiChange15mPercent), N(r.OiChange30mPercent), N(r.OiChange60mPercent), N(r.OiZScoreRecent), N(r.TakerBuySellImbalance), N(r.TakerImbalance1h), N(r.GlobalLongShortRatio), N(r.GlobalLongShortRatioChange1hPercent), N(r.GlobalLongShortZScore), N(r.TopLongShortRatio), N(r.TopLongShortZScore), N(r.FundingRate), N(r.FundingZScore), N(r.MarkIndexDivergencePercent), N(r.BtcReturn30mPercent), N(r.BtcReturn60mPercent), N(r.BtcTrendSlopePercentPerHour), Csv(r.VolatilityRegime), N(r.AtrPercent), N(r.DistanceFromRecentHighPercent), N(r.DistanceFromRecentLowPercent)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "no-paid-short-window-feature-sample.csv"), sb.ToString(), ct);
    }

    private async Task SummaryCsv(IReadOnlyList<ShortWindowSummaryRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ActivationRuleName,PerfCondition,FlowCondition,Description,CheckpointFrequencyHours,LookbackDays,ActivationPeriodHours,MinLookbackTrades,ProfitFactorThreshold,CostScenario,TotalTrades,BaselineTrades,NetPnlQuote,BaselineNetPnlQuote,Delta,WinRate,ProfitFactor,MaxDrawdownQuote,MaxConsecutiveLosses,CheckpointCount,ActivatedPeriodCount,PositivePeriodCount,PositivePeriodRate,ActivationClusterCount,SparseActivationCount,FlowUnavailableCheckpointCount,MeetsMinExecutedTrades,SparseFlagged,NetPositive,Latency002NetPnl,SurvivesModerateSlippage002,MultipleClusters,PassesSuccessCriteria,Verdict");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.ActivationRuleName), Csv(r.PerfCondition), Csv(r.FlowCondition), Csv(r.Description), r.CheckpointFrequencyHours, r.LookbackDays, r.ActivationPeriodHours, r.MinLookbackTrades, N(r.ProfitFactorThreshold), Csv(r.CostScenario), r.TotalTrades, r.BaselineTrades, r.NetPnlQuote, r.BaselineNetPnlQuote, r.Delta, r.WinRate, r.ProfitFactor, r.MaxDrawdownQuote, r.MaxConsecutiveLosses, r.CheckpointCount, r.ActivatedPeriodCount, r.PositivePeriodCount, r.PositivePeriodRate, r.ActivationClusterCount, r.SparseActivationCount, r.FlowUnavailableCheckpointCount, r.MeetsMinExecutedTrades, r.SparseFlagged, r.NetPositive, N(r.Latency002NetPnl), r.SurvivesModerateSlippage002, r.MultipleClusters, r.PassesSuccessCriteria, Csv(r.Verdict)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "no-paid-short-window-activation-summary.csv"), sb.ToString(), ct);
    }

    private async Task TradesCsv(IReadOnlyList<ShortWindowTradeRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ActivationRuleName,EntryTimeUtc,ExitTimeUtc,NetPnlQuote,IsWinner,ExitReason,CostScenario,ActivationStartUtc,ActivationEndUtc,SparseLookbackActivation");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.ActivationRuleName), Dt(r.EntryTimeUtc), Dt(r.ExitTimeUtc), r.NetPnlQuote, r.IsWinner, Csv(r.ExitReason), Csv(r.CostScenario), Dt(r.ActivationStartUtc), Dt(r.ActivationEndUtc), r.SparseLookbackActivation));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "no-paid-short-window-trades.csv"), sb.ToString(), ct);
    }

    private async Task PeriodsCsv(IReadOnlyList<ShortWindowPeriodRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ActivationRuleName,CheckpointFrequencyHours,LookbackDays,ActivationPeriodHours,CheckpointUtc,ActivationStartUtc,ActivationEndUtc,LookbackTradeCount,LookbackNetPnl,LookbackProfitFactor,LookbackWinRate,PerfConditionPass,FlowDataAvailable,FlowConditionPass,SparseLookback,Activated,SkipReason,TradesInActivationWindow,NetInActivationWindow,OiChange60mPercent,TakerImbalance1h,GlobalLongShortZScore,FundingZScore,BtcReturn30mPercent,BtcReturn60mPercent,DistanceFromRecentHighPercent,CostScenario");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.ActivationRuleName), r.CheckpointFrequencyHours, r.LookbackDays, r.ActivationPeriodHours, Dt(r.CheckpointUtc), Dt(r.ActivationStartUtc), Dt(r.ActivationEndUtc), r.LookbackTradeCount, r.LookbackNetPnl, r.LookbackProfitFactor, r.LookbackWinRate, r.PerfConditionPass, r.FlowDataAvailable, r.FlowConditionPass, r.SparseLookback, r.Activated, Csv(r.SkipReason), r.TradesInActivationWindow, r.NetInActivationWindow, N(r.OiChange60mPercent), N(r.TakerImbalance1h), N(r.GlobalLongShortZScore), N(r.FundingZScore), N(r.BtcReturn30mPercent), N(r.BtcReturn60mPercent), N(r.DistanceFromRecentHighPercent), Csv(r.CostScenario)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "no-paid-short-window-periods.csv"), sb.ToString(), ct);
    }

    private async Task CostCsv(IReadOnlyList<ShortWindowCostSensitivityRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ActivationRuleName,CostScenario,TradeCount,NetPnlQuote,WinRate,ProfitFactor,NetPositive,SurvivesModerateSlippage002,SurvivesStress");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.ActivationRuleName), Csv(r.CostScenario), r.TradeCount, r.NetPnlQuote, r.WinRate, r.ProfitFactor, r.NetPositive, r.SurvivesModerateSlippage002, r.SurvivesStress));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "no-paid-short-window-cost-sensitivity.csv"), sb.ToString(), ct);
    }

    private static string Dt(DateTime? value) => value?.ToString("o", CultureInfo.InvariantCulture) ?? "";
    private static string N(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
