using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

/// <summary>
/// Writes the fixed-frequency forward-incubation outputs with the required file layout:
/// frozen-profile.json, forward-incubation-summary.{json,csv,txt}, forward-incubation-history.{json,csv},
/// forward-incubation-trades.{json,csv}, forward-incubation-health-gates.{json,csv},
/// forward-incubation-no-trade-diagnostics.{json,csv,txt}. Diagnostic/research only.
/// </summary>
public sealed class FixedFrequencyForwardIncubationV1ReportWriter(string outputDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task WriteAsync(FixedFrequencyForwardIncubationV1RunResult result, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDirectory);

        await Json("frozen-profile.json", result.FrozenProfile, ct);
        await Json("forward-incubation-summary.json", result.Summary, ct);
        await Json("forward-incubation-history.json", result.History, ct);
        await Json("forward-incubation-trades.json", result.ForwardTrades.Select(NormalizedRiskPnlModule.ToReportRow).ToArray(), ct);
        await Json("forward-incubation-health-gates.json", result.HealthGates, ct);
        await Json("forward-incubation-no-trade-diagnostics.json", result.NoTradeDiagnostics, ct);

        await SummaryCsv(result.Summary, ct);
        await SummaryText(result.Summary, ct);
        await HistoryCsv(result.History, ct);
        await TradesCsv(result.ForwardTrades, ct);
        await HealthGatesCsv(result.HealthGates, ct);
        await NoTradeDiagnosticsCsv(result.NoTradeDiagnostics, ct);
        await NoTradeDiagnosticsText(result.NoTradeDiagnostics, ct);
    }

    private async Task Json<T>(string fileName, T payload, CancellationToken ct)
        => await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, fileName), JsonSerializer.Serialize(payload, JsonOptions), ct);

    private async Task SummaryCsv(FixedFrequencyForwardIncubationSummary s, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileName,Symbol,Interval,Direction,TargetPercent,StopPercent,HoldHours,ActivationRule,FrozenStartUtc,ForwardWindowStartUtc,ForwardWindowEndUtc,ForwardSpanDays,ForwardTrades,NewTradesSincePreviousRun,NetModerate,NetStressPlus,WinRate,ProfitFactor,MaxDrawdown,MaxConsecutiveLosses,ActivationCheckpointCount,ActivatedCheckpointCount,ActivationFailedCheckpointCount,ActivatedButNoEntryCount,BaseSignalsInsideForwardWindow,BaseSignalsInsideActivatedWindows,CurrentExactEntryPresent,LatestStatus,Verdict,NextAction,HealthScore,FailedHealthGates,TestnetOrderCandidate,RealOrdersPlaced,TestnetOrdersEnabled,LiveTradingEnabled," + NormalizedRiskPnlModule.SummaryCsvHeaderSuffix());
        sb.AppendLine(string.Join(",",
            Csv(s.ProfileName), Csv(s.Symbol), Csv(s.Interval), Csv(s.Direction),
            s.TargetPercent, s.StopPercent, s.HoldHours, Csv(s.ActivationRule),
            Dt(s.FrozenStartUtc), Dt(s.ForwardWindowStartUtc), Dt(s.ForwardWindowEndUtc), s.ForwardSpanDays,
            s.ForwardTrades, s.NewTradesSincePreviousRun, s.NetModerate, s.NetStressPlus,
            s.WinRate, s.ProfitFactor, s.MaxDrawdown, s.MaxConsecutiveLosses,
            s.ActivationCheckpointCount, s.ActivatedCheckpointCount, s.ActivationFailedCheckpointCount, s.ActivatedButNoEntryCount,
            s.BaseSignalsInsideForwardWindow, s.BaseSignalsInsideActivatedWindows,
            s.CurrentExactEntryPresent, Csv(s.LatestStatus), Csv(s.Verdict), Csv(s.NextAction),
            s.HealthScore, Csv(s.FailedHealthGates), s.TestnetOrderCandidate, s.RealOrdersPlaced,
            s.TestnetOrdersEnabled, s.LiveTradingEnabled,
            NormalizedRiskPnlModule.SummaryCsvValues(s.NormalizedRisk)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "forward-incubation-summary.csv"), sb.ToString(), ct);
    }

    private async Task SummaryText(FixedFrequencyForwardIncubationSummary s, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine(s.CompactSummaryLine);
        sb.AppendLine();
        sb.AppendLine("Fixed-Frequency Forward Incubation V1 — diagnostic/research only. No orders placed; testnet/live disabled.");
        sb.AppendLine();
        sb.AppendLine($"ProfileName: {s.ProfileName}");
        sb.AppendLine($"Candidate: {s.Symbol} {s.Interval} {s.Direction} T{s.TargetPercent:0.00}/S{s.StopPercent:0.00} Hold{s.HoldHours}h {s.ActivationRule}");
        sb.AppendLine($"FrozenStartUtc: {s.FrozenStartUtc:o}");
        sb.AppendLine($"ForwardWindow: {s.ForwardWindowStartUtc:o} -> {s.ForwardWindowEndUtc:o} ({s.ForwardSpanDays} day(s))");
        sb.AppendLine($"ForwardTrades: {s.ForwardTrades} (new since previous run: {s.NewTradesSincePreviousRun})");
        sb.AppendLine($"NetModerate: {s.NetModerate} | NetStressPlus: {s.NetStressPlus}");
        sb.AppendLine($"WinRate: {s.WinRate:P2} | ProfitFactor: {s.ProfitFactor} | MaxDrawdown: {s.MaxDrawdown} | MaxConsecutiveLosses: {s.MaxConsecutiveLosses}");
        sb.AppendLine($"Checkpoints: total={s.ActivationCheckpointCount}, activated={s.ActivatedCheckpointCount}, activationFailed={s.ActivationFailedCheckpointCount}, activatedButNoEntry={s.ActivatedButNoEntryCount}");
        sb.AppendLine($"BaseSignals: insideForwardWindow={s.BaseSignalsInsideForwardWindow}, insideActivatedWindows={s.BaseSignalsInsideActivatedWindows}");
        sb.AppendLine($"CurrentExactEntryPresent: {s.CurrentExactEntryPresent}");
        sb.AppendLine($"LatestStatus: {s.LatestStatus}");
        sb.AppendLine($"HealthScore: {s.HealthScore}/100");
        sb.AppendLine($"FailedHealthGates: {s.FailedHealthGates}");
        sb.AppendLine($"Verdict: {s.Verdict}");
        sb.AppendLine($"NextAction: {s.NextAction}");
        sb.AppendLine($"TestnetOrderCandidate: {s.TestnetOrderCandidate} | RealOrdersPlaced: {s.RealOrdersPlaced} | TestnetOrdersEnabled: {s.TestnetOrdersEnabled} | LiveTradingEnabled: {s.LiveTradingEnabled}");
        sb.AppendLine();
        NormalizedRiskPnlModule.AppendSummaryRiskLines(sb, s.NormalizedRisk);
        sb.AppendLine(s.DiscoveryEvidenceNote);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "forward-incubation-summary.txt"), sb.ToString(), ct);
    }

    private async Task HistoryCsv(IReadOnlyList<ForwardIncubationHistoryEntry> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RunAtUtc,FrozenStartUtc,ForwardWindowEndUtc,ForwardSpanDays,ForwardTrades,ForwardNetModerate,ForwardNetLatency002,ForwardNetStressPlus,MaxConsecutiveLosses,PositivePeriodRate,HealthGatesPassed,HealthGatesTotal,Verdict");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Dt(r.RunAtUtc), Dt(r.FrozenStartUtc), Dt(r.ForwardWindowEndUtc), r.ForwardSpanDays, r.ForwardTrades, r.ForwardNetModerate, r.ForwardNetLatency002, r.ForwardNetStressPlus, r.MaxConsecutiveLosses, r.PositivePeriodRate, r.HealthGatesPassed, r.HealthGatesTotal, Csv(r.Verdict)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "forward-incubation-history.csv"), sb.ToString(), ct);
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
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "forward-incubation-trades.csv"), sb.ToString(), ct);
    }

    private async Task HealthGatesCsv(IReadOnlyList<ForwardHealthGateRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("GateName,Requirement,ObservedValue,Applicable,Pass,Notes");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Csv(r.GateName), Csv(r.Requirement), Csv(r.ObservedValue), r.Applicable, r.Pass, Csv(r.Notes)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "forward-incubation-health-gates.csv"), sb.ToString(), ct);
    }

    private async Task NoTradeDiagnosticsCsv(ForwardIncubationNoTradeReasonSummary s, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("LatestRunStatus,DataAdvancedSincePreviousRun,NewTradesSincePreviousRun,ForwardWindowStartUtc,ForwardWindowEndUtc,ForwardSpanDays,ActivationCheckpointCount,ActivatedCheckpointCount,ActivationFailedCheckpointCount,ActivatedButNoEntryCount,ForwardTrades,NetModerate,NetStressPlus,FailedGates,Verdict,NextAction,TopActivationSkipReasons,TopEntrySkipReasons," + NormalizedRiskPnlModule.SummaryCsvHeaderSuffix());
        sb.AppendLine(string.Join(",",
            Csv(s.LatestRunStatus), s.DataAdvancedSincePreviousRun, s.NewTradesSincePreviousRun,
            Dt(s.ForwardWindowStartUtc), Dt(s.ForwardWindowEndUtc), s.ForwardSpanDays,
            s.ActivationCheckpointCount, s.ActivatedCheckpointCount, s.ActivationFailedCheckpointCount, s.ActivatedButNoEntryCount,
            s.ForwardTrades, s.NetModerate, s.NetStressPlus,
            Csv(s.FailedGates), Csv(s.Verdict), Csv(s.NextAction),
            Csv(FormatSkipReasons(s.TopActivationSkipReasons)), Csv(FormatSkipReasons(s.TopEntrySkipReasons)),
            NormalizedRiskPnlModule.SummaryCsvValues(s.NormalizedRisk)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "forward-incubation-no-trade-diagnostics.csv"), sb.ToString(), ct);
    }

    private async Task NoTradeDiagnosticsText(ForwardIncubationNoTradeReasonSummary s, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine(s.CompactSummaryLine);
        sb.AppendLine();
        sb.AppendLine("Fixed-Frequency Forward Incubation V1 no-trade diagnostics — diagnostic/research only. No orders.");
        sb.AppendLine();
        sb.AppendLine($"FrozenProfileName: {s.FrozenProfileName}");
        sb.AppendLine($"LatestRunStatus: {s.LatestRunStatus}");
        sb.AppendLine($"DataAdvancedSincePreviousRun: {s.DataAdvancedSincePreviousRun}");
        sb.AppendLine($"NewTradesSincePreviousRun: {s.NewTradesSincePreviousRun}");
        sb.AppendLine($"ForwardWindow: {s.ForwardWindowStartUtc:o} -> {s.ForwardWindowEndUtc:o} ({s.ForwardSpanDays} day(s))");
        sb.AppendLine($"Checkpoints: total={s.ActivationCheckpointCount}, activated={s.ActivatedCheckpointCount}, activationFailed={s.ActivationFailedCheckpointCount}, activatedButNoEntry={s.ActivatedButNoEntryCount}");
        sb.AppendLine($"ForwardTrades: {s.ForwardTrades} | NetModerate: {s.NetModerate} | NetStressPlus: {s.NetStressPlus}");
        sb.AppendLine($"FailedGates: {s.FailedGates}");
        sb.AppendLine($"Verdict: {s.Verdict}");
        sb.AppendLine($"NextAction: {s.NextAction}");
        sb.AppendLine();
        NormalizedRiskPnlModule.AppendSummaryRiskLines(sb, s.NormalizedRisk);
        sb.AppendLine($"TopActivationSkipReasons: {FormatSkipReasons(s.TopActivationSkipReasons)}");
        sb.AppendLine($"TopEntrySkipReasons: {FormatSkipReasons(s.TopEntrySkipReasons)}");

        if (s.PeriodDiagnostics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Period diagnostics:");
            foreach (var p in s.PeriodDiagnostics)
                sb.AppendLine($"  checkpoint={p.CheckpointUtc:o} {p.Classification}: {p.Reason} (trades={p.TradesInWindow}, net={p.NetInWindow})");
        }

        if (s.TradeDiagnostics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Forward trades:");
            foreach (var t in s.TradeDiagnostics)
                sb.AppendLine($"  #{t.TradeIndex} entry={t.EntryTimeUtc:o} exit={t.ExitTimeUtc:o} net={t.NetPnlQuote}");
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "forward-incubation-no-trade-diagnostics.txt"), sb.ToString(), ct);
    }

    private static string FormatSkipReasons(IReadOnlyList<SkipReasonCountRow> rows)
        => rows.Count == 0 ? string.Empty : string.Join("; ", rows.Select(r => $"{r.Reason}={r.Count}"));

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
