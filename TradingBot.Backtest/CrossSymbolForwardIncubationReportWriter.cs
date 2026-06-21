using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed class CrossSymbolForwardIncubationReportWriter(
    string outputDirectory,
    string frozenSummaryPrefix,
    string incubationPrefix,
    string historyPrefix)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task WriteAsync(CrossSymbolForwardIncubationRunResult result, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDirectory);
        await Json($"{frozenSummaryPrefix}.json", result.FrozenSummary, ct);
        await Json($"{incubationPrefix}-data-coverage.json", result.DataCoverage, ct);
        await Json($"{incubationPrefix}-trades.json", result.ForwardTrades.Select(NormalizedRiskPnlModule.ToReportRow).ToArray(), ct);
        await Json($"{incubationPrefix}-periods.json", result.ForwardPeriods, ct);
        await Json($"{incubationPrefix}-cost-sensitivity.json", result.CostSensitivity, ct);
        await Json($"{incubationPrefix}-health-gates.json", result.HealthGates, ct);
        await Json($"{incubationPrefix}-research-answers.json", result.Answers, ct);
        await Json($"{historyPrefix}.json", result.History, ct);
        await Json($"{incubationPrefix}-no-trade-summary.json", result.NoTradeReasonSummary, ct);

        await FrozenSummaryCsv(result.FrozenSummary, ct);
        await CoverageCsv(result.DataCoverage, ct);
        await TradesCsv(result.ForwardTrades, ct);
        await PeriodsCsv(result.ForwardPeriods, ct);
        await CostCsv(result.CostSensitivity, ct);
        await HealthGatesCsv(result.HealthGates, ct);
        await HistoryCsv(result.History, ct);
        await NoTradeSummaryCsv(result.NoTradeReasonSummary, ct);
        await NoTradeSummaryText(result.NoTradeReasonSummary, ct);
        await ForwardIncubationAcceleratedValidationReportWriter.WriteAsync(
            outputDirectory, result.AcceleratedValidation, ct);
    }

    private async Task Json<T>(string fileName, T payload, CancellationToken ct)
        => await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, fileName), JsonSerializer.Serialize(payload, JsonOptions), ct);

    private async Task FrozenSummaryCsv(FrozenCandidateSummaryRow r, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileName,CreatedAtUtc,FrozenStartUtc,BaseRule,Symbol,Interval,EntryMode,TargetPercent,StopPercent,MaxHoldMinutes,CooldownCandles,OverlapPolicy,ActivationFlowCondition,CheckpointFrequencyHours,ActivationPeriodHours,LookbackDaysInformational,DiscoveryWindow,DiscoveryBaselineTrades,DiscoveryBaselineNet,DiscoveryCandidateTrades,DiscoveryCandidateNet,DiscoveryCandidateProfitFactor,DiscoveryCandidateStressPlusNet,Caveats,RunAtUtc,ForwardWindowEndUtc,ForwardSpanDays,ForwardTrades,ForwardNetModerate,Verdict," + NormalizedRiskPnlModule.SummaryCsvHeaderSuffix());
        sb.AppendLine(string.Join(",", Csv(r.ProfileName), Dt(r.CreatedAtUtc), Dt(r.FrozenStartUtc), Csv(r.BaseRule), Csv(r.Symbol), Csv(r.Interval), Csv(r.EntryMode), r.TargetPercent, r.StopPercent, r.MaxHoldMinutes, r.CooldownCandles, Csv(r.OverlapPolicy), Csv(r.ActivationFlowCondition), r.CheckpointFrequencyHours, r.ActivationPeriodHours, r.LookbackDaysInformational, Csv(r.DiscoveryWindow), r.DiscoveryBaselineTrades, r.DiscoveryBaselineNet, r.DiscoveryCandidateTrades, r.DiscoveryCandidateNet, r.DiscoveryCandidateProfitFactor, r.DiscoveryCandidateStressPlusNet, Csv(r.Caveats), Dt(r.RunAtUtc), Dt(r.ForwardWindowEndUtc), r.ForwardSpanDays, r.ForwardTrades, r.ForwardNetModerate, Csv(r.Verdict), NormalizedRiskPnlModule.SummaryCsvValues(r.NormalizedRisk)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{frozenSummaryPrefix}.csv"), sb.ToString(), ct);
    }

    private async Task CoverageCsv(IReadOnlyList<ForwardDataCoverageRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,SourceKey,LocalFilePresent,LocalRecordCount,LocalStartUtc,LocalEndUtc,LocalSpanDays,DaysBeyondFrozenStart,Notes");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Csv(r.SourceKey), r.LocalFilePresent, r.LocalRecordCount, Dt(r.LocalStartUtc), Dt(r.LocalEndUtc), r.LocalSpanDays, r.DaysBeyondFrozenStart, Csv(r.Notes)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{incubationPrefix}-data-coverage.csv"), sb.ToString(), ct);
    }

    private async Task TradesCsv(IReadOnlyList<CrossSymbolTradeRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Interval,Direction,TargetPercent,StopPercent,ActivationRule,EntryTimeUtc,ExitTimeUtc,NetPnlQuote,IsWinner,ExitReason,CostScenario," + NormalizedRiskPnlModule.TradeCsvHeaderSuffix());
        foreach (var r in rows)
        {
            var report = NormalizedRiskPnlModule.ToReportRow(r);
            sb.AppendLine(string.Join(",",
                Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), r.TargetPercent, r.StopPercent, Csv(r.ActivationRule),
                Dt(r.EntryTimeUtc), Dt(r.ExitTimeUtc), r.NetPnlQuote, r.IsWinner, Csv(r.ExitReason), Csv(r.CostScenario),
                NormalizedRiskPnlModule.TradeCsvValues(report.NormalizedRisk)));
        }
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{incubationPrefix}-trades.csv"), sb.ToString(), ct);
    }

    private async Task PeriodsCsv(IReadOnlyList<CrossSymbolPeriodRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Interval,Direction,TargetPercent,StopPercent,ActivationRule,CheckpointUtc,ActivationStartUtc,ActivationEndUtc,LookbackTradeCount,LookbackNetPnl,LookbackProfitFactor,PerfPass,FlowDataAvailable,FlowPass,Activated,SkipReason,TradesInActivationWindow,NetInActivationWindow,CostScenario");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), r.TargetPercent, r.StopPercent, Csv(r.ActivationRule), Dt(r.CheckpointUtc), Dt(r.ActivationStartUtc), Dt(r.ActivationEndUtc), r.LookbackTradeCount, r.LookbackNetPnl, r.LookbackProfitFactor, r.PerfPass, r.FlowDataAvailable, r.FlowPass, r.Activated, Csv(r.SkipReason), r.TradesInActivationWindow, r.NetInActivationWindow, Csv(r.CostScenario)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{incubationPrefix}-periods.csv"), sb.ToString(), ct);
    }

    private async Task CostCsv(IReadOnlyList<CrossSymbolCostSensitivityRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Interval,Direction,TargetPercent,StopPercent,ActivationRule,CostScenario,TradeCount,NetPnlQuote,WinRate,ProfitFactor,NetPositive");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction), r.TargetPercent, r.StopPercent, Csv(r.ActivationRule), Csv(r.CostScenario), r.TradeCount, r.NetPnlQuote, r.WinRate, r.ProfitFactor, r.NetPositive));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{incubationPrefix}-cost-sensitivity.csv"), sb.ToString(), ct);
    }

    private async Task HealthGatesCsv(IReadOnlyList<ForwardHealthGateRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("GateName,Requirement,ObservedValue,Applicable,Pass,Notes");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.GateName), Csv(r.Requirement), Csv(r.ObservedValue), r.Applicable, r.Pass, Csv(r.Notes)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{incubationPrefix}-health-gates.csv"), sb.ToString(), ct);
    }

    private async Task HistoryCsv(IReadOnlyList<ForwardIncubationHistoryEntry> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RunAtUtc,FrozenStartUtc,ForwardWindowEndUtc,ForwardSpanDays,ForwardTrades,ForwardNetModerate,ForwardNetLatency002,ForwardNetStressPlus,MaxConsecutiveLosses,PositivePeriodRate,HealthGatesPassed,HealthGatesTotal,Verdict");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Dt(r.RunAtUtc), Dt(r.FrozenStartUtc), Dt(r.ForwardWindowEndUtc), r.ForwardSpanDays, r.ForwardTrades, r.ForwardNetModerate, r.ForwardNetLatency002, r.ForwardNetStressPlus, r.MaxConsecutiveLosses, r.PositivePeriodRate, r.HealthGatesPassed, r.HealthGatesTotal, Csv(r.Verdict)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{historyPrefix}.csv"), sb.ToString(), ct);
    }

    private async Task NoTradeSummaryCsv(ForwardIncubationNoTradeReasonSummary s, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CompactSummaryLine,ReportStatus,LatestRunStatus,PreviousRunForwardWindowEndUtc,CurrentRunForwardWindowEndUtc,DataAdvancedSincePreviousRun,NewTradesSincePreviousRun,NewNetModerateSincePreviousRun,NewNetStressPlusSincePreviousRun,ForwardWindowStartUtc,ForwardWindowEndUtc,ForwardSpanDays,LatestDataUtc,LatestCandleUtc,FrozenProfileName,FrozenHashStatus,BnbFrozenHashStatus,SolFrozenHashStatus,FrozenFilesTouched,ActivationCheckpointCount,ActivatedCheckpointCount,ActivationFailedCheckpointCount,ActivatedButNoEntryCount,ForwardTrades,NetModerate,NetModerateLatency002,NetStressPlus,HealthGatesPassed,HealthGatesTotal,FailedGates,Verdict,NextAction,TopActivationSkipReasons,TopEntrySkipReasons," + NormalizedRiskPnlModule.SummaryCsvHeaderSuffix());
        sb.AppendLine(string.Join(",",
            Csv(s.CompactSummaryLine), Csv(s.ReportStatus), Csv(s.LatestRunStatus), Dt(s.PreviousRunForwardWindowEndUtc), Dt(s.CurrentRunForwardWindowEndUtc), s.DataAdvancedSincePreviousRun,
            s.NewTradesSincePreviousRun, s.NewNetModerateSincePreviousRun, s.NewNetStressPlusSincePreviousRun,
            Dt(s.ForwardWindowStartUtc), Dt(s.ForwardWindowEndUtc), s.ForwardSpanDays,
            Dt(s.LatestDataUtc), Dt(s.LatestCandleUtc), Csv(s.FrozenProfileName), Csv(s.FrozenHashStatus), Csv(s.BnbFrozenHashStatus), Csv(s.SolFrozenHashStatus), Csv(s.FrozenFilesTouched),
            s.ActivationCheckpointCount, s.ActivatedCheckpointCount, s.ActivationFailedCheckpointCount, s.ActivatedButNoEntryCount,
            s.ForwardTrades, s.NetModerate, s.NetModerateLatency002, s.NetStressPlus, s.HealthGatesPassed, s.HealthGatesTotal,
            Csv(s.FailedGates), Csv(s.Verdict), Csv(s.NextAction),
            Csv(FormatSkipReasons(s.TopActivationSkipReasons)), Csv(FormatSkipReasons(s.TopEntrySkipReasons)),
            NormalizedRiskPnlModule.SummaryCsvValues(s.NormalizedRisk)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{incubationPrefix}-no-trade-summary.csv"), sb.ToString(), ct);
    }

    private async Task NoTradeSummaryText(ForwardIncubationNoTradeReasonSummary s, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine(s.CompactSummaryLine);
        sb.AppendLine();
        NormalizedRiskPnlModule.AppendSummaryRiskLines(sb, s.NormalizedRisk);
        sb.AppendLine($"LatestRunStatus: {s.LatestRunStatus}");
        sb.AppendLine($"ReportStatus: {s.ReportStatus}");
        sb.AppendLine($"PreviousRunForwardWindowEndUtc: {Dt(s.PreviousRunForwardWindowEndUtc)}");
        sb.AppendLine($"CurrentRunForwardWindowEndUtc: {s.CurrentRunForwardWindowEndUtc:o}");
        sb.AppendLine($"DataAdvancedSincePreviousRun: {s.DataAdvancedSincePreviousRun}");
        sb.AppendLine($"NewTradesSincePreviousRun: {s.NewTradesSincePreviousRun}");
        sb.AppendLine($"NewNetModerateSincePreviousRun: {s.NewNetModerateSincePreviousRun}");
        sb.AppendLine($"NewNetStressPlusSincePreviousRun: {s.NewNetStressPlusSincePreviousRun}");
        sb.AppendLine($"ForwardWindowStartUtc: {s.ForwardWindowStartUtc:o}");
        sb.AppendLine($"ForwardWindowEndUtc: {s.ForwardWindowEndUtc:o}");
        sb.AppendLine($"ForwardSpanDays: {s.ForwardSpanDays}");
        sb.AppendLine($"LatestDataUtc: {s.LatestDataUtc:o}");
        sb.AppendLine($"LatestCandleUtc: {s.LatestCandleUtc:o}");
        sb.AppendLine($"FrozenProfileName: {s.FrozenProfileName}");
        sb.AppendLine($"FrozenHashStatus: {s.FrozenHashStatus}");
        sb.AppendLine($"BnbFrozenHashStatus: {s.BnbFrozenHashStatus}");
        sb.AppendLine($"SolFrozenHashStatus: {s.SolFrozenHashStatus}");
        sb.AppendLine($"FrozenFilesTouched: {s.FrozenFilesTouched}");
        sb.AppendLine($"ActivationCheckpointCount: {s.ActivationCheckpointCount}");
        sb.AppendLine($"ActivatedCheckpointCount: {s.ActivatedCheckpointCount}");
        sb.AppendLine($"ActivationFailedCheckpointCount: {s.ActivationFailedCheckpointCount}");
        sb.AppendLine($"ActivatedButNoEntryCount: {s.ActivatedButNoEntryCount}");
        sb.AppendLine($"ForwardTrades: {s.ForwardTrades}");
        sb.AppendLine($"NetModerate: {s.NetModerate}");
        sb.AppendLine($"NetModerateLatency002: {s.NetModerateLatency002}");
        sb.AppendLine($"NetStressPlus: {s.NetStressPlus}");
        sb.AppendLine($"HealthGatesPassed: {s.HealthGatesPassed}/{s.HealthGatesTotal}");
        sb.AppendLine($"FailedGates: {s.FailedGates}");
        sb.AppendLine($"Verdict: {s.Verdict}");
        sb.AppendLine($"NextAction: {s.NextAction}");
        sb.AppendLine($"TopActivationSkipReasons: {FormatSkipReasons(s.TopActivationSkipReasons)}");
        sb.AppendLine($"TopEntrySkipReasons: {FormatSkipReasons(s.TopEntrySkipReasons)}");
        sb.AppendLine("Backtest/research only. liveFuturesRecommended=false.");
        if (s.TradeDiagnostics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Forward trades:");
            foreach (var t in s.TradeDiagnostics)
                sb.AppendLine($"  #{t.TradeIndex} entry={t.EntryTimeUtc:o} exit={t.ExitTimeUtc:o} net={t.NetPnlQuote}");
        }

        if (s.PeriodDiagnostics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Period diagnostics:");
            foreach (var p in s.PeriodDiagnostics)
                sb.AppendLine($"  checkpoint={p.CheckpointUtc:o} {p.Classification}: {p.Reason} (trades={p.TradesInWindow}, net={p.NetInWindow})");
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{incubationPrefix}-no-trade-summary.txt"), sb.ToString(), ct);
    }

    private static string FormatSkipReasons(IReadOnlyList<SkipReasonCountRow> rows)
        => rows.Count == 0
            ? string.Empty
            : string.Join("; ", rows.Select(r => $"{r.Reason}={r.Count}"));

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
