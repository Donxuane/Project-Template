using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Application.CrossSymbolShadowBridge;

public static class CrossSymbolShadowBridgeReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task WriteAsync(
        string outputDirectory,
        CrossSymbolShadowBridgeRunResult result,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        const string warning =
            "Cross-symbol shadow bridge is actual-bot shadow/dry-run integration only. RealOrdersPlaced=false always. No orders are placed.";

        await WriteStatusAsync(outputDirectory, result, warning, cancellationToken);
        await WriteDecisionsAsync(outputDirectory, result, warning, cancellationToken);
        await WriteRiskAsync(outputDirectory, result, warning, cancellationToken);
    }

    private static async Task WriteStatusAsync(
        string outputDirectory,
        CrossSymbolShadowBridgeRunResult result,
        string warning,
        CancellationToken cancellationToken)
    {
        var s = result.Status;
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "cross-symbol-shadow-bridge-status.json"),
            JsonSerializer.Serialize(new
            {
                warning,
                s.EvaluatedAtUtc,
                s.Status,
                s.CompactSummaryLine,
                s.Message,
                s.Enabled,
                ShadowOnly = s.ShadowOnly,
                s.BacktestOnly,
                s.DryRunOnly,
                AllowOrders = s.AllowOrders,
                AllowTestnetOrders = s.AllowTestnetOrders,
                AllowRealOrders = s.AllowRealOrders,
                RealOrdersPlaced = s.RealOrdersPlaced,
                LiveFuturesRecommended = s.LiveFuturesRecommended,
                s.CandidateInputDirectory,
                s.OutputDirectory,
                s.TotalCandidatesLoaded,
                s.ExecutionReadyCandidateCount,
                s.ResearchPromotedShadowOnlyCount,
                s.RejectedOrParkedCount,
                s.ExecutionReadyPortfolioCandidateCount,
                s.ShadowDecisionsAvailable,
                s.BottleneckAuditAvailable,
                s.CandidatesFileExists,
                s.SummaryFileExists,
                s.ExecutionPortfolioFileExists,
                missingInputFiles = s.MissingInputFiles
            }, JsonOptions),
            cancellationToken);
    }

    private static async Task WriteDecisionsAsync(
        string outputDirectory,
        CrossSymbolShadowBridgeRunResult result,
        string warning,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "cross-symbol-shadow-bridge-decisions.json"),
            JsonSerializer.Serialize(new { warning, decisions = result.Decisions }, JsonOptions),
            cancellationToken);

        var header =
            "EvaluatedAtUtc,CandidateKey,Symbol,Interval,Direction,TargetPercent,StopPercent,ActivationRule,Category,ResearchPromotionStatus,CurrentExecutionReadiness,CanEnterTestnetOrderMode,CurrentForwardTrades,CurrentForwardNetModerate,CurrentForwardNetStressPlus,LatestShadowActivationPassed,LatestShadowEntrySignalPresent,LatestShadowRiskStatus,ExecutionReadinessExplanation,AssignedShadowNotionalUSDT,WouldPlaceOrder,ReasonIfBlocked";

        var sb = new StringBuilder();
        sb.AppendLine($"Warning,{Csv(warning)}");
        sb.AppendLine(header);
        foreach (var d in result.Decisions)
        {
            sb.AppendLine(string.Join(",",
                Dt(d.EvaluatedAtUtc), Csv(d.CandidateKey), Csv(d.Symbol), Csv(d.Interval), Csv(d.Direction),
                d.TargetPercent, d.StopPercent, Csv(d.ActivationRule), Csv(d.Category),
                Csv(d.ResearchPromotionStatus), Csv(d.CurrentExecutionReadiness), d.CanEnterTestnetOrderMode,
                d.CurrentForwardTrades, d.CurrentForwardNetModerate, d.CurrentForwardNetStressPlus,
                d.LatestShadowActivationPassed, d.LatestShadowEntrySignalPresent, Csv(d.LatestShadowRiskStatus),
                Csv(d.ExecutionReadinessExplanation), d.AssignedShadowNotionalUsdt, d.WouldPlaceOrder,
                Csv(d.ReasonIfBlocked)));
        }

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "cross-symbol-shadow-bridge-decisions.csv"),
            sb.ToString(),
            cancellationToken);
    }

    private static async Task WriteRiskAsync(
        string outputDirectory,
        CrossSymbolShadowBridgeRunResult result,
        string warning,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "cross-symbol-shadow-bridge-risk.json"),
            JsonSerializer.Serialize(new
            {
                warning,
                evaluatedAtUtc = result.Status.EvaluatedAtUtc,
                bridgeStatus = result.Status.Status,
                realOrdersPlaced = false,
                liveFuturesRecommended = false,
                allowOrders = false,
                allowTestnetOrders = false,
                allowRealOrders = false,
                shadowOnly = true,
                riskRows = result.RiskRows
            }, JsonOptions),
            cancellationToken);

        var header =
            "CandidateKey,Symbol,Category,RiskStatus,ReasonIfBlocked,OrdersPermitted,TestnetOrdersPermitted,RealOrdersPermitted,ShadowOnly";
        var sb = new StringBuilder();
        sb.AppendLine($"Warning,{Csv(warning)}");
        sb.AppendLine(header);
        foreach (var r in result.RiskRows)
        {
            sb.AppendLine(string.Join(",",
                Csv(r.CandidateKey), Csv(r.Symbol), Csv(r.Category), Csv(r.RiskStatus), Csv(r.ReasonIfBlocked),
                r.OrdersPermitted, r.TestnetOrdersPermitted, r.RealOrdersPermitted, r.ShadowOnly));
        }

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "cross-symbol-shadow-bridge-risk.csv"),
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
