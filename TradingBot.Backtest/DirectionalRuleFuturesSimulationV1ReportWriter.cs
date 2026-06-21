using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed class DirectionalRuleFuturesSimulationV1ReportWriter(string outputDirectory)
{
    private bool _tradesJsonNeedsComma;
    private StreamWriter? _tradesJsonWriter;
    private StreamWriter? _tradesCsvWriter;

    public async Task InitializeStreamingTradesAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var csvPath = Path.Combine(outputDirectory, "directional-rule-futures-trades.csv");
        _tradesCsvWriter = new StreamWriter(csvPath, append: false);
        await _tradesCsvWriter.WriteLineAsync(
            "RuleName,Direction,Symbol,Interval,WindowLabel,TimeUtc,EntryPrice,ExitPrice,ExitReason,TargetPercent,StopPercent,MaxHoldMinutes,CostScenarioLabel,GrossPnlQuote,NetPnlQuote,FundingEstimateQuote,SlippageEstimateQuote,BtcReturn30mPercent,VolatilityRegime,RangeWidthPercent,DistanceFromRecentHighPercent,DistanceFromRecentLowPercent,AtrPercent,TrendSlopePercent,MfePercent,MaePercent,DurationMinutes,EntryMode");

        var jsonPath = Path.Combine(outputDirectory, "directional-rule-futures-trades.json");
        _tradesJsonWriter = new StreamWriter(jsonPath, append: false);
        await _tradesJsonWriter.WriteAsync("[");
        _tradesJsonNeedsComma = false;
    }

    public async Task AppendStreamingTradesAsync(
        IReadOnlyList<DirectionalRuleFuturesTradeRecord> rows,
        CancellationToken cancellationToken)
    {
        if (_tradesCsvWriter is null || _tradesJsonWriter is null)
            throw new InvalidOperationException("Call InitializeStreamingTradesAsync before appending trades.");

        var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _tradesCsvWriter.WriteLineAsync(FormatTradeCsvLine(row));
            if (_tradesJsonNeedsComma)
                await _tradesJsonWriter.WriteAsync(",");
            await _tradesJsonWriter.WriteAsync(Environment.NewLine);
            await _tradesJsonWriter.WriteAsync(JsonSerializer.Serialize(row, jsonOptions));
            _tradesJsonNeedsComma = true;
        }
    }

    public async Task FinalizeStreamingTradesAsync(CancellationToken cancellationToken)
    {
        if (_tradesJsonWriter is not null)
        {
            await _tradesJsonWriter.WriteAsync(Environment.NewLine + "]");
            await _tradesJsonWriter.DisposeAsync();
            _tradesJsonWriter = null;
        }

        if (_tradesCsvWriter is not null)
        {
            await _tradesCsvWriter.DisposeAsync();
            _tradesCsvWriter = null;
        }
    }

    public async Task WriteAsync(
        IReadOnlyList<DirectionalRuleFuturesSummaryRow> summaries,
        IReadOnlyList<DirectionalRuleFuturesTradeRecord> trades,
        IReadOnlyList<DirectionalRuleFuturesRulePerformanceRow> rulePerformance,
        IReadOnlyList<DirectionalRuleFuturesWindowRobustnessRow> windowRobustness,
        IReadOnlyList<DirectionalRuleFuturesCostSensitivityRow> costSensitivity,
        IReadOnlyList<ReachabilityResearchAnswer> researchAnswers,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        await WriteJsonAsync("directional-rule-futures-summary.json", summaries, cancellationToken);
        if (trades.Count > 0)
            await WriteJsonAsync("directional-rule-futures-trades.json", trades, cancellationToken);
        await WriteJsonAsync("directional-rule-futures-rule-performance.json", rulePerformance, cancellationToken);
        await WriteJsonAsync("directional-rule-futures-window-robustness.json", windowRobustness, cancellationToken);
        await WriteJsonAsync("directional-rule-futures-cost-sensitivity.json", costSensitivity, cancellationToken);
        await WriteJsonAsync("directional-rule-futures-research-answers.json", researchAnswers, cancellationToken);

        await WriteSummaryCsvAsync("directional-rule-futures-summary.csv", summaries, cancellationToken);
        if (trades.Count > 0)
            await WriteTradesCsvAsync("directional-rule-futures-trades.csv", trades, cancellationToken);
        await WriteRulePerformanceCsvAsync("directional-rule-futures-rule-performance.csv", rulePerformance, cancellationToken);
        await WriteWindowRobustnessCsvAsync("directional-rule-futures-window-robustness.csv", windowRobustness, cancellationToken);
        await WriteCostSensitivityCsvAsync("directional-rule-futures-cost-sensitivity.csv", costSensitivity, cancellationToken);
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
        IReadOnlyList<DirectionalRuleFuturesSummaryRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,Direction,Symbol,Interval,WindowLabel,EntryMode,TargetPercent,StopPercent,MaxHoldMinutes,CostScenarioLabel,TradeCount,NetWinnerCount,GrossPnlQuote,NetPnlQuote,AvgNetPnlPerTrade,MedianNetPnlPerTrade,ProfitTargetRate,StopLossRate,TimeStopRate,Verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.RuleName), row.Direction, row.Symbol, Csv(row.Interval), Csv(row.WindowLabel),
                Csv(row.EntryMode), row.TargetPercent, row.StopPercent, row.MaxHoldMinutes, Csv(row.CostScenarioLabel),
                row.TradeCount, row.NetWinnerCount, row.GrossPnlQuote, row.NetPnlQuote,
                row.AvgNetPnlPerTrade?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.MedianNetPnlPerTrade?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.ProfitTargetRate, row.StopLossRate, row.TimeStopRate, Csv(row.Verdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private async Task WriteTradesCsvAsync(
        string fileName,
        IReadOnlyList<DirectionalRuleFuturesTradeRecord> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,Direction,Symbol,Interval,WindowLabel,TimeUtc,EntryPrice,ExitPrice,ExitReason,TargetPercent,StopPercent,MaxHoldMinutes,CostScenarioLabel,GrossPnlQuote,NetPnlQuote,FundingEstimateQuote,SlippageEstimateQuote,BtcReturn30mPercent,VolatilityRegime,RangeWidthPercent,DistanceFromRecentHighPercent,DistanceFromRecentLowPercent,AtrPercent,TrendSlopePercent,MfePercent,MaePercent,DurationMinutes,EntryMode");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.RuleName), row.Direction, row.Symbol, Csv(row.Interval), Csv(row.WindowLabel),
                row.TimeUtc.ToString("O", CultureInfo.InvariantCulture),
                row.EntryPrice, row.ExitPrice, Csv(row.ExitReason),
                row.TargetPercent, row.StopPercent, row.MaxHoldMinutes, Csv(row.CostScenarioLabel),
                row.GrossPnlQuote, row.NetPnlQuote, row.FundingEstimateQuote, row.SlippageEstimateQuote,
                row.BtcReturn30mPercent?.ToString(CultureInfo.InvariantCulture) ?? "",
                Csv(row.VolatilityRegime), row.RangeWidthPercent, row.DistanceFromRecentHighPercent,
                row.DistanceFromRecentLowPercent, row.AtrPercent, row.TrendSlopePercent,
                row.MfePercent?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.MaePercent?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.DurationMinutes, Csv(row.EntryMode)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private async Task WriteRulePerformanceCsvAsync(
        string fileName,
        IReadOnlyList<DirectionalRuleFuturesRulePerformanceRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,Direction,Symbol,Interval,EntryMode,TargetPercent,StopPercent,MaxHoldMinutes,CostScenarioLabel,TradeCount,NetPnlQuote,GrossPnlQuote,AvgNetPnlPerTrade,ProfitTargetRate,StopLossRate,TimeStopRate,Verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.RuleName), row.Direction, row.Symbol, Csv(row.Interval), Csv(row.EntryMode),
                row.TargetPercent, row.StopPercent, row.MaxHoldMinutes, Csv(row.CostScenarioLabel),
                row.TradeCount, row.NetPnlQuote, row.GrossPnlQuote,
                row.AvgNetPnlPerTrade?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.ProfitTargetRate, row.StopLossRate, row.TimeStopRate, Csv(row.Verdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private async Task WriteWindowRobustnessCsvAsync(
        string fileName,
        IReadOnlyList<DirectionalRuleFuturesWindowRobustnessRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,Direction,Symbol,Interval,EntryMode,TargetPercent,StopPercent,MaxHoldMinutes,CostScenarioLabel,Window30dTrades,Window60dTrades,Window90dTrades,Window30dNetPnl,Window60dNetPnl,Window90dNetPnl,RobustnessVerdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.RuleName), row.Direction, row.Symbol, Csv(row.Interval), Csv(row.EntryMode),
                row.TargetPercent, row.StopPercent, row.MaxHoldMinutes, Csv(row.CostScenarioLabel),
                row.Window30dTrades, row.Window60dTrades, row.Window90dTrades,
                row.Window30dNetPnl, row.Window60dNetPnl, row.Window90dNetPnl, Csv(row.RobustnessVerdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private async Task WriteCostSensitivityCsvAsync(
        string fileName,
        IReadOnlyList<DirectionalRuleFuturesCostSensitivityRow> rows,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RuleName,Direction,CostScenarioLabel,RoundTripCostPercent,FundingRatePercentPerHour,TradeCount,NetPnlQuote,AvgNetPnlPerTrade,MedianNetPnlPerTrade,Verdict");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(row.RuleName), row.Direction, Csv(row.CostScenarioLabel),
                row.RoundTripCostPercent, row.FundingRatePercentPerHour,
                row.TradeCount, row.NetPnlQuote,
                row.AvgNetPnlPerTrade?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.MedianNetPnlPerTrade?.ToString(CultureInfo.InvariantCulture) ?? "",
                Csv(row.Verdict)));
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString(), cancellationToken);
    }

    private static string FormatTradeCsvLine(DirectionalRuleFuturesTradeRecord row)
        => string.Join(",",
            Csv(row.RuleName), row.Direction, row.Symbol, Csv(row.Interval), Csv(row.WindowLabel),
            row.TimeUtc.ToString("O", CultureInfo.InvariantCulture),
            row.EntryPrice, row.ExitPrice, Csv(row.ExitReason),
            row.TargetPercent, row.StopPercent, row.MaxHoldMinutes, Csv(row.CostScenarioLabel),
            row.GrossPnlQuote, row.NetPnlQuote, row.FundingEstimateQuote, row.SlippageEstimateQuote,
            row.BtcReturn30mPercent?.ToString(CultureInfo.InvariantCulture) ?? "",
            Csv(row.VolatilityRegime), row.RangeWidthPercent, row.DistanceFromRecentHighPercent,
            row.DistanceFromRecentLowPercent, row.AtrPercent, row.TrendSlopePercent,
            row.MfePercent?.ToString(CultureInfo.InvariantCulture) ?? "",
            row.MaePercent?.ToString(CultureInfo.InvariantCulture) ?? "",
            row.DurationMinutes, Csv(row.EntryMode));

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
