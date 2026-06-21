using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public static class CrossSymbolExactEntryReconciliationAuditV1ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const string CandidateHeader =
        "CandidateKey,Symbol,Interval,Direction,TargetPercent,StopPercent,ActivationRule,V1TradeCount,V1TradeEntryTimes,V1ActivatedPeriodCount,V1TradesInsideActivatedPeriods,FrequencyStudyExactEntryCount,ReplayedExactEntryCount,MatchedByEntryTimeCount,MatchedWithinOneCandleCount,MatchedWithinActivationWindowCount,FirstMismatchTimeUtc,MismatchType,ReconciliationStatus,LeaderboardKeyPresent,V2KeyPresent,FrequencyStudyKeyPresent";

    private const string SampleHeader =
        "CandidateKey,V1EntryTimeUtc,ActivationStartUtc,ActivationEndUtc,EvaluatorCheckedTimeUtc,EvaluatorActivationPassed,EvaluatorEntryPresent,Reason,MismatchType";

    public static async Task WriteAsync(
        string outputDirectory,
        CrossSymbolExactEntryReconciliationAuditV1RunResult result,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var prefix = CrossSymbolExactEntryReconciliationAuditV1Catalog.OutputPrefix;
        var warning = CrossSymbolExactEntryReconciliationAuditV1SummaryRow.DiagnosticWarning;

        await WriteSummaryAsync(outputDirectory, prefix, result, warning, cancellationToken);
        await WriteCandidateFilesAsync(outputDirectory, $"{prefix}-candidates", result.Candidates, warning, cancellationToken);
        await WriteCandidateFilesAsync(outputDirectory, $"{prefix}-mismatches", result.Mismatches, warning, cancellationToken);
        await WriteSampleFilesAsync(outputDirectory, $"{prefix}-sample-trades", result.SampleTrades, warning, cancellationToken);
    }

    private static async Task WriteSummaryAsync(
        string outputDirectory,
        string prefix,
        CrossSymbolExactEntryReconciliationAuditV1RunResult result,
        string warning,
        CancellationToken cancellationToken)
    {
        var s = result.Summary;

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, $"{prefix}-summary.json"),
            JsonSerializer.Serialize(new
            {
                warning,
                studyNote = "Reconciliation is diagnostic only and does not modify V1 trades, thresholds, or strategy logic.",
                s.RunAtUtc,
                s.StudyStartUtc,
                s.StudyEndUtc,
                s.EvaluatedCandidateCount,
                s.CandidatesWithV1Trades,
                s.CandidatesFrequencyZero,
                s.CandidatesReplayedNonZero,
                s.CandidatesExactMatch,
                s.CandidatesV1TradesButFrequencyZero,
                s.CandidatesEntryEvaluatorMismatch,
                s.CandidatesActivationWindowMismatch,
                s.CandidatesTimeAlignmentMismatch,
                s.TotalV1TradesInWindow,
                s.TotalV1TradesInsideActivatedPeriods,
                s.TotalReplayedExactEntries,
                s.TotalFrequencyStudyExactEntries,
                s.PrimaryRootCause,
                s.CompactSummaryLine,
                s.PlainEnglish,
                s.BacktestOnly,
                s.RealOrdersPlaced
            }, JsonOptions),
            cancellationToken);

        var csv = new StringBuilder();
        csv.AppendLine($"Warning,{Csv(warning)}");
        csv.AppendLine($"RunAtUtc,{Dt(s.RunAtUtc)}");
        csv.AppendLine($"StudyStartUtc,{Dt(s.StudyStartUtc)}");
        csv.AppendLine($"StudyEndUtc,{Dt(s.StudyEndUtc)}");
        csv.AppendLine($"EvaluatedCandidateCount,{s.EvaluatedCandidateCount}");
        csv.AppendLine($"CandidatesWithV1Trades,{s.CandidatesWithV1Trades}");
        csv.AppendLine($"CandidatesFrequencyZero,{s.CandidatesFrequencyZero}");
        csv.AppendLine($"TotalV1TradesInWindow,{s.TotalV1TradesInWindow}");
        csv.AppendLine($"TotalFrequencyStudyExactEntries,{s.TotalFrequencyStudyExactEntries}");
        csv.AppendLine($"TotalReplayedExactEntries,{s.TotalReplayedExactEntries}");
        csv.AppendLine($"PrimaryRootCause,{Csv(s.PrimaryRootCause)}");
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{prefix}-summary.csv"), csv.ToString(), cancellationToken);

        var txt = new StringBuilder();
        txt.AppendLine(warning);
        txt.AppendLine();
        txt.AppendLine(s.CompactSummaryLine);
        txt.AppendLine($"RunAtUtc: {s.RunAtUtc:o}");
        txt.AppendLine($"Study window: {s.StudyStartUtc:o} to {s.StudyEndUtc:o}");
        txt.AppendLine($"Primary root cause: {s.PrimaryRootCause}");
        txt.AppendLine();
        txt.AppendLine("1. Are V1 discovery trades real exact base entries or not?");
        txt.AppendLine($"   {s.PlainEnglish.AreV1TradesRealExactBaseEntries}");
        txt.AppendLine();
        txt.AppendLine("2. Why did the frequency study report zero exact entries?");
        txt.AppendLine($"   {s.PlainEnglish.WhyFrequencyStudyReportedZero}");
        txt.AppendLine();
        txt.AppendLine("3. Is the zero exact-entry result valid or invalid?");
        txt.AppendLine($"   {s.PlainEnglish.IsZeroExactEntryResultValid}");
        txt.AppendLine();
        txt.AppendLine("4. Should Cross-Candidate Exact Entry Frequency Study V1 be fixed?");
        txt.AppendLine($"   {s.PlainEnglish.ShouldFrequencyStudyBeFixed}");
        txt.AppendLine();
        txt.AppendLine("5. What exact field/time alignment should be used going forward?");
        txt.AppendLine($"   {s.PlainEnglish.ExactFieldTimeAlignmentGoingForward}");
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{prefix}-summary.txt"), txt.ToString(), cancellationToken);
    }

    private static async Task WriteCandidateFilesAsync(
        string outputDirectory,
        string filePrefix,
        IReadOnlyList<CrossSymbolExactEntryReconciliationAuditV1CandidateRow> rows,
        string warning,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, $"{filePrefix}.json"),
            JsonSerializer.Serialize(new { warning, candidates = rows }, JsonOptions),
            cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine(CandidateHeader);
        foreach (var c in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(c.CandidateKey), Csv(c.Symbol), Csv(c.Interval), Csv(c.Direction),
                c.TargetPercent, c.StopPercent, Csv(c.ActivationRule),
                c.V1TradeCount, Csv(string.Join("|", c.V1TradeEntryTimes.Select(Dt))),
                c.V1ActivatedPeriodCount, c.V1TradesInsideActivatedPeriods,
                c.FrequencyStudyExactEntryCount, c.ReplayedExactEntryCount,
                c.MatchedByEntryTimeCount, c.MatchedWithinOneCandleCount, c.MatchedWithinActivationWindowCount,
                c.FirstMismatchTimeUtc.HasValue ? Dt(c.FirstMismatchTimeUtc.Value) : string.Empty,
                Csv(c.MismatchType), Csv(c.ReconciliationStatus),
                c.LeaderboardKeyPresent, c.V2KeyPresent, c.FrequencyStudyKeyPresent));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{filePrefix}.csv"), sb.ToString(), cancellationToken);
    }

    private static async Task WriteSampleFilesAsync(
        string outputDirectory,
        string filePrefix,
        IReadOnlyList<CrossSymbolExactEntryReconciliationAuditV1SampleRow> rows,
        string warning,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, $"{filePrefix}.json"),
            JsonSerializer.Serialize(new { warning, samples = rows }, JsonOptions),
            cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine(SampleHeader);
        foreach (var s in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(s.CandidateKey),
                Dt(s.V1EntryTimeUtc),
                s.ActivationStartUtc.HasValue ? Dt(s.ActivationStartUtc.Value) : string.Empty,
                s.ActivationEndUtc.HasValue ? Dt(s.ActivationEndUtc.Value) : string.Empty,
                Dt(s.EvaluatorCheckedTimeUtc),
                s.EvaluatorActivationPassed,
                s.EvaluatorEntryPresent,
                Csv(s.Reason),
                Csv(s.MismatchType)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{filePrefix}.csv"), sb.ToString(), cancellationToken);
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string Dt(DateTime value)
        => value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
}
