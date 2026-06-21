using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed class DirectionalRuleFuturesValidationV31ReportWriter(string outputDirectory)
{
    // Trade-level rows are streamed to disk during the scan (see DirectionalRuleV31TradesCsvStream)
    // rather than collected in memory, so the aggregate reports below stay small and OOM-safe even
    // on the full cross-symbol matrix.
    public async Task WriteAsync(
        IReadOnlyList<DirectionalRuleV31SummaryRow> bestBnbSummary,
        IReadOnlyList<DirectionalRuleV31SummaryRow> crossSymbolSummary,
        IReadOnlyList<DirectionalRuleV31WindowRobustnessRow> windowRobustness,
        IReadOnlyList<DirectionalRuleV31CostSensitivityRow> costSensitivity,
        IReadOnlyList<DirectionalRuleV31DrawdownRow> drawdown,
        IReadOnlyList<DirectionalRuleV31MonthlyWeeklyPnlRow> monthlyWeekly,
        IReadOnlyList<ReachabilityResearchAnswer> generalizationAnswers,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        await WriteJsonAsync("directional-rule-v31-best-bnb-long-history-summary.json", bestBnbSummary, cancellationToken);
        await WriteJsonAsync("directional-rule-v31-cross-symbol-summary.json", crossSymbolSummary, cancellationToken);
        await WriteJsonAsync("directional-rule-v31-window-robustness.json", windowRobustness, cancellationToken);
        await WriteJsonAsync("directional-rule-v31-cost-sensitivity.json", costSensitivity, cancellationToken);
        await WriteJsonAsync("directional-rule-v31-drawdown.json", drawdown, cancellationToken);
        await WriteJsonAsync("directional-rule-v31-monthly-weekly-pnl.json", monthlyWeekly, cancellationToken);
        await WriteJsonAsync("directional-rule-v31-generalization-answers.json", generalizationAnswers, cancellationToken);

        await WriteSummaryCsvAsync("directional-rule-v31-best-bnb-long-history-summary.csv", bestBnbSummary, cancellationToken);
        await WriteSummaryCsvAsync("directional-rule-v31-cross-symbol-summary.csv", crossSymbolSummary, cancellationToken);
        await WriteWindowRobustnessCsvAsync(windowRobustness, cancellationToken);
        await WriteCostSensitivityCsvAsync(costSensitivity, cancellationToken);
        await WriteDrawdownCsvAsync(drawdown, cancellationToken);
        await WriteMonthlyWeeklyCsvAsync(monthlyWeekly, cancellationToken);
    }

    private async Task WriteJsonAsync<T>(string fileName, T payload, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, fileName),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private async Task WriteSummaryCsvAsync(
        string fileName,
        IReadOnlyList<DirectionalRuleV31SummaryRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileKey,VariantLabel,ValidationTrack,IsBestBnbCandidate,Symbol,Interval,WindowLabel,EntryMode,OverlapPolicy,CooldownCandlesAfterExit,TargetPercent,StopPercent,MaxHoldMinutes,CostScenarioLabel,SignalCount,ExecutedTrades,GrossPnlQuote,NetPnlQuote,AvgNetPnlPerTrade,MedianNetPerTrade,WinRate,AverageWin,AverageLoss,ProfitFactor,AverageHoldMinutes,TimeStopRate,StopLossRate,ProfitTargetRate,SymbolPositive,AllWindowsPositive,HoldoutPositive,StressPositive,StressAllWindowsPositive,TradeCountSufficient,LongHistoryPositive,OverfitWarning,Verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.ProfileKey), Csv(row.VariantLabel), row.ValidationTrack, row.IsBestBnbCandidate,
                row.Symbol, Csv(row.Interval), Csv(row.WindowLabel), Csv(row.EntryMode), Csv(row.OverlapPolicy),
                row.CooldownCandlesAfterExit, row.TargetPercent, row.StopPercent, row.MaxHoldMinutes,
                Csv(row.CostScenarioLabel), row.SignalCount, row.ExecutedTrades,
                row.GrossPnlQuote, row.NetPnlQuote, F(row.AvgNetPnlPerTrade), F(row.MedianNetPerTrade),
                row.WinRate, F(row.AverageWin), F(row.AverageLoss), F(row.ProfitFactor),
                row.AverageHoldMinutes, row.TimeStopRate, row.StopLossRate, row.ProfitTargetRate,
                row.SymbolPositive, row.AllWindowsPositive, row.HoldoutPositive,
                row.StressPositive, row.StressAllWindowsPositive, row.TradeCountSufficient,
                row.LongHistoryPositive, row.OverfitWarning, Csv(row.Verdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    internal const string TradesCsvHeader =
        "ProfileKey,VariantLabel,ValidationTrack,IsBestBnbCandidate,RuleName,Direction,Symbol,Interval,WindowLabel,EntryMode,TargetPercent,StopPercent,MaxHoldMinutes,CooldownCandlesAfterExit,OverlapPolicy,CostScenarioLabel,EntryTimeUtc,ExitTimeUtc,EntryPrice,ExitPrice,ExitReason,GrossPnlQuote,NetPnlQuote,FeesQuote,SlippageQuote,FundingQuote,DurationMinutes";

    internal static string FormatTradeCsvRow(DirectionalRuleV31TradeRecord row)
        => string.Join(",",
            Csv(row.ProfileKey), Csv(row.VariantLabel), row.ValidationTrack, row.IsBestBnbCandidate,
            Csv(row.RuleName), row.Direction, row.Symbol, Csv(row.Interval), Csv(row.WindowLabel),
            Csv(row.EntryMode), row.TargetPercent, row.StopPercent, row.MaxHoldMinutes,
            row.CooldownCandlesAfterExit, Csv(row.OverlapPolicy), Csv(row.CostScenarioLabel),
            row.EntryTimeUtc.ToString("O", CultureInfo.InvariantCulture),
            row.ExitTimeUtc.ToString("O", CultureInfo.InvariantCulture),
            row.EntryPrice, row.ExitPrice, Csv(row.ExitReason),
            row.GrossPnlQuote, row.NetPnlQuote, row.FeesQuote, row.SlippageQuote, row.FundingQuote,
            row.DurationMinutes);

    private async Task WriteWindowRobustnessCsvAsync(
        IReadOnlyList<DirectionalRuleV31WindowRobustnessRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileKey,VariantLabel,ValidationTrack,IsBestBnbCandidate,Symbol,Interval,CostScenarioLabel,Window30dTrades,Window60dTrades,Window90dTrades,Window120dTrades,Window180dTrades,Window270dTrades,Window365dTrades,Holdout30dTrades,TrainReferenceTrades,Window30dNetPnl,Window60dNetPnl,Window90dNetPnl,Window120dNetPnl,Window180dNetPnl,Window270dNetPnl,Window365dNetPnl,Holdout30dNetPnl,TrainReferenceNetPnl,AggregateNetPnl,SymbolPositive,AllWindowsPositive,HoldoutPositive,StressPositive,StressAllWindowsPositive,TradeCountSufficient,LongHistoryPositive,OverfitWarning,RobustnessVerdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.ProfileKey), Csv(row.VariantLabel), row.ValidationTrack, row.IsBestBnbCandidate,
                row.Symbol, Csv(row.Interval), Csv(row.CostScenarioLabel),
                row.Window30dTrades, row.Window60dTrades, row.Window90dTrades, row.Window120dTrades,
                row.Window180dTrades, row.Window270dTrades, row.Window365dTrades,
                row.Holdout30dTrades, row.TrainReferenceTrades,
                row.Window30dNetPnl, row.Window60dNetPnl, row.Window90dNetPnl, row.Window120dNetPnl,
                row.Window180dNetPnl, row.Window270dNetPnl, row.Window365dNetPnl,
                row.Holdout30dNetPnl, row.TrainReferenceNetPnl, row.AggregateNetPnl,
                row.SymbolPositive, row.AllWindowsPositive, row.HoldoutPositive,
                row.StressPositive, row.StressAllWindowsPositive, row.TradeCountSufficient,
                row.LongHistoryPositive, row.OverfitWarning, Csv(row.RobustnessVerdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v31-window-robustness.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteCostSensitivityCsvAsync(
        IReadOnlyList<DirectionalRuleV31CostSensitivityRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileKey,VariantLabel,ValidationTrack,IsBestBnbCandidate,Symbol,Interval,CostScenarioLabel,RoundTripCostPercent,ExtraAdverseSlippagePercentPerSide,TradeCount,NetPnlQuote,AvgNetPnlPerTrade,SymbolPositive,StressPositive,Verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.ProfileKey), Csv(row.VariantLabel), row.ValidationTrack, row.IsBestBnbCandidate,
                row.Symbol, Csv(row.Interval), Csv(row.CostScenarioLabel),
                row.RoundTripCostPercent, row.ExtraAdverseSlippagePercentPerSide,
                row.TradeCount, row.NetPnlQuote, F(row.AvgNetPnlPerTrade),
                row.SymbolPositive, row.StressPositive, Csv(row.Verdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v31-cost-sensitivity.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteDrawdownCsvAsync(
        IReadOnlyList<DirectionalRuleV31DrawdownRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileKey,VariantLabel,ValidationTrack,IsBestBnbCandidate,Symbol,Interval,WindowLabel,CostScenarioLabel,TradeCount,MaxConsecutiveLosses,MaxDrawdownQuote,WorstWindowNet,WorstTradeNet,ProfitFactor,WinRate,AverageWin,AverageLoss,MedianNetPerTrade,AverageHoldMinutes,TimeStopRate,StopLossRate,ProfitTargetRate,LongestFlatPeriodDays,LargestGivebackFromPeak");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.ProfileKey), Csv(row.VariantLabel), row.ValidationTrack, row.IsBestBnbCandidate,
                row.Symbol, Csv(row.Interval), Csv(row.WindowLabel), Csv(row.CostScenarioLabel),
                row.TradeCount, row.MaxConsecutiveLosses, row.MaxDrawdownQuote, row.WorstWindowNet,
                row.WorstTradeNet, F(row.ProfitFactor), row.WinRate, F(row.AverageWin), F(row.AverageLoss),
                F(row.MedianNetPerTrade), row.AverageHoldMinutes, row.TimeStopRate, row.StopLossRate,
                row.ProfitTargetRate, row.LongestFlatPeriodDays, row.LargestGivebackFromPeak));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v31-drawdown.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteMonthlyWeeklyCsvAsync(
        IReadOnlyList<DirectionalRuleV31MonthlyWeeklyPnlRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileKey,VariantLabel,ValidationTrack,IsBestBnbCandidate,Symbol,Interval,WindowLabel,CostScenarioLabel,PeriodType,PeriodKey,TradeCount,NetPnlQuote,MaxDrawdownQuote,MaxConsecutiveLosses");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.ProfileKey), Csv(row.VariantLabel), row.ValidationTrack, row.IsBestBnbCandidate,
                row.Symbol, Csv(row.Interval), Csv(row.WindowLabel), Csv(row.CostScenarioLabel),
                Csv(row.PeriodType), Csv(row.PeriodKey), row.TradeCount, row.NetPnlQuote,
                row.MaxDrawdownQuote, row.MaxConsecutiveLosses));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v31-monthly-weekly-pnl.csv"), sb.ToString(), cancellationToken);
    }

    private static string F(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}

/// <summary>
/// Streams expanded trade rows to the V31 trades CSV during the scan so the full trade set is never
/// held in memory. Building a single multi-million-row string previously caused OutOfMemory on the
/// full cross-symbol matrix.
/// </summary>
public sealed class DirectionalRuleV31TradesCsvStream : IAsyncDisposable
{
    private readonly StreamWriter _writer;
    public long RowsWritten { get; private set; }

    private DirectionalRuleV31TradesCsvStream(StreamWriter writer) => _writer = writer;

    public static async Task<DirectionalRuleV31TradesCsvStream> CreateAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "directional-rule-v31-trades.csv");
        var writer = new StreamWriter(path, append: false);
        await writer.WriteLineAsync(DirectionalRuleFuturesValidationV31ReportWriter.TradesCsvHeader.AsMemory(), cancellationToken);
        return new DirectionalRuleV31TradesCsvStream(writer);
    }

    public async Task WriteBatchAsync(IReadOnlyList<DirectionalRuleV31TradeRecord> trades, CancellationToken cancellationToken)
    {
        foreach (var trade in trades)
        {
            await _writer.WriteLineAsync(
                DirectionalRuleFuturesValidationV31ReportWriter.FormatTradeCsvRow(trade).AsMemory(),
                cancellationToken);
            RowsWritten++;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.FlushAsync();
        await _writer.DisposeAsync();
    }
}
