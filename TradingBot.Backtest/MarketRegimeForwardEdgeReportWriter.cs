using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed class MarketRegimeForwardEdgeReportWriter(string outputDirectory)
{
    public async Task WriteAsync(
        IReadOnlyList<MarketRegimeForwardEdgeSummaryRow> summary,
        IReadOnlyList<SymbolIntervalEdgeRankingRow> symbolIntervalRanking,
        IReadOnlyList<RegimeBucketEdgeRankingRow> regimeBucketRanking,
        IReadOnlyList<SessionEdgeRankingRow> sessionRanking,
        IReadOnlyList<TargetBeforeStopMatrixRow> targetBeforeStopMatrix,
        IReadOnlyList<ReachabilityResearchAnswer> researchAnswers,
        IReadOnlyList<BtcContextEdgeRankingRow>? btcContextRanking,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        await WriteJsonAsync("market-regime-forward-edge-summary.json", summary, cancellationToken);
        await WriteJsonAsync("symbol-interval-edge-ranking.json", symbolIntervalRanking, cancellationToken);
        await WriteJsonAsync("regime-bucket-edge-ranking.json", regimeBucketRanking, cancellationToken);
        await WriteJsonAsync("session-edge-ranking.json", sessionRanking, cancellationToken);
        await WriteJsonAsync("target-before-stop-matrix.json", targetBeforeStopMatrix, cancellationToken);
        await WriteJsonAsync("market-regime-research-answers.json", researchAnswers, cancellationToken);
        if (btcContextRanking is { Count: > 0 })
            await WriteJsonAsync("market-regime-btc-context-ranking.json", btcContextRanking, cancellationToken);

        await WriteSummaryCsvAsync(summary, cancellationToken);
        await WriteSymbolIntervalCsvAsync(symbolIntervalRanking, cancellationToken);
        await WriteRegimeBucketCsvAsync(regimeBucketRanking, cancellationToken);
        await WriteSessionCsvAsync(sessionRanking, cancellationToken);
        await WriteTargetMatrixCsvAsync(targetBeforeStopMatrix, cancellationToken);
        if (btcContextRanking is { Count: > 0 })
            await WriteBtcContextCsvAsync(btcContextRanking, cancellationToken);
    }

    private async Task WriteJsonAsync<T>(string fileName, T value, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, fileName),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private async Task WriteSummaryCsvAsync(IReadOnlyList<MarketRegimeForwardEdgeSummaryRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("windowLabel,symbol,interval,sampleCount,medianForwardMfePercent,medianForwardMaePercent,medianExpectedNetAfterCostPercent,target050BeforeStop050Rate,longEdgeScore,verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.WindowLabel, row.Symbol, row.Interval, row.SampleCount,
                row.MedianForwardMfePercent.ToString(CultureInfo.InvariantCulture),
                row.MedianForwardMaePercent.ToString(CultureInfo.InvariantCulture),
                row.MedianExpectedNetAfterCostPercent.ToString(CultureInfo.InvariantCulture),
                row.Target050BeforeStop050Rate.ToString(CultureInfo.InvariantCulture),
                row.LongEdgeScore.ToString(CultureInfo.InvariantCulture),
                row.Verdict));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "market-regime-forward-edge-summary.csv"), sb.ToString(), ct);
    }

    private async Task WriteSymbolIntervalCsvAsync(IReadOnlyList<SymbolIntervalEdgeRankingRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("rank,windowLabel,symbol,interval,sampleCount,medianForwardMfePercent,medianForwardMaePercent,medianExpectedNetAfterCostPercent,target050BeforeStop050Rate,longEdgeScore,verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.Rank, row.WindowLabel, row.Symbol, row.Interval, row.SampleCount,
                row.MedianForwardMfePercent.ToString(CultureInfo.InvariantCulture),
                row.MedianForwardMaePercent.ToString(CultureInfo.InvariantCulture),
                row.MedianExpectedNetAfterCostPercent.ToString(CultureInfo.InvariantCulture),
                row.Target050BeforeStop050Rate.ToString(CultureInfo.InvariantCulture),
                row.LongEdgeScore.ToString(CultureInfo.InvariantCulture),
                row.Verdict));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "symbol-interval-edge-ranking.csv"), sb.ToString(), ct);
    }

    private async Task WriteRegimeBucketCsvAsync(IReadOnlyList<RegimeBucketEdgeRankingRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("rank,windowLabel,bucketType,bucketLabel,symbol,interval,sampleCount,medianForwardMfePercent,medianForwardMaePercent,medianExpectedNetAfterCostPercent,target050BeforeStop050Rate,longEdgeScore,verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.Rank, row.WindowLabel, Escape(row.BucketType), Escape(row.BucketLabel),
                row.Symbol, row.Interval, row.SampleCount,
                row.MedianForwardMfePercent.ToString(CultureInfo.InvariantCulture),
                row.MedianForwardMaePercent.ToString(CultureInfo.InvariantCulture),
                row.MedianExpectedNetAfterCostPercent.ToString(CultureInfo.InvariantCulture),
                row.Target050BeforeStop050Rate.ToString(CultureInfo.InvariantCulture),
                row.LongEdgeScore.ToString(CultureInfo.InvariantCulture),
                row.Verdict));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "regime-bucket-edge-ranking.csv"), sb.ToString(), ct);
    }

    private async Task WriteSessionCsvAsync(IReadOnlyList<SessionEdgeRankingRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("rank,windowLabel,sessionBucket,hourOfDayUtc,symbol,interval,sampleCount,medianExpectedNetAfterCostPercent,target050BeforeStop050Rate,longEdgeScore,verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.Rank, row.WindowLabel, row.SessionBucket, row.HourOfDayUtc,
                row.Symbol, row.Interval, row.SampleCount,
                row.MedianExpectedNetAfterCostPercent.ToString(CultureInfo.InvariantCulture),
                row.Target050BeforeStop050Rate.ToString(CultureInfo.InvariantCulture),
                row.LongEdgeScore.ToString(CultureInfo.InvariantCulture),
                row.Verdict));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "session-edge-ranking.csv"), sb.ToString(), ct);
    }

    private async Task WriteTargetMatrixCsvAsync(IReadOnlyList<TargetBeforeStopMatrixRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("windowLabel,symbol,interval,targetPercent,stopPercent,sampleCount,targetBeforeStopCount,stopBeforeTargetCount,unresolvedCount,targetBeforeStopRate,expectedNetAfterCostPercent");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.WindowLabel, row.Symbol, row.Interval,
                row.TargetPercent.ToString(CultureInfo.InvariantCulture),
                row.StopPercent.ToString(CultureInfo.InvariantCulture),
                row.SampleCount, row.TargetBeforeStopCount, row.StopBeforeTargetCount, row.UnresolvedCount,
                row.TargetBeforeStopRate.ToString(CultureInfo.InvariantCulture),
                row.ExpectedNetAfterCostPercent.ToString(CultureInfo.InvariantCulture)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "target-before-stop-matrix.csv"), sb.ToString(), ct);
    }

    private async Task WriteBtcContextCsvAsync(IReadOnlyList<BtcContextEdgeRankingRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("rank,windowLabel,symbol,interval,btcContextBucketType,btcContextBucketLabel,sampleCount,medianForwardMfePercent,medianForwardMaePercent,medianExpectedNetAfterCostPercent,target050BeforeStop050Rate,longEdgeScore,verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.Rank, row.WindowLabel, row.Symbol, row.Interval,
                Escape(row.BtcContextBucketType), Escape(row.BtcContextBucketLabel),
                row.SampleCount,
                row.MedianForwardMfePercent.ToString(CultureInfo.InvariantCulture),
                row.MedianForwardMaePercent.ToString(CultureInfo.InvariantCulture),
                row.MedianExpectedNetAfterCostPercent.ToString(CultureInfo.InvariantCulture),
                row.Target050BeforeStop050Rate.ToString(CultureInfo.InvariantCulture),
                row.LongEdgeScore.ToString(CultureInfo.InvariantCulture),
                row.Verdict));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "market-regime-btc-context-ranking.csv"), sb.ToString(), ct);
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (value.Contains(',') || value.Contains('"'))
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        return value;
    }
}
