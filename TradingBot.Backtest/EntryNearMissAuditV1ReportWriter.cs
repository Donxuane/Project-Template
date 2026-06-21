using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public static class EntryNearMissAuditV1ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const string CandidateHeader =
        "CandidateKey,Symbol,Interval,Direction,TargetPercent,StopPercent,ActivationRule,ResearchPromotionStatus,CurrentExecutionReadiness,ResearchScore,NetModerate,NetStressPlus,WinRate,ProfitFactor,LatestCandleUtc,ActivationCurrentlyPassed,BaseEntrySignalPresentNow,EntrySignalFailureReason,NearMissScore,NearMissClassification,DistanceToEntryPercent,DistanceToNearExtremeThresholdPercent,LatestClose,RecentHigh,RecentLow,DistanceToRecentHighPercent,DistanceToRecentLowPercent,VolatilityState,ElevatedVolPassed,TrendContextPassed,FlowContextPassed,CandlePatternPassed,RequiredEntryConditions,PassedEntryConditions,FailedEntryConditions,FailedConditionCount,WouldBecomeEntryIfOneConditionRelaxed,OneConditionRelaxationName,HypotheticalSignalDirection,HypotheticalRiskNote,IsTopNearMiss";

    public static async Task WriteAsync(
        string outputDirectory,
        EntryNearMissAuditV1RunResult result,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var warning = EntryNearMissAuditV1SummaryRow.DiagnosticWarning;

        await WriteSummaryAsync(outputDirectory, result, warning, cancellationToken);
        await WriteCandidateFilesAsync(
            outputDirectory,
            "entry-near-miss-audit-v1-candidates",
            result.Candidates,
            warning,
            cancellationToken);
        await WriteCandidateFilesAsync(
            outputDirectory,
            "entry-near-miss-audit-v1-top-near-misses",
            result.TopNearMisses,
            warning,
            cancellationToken);
        await WriteCandidateFilesAsync(
            outputDirectory,
            "entry-near-miss-audit-v1-far-misses",
            result.FarMisses,
            warning,
            cancellationToken);
    }

    private static async Task WriteSummaryAsync(
        string outputDirectory,
        EntryNearMissAuditV1RunResult result,
        string warning,
        CancellationToken cancellationToken)
    {
        var s = result.Summary;

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "entry-near-miss-audit-v1-summary.json"),
            JsonSerializer.Serialize(new
            {
                warning,
                auditNote = "Diagnostic/shadow only. Near-miss proximity is not forward proof and must not be traded.",
                s.RunAtUtc,
                s.EvaluatedAtUtc,
                s.CompactSummaryLine,
                s.EvaluatedActivationPassedCount,
                s.ExactEntrySignalCount,
                s.OneConditionAwayCount,
                s.TwoConditionsAwayCount,
                s.PriceDistanceNearCount,
                s.FarFromEntryCount,
                s.ResearchWeakIgnoreCount,
                s.BottleneckBlockedIgnoreCount,
                s.TopNearMissCount,
                s.TopNearMissCandidate,
                s.TopNearMissReason,
                s.EntryRarityVerdict,
                s.TopNearMisses,
                s.FarMisses,
                s.PlainEnglish,
                s.ScannerInputDirectory,
                s.V1InputDirectory,
                s.V2InputDirectory,
                s.BacktestOnly,
                s.RealOrdersPlaced,
                s.LiveFuturesRecommended
            }, JsonOptions),
            cancellationToken);

        var txt = new StringBuilder();
        txt.AppendLine(warning);
        txt.AppendLine();
        txt.AppendLine(s.CompactSummaryLine);
        txt.AppendLine($"RunAtUtc: {s.RunAtUtc:o}");
        txt.AppendLine($"EvaluatedAtUtc: {s.EvaluatedAtUtc:o}");
        txt.AppendLine(
            $"ActivationPassed={s.EvaluatedActivationPassedCount} ExactEntry={s.ExactEntrySignalCount} OneAway={s.OneConditionAwayCount} TwoAway={s.TwoConditionsAwayCount} PriceNear={s.PriceDistanceNearCount} FarFromEntry={s.FarFromEntryCount}");
        txt.AppendLine($"EntryRarityVerdict: {s.EntryRarityVerdict}");
        txt.AppendLine($"TopNearMissCandidate: {(string.IsNullOrEmpty(s.TopNearMissCandidate) ? "(none)" : s.TopNearMissCandidate)}");
        if (!string.IsNullOrEmpty(s.TopNearMissReason))
            txt.AppendLine($"TopNearMissReason: {s.TopNearMissReason}");
        txt.AppendLine();
        txt.AppendLine("Why are activated candidates not entering?");
        txt.AppendLine($"  {s.PlainEnglish.WhyActivatedCandidatesNotEntering}");
        txt.AppendLine();
        txt.AppendLine("Are any candidates close enough to watch aggressively?");
        txt.AppendLine($"  {s.PlainEnglish.AreAnyCloseEnoughToWatchAggressively}");
        txt.AppendLine();
        txt.AppendLine("Is SOLUSDT 30m Short close or far?");
        txt.AppendLine($"  {s.PlainEnglish.SolUsdt30mShortProximity}");
        txt.AppendLine();
        txt.AppendLine("Should we create a new incubation candidate from near-miss logic?");
        txt.AppendLine($"  {s.PlainEnglish.ShouldCreateIncubationFromNearMiss}");
        txt.AppendLine();
        txt.AppendLine("Should we wait for an actual entry signal?");
        txt.AppendLine($"  {s.PlainEnglish.ShouldWaitForActualEntrySignal}");
        txt.AppendLine();
        txt.AppendLine($"ScannerInputDirectory: {s.ScannerInputDirectory}");
        txt.AppendLine($"V1InputDirectory: {s.V1InputDirectory}");
        txt.AppendLine($"V2InputDirectory: {s.V2InputDirectory}");
        txt.AppendLine($"backtestOnly={s.BacktestOnly} realOrdersPlaced={s.RealOrdersPlaced} liveFuturesRecommended={s.LiveFuturesRecommended}");

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "entry-near-miss-audit-v1-summary.txt"),
            txt.ToString(),
            cancellationToken);

        var csv = new StringBuilder();
        csv.AppendLine($"Warning,{Csv(warning)}");
        csv.AppendLine($"RunAtUtc,{Dt(s.RunAtUtc)}");
        csv.AppendLine($"EvaluatedAtUtc,{Dt(s.EvaluatedAtUtc)}");
        csv.AppendLine($"CompactSummaryLine,{Csv(s.CompactSummaryLine)}");
        csv.AppendLine($"EvaluatedActivationPassedCount,{s.EvaluatedActivationPassedCount}");
        csv.AppendLine($"ExactEntrySignalCount,{s.ExactEntrySignalCount}");
        csv.AppendLine($"OneConditionAwayCount,{s.OneConditionAwayCount}");
        csv.AppendLine($"TwoConditionsAwayCount,{s.TwoConditionsAwayCount}");
        csv.AppendLine($"PriceDistanceNearCount,{s.PriceDistanceNearCount}");
        csv.AppendLine($"FarFromEntryCount,{s.FarFromEntryCount}");
        csv.AppendLine($"ResearchWeakIgnoreCount,{s.ResearchWeakIgnoreCount}");
        csv.AppendLine($"BottleneckBlockedIgnoreCount,{s.BottleneckBlockedIgnoreCount}");
        csv.AppendLine($"TopNearMissCount,{s.TopNearMissCount}");
        csv.AppendLine($"TopNearMissCandidate,{Csv(s.TopNearMissCandidate)}");
        csv.AppendLine($"TopNearMissReason,{Csv(s.TopNearMissReason)}");
        csv.AppendLine($"EntryRarityVerdict,{Csv(s.EntryRarityVerdict)}");
        csv.AppendLine($"ScannerInputDirectory,{Csv(s.ScannerInputDirectory)}");
        csv.AppendLine($"V1InputDirectory,{Csv(s.V1InputDirectory)}");
        csv.AppendLine($"V2InputDirectory,{Csv(s.V2InputDirectory)}");
        csv.AppendLine($"BacktestOnly,{s.BacktestOnly}");
        csv.AppendLine($"RealOrdersPlaced,{s.RealOrdersPlaced}");
        csv.AppendLine($"LiveFuturesRecommended,{s.LiveFuturesRecommended}");
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "entry-near-miss-audit-v1-summary.csv"),
            csv.ToString(),
            cancellationToken);
    }

    private static async Task WriteCandidateFilesAsync(
        string outputDirectory,
        string filePrefix,
        IReadOnlyList<EntryNearMissAuditV1CandidateRow> rows,
        string warning,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, $"{filePrefix}.json"),
            JsonSerializer.Serialize(new { warning, candidates = rows }, JsonOptions),
            cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine($"Warning,{Csv(warning)}");
        sb.AppendLine(CandidateHeader);
        foreach (var r in rows)
            sb.AppendLine(FormatCandidateCsvRow(r));

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, $"{filePrefix}.csv"),
            sb.ToString(),
            cancellationToken);
    }

    private static string FormatCandidateCsvRow(EntryNearMissAuditV1CandidateRow r)
        => string.Join(",",
            Csv(r.CandidateKey), Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction),
            r.TargetPercent, r.StopPercent, Csv(r.ActivationRule),
            Csv(r.ResearchPromotionStatus), Csv(r.CurrentExecutionReadiness), r.ResearchScore,
            r.NetModerate, r.NetStressPlus, r.WinRate, r.ProfitFactor, Dt(r.LatestCandleUtc),
            r.ActivationCurrentlyPassed, r.BaseEntrySignalPresentNow, Csv(r.EntrySignalFailureReason),
            r.NearMissScore, Csv(r.NearMissClassification), r.DistanceToEntryPercent,
            r.DistanceToNearExtremeThresholdPercent, r.LatestClose, r.RecentHigh, r.RecentLow,
            r.DistanceToRecentHighPercent, r.DistanceToRecentLowPercent, Csv(r.VolatilityState),
            r.ElevatedVolPassed, r.TrendContextPassed, r.FlowContextPassed, r.CandlePatternPassed,
            Csv(string.Join("|", r.RequiredEntryConditions)),
            Csv(string.Join("|", r.PassedEntryConditions)),
            Csv(string.Join("|", r.FailedEntryConditions)),
            r.FailedConditionCount, r.WouldBecomeEntryIfOneConditionRelaxed,
            Csv(r.OneConditionRelaxationName), Csv(r.HypotheticalSignalDirection),
            Csv(r.HypotheticalRiskNote), r.IsTopNearMiss);

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
