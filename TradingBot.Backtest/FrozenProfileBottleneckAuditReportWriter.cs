using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public static class FrozenProfileBottleneckAuditReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task WriteAsync(string outputDirectory, FrozenProfileBottleneckAuditSummary summary, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDirectory);
        var warning = FrozenProfileBottleneckAuditSummary.DiagnosticWarning;

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "frozen-profile-bottleneck-audit.json"),
            JsonSerializer.Serialize(new
            {
                warning,
                summary.RunAtUtc,
                summary.CompactSummaryLine,
                profiles = summary.Profiles
            }, JsonOptions),
            ct);

        await WriteCsvAsync(outputDirectory, summary, warning, ct);
        await WriteTextAsync(outputDirectory, summary, warning, ct);
    }

    private static async Task WriteCsvAsync(
        string outputDirectory,
        FrozenProfileBottleneckAuditSummary summary,
        string warning,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Warning,{Csv(warning)}");
        sb.AppendLine($"RunAtUtc,{Dt(summary.RunAtUtc)}");
        sb.AppendLine($"CompactSummaryLine,{Csv(summary.CompactSummaryLine)}");
        sb.AppendLine("ProfileName,Symbol,Interval,Direction,ForwardWindowStartUtc,ForwardWindowEndUtc,ForwardSpanDays,ForwardTrades,NetModerate,NetStressPlus,ActivationCheckpointCount,ActivatedCheckpointCount,ActivationFailedCheckpointCount,ActivatedButNoEntryCount,BaseSignalsInsideForwardWindow,BaseSignalsInsideActivatedWindows,BaseSignalsInsideActivationFailedWindows,NetIfAllBaseSignalsAllowed,NetIfOnlyActivatedBaseSignalsAllowed,NetIfActivationFailedBaseSignalsAllowed,TopActivationFailureReasons,TopEntryFailureReasons,LookbackTradeCountsByCheckpoint,CooldownBlockedCount,HindsightOnlyMoveCount,RealMissedWinnerCount,BlockedLoserCount,BottleneckClassification,Recommendation,BottleneckExplanation,ShadowActivationPassed,ShadowEntrySignalPresent");

        foreach (var r in summary.Profiles)
        {
            sb.AppendLine(string.Join(",",
                Csv(r.ProfileName), Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction),
                Dt(r.ForwardWindowStartUtc), Dt(r.ForwardWindowEndUtc), r.ForwardSpanDays,
                r.ForwardTrades, r.NetModerate, r.NetStressPlus,
                r.ActivationCheckpointCount, r.ActivatedCheckpointCount, r.ActivationFailedCheckpointCount, r.ActivatedButNoEntryCount,
                r.BaseSignalsInsideForwardWindow, r.BaseSignalsInsideActivatedWindows, r.BaseSignalsInsideActivationFailedWindows,
                r.NetIfAllBaseSignalsAllowed, r.NetIfOnlyActivatedBaseSignalsAllowed, r.NetIfActivationFailedBaseSignalsAllowed,
                Csv(FormatSkipReasons(r.TopActivationFailureReasons)),
                Csv(FormatSkipReasons(r.TopEntryFailureReasons)),
                Csv(FormatLookbackCounts(r.LookbackTradeCountsByCheckpoint)),
                r.CooldownBlockedCount, r.HindsightOnlyMoveCount, r.RealMissedWinnerCount, r.BlockedLoserCount,
                Csv(r.BottleneckClassification), Csv(r.Recommendation), Csv(r.BottleneckExplanation),
                r.ShadowActivationPassed, r.ShadowEntrySignalPresent));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "frozen-profile-bottleneck-audit.csv"), sb.ToString(), ct);
    }

    private static async Task WriteTextAsync(
        string outputDirectory,
        FrozenProfileBottleneckAuditSummary summary,
        string warning,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine(warning);
        sb.AppendLine();
        sb.AppendLine($"RunAtUtc: {summary.RunAtUtc:o}");
        sb.AppendLine(summary.CompactSummaryLine);
        sb.AppendLine();

        foreach (var r in summary.Profiles)
        {
            sb.AppendLine($"=== {r.ProfileName} ===");
            sb.AppendLine($"Symbol={r.Symbol} Interval={r.Interval} Direction={r.Direction}");
            sb.AppendLine($"ForwardWindow: {r.ForwardWindowStartUtc:o} -> {r.ForwardWindowEndUtc:o} ({r.ForwardSpanDays} days)");
            sb.AppendLine($"ForwardTrades={r.ForwardTrades} NetModerate={r.NetModerate} NetStressPlus={r.NetStressPlus}");
            sb.AppendLine($"Checkpoints: total={r.ActivationCheckpointCount} activated={r.ActivatedCheckpointCount} failed={r.ActivationFailedCheckpointCount} activatedButNoEntry={r.ActivatedButNoEntryCount}");
            sb.AppendLine($"BaseSignals: forward={r.BaseSignalsInsideForwardWindow} activatedWindows={r.BaseSignalsInsideActivatedWindows} failedWindows={r.BaseSignalsInsideActivationFailedWindows}");
            sb.AppendLine($"CounterfactualNet: allSignals={r.NetIfAllBaseSignalsAllowed} activatedOnly={r.NetIfOnlyActivatedBaseSignalsAllowed} failedWindows={r.NetIfActivationFailedBaseSignalsAllowed}");
            sb.AppendLine($"TopActivationFailureReasons: {FormatSkipReasons(r.TopActivationFailureReasons)}");
            sb.AppendLine($"TopEntryFailureReasons: {FormatSkipReasons(r.TopEntryFailureReasons)}");
            sb.AppendLine($"LookbackTradeCountsByCheckpoint: {FormatLookbackCounts(r.LookbackTradeCountsByCheckpoint)}");
            sb.AppendLine($"CooldownBlockedCount={r.CooldownBlockedCount} HindsightOnlyMoveCount={r.HindsightOnlyMoveCount} RealMissedWinnerCount={r.RealMissedWinnerCount} BlockedLoserCount={r.BlockedLoserCount}");
            sb.AppendLine($"Shadow: activationPassed={r.ShadowActivationPassed} entrySignalPresent={r.ShadowEntrySignalPresent}");
            sb.AppendLine($"BottleneckClassification: {r.BottleneckClassification}");
            sb.AppendLine($"Recommendation: {r.Recommendation}");
            sb.AppendLine($"Explanation: {r.BottleneckExplanation}");
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "frozen-profile-bottleneck-audit.txt"), sb.ToString(), ct);
    }

    private static string FormatSkipReasons(IReadOnlyList<SkipReasonCountRow> rows)
        => rows.Count == 0
            ? string.Empty
            : string.Join("; ", rows.Select(r => $"{r.Reason}={r.Count}"));

    private static string FormatLookbackCounts(IReadOnlyList<LookbackTradeCountRow> rows)
        => rows.Count == 0
            ? string.Empty
            : string.Join("; ", rows.Select(r => $"{r.CheckpointUtc:yyyy-MM-ddTHH:mm}Z={r.LookbackTradeCount}"));

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
