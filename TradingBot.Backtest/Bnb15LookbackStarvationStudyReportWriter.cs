using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public static class Bnb15LookbackStarvationStudyReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task WriteAsync(
        string outputDirectory,
        Bnb15LookbackStarvationStudyResult result,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var warning = Bnb15LookbackStarvationStudySummary.DiagnosticWarning;

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "bnb15-lookback-starvation-study.json"),
            JsonSerializer.Serialize(new
            {
                warning,
                result.Summary,
                checkpoints = result.Checkpoints,
                variants = result.Variants
            }, JsonOptions),
            cancellationToken);

        await WriteCsvAsync(outputDirectory, result, warning, cancellationToken);
        await WriteTxtAsync(outputDirectory, result, warning, cancellationToken);
    }

    private static async Task WriteCsvAsync(
        string outputDirectory,
        Bnb15LookbackStarvationStudyResult result,
        string warning,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Warning,{Csv(warning)}");
        sb.AppendLine($"RunAtUtc,{Dt(result.Summary.RunAtUtc)}");
        sb.AppendLine($"FrozenProfileName,{Csv(result.Summary.FrozenProfileName)}");
        sb.AppendLine($"PrimaryRootCause,{Csv(result.Summary.PrimaryRootCause)}");
        sb.AppendLine($"OverallRecommendation,{Csv(result.Summary.PlainEnglish.OverallStudyRecommendation)}");
        sb.AppendLine();
        sb.AppendLine("Section,CheckpointUtc,LookbackStartUtc,LookbackEndUtc,RequiredMinLookbackTrades,ActualLookbackTrades,LookbackNetModerate,LookbackNetStressPlus,LookbackProfitFactor,ActivationPassed,SkipReason,ForwardBaseSignalsInActivationWindow,ForwardTradesInActivationWindow,NetIfActivated,StressNetIfActivated,RootCauseClassification");

        foreach (var c in result.Checkpoints)
        {
            sb.AppendLine(string.Join(",",
                "Checkpoint", Dt(c.CheckpointUtc), Dt(c.LookbackStartUtc), Dt(c.LookbackEndUtc),
                c.RequiredMinLookbackTrades, c.ActualLookbackTrades, c.LookbackNetModerate, c.LookbackNetStressPlus,
                c.LookbackProfitFactor, c.ActivationPassed, Csv(c.SkipReason), c.ForwardBaseSignalsInActivationWindow,
                c.ForwardTradesInActivationWindow, c.NetIfActivated, c.StressNetIfActivated, Csv(c.RootCauseClassification)));
        }

        sb.AppendLine();
        sb.AppendLine("Section,VariantName,MinLookbackTrades,LookbackDays,CheckpointFrequencyHours,ActivationDurationHours,RequireNetPositive,RequireStressPlusPositive,ActivatedCheckpointCount,ForwardTrades,NetModerate,NetStressPlus,WinRate,ProfitFactor,MaxDrawdown,MaxConsecutiveLosses,StressPassed,ForwardTradeCountPassed,Recommendation");

        foreach (var v in result.Variants)
        {
            sb.AppendLine(string.Join(",",
                "Variant", Csv(v.VariantName), v.MinLookbackTrades, v.LookbackDays, v.CheckpointFrequencyHours,
                v.ActivationDurationHours, v.RequireNetPositive, v.RequireStressPlusPositive, v.ActivatedCheckpointCount,
                v.ForwardTrades, v.NetModerate, v.NetStressPlus, v.WinRate, v.ProfitFactor, v.MaxDrawdown,
                v.MaxConsecutiveLosses, v.StressPassed, v.ForwardTradeCountPassed, Csv(v.Recommendation)));
        }

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "bnb15-lookback-starvation-study.csv"),
            sb.ToString(),
            cancellationToken);
    }

    private static async Task WriteTxtAsync(
        string outputDirectory,
        Bnb15LookbackStarvationStudyResult result,
        string warning,
        CancellationToken cancellationToken)
    {
        var s = result.Summary;
        var p = s.PlainEnglish;
        var sb = new StringBuilder();
        sb.AppendLine(warning);
        sb.AppendLine();
        sb.AppendLine($"RunAtUtc: {s.RunAtUtc:o}");
        sb.AppendLine($"FrozenProfile: {s.FrozenProfileName}");
        sb.AppendLine($"Geometry: {s.Symbol} {s.Interval} {s.Direction} T{s.TargetPercent:0.00} S{s.StopPercent:0.00}");
        sb.AppendLine($"FrozenActivationRule: {s.FrozenActivationRule}");
        sb.AppendLine($"ForwardWindow: {s.ForwardWindowStartUtc:o} -> {s.ForwardWindowEndUtc:o} ({s.ForwardSpanDays}d)");
        sb.AppendLine($"FrozenForwardTrades: {s.FrozenForwardTrades}");
        sb.AppendLine($"FrozenActivatedCheckpoints: {s.FrozenActivatedCheckpointCount}/{s.FrozenActivationCheckpointCount}");
        sb.AppendLine($"PrimaryRootCause: {s.PrimaryRootCause}");
        sb.AppendLine(s.CompactSummaryLine);
        sb.AppendLine();
        sb.AppendLine("Plain-English summary");
        sb.AppendLine($"- Sparse vs strict gate: {p.SparseVsStrictGate}");
        sb.AppendLine($"- Primary starvation gate: {p.PrimaryStarvationGate}");
        sb.AppendLine($"- Safe new incubation candidate: {p.SafeNewIncubationCandidate}");
        sb.AppendLine($"- Should current BNB15 stay parked: {p.ShouldCurrentBnb15StayParked}");
        sb.AppendLine($"- Overall recommendation: {p.OverallStudyRecommendation}");
        sb.AppendLine();
        sb.AppendLine("Root cause counts:");
        foreach (var kv in s.RootCauseCounts.OrderByDescending(kv => kv.Value))
            sb.AppendLine($"  {kv.Key}: {kv.Value}");
        sb.AppendLine();
        sb.AppendLine("Checkpoint diagnostics (frozen profile):");
        foreach (var c in result.Checkpoints)
        {
            sb.AppendLine(
                $"  {c.CheckpointUtc:yyyy-MM-dd HH:mm}Z lookbackTrades={c.ActualLookbackTrades}/{c.RequiredMinLookbackTrades} net={c.LookbackNetModerate:F2} stress={c.LookbackNetStressPlus:F2} activated={c.ActivationPassed} cause={c.RootCauseClassification} skip={c.SkipReason}");
        }
        sb.AppendLine();
        sb.AppendLine("Top diagnostic variants (CandidateForNewIncubation):");
        foreach (var v in result.Variants.Where(v => v.Recommendation == "CandidateForNewIncubation").Take(10))
        {
            sb.AppendLine(
                $"  {v.VariantName}: trades={v.ForwardTrades} net={v.NetModerate:F2} stressPlus={v.NetStressPlus:F2} activated={v.ActivatedCheckpointCount}");
        }
        if (!result.Variants.Any(v => v.Recommendation == "CandidateForNewIncubation"))
            sb.AppendLine("  (none)");
        sb.AppendLine();
        sb.AppendLine("backtestOnly=true realOrdersPlaced=false liveFuturesRecommended=false");

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "bnb15-lookback-starvation-study.txt"),
            sb.ToString(),
            cancellationToken);
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string Dt(DateTime dt)
        => dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
