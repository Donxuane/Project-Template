using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public static class CurrentOpportunityScannerV1ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly string CandidateHeader =
        "CandidateKey,Symbol,Interval,Direction,TargetPercent,StopPercent,ActivationRule,ResearchPromotionStatus,CurrentExecutionReadiness,ResearchScore,TradeCount,NetModerate,NetStressPlus,WinRate,ProfitFactor,SparseWarning,OverfitWarning,SingleClusterWarning,LatestCandleUtc,ActivationCurrentlyPassed,ActivationFailureReason,BaseEntrySignalPresentNow,EntrySignalFailureReason,WouldBeShadowActionable,WouldPlaceOrder,ReasonIfBlocked,BlockedReasonCategory,NormalizedNetPer100USDT,AssignedShadowNotionalUSDT,RiskStatus,PrecisionStatus,AlmostActionable," + NormalizedRiskPnlModule.SummaryCsvHeaderSuffix();

    public static async Task WriteAsync(
        string outputDirectory,
        CurrentOpportunityScannerV1RunResult result,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var warning = CurrentOpportunityScannerV1SummaryRow.DiagnosticWarning;

        await WriteSummaryAsync(outputDirectory, result, warning, cancellationToken);
        await WriteCandidateFilesAsync(
            outputDirectory,
            "current-opportunity-scanner-v1-candidates",
            result.Candidates,
            warning,
            cancellationToken);
        await WriteCandidateFilesAsync(
            outputDirectory,
            "current-opportunity-scanner-v1-actionable-shadow",
            result.ActionableShadow,
            warning,
            cancellationToken);
        await WriteCandidateFilesAsync(
            outputDirectory,
            "current-opportunity-scanner-v1-blocked",
            result.Blocked,
            warning,
            cancellationToken);
    }

    private static async Task WriteSummaryAsync(
        string outputDirectory,
        CurrentOpportunityScannerV1RunResult result,
        string warning,
        CancellationToken cancellationToken)
    {
        var s = result.Summary;
        var headline = s.ActionableShadowCount == 0
            ? "No current opportunity"
            : "Shadow opportunity exists";

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "current-opportunity-scanner-v1-summary.json"),
            JsonSerializer.Serialize(new
            {
                warning,
                scannerNote = "Point-in-time diagnostic only. Scanner results are not forward proof and never place orders.",
                headline,
                s.RunAtUtc,
                s.EvaluatedAtUtc,
                s.CompactSummaryLine,
                s.EvaluatedCandidateCount,
                s.ActivationPassedCount,
                s.BaseEntrySignalPresentCount,
                s.ActionableShadowCount,
                s.AlmostActionableCount,
                s.BlockedByActivationCount,
                s.BlockedByEntryMissingCount,
                s.BlockedByResearchStressCount,
                s.BlockedByBottleneckCount,
                s.BlockedByExecutionReadinessCount,
                s.BlockedByResearchQualityCount,
                s.TopBlockers,
                s.TopActionableCandidates,
                s.TopAlmostActionableCandidates,
                s.NormalizedRisk,
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
        txt.AppendLine(headline);
        txt.AppendLine($"RunAtUtc: {s.RunAtUtc:o}");
        txt.AppendLine($"EvaluatedAtUtc: {s.EvaluatedAtUtc:o}");
        txt.AppendLine(s.CompactSummaryLine);
        txt.AppendLine();
        NormalizedRiskPnlModule.AppendSummaryRiskLines(txt, s.NormalizedRisk);
        txt.AppendLine(
            $"Evaluated={s.EvaluatedCandidateCount} ActivationPassed={s.ActivationPassedCount} EntryPresent={s.BaseEntrySignalPresentCount} ActionableShadow={s.ActionableShadowCount}");
        txt.AppendLine(
            $"Blocked: activation={s.BlockedByActivationCount} entryMissing={s.BlockedByEntryMissingCount} researchStress={s.BlockedByResearchStressCount} bottleneck={s.BlockedByBottleneckCount} executionReadiness={s.BlockedByExecutionReadinessCount} researchQuality={s.BlockedByResearchQualityCount}");
        txt.AppendLine($"V1InputDirectory: {s.V1InputDirectory}");
        txt.AppendLine($"V2InputDirectory: {s.V2InputDirectory}");
        txt.AppendLine($"backtestOnly={s.BacktestOnly} realOrdersPlaced={s.RealOrdersPlaced} liveFuturesRecommended={s.LiveFuturesRecommended}");
        txt.AppendLine();
        txt.AppendLine("Top blockers:");
        if (s.TopBlockers.Count == 0)
            txt.AppendLine("  (none)");
        else
        {
            foreach (var blocker in s.TopBlockers)
                txt.AppendLine($"  {blocker}");
        }

        if (s.TopActionableCandidates.Count > 0)
        {
            txt.AppendLine();
            txt.AppendLine("Top actionable shadow candidates:");
            foreach (var c in s.TopActionableCandidates)
            {
                txt.AppendLine(
                    $"  {c.CandidateKey} score={c.ResearchScore:F4} netPer100={c.NormalizedNetPer100Usdt:F4} readiness={c.CurrentExecutionReadiness}");
            }
        }

        if (s.TopAlmostActionableCandidates.Count > 0)
        {
            txt.AppendLine();
            txt.AppendLine("Top almost actionable candidates:");
            foreach (var c in s.TopAlmostActionableCandidates)
            {
                txt.AppendLine(
                    $"  {c.CandidateKey} activation={c.ActivationCurrentlyPassed} entry={c.BaseEntrySignalPresentNow} blocked={c.BlockedReasonCategory}");
            }
        }

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "current-opportunity-scanner-v1-summary.txt"),
            txt.ToString(),
            cancellationToken);

        var csv = new StringBuilder();
        csv.AppendLine($"Warning,{Csv(warning)}");
        csv.AppendLine($"Headline,{Csv(headline)}");
        csv.AppendLine($"RunAtUtc,{Dt(s.RunAtUtc)}");
        csv.AppendLine($"EvaluatedAtUtc,{Dt(s.EvaluatedAtUtc)}");
        csv.AppendLine($"CompactSummaryLine,{Csv(s.CompactSummaryLine)}");
        csv.AppendLine($"EvaluatedCandidateCount,{s.EvaluatedCandidateCount}");
        csv.AppendLine($"ActivationPassedCount,{s.ActivationPassedCount}");
        csv.AppendLine($"BaseEntrySignalPresentCount,{s.BaseEntrySignalPresentCount}");
        csv.AppendLine($"ActionableShadowCount,{s.ActionableShadowCount}");
        csv.AppendLine($"AlmostActionableCount,{s.AlmostActionableCount}");
        csv.AppendLine($"BlockedByActivationCount,{s.BlockedByActivationCount}");
        csv.AppendLine($"BlockedByEntryMissingCount,{s.BlockedByEntryMissingCount}");
        csv.AppendLine($"BlockedByResearchStressCount,{s.BlockedByResearchStressCount}");
        csv.AppendLine($"BlockedByBottleneckCount,{s.BlockedByBottleneckCount}");
        csv.AppendLine($"BlockedByExecutionReadinessCount,{s.BlockedByExecutionReadinessCount}");
        csv.AppendLine($"BlockedByResearchQualityCount,{s.BlockedByResearchQualityCount}");
        csv.AppendLine($"TopBlockers,{Csv(string.Join("; ", s.TopBlockers))}");
        csv.AppendLine($"V1InputDirectory,{Csv(s.V1InputDirectory)}");
        csv.AppendLine($"V2InputDirectory,{Csv(s.V2InputDirectory)}");
        csv.AppendLine($"BacktestOnly,{s.BacktestOnly}");
        csv.AppendLine($"RealOrdersPlaced,{s.RealOrdersPlaced}");
        csv.AppendLine($"LiveFuturesRecommended,{s.LiveFuturesRecommended}");
        csv.AppendLine(NormalizedRiskPnlModule.SummaryCsvHeaderSuffix());
        csv.AppendLine(NormalizedRiskPnlModule.SummaryCsvValues(s.NormalizedRisk));
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "current-opportunity-scanner-v1-summary.csv"),
            csv.ToString(),
            cancellationToken);
    }

    private static async Task WriteCandidateFilesAsync(
        string outputDirectory,
        string filePrefix,
        IReadOnlyList<CurrentOpportunityScannerV1CandidateRow> rows,
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

    private static string FormatCandidateCsvRow(CurrentOpportunityScannerV1CandidateRow r)
        => string.Join(",",
            Csv(r.CandidateKey), Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction),
            r.TargetPercent, r.StopPercent, Csv(r.ActivationRule),
            Csv(r.ResearchPromotionStatus), Csv(r.CurrentExecutionReadiness), r.ResearchScore,
            r.TradeCount, r.NetModerate, r.NetStressPlus, r.WinRate, r.ProfitFactor,
            r.SparseWarning, r.OverfitWarning, r.SingleClusterWarning, Dt(r.LatestCandleUtc),
            r.ActivationCurrentlyPassed, Csv(r.ActivationFailureReason),
            r.BaseEntrySignalPresentNow, Csv(r.EntrySignalFailureReason),
            r.WouldBeShadowActionable, r.WouldPlaceOrder, Csv(r.ReasonIfBlocked),
            Csv(r.BlockedReasonCategory), r.NormalizedNetPer100Usdt, r.AssignedShadowNotionalUsdt,
            Csv(r.RiskStatus), Csv(r.PrecisionStatus), r.AlmostActionable,
            NormalizedRiskPnlModule.SummaryCsvValues(r.NormalizedRisk));

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
