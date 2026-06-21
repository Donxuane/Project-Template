using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public static class MetaStrategyResearchReportWriter
{
    public static async Task WriteAsync(
        string outputDirectory,
        MetaStrategyResearchDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        await WriteJsonAsync(outputDirectory, "meta-strategy-family-summary.json", diagnostics.StrategyFamilySummary, cancellationToken);
        await WriteJsonAsync(outputDirectory, "meta-symbol-interval-summary.json", diagnostics.SymbolIntervalSummary, cancellationToken);
        await WriteJsonAsync(outputDirectory, "meta-feature-bucket-summary.json", diagnostics.FeatureBucketSummary, cancellationToken);
        await WriteJsonAsync(outputDirectory, "meta-exit-reason-summary.json", diagnostics.ExitReasonSummary, cancellationToken);
        await WriteJsonAsync(outputDirectory, "meta-best-subsets.json", diagnostics.BestSubsets, cancellationToken);
        await WriteJsonAsync(outputDirectory, "meta-overfit-warnings.json", diagnostics.OverfitWarnings, cancellationToken);
        await WriteJsonAsync(outputDirectory, "meta-entry-time-rule-discovery.json", diagnostics.EntryTimeRuleDiscovery, cancellationToken);
        await WriteJsonAsync(outputDirectory, "meta-outcome-diagnostic-rule-discovery.json", diagnostics.OutcomeDiagnosticRuleDiscovery, cancellationToken);
        await WriteJsonAsync(outputDirectory, "meta-rule-discovery-deprecated.json", new
        {
            Deprecated = true,
            Message = "Use meta-entry-time-rule-discovery.json (tradable) and meta-outcome-diagnostic-rule-discovery.json (explanatory only).",
            EntryTimeRuleDiscovery = diagnostics.EntryTimeRuleDiscovery,
            OutcomeDiagnosticRuleDiscovery = diagnostics.OutcomeDiagnosticRuleDiscovery
        }, cancellationToken);
        await WriteJsonAsync(outputDirectory, "meta-research-answers.json", diagnostics.ResearchAnswers, cancellationToken);
        await WriteJsonAsync(outputDirectory, "meta-import-report.json", diagnostics.ImportReport, cancellationToken);

        var executed = diagnostics.Records.Where(r => r.CandidateWasExecuted).ToArray();
        await WriteJsonAsync(outputDirectory, "meta-unified-research-dataset.json", executed, cancellationToken);

        await WriteStrategyFamilyCsvAsync(outputDirectory, diagnostics.StrategyFamilySummary, cancellationToken);
        await WriteSymbolIntervalCsvAsync(outputDirectory, diagnostics.SymbolIntervalSummary, cancellationToken);
        await WriteFeatureBucketCsvAsync(outputDirectory, diagnostics.FeatureBucketSummary, cancellationToken);
        await WriteExitReasonCsvAsync(outputDirectory, diagnostics.ExitReasonSummary, cancellationToken);
        await WriteBestSubsetsCsvAsync(outputDirectory, diagnostics.BestSubsets, cancellationToken);
        await WriteOverfitWarningsCsvAsync(outputDirectory, diagnostics.OverfitWarnings, cancellationToken);
        await WriteRuleDiscoveryCsvAsync(outputDirectory, "meta-entry-time-rule-discovery.csv", diagnostics.EntryTimeRuleDiscovery, cancellationToken);
        await WriteRuleDiscoveryCsvAsync(outputDirectory, "meta-outcome-diagnostic-rule-discovery.csv", diagnostics.OutcomeDiagnosticRuleDiscovery, cancellationToken);
        await WriteUnifiedDatasetCsvAsync(outputDirectory, executed, cancellationToken);
    }

    private static async Task WriteJsonAsync<T>(string dir, string fileName, T value, CancellationToken ct)
    {
        await File.WriteAllTextAsync(
            Path.Combine(dir, fileName),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
            ct);
    }

    private static async Task WriteStrategyFamilyCsvAsync(
        string dir,
        IReadOnlyList<MetaStrategyFamilySummaryRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("strategyFamily,trades,executedCandidates,blockedCandidates,netWinners,netPnlQuote,netPerTrade,netWinnerRate,stopLossRate,timeStopRate,profitExitRate,windowCount");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.StrategyFamily),
                row.Trades,
                row.ExecutedCandidates,
                row.BlockedCandidates,
                row.NetWinners,
                F(row.NetPnlQuote),
                F(row.NetPerTrade),
                F(row.NetWinnerRate),
                F(row.StopLossRate),
                F(row.TimeStopRate),
                F(row.ProfitExitRate),
                row.WindowCount));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "meta-strategy-family-summary.csv"), sb.ToString(), ct);
    }

    private static async Task WriteSymbolIntervalCsvAsync(
        string dir,
        IReadOnlyList<MetaSymbolIntervalSummaryRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("strategyFamily,symbol,interval,trades,netWinners,netPnlQuote,netPerTrade,netWinnerRate,stopLossRate,windowCount,meetsMinimumSample");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.StrategyFamily),
                Escape(row.Symbol),
                Escape(row.Interval),
                row.Trades,
                row.NetWinners,
                F(row.NetPnlQuote),
                F(row.NetPerTrade),
                F(row.NetWinnerRate),
                F(row.StopLossRate),
                row.WindowCount,
                row.MeetsMinimumSample));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "meta-symbol-interval-summary.csv"), sb.ToString(), ct);
    }

    private static async Task WriteFeatureBucketCsvAsync(
        string dir,
        IReadOnlyList<MetaFeatureBucketSummaryRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("featureName,bucketLabel,bucketIndex,bucketMin,bucketMax,trades,netPnlQuote,netPerTrade,netWinnerRate,profitExitRate,stopLossRate,timeStopRate,medianMfePercent,medianMaePercent");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.FeatureName),
                Escape(row.BucketLabel),
                row.BucketIndex,
                F(row.BucketMin),
                F(row.BucketMax),
                row.Trades,
                F(row.NetPnlQuote),
                F(row.NetPerTrade),
                F(row.NetWinnerRate),
                F(row.ProfitExitRate),
                F(row.StopLossRate),
                F(row.TimeStopRate),
                F(row.MedianMfePercent),
                F(row.MedianMaePercent)));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "meta-feature-bucket-summary.csv"), sb.ToString(), ct);
    }

    private static async Task WriteExitReasonCsvAsync(
        string dir,
        IReadOnlyList<MetaExitReasonSummaryRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("strategyFamily,exitReason,count,netPnlQuote,grossPnlQuote,shareOfExits");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.StrategyFamily),
                Escape(row.ExitReason),
                row.Count,
                F(row.NetPnlQuote),
                F(row.GrossPnlQuote),
                F(row.ShareOfExits)));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "meta-exit-reason-summary.csv"), sb.ToString(), ct);
    }

    private static async Task WriteBestSubsetsCsvAsync(
        string dir,
        IReadOnlyList<MetaBestSubsetRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("subsetKey,ruleDescription,trades,windowCount,windowsRepresented,netPnlQuote,netPerTrade,netWinnerRate,stopLossRate,meetsRobustnessCriteria,verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.SubsetKey),
                Escape(row.RuleDescription),
                row.Trades,
                row.WindowCount,
                Escape(string.Join("|", row.WindowsRepresented)),
                F(row.NetPnlQuote),
                F(row.NetPerTrade),
                F(row.NetWinnerRate),
                F(row.StopLossRate),
                row.MeetsRobustnessCriteria,
                Escape(row.Verdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "meta-best-subsets.csv"), sb.ToString(), ct);
    }

    private static async Task WriteRuleDiscoveryCsvAsync(
        string dir,
        string fileName,
        IReadOnlyList<MetaRuleDiscoveryRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ruleGroup,ruleDescription,featuresUsed,usesFutureInformation,tradableRule,trainWindows,holdoutWindows,trainTrades,holdoutTrades,trainNetPnlQuote,holdoutNetPnlQuote,trainNetPerTrade,holdoutNetPerTrade,verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.RuleGroup),
                Escape(row.RuleDescription),
                Escape(string.Join("|", row.FeaturesUsed)),
                row.UsesFutureInformation,
                row.TradableRule,
                Escape(row.TrainWindows),
                Escape(row.HoldoutWindows),
                row.TrainTrades,
                row.HoldoutTrades,
                F(row.TrainNetPnlQuote),
                F(row.HoldoutNetPnlQuote),
                F(row.TrainNetPerTrade),
                F(row.HoldoutNetPerTrade),
                Escape(row.Verdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, fileName), sb.ToString(), ct);
    }

    private static async Task WriteOverfitWarningsCsvAsync(
        string dir,
        IReadOnlyList<MetaOverfitWarningRow> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("warningType,subsetKey,trades,windowCount,netPnlQuote,message");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.WarningType),
                Escape(row.SubsetKey),
                row.Trades,
                row.WindowCount,
                F(row.NetPnlQuote),
                Escape(row.Message)));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "meta-overfit-warnings.csv"), sb.ToString(), ct);
    }

    private static async Task WriteUnifiedDatasetCsvAsync(
        string dir,
        IReadOnlyList<MetaStrategyResearchRecord> rows,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("strategyFamily,profileName,symbol,interval,windowLabel,timeUtc,entryPrice,exitReason,grossPnlQuote,netPnlQuote,isNetWinner,candidateWasExecuted,rejectionReason,expectedMovePercent,requiredGrossMovePercent,stopDistancePercent,rewardRisk,mfePercent,maePercent,forwardMfe60Percent,forwardMae60Percent,timeToTargetMinutes,durationMinutes,volatilityRegime,trendStrengthPercent,shortMaSlopePercent,rangeWidthPercent,breakoutBodyStrengthPercent,volumeExpansionRatio,atrExpansionRatio,distanceToInvalidationPercent,stopToLockRatio,targetModelName,exitPolicyName,sourceDirectory");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Escape(row.StrategyFamily),
                Escape(row.ProfileName),
                Escape(row.Symbol),
                Escape(row.Interval),
                Escape(row.WindowLabel),
                Escape(row.TimeUtc.ToString("O")),
                F(row.EntryPrice),
                Escape(row.ExitReason),
                F(row.GrossPnlQuote),
                F(row.NetPnlQuote),
                row.IsNetWinner,
                row.CandidateWasExecuted,
                Escape(row.RejectionReason),
                F(row.ExpectedMovePercent),
                F(row.RequiredGrossMovePercent),
                F(row.StopDistancePercent),
                F(row.RewardRisk),
                F(row.MfePercent),
                F(row.MaePercent),
                F(row.ForwardMfe60Percent),
                F(row.ForwardMae60Percent),
                row.TimeToTargetMinutes,
                F(row.DurationMinutes),
                Escape(row.VolatilityRegime),
                F(row.TrendStrengthPercent),
                F(row.ShortMaSlopePercent),
                F(row.RangeWidthPercent),
                F(row.BreakoutBodyStrengthPercent),
                F(row.VolumeExpansionRatio),
                F(row.AtrExpansionRatio),
                F(row.DistanceToInvalidationPercent),
                F(row.StopToLockRatio),
                Escape(row.TargetModelName),
                Escape(row.ExitPolicyName),
                Escape(row.SourceDirectory)));
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "meta-unified-research-dataset.csv"), sb.ToString(), ct);
    }

    private static string F(decimal? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string F(decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        return value;
    }
}
