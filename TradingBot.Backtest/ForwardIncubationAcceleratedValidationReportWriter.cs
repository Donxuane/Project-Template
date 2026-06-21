using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public static class ForwardIncubationAcceleratedValidationReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task WriteAsync(string outputDirectory, ForwardIncubationAcceleratedValidationSummary summary, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDirectory);
        var warning = ForwardIncubationAcceleratedValidationSummary.DiagnosticWarning;

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "historical-stress-replay.json"),
            JsonSerializer.Serialize(new
            {
                warning,
                summary.CompactSummaryLine,
                summary.TrueForwardNet,
                summary.PreFreezeReplayNet3d,
                summary.PreFreezeReplayNet7d,
                summary.PreFreezeReplayNet14d,
                replays = summary.HistoricalStressReplay
            }, JsonOptions),
            ct);

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "missed-opportunity-audit.json"),
            JsonSerializer.Serialize(new
            {
                warning,
                summary.CompactSummaryLine,
                summary.MissedWinnersCount,
                summary.BlockedLosersCount,
                summary.MainFinding,
                opportunities = summary.MissedOpportunityAudit
            }, JsonOptions),
            ct);

        await WriteHistoricalStressReplayCsv(outputDirectory, summary, warning, ct);
        await WriteMissedOpportunityAuditCsv(outputDirectory, summary, warning, ct);
        await WriteHistoricalStressReplayText(outputDirectory, summary, warning, ct);
        await WriteMissedOpportunityAuditText(outputDirectory, summary, warning, ct);
    }

    private static async Task WriteHistoricalStressReplayCsv(
        string outputDirectory,
        ForwardIncubationAcceleratedValidationSummary summary,
        string warning,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Warning,{Csv(warning)}");
        sb.AppendLine($"CompactSummaryLine,{Csv(summary.CompactSummaryLine)}");
        sb.AppendLine("Label,ReplayDaysBeforeFreeze,ReplayStartUtc,ReplayEndUtc,Trades,NetModerate,NetModerateLatency002,NetStressPlus,WinRate,ProfitFactor,MaxDrawdown,MaxConsecutiveLosses,ActivationCheckpointCount,ActivatedCheckpointCount,ActivationFailedCheckpointCount,ActivatedButNoEntryCount,TopActivationSkipReasons,TopEntrySkipReasons");
        foreach (var r in summary.HistoricalStressReplay)
        {
            sb.AppendLine(string.Join(",",
                Csv(r.Label), r.ReplayDaysBeforeFreeze, Dt(r.ReplayStartUtc), Dt(r.ReplayEndUtc),
                r.Trades, r.NetModerate, r.NetModerateLatency002, r.NetStressPlus,
                r.WinRate, r.ProfitFactor, r.MaxDrawdown, r.MaxConsecutiveLosses,
                r.ActivationCheckpointCount, r.ActivatedCheckpointCount, r.ActivationFailedCheckpointCount, r.ActivatedButNoEntryCount,
                Csv(FormatSkipReasons(r.TopActivationSkipReasons)), Csv(FormatSkipReasons(r.TopEntrySkipReasons))));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "historical-stress-replay.csv"), sb.ToString(), ct);
    }

    private static async Task WriteMissedOpportunityAuditCsv(
        string outputDirectory,
        ForwardIncubationAcceleratedValidationSummary summary,
        string warning,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Warning,{Csv(warning)}");
        sb.AppendLine($"CompactSummaryLine,{Csv(summary.CompactSummaryLine)}");
        sb.AppendLine($"MissedWinnersCount,{summary.MissedWinnersCount}");
        sb.AppendLine($"BlockedLosersCount,{summary.BlockedLosersCount}");
        sb.AppendLine($"MainFinding,{Csv(summary.MainFinding)}");
        sb.AppendLine("Symbol,Interval,PeriodLabel,WindowStartUtc,WindowEndUtc,MaxFavorableShortMovePercent,WouldHitTarget,WouldHitStop,EstimatedNetModerate,HypotheticalEntryPrice,HypotheticalExitPrice,HypotheticalExitReason,HypotheticalNetModerate,HypotheticalNetLatency002,HypotheticalNetStressPlus,WasBaseSignalPresent,BaseSignalCount,WasActivationPassed,IsHindsightOnly,ActivationState,EntrySignalState,Classification,Reason");
        foreach (var r in summary.MissedOpportunityAudit)
        {
            sb.AppendLine(string.Join(",",
                Csv(r.Symbol), Csv(r.Interval), Csv(r.PeriodLabel),
                Dt(r.WindowStartUtc), Dt(r.WindowEndUtc),
                r.MaxFavorableShortMovePercent, r.WouldHitTarget, r.WouldHitStop, r.EstimatedNetModerate,
                r.HypotheticalEntryPrice, r.HypotheticalExitPrice, Csv(r.HypotheticalExitReason),
                r.HypotheticalNetModerate, r.HypotheticalNetLatency002, r.HypotheticalNetStressPlus,
                r.WasBaseSignalPresent, r.BaseSignalCount, r.WasActivationPassed, r.IsHindsightOnly,
                Csv(r.ActivationState), Csv(r.EntrySignalState), Csv(r.Classification), Csv(r.Reason)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "missed-opportunity-audit.csv"), sb.ToString(), ct);
    }

    private static async Task WriteHistoricalStressReplayText(
        string outputDirectory,
        ForwardIncubationAcceleratedValidationSummary summary,
        string warning,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine(warning);
        sb.AppendLine();
        sb.AppendLine(summary.CompactSummaryLine);
        sb.AppendLine($"TrueForwardNet: {summary.TrueForwardNet}");
        sb.AppendLine($"PreFreezeReplayNet3d: {summary.PreFreezeReplayNet3d}");
        sb.AppendLine($"PreFreezeReplayNet7d: {summary.PreFreezeReplayNet7d}");
        sb.AppendLine($"PreFreezeReplayNet14d: {summary.PreFreezeReplayNet14d}");
        sb.AppendLine();
        foreach (var r in summary.HistoricalStressReplay)
        {
            sb.AppendLine($"[{r.Label} {r.ReplayDaysBeforeFreeze}d] {r.ReplayStartUtc:o} -> {r.ReplayEndUtc:o}");
            sb.AppendLine($"  Trades={r.Trades} NetModerate={r.NetModerate} NetLatency002={r.NetModerateLatency002} NetStressPlus={r.NetStressPlus}");
            sb.AppendLine($"  WinRate={r.WinRate:P1} PF={r.ProfitFactor} MaxDD={r.MaxDrawdown} MaxConsecLosses={r.MaxConsecutiveLosses}");
            sb.AppendLine($"  Checkpoints={r.ActivationCheckpointCount} Activated={r.ActivatedCheckpointCount} Failed={r.ActivationFailedCheckpointCount} ActivatedButNoEntry={r.ActivatedButNoEntryCount}");
            sb.AppendLine($"  TopActivationSkipReasons: {FormatSkipReasons(r.TopActivationSkipReasons)}");
            sb.AppendLine($"  TopEntrySkipReasons: {FormatSkipReasons(r.TopEntrySkipReasons)}");
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "historical-stress-replay.txt"), sb.ToString(), ct);
    }

    private static async Task WriteMissedOpportunityAuditText(
        string outputDirectory,
        ForwardIncubationAcceleratedValidationSummary summary,
        string warning,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine(warning);
        sb.AppendLine();
        sb.AppendLine(summary.CompactSummaryLine);
        sb.AppendLine($"MissedWinnersCount: {summary.MissedWinnersCount}");
        sb.AppendLine($"BlockedLosersCount: {summary.BlockedLosersCount}");
        sb.AppendLine($"MainFinding: {summary.MainFinding}");
        sb.AppendLine();
        foreach (var r in summary.MissedOpportunityAudit)
        {
            sb.AppendLine($"{r.PeriodLabel} {r.WindowStartUtc:yyyy-MM-dd} {r.Classification}: move={r.MaxFavorableShortMovePercent:F2}% hypoNet={r.HypotheticalNetModerate:F2} activation={r.ActivationState} signal={r.EntrySignalState} hindsight={r.IsHindsightOnly}");
            sb.AppendLine($"  Entry={r.HypotheticalEntryPrice} Exit={r.HypotheticalExitPrice} ExitReason={r.HypotheticalExitReason} NetModerate={r.HypotheticalNetModerate} NetLatency002={r.HypotheticalNetLatency002} NetStressPlus={r.HypotheticalNetStressPlus}");
            sb.AppendLine($"  WasBaseSignal={r.WasBaseSignalPresent} BaseSignalCount={r.BaseSignalCount} WasActivationPassed={r.WasActivationPassed}");
            sb.AppendLine($"  Reason: {r.Reason}");
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "missed-opportunity-audit.txt"), sb.ToString(), ct);
    }

    private static string FormatSkipReasons(IReadOnlyList<SkipReasonCountRow> rows)
        => rows.Count == 0
            ? string.Empty
            : string.Join("; ", rows.Select(r => $"{r.Reason}={r.Count}"));

    private static string Dt(DateTime value) => value.ToString("o", CultureInfo.InvariantCulture);

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
