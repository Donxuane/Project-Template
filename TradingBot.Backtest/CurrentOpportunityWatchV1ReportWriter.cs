using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public static class CurrentOpportunityWatchV1ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const string WatchlistHeader =
        "WatchRank,WatchKind,CandidateKey,Symbol,Interval,Direction,TargetPercent,StopPercent,ActivationRule,ResearchScore,NetModerate,NetStressPlus,ActivationCurrentlyPassed,BaseEntrySignalPresentNow,WouldBeShadowActionable,NearMissClassification,DistanceToEntryPercent,FailedCondition,SparseWarning,OverfitWarning,SingleClusterWarning,IsSolUsdt30mShort,IsFixedFrequencyPromoted,FixedFrequencyRecommendation,ExactEntryCountInsideActivatedWindows,ExactEntriesPerDay,LastExactEntryUtc,DaysSinceLastExactEntry,WatchReason";

    private static readonly string FixedFrequencyHeader =
        "WatchPriority,CandidateKey,Symbol,Interval,Direction,TargetPercent,StopPercent,ActivationRule,FixedFrequencyRecommendation,ExactEntryCountInsideActivatedWindows,ExactEntriesPerDay,LastExactEntryUtc,DaysSinceLastExactEntry,NetModerate,NetStressPlus,WinRate,ProfitFactor,ResearchPromotionStatus,CurrentExecutionReadiness,CurrentBottleneckClassification,CurrentBottleneckRecommendation,ActivationCurrentlyPassed,BaseEntrySignalPresentNow,ActionableShadow,WatchPriorityTier,WatchReason,WatchStatus,WouldPlaceOrder,CanEnterTestnetOrderMode," + NormalizedRiskPnlModule.SummaryCsvHeaderSuffix();

    private const string HistoryHeader =
        "RunAtUtc,EvaluatedAtUtc,WatchStatus,EvaluatedCandidateCount,ActivationPassedCount,EntrySignalPresentCount,ActionableShadowCount,TopNearMissCount,TopWatchCandidate,ExactEntrySignalCandidate";

    public static async Task WriteAsync(
        string outputDirectory,
        CurrentOpportunityWatchV1RunResult result,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var warning = CurrentOpportunityWatchV1StatusRow.DiagnosticWarning;

        await WriteStatusAsync(outputDirectory, result, warning, cancellationToken);
        await WriteHistoryAsync(outputDirectory, result, warning, cancellationToken);
        await WriteWatchlistFilesAsync(
            outputDirectory,
            "current-opportunity-watch-v1-top-watchlist",
            result.TopWatchlist,
            warning,
            cancellationToken);
        await WriteWatchlistFilesAsync(
            outputDirectory,
            "current-opportunity-watch-v1-exact-entry-signals",
            result.ExactEntrySignals,
            warning,
            cancellationToken);
        await WriteWatchlistFilesAsync(
            outputDirectory,
            "current-opportunity-watch-v1-near-misses",
            result.NearMisses,
            warning,
            cancellationToken);
        await WriteFixedFrequencyFilesAsync(
            outputDirectory,
            "current-opportunity-watch-v1-fixed-frequency-watchlist",
            result.FixedFrequencyWatchlist,
            warning,
            cancellationToken);
    }

    private static async Task WriteFixedFrequencyFilesAsync(
        string outputDirectory,
        string filePrefix,
        IReadOnlyList<CurrentOpportunityWatchV1FixedFrequencyRow> rows,
        string warning,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, $"{filePrefix}.json"),
            JsonSerializer.Serialize(new
            {
                warning,
                note = "Fixed-frequency promoted candidates from the corrected cross-candidate exact-entry frequency study. Historical frequency is research only, not forward proof. Never places orders; CanEnterTestnetOrderMode stays false in diagnostic mode.",
                watchlist = rows
            }, JsonOptions),
            cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine($"Warning,{Csv(warning)}");
        sb.AppendLine(FixedFrequencyHeader);
        foreach (var r in rows)
            sb.AppendLine(FormatFixedFrequencyCsvRow(r));

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, $"{filePrefix}.csv"),
            sb.ToString(),
            cancellationToken);
    }

    private static string FormatFixedFrequencyCsvRow(CurrentOpportunityWatchV1FixedFrequencyRow r)
        => string.Join(",",
            r.WatchPriority, Csv(r.CandidateKey), Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction),
            r.TargetPercent, r.StopPercent, Csv(r.ActivationRule), Csv(r.FixedFrequencyRecommendation),
            r.ExactEntryCountInsideActivatedWindows, r.ExactEntriesPerDay,
            DtNullable(r.LastExactEntryUtc),
            r.DaysSinceLastExactEntry?.ToString(CultureInfo.InvariantCulture) ?? "",
            r.NetModerate, r.NetStressPlus, r.WinRate, r.ProfitFactor,
            Csv(r.ResearchPromotionStatus), Csv(r.CurrentExecutionReadiness),
            Csv(r.CurrentBottleneckClassification), Csv(r.CurrentBottleneckRecommendation),
            r.ActivationCurrentlyPassed, r.BaseEntrySignalPresentNow, r.ActionableShadow,
            Csv(r.WatchPriorityTier), Csv(r.WatchReason), Csv(r.WatchStatus),
            r.WouldPlaceOrder, r.CanEnterTestnetOrderMode,
            NormalizedRiskPnlModule.SummaryCsvValues(r.NormalizedRisk));

    private static async Task WriteStatusAsync(
        string outputDirectory,
        CurrentOpportunityWatchV1RunResult result,
        string warning,
        CancellationToken cancellationToken)
    {
        var s = result.Status;

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "current-opportunity-watch-v1-status.json"),
            JsonSerializer.Serialize(new
            {
                warning,
                watchNote = "Shadow/diagnostic only. Uses confirmed closed candles. Never places orders.",
                s.RunAtUtc,
                s.EvaluatedAtUtc,
                s.WatchStatus,
                s.CompactSummaryLine,
                s.EvaluatedCandidateCount,
                s.ActivationPassedCount,
                s.EntrySignalPresentCount,
                s.ActionableShadowCount,
                s.TopNearMissCount,
                s.TopWatchCandidate,
                s.TopWatchSymbol,
                s.TopWatchInterval,
                s.TopWatchDirection,
                s.TopWatchActivationRule,
                s.TopWatchDistanceToEntryPercent,
                s.TopWatchFailedCondition,
                s.ExactEntrySignalCandidate,
                s.ExactEntryAppearedNote,
                s.DataLastCandleUtc,
                s.EvalAdvancedSincePreviousCycle,
                s.CycleNumber,
                s.UsesConfirmedClosedCandlesOnly,
                s.WouldPlaceOrder,
                s.RealOrdersPlaced,
                s.LiveFuturesRecommended,
                s.FixedFrequencyPromotedCount,
                s.FixedFrequencyWatchedCount,
                s.FixedFrequencyBlockedByReadinessCount,
                s.FixedFrequencyNeedsIncubationCount,
                s.FixedFrequencyExactEntryPresent,
                s.FixedFrequencyExactEntryCandidate,
                s.ClosestFixedFrequencyCandidate,
                s.ClosestFixedFrequencyWatchReason,
                s.CountingBugFixed,
                s.CanEnterTestnetOrderMode,
                s.NormalizedRisk,
                s.PlainEnglish
            }, JsonOptions),
            cancellationToken);

        var txt = new StringBuilder();
        txt.AppendLine(warning);
        txt.AppendLine();
        txt.AppendLine(s.CompactSummaryLine);
        txt.AppendLine();
        NormalizedRiskPnlModule.AppendSummaryRiskLines(txt, s.NormalizedRisk);
        txt.AppendLine($"RunAtUtc: {s.RunAtUtc:o}");
        txt.AppendLine($"EvaluatedAtUtc: {s.EvaluatedAtUtc:o}");
        txt.AppendLine($"WatchStatus: {s.WatchStatus}");
        txt.AppendLine(
            $"Evaluated={s.EvaluatedCandidateCount} ActivationPassed={s.ActivationPassedCount} EntryPresent={s.EntrySignalPresentCount} Actionable={s.ActionableShadowCount} TopNearMiss={s.TopNearMissCount}");
        txt.AppendLine($"TopWatchCandidate: {(string.IsNullOrEmpty(s.TopWatchCandidate) ? "(none)" : s.TopWatchCandidate)}");
        if (!string.IsNullOrEmpty(s.ExactEntrySignalCandidate))
            txt.AppendLine($"ExactEntrySignalCandidate: {s.ExactEntrySignalCandidate}");
        if (!string.IsNullOrEmpty(s.ExactEntryAppearedNote))
            txt.AppendLine(s.ExactEntryAppearedNote);
        txt.AppendLine($"UsesConfirmedClosedCandlesOnly={s.UsesConfirmedClosedCandlesOnly}");
        txt.AppendLine($"DataLastCandleUtc: {s.DataLastCandleUtc:o}");
        txt.AppendLine($"CycleNumber={s.CycleNumber} EvalAdvancedSincePreviousCycle={s.EvalAdvancedSincePreviousCycle}");
        txt.AppendLine($"WouldPlaceOrder={s.WouldPlaceOrder} RealOrdersPlaced={s.RealOrdersPlaced} LiveFuturesRecommended={s.LiveFuturesRecommended}");
        txt.AppendLine(
            $"FixedFrequencyPromoted={s.FixedFrequencyPromotedCount} Watched={s.FixedFrequencyWatchedCount} BlockedByReadiness={s.FixedFrequencyBlockedByReadinessCount} NeedsIncubation={s.FixedFrequencyNeedsIncubationCount} CountingBugFixed={s.CountingBugFixed} CanEnterTestnetOrderMode={s.CanEnterTestnetOrderMode}");
        txt.AppendLine();
        txt.AppendLine("Is there an exact entry signal now?");
        txt.AppendLine($"  {s.PlainEnglish.IsExactEntrySignalNow}");
        txt.AppendLine();
        txt.AppendLine("Is there a near-miss worth watching?");
        txt.AppendLine($"  {s.PlainEnglish.IsNearMissWorthWatching}");
        txt.AppendLine();
        txt.AppendLine("Which candidate is closest?");
        txt.AppendLine($"  {s.PlainEnglish.ClosestCandidate}");
        txt.AppendLine();
        txt.AppendLine("What condition is missing?");
        txt.AppendLine($"  {s.PlainEnglish.MissingCondition}");
        txt.AppendLine();
        txt.AppendLine("=== Fixed-frequency promoted candidate watch ===");
        txt.AppendLine();
        txt.AppendLine("Which fixed-frequency candidates are being watched?");
        txt.AppendLine($"  {s.PlainEnglish.WhichFixedFrequencyCandidatesWatched}");
        txt.AppendLine();
        txt.AppendLine("Which one is closest / currently activated?");
        txt.AppendLine($"  {s.PlainEnglish.WhichFixedFrequencyClosestOrActivated}");
        txt.AppendLine();
        txt.AppendLine("Are any current exact entries present?");
        txt.AppendLine($"  {s.PlainEnglish.AnyCurrentExactEntries}");
        txt.AppendLine();
        txt.AppendLine("Are any candidates blocked by current readiness?");
        txt.AppendLine($"  {s.PlainEnglish.AnyBlockedByCurrentReadiness}");
        txt.AppendLine();
        txt.AppendLine("Should we trade now?");
        txt.AppendLine($"  {s.PlainEnglish.ShouldWeTradeNow}");
        txt.AppendLine();
        txt.AppendLine("Should we trade?");
        txt.AppendLine($"  {s.PlainEnglish.ShouldWeTrade}");

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "current-opportunity-watch-v1-status.txt"),
            txt.ToString(),
            cancellationToken);

        var csv = new StringBuilder();
        csv.AppendLine($"Warning,{Csv(warning)}");
        csv.AppendLine($"RunAtUtc,{Dt(s.RunAtUtc)}");
        csv.AppendLine($"EvaluatedAtUtc,{Dt(s.EvaluatedAtUtc)}");
        csv.AppendLine($"WatchStatus,{Csv(s.WatchStatus)}");
        csv.AppendLine($"CompactSummaryLine,{Csv(s.CompactSummaryLine)}");
        csv.AppendLine($"EvaluatedCandidateCount,{s.EvaluatedCandidateCount}");
        csv.AppendLine($"ActivationPassedCount,{s.ActivationPassedCount}");
        csv.AppendLine($"EntrySignalPresentCount,{s.EntrySignalPresentCount}");
        csv.AppendLine($"ActionableShadowCount,{s.ActionableShadowCount}");
        csv.AppendLine($"TopNearMissCount,{s.TopNearMissCount}");
        csv.AppendLine($"TopWatchCandidate,{Csv(s.TopWatchCandidate)}");
        csv.AppendLine($"TopWatchSymbol,{Csv(s.TopWatchSymbol)}");
        csv.AppendLine($"TopWatchInterval,{Csv(s.TopWatchInterval)}");
        csv.AppendLine($"TopWatchDirection,{Csv(s.TopWatchDirection)}");
        csv.AppendLine($"TopWatchActivationRule,{Csv(s.TopWatchActivationRule)}");
        csv.AppendLine($"TopWatchDistanceToEntryPercent,{s.TopWatchDistanceToEntryPercent}");
        csv.AppendLine($"TopWatchFailedCondition,{Csv(s.TopWatchFailedCondition)}");
        csv.AppendLine($"ExactEntrySignalCandidate,{Csv(s.ExactEntrySignalCandidate)}");
        csv.AppendLine($"ExactEntryAppearedNote,{Csv(s.ExactEntryAppearedNote)}");
        csv.AppendLine($"DataLastCandleUtc,{Dt(s.DataLastCandleUtc)}");
        csv.AppendLine($"EvalAdvancedSincePreviousCycle,{s.EvalAdvancedSincePreviousCycle}");
        csv.AppendLine($"CycleNumber,{s.CycleNumber}");
        csv.AppendLine($"UsesConfirmedClosedCandlesOnly,{s.UsesConfirmedClosedCandlesOnly}");
        csv.AppendLine($"WouldPlaceOrder,{s.WouldPlaceOrder}");
        csv.AppendLine($"RealOrdersPlaced,{s.RealOrdersPlaced}");
        csv.AppendLine($"LiveFuturesRecommended,{s.LiveFuturesRecommended}");
        csv.AppendLine($"FixedFrequencyPromotedCount,{s.FixedFrequencyPromotedCount}");
        csv.AppendLine($"FixedFrequencyWatchedCount,{s.FixedFrequencyWatchedCount}");
        csv.AppendLine($"FixedFrequencyBlockedByReadinessCount,{s.FixedFrequencyBlockedByReadinessCount}");
        csv.AppendLine($"FixedFrequencyNeedsIncubationCount,{s.FixedFrequencyNeedsIncubationCount}");
        csv.AppendLine($"FixedFrequencyExactEntryPresent,{s.FixedFrequencyExactEntryPresent}");
        csv.AppendLine($"FixedFrequencyExactEntryCandidate,{Csv(s.FixedFrequencyExactEntryCandidate)}");
        csv.AppendLine($"ClosestFixedFrequencyCandidate,{Csv(s.ClosestFixedFrequencyCandidate)}");
        csv.AppendLine($"ClosestFixedFrequencyWatchReason,{Csv(s.ClosestFixedFrequencyWatchReason)}");
        csv.AppendLine($"CountingBugFixed,{s.CountingBugFixed}");
        csv.AppendLine($"CanEnterTestnetOrderMode,{s.CanEnterTestnetOrderMode}");
        csv.AppendLine(NormalizedRiskPnlModule.SummaryCsvHeaderSuffix());
        csv.AppendLine(NormalizedRiskPnlModule.SummaryCsvValues(s.NormalizedRisk));
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "current-opportunity-watch-v1-status.csv"),
            csv.ToString(),
            cancellationToken);
    }

    private static async Task WriteHistoryAsync(
        string outputDirectory,
        CurrentOpportunityWatchV1RunResult result,
        string warning,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "current-opportunity-watch-v1-history.json"),
            JsonSerializer.Serialize(new { warning, history = result.History }, JsonOptions),
            cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine($"Warning,{Csv(warning)}");
        sb.AppendLine(HistoryHeader);
        foreach (var row in result.History)
        {
            sb.AppendLine(string.Join(",",
                Dt(row.RunAtUtc), Dt(row.EvaluatedAtUtc), Csv(row.WatchStatus),
                row.EvaluatedCandidateCount, row.ActivationPassedCount, row.EntrySignalPresentCount,
                row.ActionableShadowCount, row.TopNearMissCount, Csv(row.TopWatchCandidate),
                Csv(row.ExactEntrySignalCandidate)));
        }

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "current-opportunity-watch-v1-history.csv"),
            sb.ToString(),
            cancellationToken);
    }

    private static async Task WriteWatchlistFilesAsync(
        string outputDirectory,
        string filePrefix,
        IReadOnlyList<CurrentOpportunityWatchV1WatchlistRow> rows,
        string warning,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, $"{filePrefix}.json"),
            JsonSerializer.Serialize(new { warning, watchlist = rows }, JsonOptions),
            cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine($"Warning,{Csv(warning)}");
        sb.AppendLine(WatchlistHeader);
        foreach (var r in rows)
            sb.AppendLine(FormatWatchlistCsvRow(r));

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, $"{filePrefix}.csv"),
            sb.ToString(),
            cancellationToken);
    }

    private static string FormatWatchlistCsvRow(CurrentOpportunityWatchV1WatchlistRow r)
        => string.Join(",",
            r.WatchRank, Csv(r.WatchKind), Csv(r.CandidateKey), Csv(r.Symbol), Csv(r.Interval),
            Csv(r.Direction), r.TargetPercent, r.StopPercent, Csv(r.ActivationRule), r.ResearchScore,
            r.NetModerate, r.NetStressPlus, r.ActivationCurrentlyPassed, r.BaseEntrySignalPresentNow,
            r.WouldBeShadowActionable, Csv(r.NearMissClassification), r.DistanceToEntryPercent,
            Csv(r.FailedCondition), r.SparseWarning, r.OverfitWarning, r.SingleClusterWarning,
            r.IsSolUsdt30mShort, r.IsFixedFrequencyPromoted, Csv(r.FixedFrequencyRecommendation),
            r.ExactEntryCountInsideActivatedWindows, r.ExactEntriesPerDay,
            DtNullable(r.LastExactEntryUtc),
            r.DaysSinceLastExactEntry?.ToString(CultureInfo.InvariantCulture) ?? "",
            Csv(r.WatchReason));

    private static string Dt(DateTime value) => value.ToString("o", CultureInfo.InvariantCulture);

    private static string DtNullable(DateTime? value)
        => value.HasValue ? value.Value.ToString("o", CultureInfo.InvariantCulture) : "";

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
