using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.TestnetExecution;

/// <summary>
/// Produces the ETH15 testnet reporting artifacts (JSON + CSV) from the testnet-scoped
/// positions persisted in the database: trade history, running PnL, win rate, consecutive
/// losses, equity curve, and maximum drawdown. Live-spot rows are excluded by construction
/// because the writer is fed only testnet-environment positions.
/// </summary>
public sealed class Eth15TestnetReportWriter(ILogger<Eth15TestnetReportWriter> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task WriteAsync(
        string outputDirectory,
        IReadOnlyList<Position> closedPositions,
        IReadOnlyList<Position> openPositions,
        Eth15GateDecision? lastGateDecision,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var ordered = closedPositions
            .OrderBy(p => p.ClosedAt ?? p.UpdatedAt)
            .ThenBy(p => p.Id)
            .ToList();

        var report = BuildReport(ordered, openPositions, lastGateDecision);

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "eth15-testnet-summary.json"),
            JsonSerializer.Serialize(report, JsonOptions),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "eth15-testnet-trade-history.json"),
            JsonSerializer.Serialize(report.Trades, JsonOptions),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "eth15-testnet-trade-history.csv"),
            BuildTradeHistoryCsv(report.Trades),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "eth15-testnet-equity-curve.json"),
            JsonSerializer.Serialize(report.EquityCurve, JsonOptions),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "eth15-testnet-equity-curve.csv"),
            BuildEquityCurveCsv(report.EquityCurve),
            cancellationToken);

        logger.LogInformation(
            "ETH15 testnet reports written. OutputDirectory={OutputDirectory} Trades={Trades} TotalPnl={TotalPnl} WinRate={WinRate} MaxConsecutiveLosses={MaxConsecutiveLosses} CurrentConsecutiveLosses={CurrentConsecutiveLosses} MaxDrawdown={MaxDrawdown} OpenPositions={OpenPositions}",
            outputDirectory,
            report.TotalTrades,
            report.TotalRealizedPnl,
            report.WinRate,
            report.MaxConsecutiveLosses,
            report.CurrentConsecutiveLosses,
            report.MaxDrawdown,
            report.OpenPositionCount);
    }

    private static Eth15TestnetReport BuildReport(
        List<Position> ordered,
        IReadOnlyList<Position> openPositions,
        Eth15GateDecision? lastGateDecision)
    {
        var trades = new List<Eth15TestnetTradeRow>();
        var equity = new List<Eth15TestnetEquityPoint>();

        decimal cumulative = 0m;
        decimal peak = 0m;
        decimal maxDrawdown = 0m;
        int wins = 0;
        int losses = 0;
        int maxConsecutiveLosses = 0;
        int currentConsecutiveLosses = 0;
        decimal grossProfit = 0m;
        decimal grossLoss = 0m;

        foreach (var p in ordered)
        {
            var pnl = p.RealizedPnl;
            cumulative += pnl;

            if (pnl > 0m)
            {
                wins++;
                grossProfit += pnl;
                currentConsecutiveLosses = 0;
            }
            else if (pnl < 0m)
            {
                losses++;
                grossLoss += Math.Abs(pnl);
                currentConsecutiveLosses++;
                maxConsecutiveLosses = Math.Max(maxConsecutiveLosses, currentConsecutiveLosses);
            }
            else
            {
                currentConsecutiveLosses = 0;
            }

            peak = Math.Max(peak, cumulative);
            var drawdown = peak - cumulative;
            maxDrawdown = Math.Max(maxDrawdown, drawdown);

            trades.Add(new Eth15TestnetTradeRow
            {
                PositionId = p.Id,
                Symbol = p.Symbol.ToString(),
                Side = p.Side.ToString(),
                Quantity = p.Quantity,
                EntryPrice = p.AveragePrice,
                ExitPrice = p.ExitPrice,
                OpenedAtUtc = p.OpenedAt,
                ClosedAtUtc = p.ClosedAt,
                ExitReason = p.ExitReason?.ToString(),
                RealizedPnl = pnl,
                CumulativePnl = cumulative
            });

            equity.Add(new Eth15TestnetEquityPoint
            {
                TimestampUtc = p.ClosedAt ?? p.UpdatedAt,
                TradePnl = pnl,
                CumulativePnl = cumulative,
                Drawdown = drawdown
            });
        }

        var totalTrades = ordered.Count;
        var winRate = totalTrades > 0 ? (decimal)wins / totalTrades : 0m;
        var profitFactor = grossLoss > 0m ? grossProfit / grossLoss : (grossProfit > 0m ? 999m : 0m);

        return new Eth15TestnetReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            ExecutionEnvironment = Eth15TestnetExecutionSettings.ExecutionEnvironment,
            ProfileName = Eth15TestnetExecutionSettings.ProfileName,
            TotalTrades = totalTrades,
            Wins = wins,
            Losses = losses,
            WinRate = winRate,
            TotalRealizedPnl = cumulative,
            RunningPnl = cumulative,
            ProfitFactor = profitFactor,
            MaxConsecutiveLosses = maxConsecutiveLosses,
            CurrentConsecutiveLosses = currentConsecutiveLosses,
            MaxDrawdown = maxDrawdown,
            OpenPositionCount = openPositions.Count,
            LastVerdict = lastGateDecision?.Verdict ?? string.Empty,
            LastBlockedReason = lastGateDecision?.BlockedReason ?? string.Empty,
            Trades = trades,
            EquityCurve = equity
        };
    }

    private static string BuildTradeHistoryCsv(IReadOnlyList<Eth15TestnetTradeRow> trades)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PositionId,Symbol,Side,Quantity,EntryPrice,ExitPrice,OpenedAtUtc,ClosedAtUtc,ExitReason,RealizedPnl,CumulativePnl");
        foreach (var t in trades)
        {
            sb.Append(t.PositionId).Append(',')
              .Append(Csv(t.Symbol)).Append(',')
              .Append(Csv(t.Side)).Append(',')
              .Append(Dec(t.Quantity)).Append(',')
              .Append(Dec(t.EntryPrice)).Append(',')
              .Append(t.ExitPrice.HasValue ? Dec(t.ExitPrice.Value) : string.Empty).Append(',')
              .Append(Iso(t.OpenedAtUtc)).Append(',')
              .Append(Iso(t.ClosedAtUtc)).Append(',')
              .Append(Csv(t.ExitReason ?? string.Empty)).Append(',')
              .Append(Dec(t.RealizedPnl)).Append(',')
              .Append(Dec(t.CumulativePnl))
              .AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildEquityCurveCsv(IReadOnlyList<Eth15TestnetEquityPoint> points)
    {
        var sb = new StringBuilder();
        sb.AppendLine("TimestampUtc,TradePnl,CumulativePnl,Drawdown");
        foreach (var p in points)
        {
            sb.Append(Iso(p.TimestampUtc)).Append(',')
              .Append(Dec(p.TradePnl)).Append(',')
              .Append(Dec(p.CumulativePnl)).Append(',')
              .Append(Dec(p.Drawdown))
              .AppendLine();
        }

        return sb.ToString();
    }

    private static string Dec(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Iso(DateTime? value) => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Csv(string value)
        => value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}

public sealed class Eth15TestnetReport
{
    public DateTime GeneratedAtUtc { get; init; }
    public string ExecutionEnvironment { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public int TotalTrades { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public decimal WinRate { get; init; }
    public decimal TotalRealizedPnl { get; init; }
    public decimal RunningPnl { get; init; }
    public decimal ProfitFactor { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public int CurrentConsecutiveLosses { get; init; }
    public decimal MaxDrawdown { get; init; }
    public int OpenPositionCount { get; init; }
    public string LastVerdict { get; init; } = string.Empty;
    public string LastBlockedReason { get; init; } = string.Empty;
    public IReadOnlyList<Eth15TestnetTradeRow> Trades { get; init; } = Array.Empty<Eth15TestnetTradeRow>();
    public IReadOnlyList<Eth15TestnetEquityPoint> EquityCurve { get; init; } = Array.Empty<Eth15TestnetEquityPoint>();
}

public sealed class Eth15TestnetTradeRow
{
    public long PositionId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal? ExitPrice { get; init; }
    public DateTime? OpenedAtUtc { get; init; }
    public DateTime? ClosedAtUtc { get; init; }
    public string? ExitReason { get; init; }
    public decimal RealizedPnl { get; init; }
    public decimal CumulativePnl { get; init; }
}

public sealed class Eth15TestnetEquityPoint
{
    public DateTime TimestampUtc { get; init; }
    public decimal TradePnl { get; init; }
    public decimal CumulativePnl { get; init; }
    public decimal Drawdown { get; init; }
}
