using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public static class Sol30mNearMissConversionHistoryStudyReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const string EventHeader =
        "EventTimeUtc,Symbol,Interval,Direction,ActivationPassed,NearMissClassification,FailedCondition,DistanceToEntryPercent,DistanceBucket,LatestClose,RecentHigh,RecentLow,AtrPercent,ElevatedVolPassed,CooldownClear,NoOpenTradeOverlap,ConvertedToExactEntry,ConversionTimeUtc,MinutesToConversion,CandlesToConversion,MaxDistanceBeforeConversion,DidPriceMoveTowardEntry,DidPriceMoveAwayFromEntry,ConversionEntryPrice,ConversionExitTimeUtc,ConversionExitReason,ConversionNetModerate,ConversionNetStressPlus,IsWinnerModerate,IsWinnerStressPlus,ConvertedWithin1Candle,ConvertedWithin2Candles,ConvertedWithin4Candles,ConvertedWithin8Candles,ConvertedWithin24h";

    public static async Task WriteAsync(
        string outputDirectory,
        Sol30mNearMissConversionHistoryStudyResult result,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var prefix = Sol30mNearMissConversionHistoryStudyCatalog.OutputPrefix;
        var warning = Sol30mNearMissConversionHistorySummaryRow.DiagnosticWarning;

        await WriteSummaryAsync(outputDirectory, prefix, result, warning, cancellationToken);
        await WriteEventsAsync(outputDirectory, $"{prefix}-events", result.Events, warning, cancellationToken);
        await WriteEventsAsync(outputDirectory, $"{prefix}-conversions", result.Conversions, warning, cancellationToken);
        await WriteEventsAsync(outputDirectory, $"{prefix}-nonconversions", result.NonConversions, warning, cancellationToken);
    }

    private static async Task WriteSummaryAsync(
        string outputDirectory,
        string prefix,
        Sol30mNearMissConversionHistoryStudyResult result,
        string warning,
        CancellationToken cancellationToken)
    {
        var s = result.Summary;

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, $"{prefix}-summary.json"),
            JsonSerializer.Serialize(new
            {
                warning,
                studyNote = "Historical near-miss conversion is research only and is not forward proof for the current live near-miss.",
                s.RunAtUtc,
                s.StudyStartUtc,
                s.StudyEndUtc,
                s.CandidateKey,
                s.ActivationRule,
                s.CurrentNearMissDistancePercent,
                s.TotalNearMissEvents,
                s.OneConditionAwayEvents,
                s.ConvertedWithin1Candle,
                s.ConvertedWithin2Candles,
                s.ConvertedWithin4Candles,
                s.ConvertedWithin8Candles,
                s.ConvertedWithin24h,
                s.ConversionRateWithin4Candles,
                s.ConversionRateWithin24h,
                s.ConvertedTradeCount,
                s.ConvertedNetModerate,
                s.ConvertedNetStressPlus,
                s.ConvertedWinRate,
                s.ConvertedProfitFactor,
                s.NonConvertedCount,
                s.AverageDistanceToEntry,
                s.MedianDistanceToEntry,
                s.BestDistanceBucket,
                s.WorstDistanceBucket,
                s.CurrentNearMissSimilarityBucket,
                s.Recommendation,
                s.CompactSummaryLine,
                s.PlainEnglish,
                s.BacktestOnly,
                s.RealOrdersPlaced,
                s.LiveFuturesRecommended,
                s.NearMissConversionIsNotForwardProof
            }, JsonOptions),
            cancellationToken);

        var csv = new StringBuilder();
        csv.AppendLine($"Warning,{Csv(warning)}");
        csv.AppendLine($"RunAtUtc,{Dt(s.RunAtUtc)}");
        csv.AppendLine($"StudyStartUtc,{Dt(s.StudyStartUtc)}");
        csv.AppendLine($"StudyEndUtc,{Dt(s.StudyEndUtc)}");
        csv.AppendLine($"CandidateKey,{Csv(s.CandidateKey)}");
        csv.AppendLine($"TotalNearMissEvents,{s.TotalNearMissEvents}");
        csv.AppendLine($"ConvertedWithin24h,{s.ConvertedWithin24h}");
        csv.AppendLine($"ConversionRateWithin4Candles,{s.ConversionRateWithin4Candles}");
        csv.AppendLine($"ConversionRateWithin24h,{s.ConversionRateWithin24h}");
        csv.AppendLine($"ConvertedNetStressPlus,{s.ConvertedNetStressPlus}");
        csv.AppendLine($"ConvertedWinRate,{s.ConvertedWinRate}");
        csv.AppendLine($"ConvertedProfitFactor,{s.ConvertedProfitFactor}");
        csv.AppendLine($"AverageDistanceToEntry,{s.AverageDistanceToEntry}");
        csv.AppendLine($"MedianDistanceToEntry,{s.MedianDistanceToEntry}");
        csv.AppendLine($"BestDistanceBucket,{Csv(s.BestDistanceBucket)}");
        csv.AppendLine($"WorstDistanceBucket,{Csv(s.WorstDistanceBucket)}");
        csv.AppendLine($"CurrentNearMissSimilarityBucket,{Csv(s.CurrentNearMissSimilarityBucket)}");
        csv.AppendLine($"Recommendation,{Csv(s.Recommendation)}");
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{prefix}-summary.csv"), csv.ToString(), cancellationToken);

        var txt = new StringBuilder();
        txt.AppendLine(warning);
        txt.AppendLine();
        txt.AppendLine(s.CompactSummaryLine);
        txt.AppendLine($"RunAtUtc: {s.RunAtUtc:o}");
        txt.AppendLine($"Study window: {s.StudyStartUtc:o} to {s.StudyEndUtc:o}");
        txt.AppendLine($"Recommendation: {s.Recommendation}");
        txt.AppendLine();
        txt.AppendLine("1. Does SOL 30m near-miss usually convert into exact entry?");
        txt.AppendLine($"   {s.PlainEnglish.DoesNearMissUsuallyConvert}");
        txt.AppendLine();
        txt.AppendLine("2. How fast does it convert when it works?");
        txt.AppendLine($"   {s.PlainEnglish.HowFastDoesItConvert}");
        txt.AppendLine();
        txt.AppendLine("3. Are converted entries profitable under stress-plus?");
        txt.AppendLine($"   {s.PlainEnglish.AreConvertedEntriesProfitableStressPlus}");
        txt.AppendLine();
        txt.AppendLine("4. Is the current 0.7%–0.8% distance bucket good or bad?");
        txt.AppendLine($"   {s.PlainEnglish.IsCurrentDistanceBucketGoodOrBad}");
        txt.AppendLine();
        txt.AppendLine("5. Should we keep watcher running?");
        txt.AppendLine($"   {s.PlainEnglish.ShouldKeepWatcherRunning}");
        txt.AppendLine();
        txt.AppendLine("6. Should we trade near-miss before exact entry?");
        txt.AppendLine($"   {s.PlainEnglish.ShouldTradeNearMissBeforeExactEntry}");
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{prefix}-summary.txt"), txt.ToString(), cancellationToken);
    }

    private static async Task WriteEventsAsync(
        string outputDirectory,
        string filePrefix,
        IReadOnlyList<Sol30mNearMissConversionHistoryEventRow> events,
        string warning,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, $"{filePrefix}.json"),
            JsonSerializer.Serialize(new { warning, events }, JsonOptions),
            cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine(EventHeader);
        foreach (var e in events)
        {
            sb.AppendLine(string.Join(",",
                Dt(e.EventTimeUtc), Csv(e.Symbol), Csv(e.Interval), Csv(e.Direction),
                e.ActivationPassed, Csv(e.NearMissClassification), Csv(e.FailedCondition),
                e.DistanceToEntryPercent, Csv(e.DistanceBucket), e.LatestClose, e.RecentHigh, e.RecentLow,
                e.AtrPercent, e.ElevatedVolPassed, e.CooldownClear, e.NoOpenTradeOverlap,
                e.ConvertedToExactEntry, DtNullable(e.ConversionTimeUtc), e.MinutesToConversion?.ToString(CultureInfo.InvariantCulture) ?? "",
                e.CandlesToConversion?.ToString(CultureInfo.InvariantCulture) ?? "",
                e.MaxDistanceBeforeConversion?.ToString(CultureInfo.InvariantCulture) ?? "",
                e.DidPriceMoveTowardEntry, e.DidPriceMoveAwayFromEntry,
                e.ConversionEntryPrice?.ToString(CultureInfo.InvariantCulture) ?? "",
                DtNullable(e.ConversionExitTimeUtc), Csv(e.ConversionExitReason),
                e.ConversionNetModerate?.ToString(CultureInfo.InvariantCulture) ?? "",
                e.ConversionNetStressPlus?.ToString(CultureInfo.InvariantCulture) ?? "",
                e.IsWinnerModerate?.ToString() ?? "", e.IsWinnerStressPlus?.ToString() ?? "",
                e.ConvertedWithin1Candle, e.ConvertedWithin2Candles, e.ConvertedWithin4Candles,
                e.ConvertedWithin8Candles, e.ConvertedWithin24h));
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
