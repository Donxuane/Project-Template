using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public static class CrossCandidateExactEntryFrequencyStudyV1ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const string CandidateHeader =
        "CandidateKey,Symbol,Interval,Direction,TargetPercent,StopPercent,ActivationRule,TradeCount,V1TradeCount,V1TradesInsideActivatedPeriods,ActivatedCheckpointCount,ExactEntryCountInsideActivatedWindows,ExactEntriesPerDay,DaysSinceLastExactEntry,LastExactEntryUtc,MedianHoursBetweenExactEntries,MaxHoursBetweenExactEntries,EvaluatorReplayPresentCount,EvaluatorOpenTradeOverlapCount,FrequencyCountingMethod,NetModerate,NetStressPlus,WinRate,ProfitFactor,MaxDrawdown,MaxConsecutiveLosses,SparseWarning,OverfitWarning,SingleClusterWarning,PositiveActivatedPeriodsPercent,EntryFrequencyScore,StressQualityScore,CombinedCandidateScore,EntryFrequencyClassification,Recommendation,ActivationCurrentlyPassed,BaseEntrySignalPresentNow";

    public static async Task WriteAsync(
        string outputDirectory,
        CrossCandidateExactEntryFrequencyStudyV1RunResult result,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var prefix = CrossCandidateExactEntryFrequencyStudyV1Catalog.OutputPrefix;
        var warning = CrossCandidateExactEntryFrequencyStudyV1SummaryRow.DiagnosticWarning;

        await WriteSummaryAsync(outputDirectory, prefix, result, warning, cancellationToken);
        await WriteCandidateFilesAsync(outputDirectory, $"{prefix}-candidates", result.Candidates, warning, cancellationToken);
        await WriteCandidateFilesAsync(outputDirectory, $"{prefix}-top-frequency", result.TopFrequency, warning, cancellationToken);
        await WriteCandidateFilesAsync(outputDirectory, $"{prefix}-top-stress-positive", result.TopStressPositive, warning, cancellationToken);
        await WriteCandidateFilesAsync(outputDirectory, $"{prefix}-too-rare", result.TooRare, warning, cancellationToken);
    }

    private static async Task WriteSummaryAsync(
        string outputDirectory,
        string prefix,
        CrossCandidateExactEntryFrequencyStudyV1RunResult result,
        string warning,
        CancellationToken cancellationToken)
    {
        var s = result.Summary;

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, $"{prefix}-summary.json"),
            JsonSerializer.Serialize(new
            {
                warning,
                studyNote = "Historical exact-entry frequency is research only and is not forward proof.",
                countingBugNote = "The previous zero-exact-entry result was invalid. Fixed frequency counts distinct V1 trade EntryTimeUtc values inside activated periods [ActivationStartUtc, ActivationEndUtc).",
                s.RunAtUtc,
                s.StudyStartUtc,
                s.StudyEndUtc,
                s.StudySpanDays,
                s.EvaluatedCandidateCount,
                s.CandidatesWithExactEntries,
                s.PromoteToExactEntryWatcherCount,
                s.TooRareCount,
                s.StressNegativeCount,
                s.TotalV1Trades,
                s.TotalV1TradesInsideActivatedPeriods,
                s.TotalExactEntriesAfterFix,
                s.CandidatesWithExactEntriesAfterFix,
                s.CandidatesStillTooRare,
                s.CandidatesStressPositiveAndFrequentEnough,
                s.CountingBugFixed,
                frequencyCountingMethod = "V1TradesInsideActivatedPeriods",
                s.CompactSummaryLine,
                s.PlainEnglish,
                topFrequency = result.TopFrequency,
                topStressPositive = result.TopStressPositive,
                s.BacktestOnly,
                s.RealOrdersPlaced,
                s.LiveFuturesRecommended,
                s.NearMissNotUsed
            }, JsonOptions),
            cancellationToken);

        var csv = new StringBuilder();
        csv.AppendLine($"Warning,{Csv(warning)}");
        csv.AppendLine($"RunAtUtc,{Dt(s.RunAtUtc)}");
        csv.AppendLine($"StudyStartUtc,{Dt(s.StudyStartUtc)}");
        csv.AppendLine($"StudyEndUtc,{Dt(s.StudyEndUtc)}");
        csv.AppendLine($"EvaluatedCandidateCount,{s.EvaluatedCandidateCount}");
        csv.AppendLine($"CandidatesWithExactEntries,{s.CandidatesWithExactEntries}");
        csv.AppendLine($"PromoteToExactEntryWatcherCount,{s.PromoteToExactEntryWatcherCount}");
        csv.AppendLine($"TooRareCount,{s.TooRareCount}");
        csv.AppendLine($"StressNegativeCount,{s.StressNegativeCount}");
        csv.AppendLine($"TotalV1Trades,{s.TotalV1Trades}");
        csv.AppendLine($"TotalV1TradesInsideActivatedPeriods,{s.TotalV1TradesInsideActivatedPeriods}");
        csv.AppendLine($"TotalExactEntriesAfterFix,{s.TotalExactEntriesAfterFix}");
        csv.AppendLine($"CandidatesWithExactEntriesAfterFix,{s.CandidatesWithExactEntriesAfterFix}");
        csv.AppendLine($"CandidatesStillTooRare,{s.CandidatesStillTooRare}");
        csv.AppendLine($"CandidatesStressPositiveAndFrequentEnough,{s.CandidatesStressPositiveAndFrequentEnough}");
        csv.AppendLine($"CountingBugFixed,{s.CountingBugFixed}");
        csv.AppendLine($"FrequencyCountingMethod,V1TradesInsideActivatedPeriods");
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{prefix}-summary.csv"), csv.ToString(), cancellationToken);

        var txt = new StringBuilder();
        txt.AppendLine(warning);
        txt.AppendLine();
        txt.AppendLine("COUNTING SEMANTICS FIX (CountingBugFixed=true)");
        txt.AppendLine($"   {s.PlainEnglish.CountingBugFixNote}");
        txt.AppendLine();
        txt.AppendLine($"   {s.PlainEnglish.FixedFrequencyMethodNote}");
        txt.AppendLine();
        txt.AppendLine($"   Totals: TotalV1Trades={s.TotalV1Trades}, TotalV1TradesInsideActivatedPeriods={s.TotalV1TradesInsideActivatedPeriods}, TotalExactEntriesAfterFix={s.TotalExactEntriesAfterFix}, CandidatesWithExactEntriesAfterFix={s.CandidatesWithExactEntriesAfterFix}, CandidatesStillTooRare={s.CandidatesStillTooRare}, CandidatesStressPositiveAndFrequentEnough={s.CandidatesStressPositiveAndFrequentEnough}.");
        txt.AppendLine();
        txt.AppendLine(s.CompactSummaryLine);
        txt.AppendLine($"RunAtUtc: {s.RunAtUtc:o}");
        txt.AppendLine($"Study window: {s.StudyStartUtc:o} to {s.StudyEndUtc:o} ({s.StudySpanDays:F1} days)");
        txt.AppendLine();
        txt.AppendLine("1. Which candidates produce exact entries most often?");
        txt.AppendLine($"   {s.PlainEnglish.WhichCandidatesProduceExactEntriesMostOften}");
        txt.AppendLine();
        txt.AppendLine("2. Which candidates are stress-positive and not too rare?");
        txt.AppendLine($"   {s.PlainEnglish.WhichAreStressPositiveAndNotTooRare}");
        txt.AppendLine();
        txt.AppendLine("3. Which candidates are profitable but too rare?");
        txt.AppendLine($"   {s.PlainEnglish.WhichAreProfitableButTooRare}");
        txt.AppendLine();
        txt.AppendLine("4. Which candidates should watcher focus on?");
        txt.AppendLine($"   {s.PlainEnglish.WhichShouldWatcherFocusOn}");
        txt.AppendLine();
        txt.AppendLine("5. Are any candidates worth moving toward testnet-order preparation?");
        txt.AppendLine($"   {s.PlainEnglish.WorthMovingTowardTestnetOrderPreparation}");
        txt.AppendLine();
        txt.AppendLine("IMPORTANT CAVEATS");
        txt.AppendLine($"   {s.PlainEnglish.HistoricalNotForwardProofNote}");
        txt.AppendLine($"   {s.PlainEnglish.LiveTestnetGuardNote}");
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{prefix}-summary.txt"), txt.ToString(), cancellationToken);
    }

    private static async Task WriteCandidateFilesAsync(
        string outputDirectory,
        string filePrefix,
        IReadOnlyList<CrossCandidateExactEntryFrequencyStudyV1CandidateRow> rows,
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
                c.TradeCount, c.V1TradeCount, c.V1TradesInsideActivatedPeriods,
                c.ActivatedCheckpointCount, c.ExactEntryCountInsideActivatedWindows,
                c.ExactEntriesPerDay,
                c.DaysSinceLastExactEntry?.ToString(CultureInfo.InvariantCulture) ?? "",
                DtNullable(c.LastExactEntryUtc),
                c.MedianHoursBetweenExactEntries?.ToString(CultureInfo.InvariantCulture) ?? "",
                c.MaxHoursBetweenExactEntries?.ToString(CultureInfo.InvariantCulture) ?? "",
                c.EvaluatorReplayPresentCount, c.EvaluatorOpenTradeOverlapCount, Csv(c.FrequencyCountingMethod),
                c.NetModerate, c.NetStressPlus, c.WinRate, c.ProfitFactor,
                c.MaxDrawdown, c.MaxConsecutiveLosses,
                c.SparseWarning, c.OverfitWarning, c.SingleClusterWarning,
                c.PositiveActivatedPeriodsPercent,
                c.EntryFrequencyScore, c.StressQualityScore, c.CombinedCandidateScore,
                Csv(c.EntryFrequencyClassification), Csv(c.Recommendation),
                c.ActivationCurrentlyPassed?.ToString() ?? "",
                c.BaseEntrySignalPresentNow?.ToString() ?? ""));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{filePrefix}.csv"), sb.ToString(), cancellationToken);
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static string Dt(DateTime value) => value.ToString("o", CultureInfo.InvariantCulture);

    private static string DtNullable(DateTime? value)
        => value.HasValue ? Dt(value.Value) : string.Empty;
}
