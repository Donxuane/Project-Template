using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed class NoPaidDataShortWindowForwardIncubationV1ReportWriter(string outputDirectory)
{
    public async Task WriteAsync(NoPaidDataShortWindowForwardIncubationV1RunResult result, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        await Json("frozen-candidate-summary.json", result.FrozenSummary, cancellationToken);
        await Json("forward-incubation-data-coverage.json", result.DataCoverage, cancellationToken);
        await Json("forward-incubation-trades.json", result.ForwardTrades.Select(t => NormalizedRiskPnlModule.ToReportRow(t)).ToArray(), cancellationToken);
        await Json("forward-incubation-periods.json", result.ForwardPeriods, cancellationToken);
        await Json("forward-incubation-cost-sensitivity.json", result.CostSensitivity, cancellationToken);
        await Json("forward-incubation-health-gates.json", result.HealthGates, cancellationToken);
        await Json("forward-incubation-history.json", result.History, cancellationToken);
        await Json("forward-incubation-research-answers.json", result.Answers, cancellationToken);
        await Json("forward-incubation-no-trade-summary.json", result.NoTradeReasonSummary, cancellationToken);

        await FrozenSummaryCsv(result.FrozenSummary, cancellationToken);
        await CoverageCsv(result.DataCoverage, cancellationToken);
        await TradesCsv(result.ForwardTrades, cancellationToken);
        await PeriodsCsv(result.ForwardPeriods, cancellationToken);
        await CostCsv(result.CostSensitivity, cancellationToken);
        await HealthGatesCsv(result.HealthGates, cancellationToken);
        await NoTradeSummaryCsv(result.NoTradeReasonSummary, cancellationToken);
        await NoTradeSummaryText(result.NoTradeReasonSummary, cancellationToken);
    }

    private async Task Json<T>(string fileName, T payload, CancellationToken ct)
        => await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), ct);

    private async Task FrozenSummaryCsv(FrozenCandidateSummaryRow r, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileName,CreatedAtUtc,FrozenStartUtc,BaseRule,Symbol,Interval,EntryMode,TargetPercent,StopPercent,MaxHoldMinutes,CooldownCandles,OverlapPolicy,ActivationFlowCondition,CheckpointFrequencyHours,ActivationPeriodHours,LookbackDaysInformational,DiscoveryWindow,DiscoveryBaselineTrades,DiscoveryBaselineNet,DiscoveryCandidateTrades,DiscoveryCandidateNet,DiscoveryCandidateProfitFactor,DiscoveryCandidateStressPlusNet,Caveats,RunAtUtc,ForwardWindowEndUtc,ForwardSpanDays,ForwardTrades,ForwardNetModerate,Verdict," + NormalizedRiskPnlModule.SummaryCsvHeaderSuffix());
        sb.AppendLine(string.Join(",", Csv(r.ProfileName), Dt(r.CreatedAtUtc), Dt(r.FrozenStartUtc), Csv(r.BaseRule), Csv(r.Symbol), Csv(r.Interval), Csv(r.EntryMode), r.TargetPercent, r.StopPercent, r.MaxHoldMinutes, r.CooldownCandles, Csv(r.OverlapPolicy), Csv(r.ActivationFlowCondition), r.CheckpointFrequencyHours, r.ActivationPeriodHours, r.LookbackDaysInformational, Csv(r.DiscoveryWindow), r.DiscoveryBaselineTrades, r.DiscoveryBaselineNet, r.DiscoveryCandidateTrades, r.DiscoveryCandidateNet, r.DiscoveryCandidateProfitFactor, r.DiscoveryCandidateStressPlusNet, Csv(r.Caveats), Dt(r.RunAtUtc), Dt(r.ForwardWindowEndUtc), r.ForwardSpanDays, r.ForwardTrades, r.ForwardNetModerate, Csv(r.Verdict), NormalizedRiskPnlModule.SummaryCsvValues(r.NormalizedRisk)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "frozen-candidate-summary.csv"), sb.ToString(), ct);
    }

    private async Task CoverageCsv(IReadOnlyList<ForwardDataCoverageRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,SourceKey,LocalFilePresent,LocalRecordCount,LocalStartUtc,LocalEndUtc,LocalSpanDays,DaysBeyondFrozenStart,Notes");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.Symbol), Csv(r.SourceKey), r.LocalFilePresent, r.LocalRecordCount, Dt(r.LocalStartUtc), Dt(r.LocalEndUtc), r.LocalSpanDays, r.DaysBeyondFrozenStart, Csv(r.Notes)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "forward-incubation-data-coverage.csv"), sb.ToString(), ct);
    }

    private async Task TradesCsv(IReadOnlyList<ShortWindowTradeRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ActivationRuleName,EntryTimeUtc,ExitTimeUtc,NetPnlQuote,IsWinner,ExitReason,CostScenario,ActivationStartUtc,ActivationEndUtc,SparseLookbackActivation," + NormalizedRiskPnlModule.TradeCsvHeaderSuffix());
        foreach (var r in rows)
        {
            var report = NormalizedRiskPnlModule.ToReportRow(r);
            sb.AppendLine(string.Join(",",
                Csv(r.ActivationRuleName), Dt(r.EntryTimeUtc), Dt(r.ExitTimeUtc), r.NetPnlQuote, r.IsWinner, Csv(r.ExitReason), Csv(r.CostScenario), Dt(r.ActivationStartUtc), Dt(r.ActivationEndUtc), r.SparseLookbackActivation,
                NormalizedRiskPnlModule.TradeCsvValues(report.NormalizedRisk)));
        }
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "forward-incubation-trades.csv"), sb.ToString(), ct);
    }

    private async Task PeriodsCsv(IReadOnlyList<ShortWindowPeriodRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ActivationRuleName,CheckpointFrequencyHours,LookbackDays,ActivationPeriodHours,CheckpointUtc,ActivationStartUtc,ActivationEndUtc,LookbackTradeCount,LookbackNetPnl,PerfConditionPass,FlowDataAvailable,FlowConditionPass,SparseLookback,Activated,SkipReason,TradesInActivationWindow,NetInActivationWindow,FundingZScore,BtcReturn30mPercent,BtcReturn60mPercent,CostScenario");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.ActivationRuleName), r.CheckpointFrequencyHours, r.LookbackDays, r.ActivationPeriodHours, Dt(r.CheckpointUtc), Dt(r.ActivationStartUtc), Dt(r.ActivationEndUtc), r.LookbackTradeCount, r.LookbackNetPnl, r.PerfConditionPass, r.FlowDataAvailable, r.FlowConditionPass, r.SparseLookback, r.Activated, Csv(r.SkipReason), r.TradesInActivationWindow, r.NetInActivationWindow, N(r.FundingZScore), N(r.BtcReturn30mPercent), N(r.BtcReturn60mPercent), Csv(r.CostScenario)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "forward-incubation-periods.csv"), sb.ToString(), ct);
    }

    private async Task CostCsv(IReadOnlyList<ShortWindowCostSensitivityRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ActivationRuleName,CostScenario,TradeCount,NetPnlQuote,WinRate,ProfitFactor,NetPositive,SurvivesModerateSlippage002,SurvivesStress");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.ActivationRuleName), Csv(r.CostScenario), r.TradeCount, r.NetPnlQuote, r.WinRate, r.ProfitFactor, r.NetPositive, r.SurvivesModerateSlippage002, r.SurvivesStress));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "forward-incubation-cost-sensitivity.csv"), sb.ToString(), ct);
    }

    private async Task HealthGatesCsv(IReadOnlyList<ForwardHealthGateRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("GateName,Requirement,ObservedValue,Applicable,Pass,Notes");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.GateName), Csv(r.Requirement), Csv(r.ObservedValue), r.Applicable, r.Pass, Csv(r.Notes)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "forward-incubation-health-gates.csv"), sb.ToString(), ct);
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
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "forward-incubation-no-trade-summary.csv"), sb.ToString(), ct);
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
        sb.AppendLine($"TopActivationSkipReasons: {FormatSkipReasons(s.TopActivationSkipReasons)}");
        sb.AppendLine($"TopEntrySkipReasons: {FormatSkipReasons(s.TopEntrySkipReasons)}");
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

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "forward-incubation-no-trade-summary.txt"), sb.ToString(), ct);
    }

    private static string FormatSkipReasons(IReadOnlyList<SkipReasonCountRow> rows)
        => rows.Count == 0
            ? string.Empty
            : string.Join("; ", rows.Select(r => $"{r.Reason}={r.Count}"));

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
