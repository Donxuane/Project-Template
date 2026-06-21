using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed class FuturesTestnetShadowReportWriter(string outputDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task WriteAsync(
        FuturesTestnetShadowRunResult result,
        FuturesTestnetShadowSettings shadowSettings,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        await WriteJson("futures-testnet-shadow-summary.json", result.Summary, cancellationToken);
        await WriteJson("futures-testnet-shadow-decisions.json", result.Decisions, cancellationToken);
        await WriteJson("futures-testnet-shadow-risk.json", result.RiskRows, cancellationToken);

        await WriteSummaryCsv(result.Summary, cancellationToken);
        await WriteDecisionsCsv(result.Decisions, cancellationToken);
        await WriteRiskCsv(result.RiskRows, cancellationToken);
        await WriteSummaryText(result, shadowSettings, cancellationToken);
    }

    private async Task WriteJson<T>(string fileName, T payload, CancellationToken cancellationToken)
        => await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, fileName),
            JsonSerializer.Serialize(payload, JsonOptions),
            cancellationToken);

    private async Task WriteSummaryCsv(FuturesTestnetShadowSummaryRow summary, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RunAtUtc,EvaluationUtc,Mode,BacktestOnly,TestnetShadowOnly,RealOrdersPlaced,LiveFuturesRecommended,DryRunOnly,AllowTestnetOrders,AllowRealOrders,ShadowEnabledInConfig,KeySafetyStatus,ProfilesEvaluated,ActivationPassedCount,EntrySignalCount,WouldPlaceOrderCount,CompactSummaryLine");
        sb.AppendLine(string.Join(",",
            Dt(summary.RunAtUtc), Dt(summary.EvaluationUtc), Csv(summary.Mode),
            summary.BacktestOnly, summary.TestnetShadowOnly, summary.RealOrdersPlaced, summary.LiveFuturesRecommended,
            summary.DryRunOnly, summary.AllowTestnetOrders, summary.AllowRealOrders, summary.ShadowEnabledInConfig,
            Csv(summary.KeySafetyStatus), summary.ProfilesEvaluated, summary.ActivationPassedCount,
            summary.EntrySignalCount, summary.WouldPlaceOrderCount, Csv(summary.CompactSummaryLine)));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "futures-testnet-shadow-summary.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteDecisionsCsv(IReadOnlyList<FuturesTestnetShadowDecisionRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("TimestampUtc,ProfileName,Symbol,Interval,Direction,ActivationPassed,ActivationReason,EntrySignalPresent,EntryReason,WouldPlaceOrder,OrderSide,IntendedEntryPrice,TargetPrice,StopPrice,HoldHours,AssumedNotionalUSDT,NetPnlPer100USDT,RequiredMarginAtLeverage,Leverage,QuantityRaw,QuantityRounded,PriceTickSize,QuantityStepSize,MinNotional,PrecisionValid,RiskStatus,ReasonIfBlocked,RequireForwardTradeEvidence,ForwardTradeCount,ForwardNetModerate,ForwardNetStressPlus,ForwardEvidencePassed,ShadowRunnerCanPlaceIfSignalAppears,ForwardEvidenceSourceFile,ForwardEvidenceSourceProfileName,ForwardEvidenceWindowStartUtc,ForwardEvidenceWindowEndUtc,ForwardEvidenceIsTrueForwardOnly");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                Dt(r.TimestampUtc), Csv(r.ProfileName), Csv(r.Symbol), Csv(r.Interval), Csv(r.Direction),
                r.ActivationPassed, Csv(r.ActivationReason), r.EntrySignalPresent, Csv(r.EntryReason),
                r.WouldPlaceOrder, Csv(r.OrderSide), Dec(r.IntendedEntryPrice), Dec(r.TargetPrice), Dec(r.StopPrice),
                r.HoldHours, Dec(r.AssumedNotionalUsdt), Dec(r.NetPnlPer100Usdt), Dec(r.RequiredMarginAtLeverage),
                r.Leverage, Dec(r.QuantityRaw), Dec(r.QuantityRounded), Dec(r.PriceTickSize), Dec(r.QuantityStepSize),
                Dec(r.MinNotional), r.PrecisionValid, Csv(r.RiskStatus), Csv(r.ReasonIfBlocked),
                r.RequireForwardTradeEvidence, r.ForwardTradeCount, Dec(r.ForwardNetModerate), Dec(r.ForwardNetStressPlus),
                r.ForwardEvidencePassed, r.ShadowRunnerCanPlaceIfSignalAppears,
                Csv(r.ForwardEvidenceSourceFile), Csv(r.ForwardEvidenceSourceProfileName),
                Dt(r.ForwardEvidenceWindowStartUtc), Dt(r.ForwardEvidenceWindowEndUtc), r.ForwardEvidenceIsTrueForwardOnly));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "futures-testnet-shadow-decisions.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteRiskCsv(IReadOnlyList<FuturesTestnetShadowRiskRow> rows, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileName,Symbol,AssumedNotionalUSDT,Leverage,RequiredMarginAtLeverage,MaxNotionalUSDTLimit,WithinMaxNotional,PrecisionValid,RiskStatus,ReasonIfBlocked");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(r.ProfileName), Csv(r.Symbol), Dec(r.AssumedNotionalUsdt), r.Leverage,
                Dec(r.RequiredMarginAtLeverage), Dec(r.MaxNotionalUsdtLimit), r.WithinMaxNotional,
                r.PrecisionValid, Csv(r.RiskStatus), Csv(r.ReasonIfBlocked)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "futures-testnet-shadow-risk.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteSummaryText(
        FuturesTestnetShadowRunResult result,
        FuturesTestnetShadowSettings shadowSettings,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Binance Futures Testnet Shadow Runner");
        sb.AppendLine("=====================================");
        sb.AppendLine("MODE: backtestOnly / testnetShadowOnly — engineering preparation only.");
        sb.AppendLine("realOrdersPlaced=false");
        sb.AppendLine("liveFuturesRecommended=false");
        sb.AppendLine($"keySafetyStatus={result.KeySafetyStatus}");
        sb.AppendLine($"DryRunOnly={shadowSettings.DryRunOnly} AllowTestnetOrders={shadowSettings.AllowTestnetOrders} AllowRealOrders={shadowSettings.AllowRealOrders}");
        sb.AppendLine($"MaxNotionalUSDT={shadowSettings.MaxNotionalUsdt} Leverage={shadowSettings.Leverage}");
        sb.AppendLine();
        sb.AppendLine(result.Summary.CompactSummaryLine);
        sb.AppendLine();
        foreach (var d in result.Decisions)
        {
            sb.AppendLine($"[{d.ProfileName}] {d.Symbol} {d.Interval} {d.Direction}");
            sb.AppendLine($"  activation={d.ActivationPassed} ({d.ActivationReason})");
            sb.AppendLine($"  entrySignal={d.EntrySignalPresent} ({d.EntryReason})");
            sb.AppendLine($"  wouldPlaceOrder={d.WouldPlaceOrder} risk={d.RiskStatus} blocked={d.ReasonIfBlocked}");
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "futures-testnet-shadow-summary.txt"), sb.ToString(), cancellationToken);
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string Dt(DateTime? value)
        => value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : string.Empty;

    private static string Dt(DateTime value)
        => value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Dec(decimal? value)
        => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
}
