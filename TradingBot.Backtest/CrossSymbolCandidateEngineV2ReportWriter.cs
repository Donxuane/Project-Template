using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public static class CrossSymbolCandidateEngineV2ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task WriteAsync(string outputDirectory, CrossSymbolCandidateEngineV2RunResult result, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDirectory);
        var warning =
            "Cross-symbol candidate engine V2 is research/shadow only. Discovery/backtest trades are not forward proof. Portfolio metrics use fixed USDT notional sizing and must not be treated as summed 1-base-coin PnL.";

        await WriteSummaryAsync(outputDirectory, result, warning, ct);
        await WriteCandidatesAsync(outputDirectory, result.Candidates, warning, ct);
        await WriteShadowPortfolioAsync(outputDirectory, "cross-symbol-candidate-engine-v2-shadow-portfolio", result.ShadowPortfolio, warning, ct);
        await WriteShadowPortfolioAsync(outputDirectory, "cross-symbol-candidate-engine-v2-execution-ready-portfolio", result.ExecutionReadyPortfolio, warning, ct);
        await WriteRejectionsAsync(outputDirectory, result.Rejections, warning, ct);
    }

    private static async Task WriteSummaryAsync(
        string outputDirectory,
        CrossSymbolCandidateEngineV2RunResult result,
        string warning,
        CancellationToken ct)
    {
        var s = result.Summary;
        var p = result.ShadowPortfolio;
        var e = result.ExecutionReadyPortfolio;

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "cross-symbol-candidate-engine-v2-summary.json"),
            JsonSerializer.Serialize(new
            {
                warning,
                overlayNote = "ResearchPromotionStatus reflects discovery/backtest quality. CurrentExecutionReadiness reflects true forward/shadow reality and is not forward proof.",
                s.RunAtUtc,
                s.V1InputDirectory,
                s.BottleneckAuditDirectory,
                s.CompactSummaryLine,
                s.CandidatesEvaluated,
                s.PromoteToShadowCount,
                s.ResearchPromotedCount,
                s.ExecutableShadowCandidateCount,
                s.CanEnterTestnetOrderModeCount,
                s.BlockedByLookbackStarvationCount,
                s.BlockedByMissingEntrySignalCount,
                s.BlockedByStressNegativeForwardCount,
                s.KeepIncubatingCount,
                s.NeedsMoreDataCount,
                s.ParkCount,
                s.RejectCount,
                s.ShadowPortfolioCandidateCount,
                s.ExecutionReadyPortfolioCandidateCount,
                s.OneCandidatePerSymbol,
                s.MaxShadowCandidates,
                s.MaxTotalShadowNotionalUsdt,
                s.MaxPerCandidateNotionalUsdt,
                s.BacktestOnly,
                s.ShadowDryRunOnly,
                s.RealOrdersPlaced,
                s.LiveFuturesRecommended,
                researchPortfolio = new
                {
                    p.TotalTrades,
                    p.NetPer100Usdt,
                    p.NetPer1000Usdt,
                    p.MaxDrawdownPer1000Usdt,
                    p.WorstDayPer1000Usdt,
                    p.BestDayPer1000Usdt,
                    p.MaxConsecutiveLosses,
                    p.OverlappingSignalCount,
                    p.TotalAssignedNotionalUsdt
                },
                executionReadyPortfolio = new
                {
                    e.PromotedCandidateCount,
                    e.TotalTrades,
                    e.NetPer100Usdt,
                    e.NetPer1000Usdt,
                    e.MaxDrawdownPer1000Usdt,
                    e.WorstDayPer1000Usdt,
                    e.BestDayPer1000Usdt,
                    e.MaxConsecutiveLosses,
                    e.OverlappingSignalCount,
                    e.TotalAssignedNotionalUsdt
                }
            }, JsonOptions),
            ct);

        var sb = new StringBuilder();
        sb.AppendLine(warning);
        sb.AppendLine();
        sb.AppendLine($"RunAtUtc: {s.RunAtUtc:o}");
        sb.AppendLine($"V1InputDirectory: {s.V1InputDirectory}");
        sb.AppendLine($"BottleneckAuditDirectory: {s.BottleneckAuditDirectory ?? "n/a"}");
        sb.AppendLine(s.CompactSummaryLine);
        sb.AppendLine($"CandidatesEvaluated={s.CandidatesEvaluated} ResearchPromoted={s.ResearchPromotedCount} ExecutableNow={s.ExecutableShadowCandidateCount} CanEnterTestnet={s.CanEnterTestnetOrderModeCount}");
        sb.AppendLine($"BlockedByLookback={s.BlockedByLookbackStarvationCount} BlockedByMissingEntry={s.BlockedByMissingEntrySignalCount} BlockedByStressNegativeForward={s.BlockedByStressNegativeForwardCount}");
        sb.AppendLine($"ResearchPortfolio: candidates={s.ShadowPortfolioCandidateCount} trades={p.TotalTrades} netPer1000USDT={p.NetPer1000Usdt}");
        sb.AppendLine($"ExecutionReadyPortfolio: candidates={s.ExecutionReadyPortfolioCandidateCount} trades={e.TotalTrades} netPer1000USDT={e.NetPer1000Usdt}");
        sb.AppendLine($"backtestOnly={s.BacktestOnly} shadowDryRunOnly={s.ShadowDryRunOnly} realOrdersPlaced={s.RealOrdersPlaced} liveFuturesRecommended={s.LiveFuturesRecommended}");
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "cross-symbol-candidate-engine-v2-summary.txt"), sb.ToString(), ct);

        var csv = new StringBuilder();
        csv.AppendLine($"Warning,{Csv(warning)}");
        csv.AppendLine($"RunAtUtc,{Dt(s.RunAtUtc)}");
        csv.AppendLine($"V1InputDirectory,{Csv(s.V1InputDirectory)}");
        csv.AppendLine($"CompactSummaryLine,{Csv(s.CompactSummaryLine)}");
        csv.AppendLine($"CandidatesEvaluated,{s.CandidatesEvaluated}");
        csv.AppendLine($"ResearchPromotedCount,{s.ResearchPromotedCount}");
        csv.AppendLine($"ExecutableShadowCandidateCount,{s.ExecutableShadowCandidateCount}");
        csv.AppendLine($"CanEnterTestnetOrderModeCount,{s.CanEnterTestnetOrderModeCount}");
        csv.AppendLine($"BlockedByLookbackStarvationCount,{s.BlockedByLookbackStarvationCount}");
        csv.AppendLine($"BlockedByMissingEntrySignalCount,{s.BlockedByMissingEntrySignalCount}");
        csv.AppendLine($"BlockedByStressNegativeForwardCount,{s.BlockedByStressNegativeForwardCount}");
        csv.AppendLine($"PromoteToShadowCount,{s.PromoteToShadowCount}");
        csv.AppendLine($"KeepIncubatingCount,{s.KeepIncubatingCount}");
        csv.AppendLine($"NeedsMoreDataCount,{s.NeedsMoreDataCount}");
        csv.AppendLine($"ParkCount,{s.ParkCount}");
        csv.AppendLine($"RejectCount,{s.RejectCount}");
        csv.AppendLine($"ShadowPortfolioCandidateCount,{s.ShadowPortfolioCandidateCount}");
        csv.AppendLine($"ExecutionReadyPortfolioCandidateCount,{s.ExecutionReadyPortfolioCandidateCount}");
        csv.AppendLine($"PortfolioTotalTrades,{p.TotalTrades}");
        csv.AppendLine($"PortfolioNetPer100USDT,{p.NetPer100Usdt}");
        csv.AppendLine($"PortfolioNetPer1000USDT,{p.NetPer1000Usdt}");
        csv.AppendLine($"PortfolioMaxDrawdownPer1000USDT,{p.MaxDrawdownPer1000Usdt}");
        csv.AppendLine($"PortfolioOverlappingSignalCount,{p.OverlappingSignalCount}");
        csv.AppendLine($"LiveFuturesRecommended,{s.LiveFuturesRecommended}");
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "cross-symbol-candidate-engine-v2-summary.csv"), csv.ToString(), ct);
    }

    private static async Task WriteCandidatesAsync(
        string outputDirectory,
        IReadOnlyList<CrossSymbolCandidateEngineV2CandidateRow> rows,
        string warning,
        CancellationToken ct)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "cross-symbol-candidate-engine-v2-candidates.json"),
            JsonSerializer.Serialize(new { warning, candidates = rows }, JsonOptions),
            ct);

        var header = "CandidateKey,Symbol,Interval,Direction,TargetPercent,StopPercent,ActivationRule,TradeCount,NetModerate,NetModerateLatency002,NetStressPlus,WinRate,ProfitFactor,MaxDrawdown,MaxConsecutiveLosses,PositiveActivatedPeriodsPercent,SparseWarning,OverfitWarning,SingleClusterWarning,CostStabilityPassed,StressPassed,MinimumTradeCountPassed,DataCoveragePassed,NormalizedNetPer100USDT,NormalizedNetPer1000USDT,EstimatedRequiredMarginAt1x,EstimatedRequiredMarginAt3x,CandidateScore,PromotionStatus,ResearchPromotionStatus,CurrentExecutionReadiness,CurrentForwardTrades,CurrentForwardNetModerate,CurrentForwardNetStressPlus,CurrentForwardHealthScore,CurrentBottleneckClassification,CurrentBottleneckRecommendation,LatestShadowActivationPassed,LatestShadowEntrySignalPresent,LatestShadowWouldPlaceOrder,LatestShadowRiskStatus,LatestShadowReasonIfBlocked,ExecutionReadinessExplanation,CanEnterTestnetOrderMode,MatchedFrozenProfileName,RejectionReason,OverlapWarning,LegacyBottleneckRisk,BottleneckRisk,V1Recommendation,SuggestedFrozenProfileName,SelectedForShadowPortfolio,SelectedForExecutionReadyPortfolio,AssignedShadowNotionalUSDT";
        var sb = new StringBuilder();
        sb.AppendLine($"Warning,{Csv(warning)}");
        sb.AppendLine(header);
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(r.CandidateKey), Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction),
                r.TargetPercent, r.StopPercent, Csv(r.ActivationRule),
                r.TradeCount, r.NetModerate, r.NetModerateLatency002, r.NetStressPlus,
                r.WinRate, r.ProfitFactor, r.MaxDrawdown, r.MaxConsecutiveLosses, r.PositiveActivatedPeriodsPercent,
                r.SparseWarning, r.OverfitWarning, r.SingleClusterWarning,
                r.CostStabilityPassed, r.StressPassed, r.MinimumTradeCountPassed, r.DataCoveragePassed,
                r.NormalizedNetPer100Usdt, r.NormalizedNetPer1000Usdt,
                r.EstimatedRequiredMarginAt1x, r.EstimatedRequiredMarginAt3x,
                r.CandidateScore, Csv(r.PromotionStatus), Csv(r.ResearchPromotionStatus), Csv(r.CurrentExecutionReadiness),
                r.CurrentForwardTrades, r.CurrentForwardNetModerate, r.CurrentForwardNetStressPlus, r.CurrentForwardHealthScore,
                Csv(r.CurrentBottleneckClassification), Csv(r.CurrentBottleneckRecommendation),
                r.LatestShadowActivationPassed, r.LatestShadowEntrySignalPresent, r.LatestShadowWouldPlaceOrder,
                Csv(r.LatestShadowRiskStatus), Csv(r.LatestShadowReasonIfBlocked), Csv(r.ExecutionReadinessExplanation),
                r.CanEnterTestnetOrderMode, Csv(r.MatchedFrozenProfileName ?? string.Empty), Csv(r.RejectionReason),
                r.OverlapWarning, Csv(r.LegacyBottleneckRisk), Csv(r.BottleneckRisk), Csv(r.V1Recommendation), Csv(r.SuggestedFrozenProfileName),
                r.SelectedForShadowPortfolio, r.SelectedForExecutionReadyPortfolio, r.AssignedShadowNotionalUsdt));
        }
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "cross-symbol-candidate-engine-v2-candidates.csv"), sb.ToString(), ct);
    }

    private static async Task WriteShadowPortfolioAsync(
        string outputDirectory,
        string filePrefix,
        CrossSymbolCandidateEngineV2ShadowPortfolioRow portfolio,
        string warning,
        CancellationToken ct)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, $"{filePrefix}.json"),
            JsonSerializer.Serialize(new { warning, portfolio }, JsonOptions),
            ct);

        var sb = new StringBuilder();
        sb.AppendLine($"Warning,{Csv(warning)}");
        sb.AppendLine("Metric,Value");
        sb.AppendLine($"PromotedCandidateCount,{portfolio.PromotedCandidateCount}");
        sb.AppendLine($"TotalAssignedNotionalUSDT,{portfolio.TotalAssignedNotionalUsdt}");
        sb.AppendLine($"TotalTrades,{portfolio.TotalTrades}");
        sb.AppendLine($"NetPer100USDT,{portfolio.NetPer100Usdt}");
        sb.AppendLine($"NetPer1000USDT,{portfolio.NetPer1000Usdt}");
        sb.AppendLine($"MaxDrawdownPer1000USDT,{portfolio.MaxDrawdownPer1000Usdt}");
        sb.AppendLine($"WorstDayPer1000USDT,{portfolio.WorstDayPer1000Usdt}");
        sb.AppendLine($"BestDayPer1000USDT,{portfolio.BestDayPer1000Usdt}");
        sb.AppendLine($"MaxConsecutiveLosses,{portfolio.MaxConsecutiveLosses}");
        sb.AppendLine($"OverlappingSignalCount,{portfolio.OverlappingSignalCount}");
        sb.AppendLine();
        sb.AppendLine("CandidateKey,Symbol,Interval,Direction,AssignedNotionalUSDT,TradeCount,NetPer100USDT,NetPer1000USDT,ShareOfPortfolioNetPer1000USDT");
        foreach (var c in portfolio.CandidateContributionBreakdown)
        {
            sb.AppendLine(string.Join(",",
                Csv(c.CandidateKey), Csv(c.Symbol), Csv(c.Interval), Csv(c.Direction),
                c.AssignedNotionalUsdt, c.TradeCount, c.NetPer100Usdt, c.NetPer1000Usdt, c.ShareOfPortfolioNetPer1000Usdt));
        }
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{filePrefix}.csv"), sb.ToString(), ct);
    }

    private static async Task WriteRejectionsAsync(
        string outputDirectory,
        IReadOnlyList<CrossSymbolCandidateEngineV2RejectionRow> rows,
        string warning,
        CancellationToken ct)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "cross-symbol-candidate-engine-v2-rejections.json"),
            JsonSerializer.Serialize(new { warning, rejections = rows }, JsonOptions),
            ct);

        var sb = new StringBuilder();
        sb.AppendLine($"Warning,{Csv(warning)}");
        sb.AppendLine("CandidateKey,Symbol,Interval,Direction,ActivationRule,PromotionStatus,RejectionReason,CandidateScore");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(r.CandidateKey), Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction),
                Csv(r.ActivationRule), Csv(r.PromotionStatus), Csv(r.RejectionReason), r.CandidateScore));
        }
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "cross-symbol-candidate-engine-v2-rejections.csv"), sb.ToString(), ct);
    }

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
