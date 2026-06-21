using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed class DirectionalRuleFuturesValidationV2ReportWriter(string outputDirectory)
{
    private bool _tradesJsonNeedsComma;
    private StreamWriter? _tradesJsonWriter;
    private StreamWriter? _tradesCsvWriter;

    public async Task InitializeStreamingTradesAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var csvPath = Path.Combine(outputDirectory, "directional-rule-v2-trades.csv");
        var csvStream = new FileStream(csvPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _tradesCsvWriter = new StreamWriter(csvStream, Encoding.UTF8, bufferSize: 65536) { AutoFlush = false };
        await _tradesCsvWriter.WriteLineAsync(
            "ProfileKey,RuleName,Direction,Symbol,Interval,WindowLabel,TimeUtc,EntryPrice,ExitPrice,ExitReason,TargetPercent,StopPercent,MaxHoldMinutes,CostScenarioLabel,GrossPnlQuote,NetPnlQuote,FundingEstimateQuote,SlippageEstimateQuote,EntryMode,OverlapPolicy,CooldownCandlesAfterExit,MfePercent,MaePercent,DurationMinutes");

        var jsonPath = Path.Combine(outputDirectory, "directional-rule-v2-trades.json");
        var jsonStream = new FileStream(jsonPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _tradesJsonWriter = new StreamWriter(jsonStream, Encoding.UTF8, bufferSize: 65536) { AutoFlush = false };
        await _tradesJsonWriter.WriteAsync("[");
        _tradesJsonNeedsComma = false;
    }

    public async Task AppendStreamingTradesAsync(
        IReadOnlyList<DirectionalRuleV2TradeRecord> rows,
        CancellationToken cancellationToken)
    {
        if (_tradesCsvWriter is null || _tradesJsonWriter is null)
            throw new InvalidOperationException("Call InitializeStreamingTradesAsync before appending trades.");

        var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
        for (var i = 0; i < rows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows[i];
            await _tradesCsvWriter.WriteLineAsync(FormatTradeCsvLine(row));
            if (_tradesJsonNeedsComma)
                await _tradesJsonWriter.WriteAsync(",");
            await _tradesJsonWriter.WriteAsync(Environment.NewLine);
            await _tradesJsonWriter.WriteAsync(JsonSerializer.Serialize(row, jsonOptions));
            _tradesJsonNeedsComma = true;
            if ((i + 1) % 512 == 0)
            {
                await _tradesCsvWriter.FlushAsync();
                await _tradesJsonWriter.FlushAsync();
            }
        }
    }

    public async Task FinalizeStreamingTradesAsync(CancellationToken cancellationToken)
    {
        if (_tradesJsonWriter is not null)
        {
            await _tradesJsonWriter.WriteAsync(Environment.NewLine + "]");
            await _tradesJsonWriter.FlushAsync();
            await _tradesJsonWriter.DisposeAsync();
            _tradesJsonWriter = null;
        }

        if (_tradesCsvWriter is not null)
        {
            await _tradesCsvWriter.FlushAsync();
            await _tradesCsvWriter.DisposeAsync();
            _tradesCsvWriter = null;
        }
    }

    public async Task WriteAsync(
        IReadOnlyList<DirectionalRuleV2SummaryRow> summaries,
        IReadOnlyList<DirectionalRuleV2WindowRobustnessRow> windowRobustness,
        IReadOnlyList<DirectionalRuleV2CostSensitivityRow> costSensitivity,
        IReadOnlyList<DirectionalRuleV2DrawdownRow> drawdown,
        IReadOnlyList<DirectionalRuleV2OverlapAnalysisRow> overlapAnalysis,
        IReadOnlyList<ReachabilityResearchAnswer> researchAnswers,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        await WriteJsonAsync("directional-rule-v2-summary.json", summaries, cancellationToken);
        await WriteJsonAsync("directional-rule-v2-window-robustness.json", windowRobustness, cancellationToken);
        await WriteJsonAsync("directional-rule-v2-cost-sensitivity.json", costSensitivity, cancellationToken);
        await WriteJsonAsync("directional-rule-v2-drawdown.json", drawdown, cancellationToken);
        await WriteJsonAsync("directional-rule-v2-overlap-analysis.json", overlapAnalysis, cancellationToken);
        await WriteJsonAsync("directional-rule-v2-research-answers.json", researchAnswers, cancellationToken);

        await WriteSummaryCsvAsync(summaries, cancellationToken);
        await WriteWindowRobustnessCsvAsync(windowRobustness, cancellationToken);
        await WriteCostSensitivityCsvAsync(costSensitivity, cancellationToken);
        await WriteDrawdownCsvAsync(drawdown, cancellationToken);
        await WriteOverlapCsvAsync(overlapAnalysis, cancellationToken);
    }

    private async Task WriteJsonAsync<T>(string fileName, T payload, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, fileName),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private async Task WriteSummaryCsvAsync(
        IReadOnlyList<DirectionalRuleV2SummaryRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileKey,RuleName,Symbol,Interval,WindowLabel,EntryMode,OverlapPolicy,CooldownCandlesAfterExit,TargetPercent,StopPercent,MaxHoldMinutes,CostScenarioLabel,SignalCount,ExecutedTrades,SkippedOverlapSignals,SkippedCooldownSignals,SkippedPrioritySignals,GrossPnlQuote,NetPnlQuote,AvgNetPnlPerTrade,MedianNetPerTrade,WinRate,AverageWin,AverageLoss,ProfitFactor,AggregateNetPositive,Window30dNetPositive,Window60dNetPositive,Window90dNetPositive,AllWindowsPositive,Holdout90dPositive,StressAggregatePositive,StressAllWindowsPositive,Verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.ProfileKey), Csv(row.RuleName), row.Symbol, Csv(row.Interval), Csv(row.WindowLabel),
                Csv(row.EntryMode), Csv(row.OverlapPolicy), row.CooldownCandlesAfterExit,
                row.TargetPercent, row.StopPercent, row.MaxHoldMinutes, Csv(row.CostScenarioLabel),
                row.SignalCount, row.ExecutedTrades, row.SkippedOverlapSignals, row.SkippedCooldownSignals,
                row.SkippedPrioritySignals, row.GrossPnlQuote, row.NetPnlQuote,
                row.AvgNetPnlPerTrade?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.MedianNetPerTrade?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.WinRate, row.AverageWin?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.AverageLoss?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.ProfitFactor?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.AggregateNetPositive, row.Window30dNetPositive, row.Window60dNetPositive,
                row.Window90dNetPositive, row.AllWindowsPositive, row.Holdout90dPositive,
                row.StressAggregatePositive, row.StressAllWindowsPositive, Csv(row.Verdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v2-summary.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteWindowRobustnessCsvAsync(
        IReadOnlyList<DirectionalRuleV2WindowRobustnessRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileKey,RuleName,Symbol,Interval,EntryMode,OverlapPolicy,CooldownCandlesAfterExit,MaxHoldMinutes,CostScenarioLabel,Window30dTrades,Window60dTrades,Window90dTrades,Window30dNetPnl,Window60dNetPnl,Window90dNetPnl,AggregateNetPnl,AggregateNetPositive,Window30dNetPositive,Window60dNetPositive,Window90dNetPositive,AllWindowsPositive,Holdout90dPositive,StressAggregatePositive,StressAllWindowsPositive,RobustnessVerdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.ProfileKey), Csv(row.RuleName), row.Symbol, Csv(row.Interval), Csv(row.EntryMode),
                Csv(row.OverlapPolicy), row.CooldownCandlesAfterExit, row.MaxHoldMinutes, Csv(row.CostScenarioLabel),
                row.Window30dTrades, row.Window60dTrades, row.Window90dTrades,
                row.Window30dNetPnl, row.Window60dNetPnl, row.Window90dNetPnl, row.AggregateNetPnl,
                row.AggregateNetPositive, row.Window30dNetPositive, row.Window60dNetPositive,
                row.Window90dNetPositive, row.AllWindowsPositive, row.Holdout90dPositive,
                row.StressAggregatePositive, row.StressAllWindowsPositive, Csv(row.RobustnessVerdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v2-window-robustness.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteCostSensitivityCsvAsync(
        IReadOnlyList<DirectionalRuleV2CostSensitivityRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileKey,RuleName,Symbol,Interval,EntryMode,OverlapPolicy,CooldownCandlesAfterExit,MaxHoldMinutes,CostScenarioLabel,RoundTripCostPercent,ExtraAdverseSlippagePercentPerSide,TradeCount,NetPnlQuote,AvgNetPnlPerTrade,AggregateNetPositive,StressAggregatePositive,StressAllWindowsPositive,Verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.ProfileKey), Csv(row.RuleName), row.Symbol, Csv(row.Interval), Csv(row.EntryMode),
                Csv(row.OverlapPolicy), row.CooldownCandlesAfterExit, row.MaxHoldMinutes, Csv(row.CostScenarioLabel),
                row.RoundTripCostPercent, row.ExtraAdverseSlippagePercentPerSide, row.TradeCount, row.NetPnlQuote,
                row.AvgNetPnlPerTrade?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.AggregateNetPositive, row.StressAggregatePositive, row.StressAllWindowsPositive, Csv(row.Verdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v2-cost-sensitivity.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteDrawdownCsvAsync(
        IReadOnlyList<DirectionalRuleV2DrawdownRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProfileKey,RuleName,Symbol,Interval,WindowLabel,EntryMode,OverlapPolicy,CooldownCandlesAfterExit,MaxHoldMinutes,CostScenarioLabel,TradeCount,MaxConsecutiveLosses,MaxDrawdownQuote,WorstWindowNet,WorstTradeNet,ProfitFactor,WinRate,AverageWin,AverageLoss,MedianNetPerTrade");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.ProfileKey), Csv(row.RuleName), row.Symbol, Csv(row.Interval), Csv(row.WindowLabel),
                Csv(row.EntryMode), Csv(row.OverlapPolicy), row.CooldownCandlesAfterExit, row.MaxHoldMinutes,
                Csv(row.CostScenarioLabel), row.TradeCount, row.MaxConsecutiveLosses, row.MaxDrawdownQuote,
                row.WorstWindowNet, row.WorstTradeNet,
                row.ProfitFactor?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.WinRate, row.AverageWin?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.AverageLoss?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.MedianNetPerTrade?.ToString(CultureInfo.InvariantCulture) ?? ""));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v2-drawdown.csv"), sb.ToString(), cancellationToken);
    }

    private async Task WriteOverlapCsvAsync(
        IReadOnlyList<DirectionalRuleV2OverlapAnalysisRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Rule01Interval,Rule05Interval,WindowLabel,Rule01SignalCount,Rule05SignalCount,CoFireWithin30mCount,CoFireRateVsRule01,CoFireRateVsRule05,RulePriorityMode,PriorityRule01Wins,PriorityRule05Wins,Notes");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                row.Symbol, Csv(row.Rule01Interval), Csv(row.Rule05Interval), Csv(row.WindowLabel),
                row.Rule01SignalCount, row.Rule05SignalCount, row.CoFireWithin30mCount,
                row.CoFireRateVsRule01, row.CoFireRateVsRule05, Csv(row.RulePriorityMode),
                row.PriorityRule01Wins, row.PriorityRule05Wins, Csv(row.Notes)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "directional-rule-v2-overlap-analysis.csv"), sb.ToString(), cancellationToken);
    }

    private static string FormatTradeCsvLine(DirectionalRuleV2TradeRecord row)
        => string.Join(",",
            Csv(row.ProfileKey), Csv(row.RuleName), row.Direction, row.Symbol, Csv(row.Interval),
            Csv(row.WindowLabel), row.TimeUtc.ToString("O", CultureInfo.InvariantCulture),
            row.EntryPrice, row.ExitPrice, Csv(row.ExitReason),
            row.TargetPercent, row.StopPercent, row.MaxHoldMinutes, Csv(row.CostScenarioLabel),
            row.GrossPnlQuote, row.NetPnlQuote, row.FundingEstimateQuote, row.SlippageEstimateQuote,
            Csv(row.EntryMode), Csv(row.OverlapPolicy), row.CooldownCandlesAfterExit,
            row.MfePercent?.ToString(CultureInfo.InvariantCulture) ?? "",
            row.MaePercent?.ToString(CultureInfo.InvariantCulture) ?? "",
            row.DurationMinutes);

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
