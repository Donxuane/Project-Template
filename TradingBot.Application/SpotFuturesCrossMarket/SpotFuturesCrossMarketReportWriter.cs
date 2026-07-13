using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.SpotFuturesCrossMarket;

/// <summary>
/// JSON + CSV reporting artifacts for the SpotFuturesCrossMarketTestnetV1 strategy, built
/// from the environment-scoped positions in the database (same pattern as the ETH15 report
/// writer): summary, trade history, and equity curve with max drawdown.
/// </summary>
public sealed class SpotFuturesCrossMarketReportWriter(ILogger<SpotFuturesCrossMarketReportWriter> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task WriteAsync(
        string outputDirectory,
        IReadOnlyList<Position> closedPositions,
        IReadOnlyList<Position> openPositions,
        CrossMarketDecision? lastDecision,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var ordered = closedPositions
            .OrderBy(p => p.ClosedAt ?? p.UpdatedAt)
            .ThenBy(p => p.Id)
            .ToList();

        var report = BuildReport(ordered, openPositions, lastDecision);

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "spot-futures-cross-market-summary.json"),
            JsonSerializer.Serialize(report, JsonOptions),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "spot-futures-cross-market-trade-history.csv"),
            BuildTradeHistoryCsv(report.Trades),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "spot-futures-cross-market-equity-curve.csv"),
            BuildEquityCurveCsv(report.EquityCurve),
            cancellationToken);

        logger.LogInformation(
            "SpotFuturesCrossMarket reports written. OutputDirectory={OutputDirectory} Trades={Trades} TotalPnl={TotalPnl} WinRate={WinRate} MaxDrawdown={MaxDrawdown} OpenPositions={OpenPositions}",
            outputDirectory, report.TotalTrades, report.TotalRealizedPnl, report.WinRate, report.MaxDrawdown, report.OpenPositionCount);
    }

    private static CrossMarketReport BuildReport(
        List<Position> ordered,
        IReadOnlyList<Position> openPositions,
        CrossMarketDecision? lastDecision)
    {
        var trades = new List<CrossMarketTradeRow>();
        var equity = new List<CrossMarketEquityPoint>();

        decimal cumulative = 0m, peak = 0m, maxDrawdown = 0m, grossProfit = 0m, grossLoss = 0m;
        int wins = 0, losses = 0, maxConsecutiveLosses = 0, currentConsecutiveLosses = 0;

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

            trades.Add(new CrossMarketTradeRow
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

            equity.Add(new CrossMarketEquityPoint
            {
                TimestampUtc = p.ClosedAt ?? p.UpdatedAt,
                TradePnl = pnl,
                CumulativePnl = cumulative,
                Drawdown = drawdown
            });
        }

        var totalTrades = ordered.Count;
        return new CrossMarketReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            ExecutionEnvironment = SpotFuturesCrossMarketSettings.ExecutionEnvironment,
            StrategyName = SpotFuturesCrossMarketSettings.StrategyName,
            TotalTrades = totalTrades,
            Wins = wins,
            Losses = losses,
            WinRate = totalTrades > 0 ? (decimal)wins / totalTrades : 0m,
            TotalRealizedPnl = cumulative,
            ProfitFactor = grossLoss > 0m ? grossProfit / grossLoss : (grossProfit > 0m ? 999m : 0m),
            MaxConsecutiveLosses = maxConsecutiveLosses,
            CurrentConsecutiveLosses = currentConsecutiveLosses,
            MaxDrawdown = maxDrawdown,
            OpenPositionCount = openPositions.Count,
            OpenUnrealizedPnl = openPositions.Sum(p => p.UnrealizedPnl),
            LastDecision = lastDecision?.Action.ToString() ?? string.Empty,
            LastDecisionReason = lastDecision?.Reason ?? string.Empty,
            Trades = trades,
            EquityCurve = equity
        };
    }

    private static string BuildTradeHistoryCsv(IReadOnlyList<CrossMarketTradeRow> trades)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PositionId,Symbol,Side,Quantity,EntryPrice,ExitPrice,OpenedAtUtc,ClosedAtUtc,ExitReason,RealizedPnl,CumulativePnl");
        foreach (var t in trades)
        {
            sb.Append(t.PositionId).Append(',')
              .Append(t.Symbol).Append(',')
              .Append(t.Side).Append(',')
              .Append(Dec(t.Quantity)).Append(',')
              .Append(Dec(t.EntryPrice)).Append(',')
              .Append(t.ExitPrice.HasValue ? Dec(t.ExitPrice.Value) : string.Empty).Append(',')
              .Append(Iso(t.OpenedAtUtc)).Append(',')
              .Append(Iso(t.ClosedAtUtc)).Append(',')
              .Append(t.ExitReason ?? string.Empty).Append(',')
              .Append(Dec(t.RealizedPnl)).Append(',')
              .Append(Dec(t.CumulativePnl))
              .AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildEquityCurveCsv(IReadOnlyList<CrossMarketEquityPoint> points)
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
}

public sealed class CrossMarketReport
{
    public DateTime GeneratedAtUtc { get; init; }
    public string ExecutionEnvironment { get; init; } = string.Empty;
    public string StrategyName { get; init; } = string.Empty;
    public int TotalTrades { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public decimal WinRate { get; init; }
    public decimal TotalRealizedPnl { get; init; }
    public decimal ProfitFactor { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public int CurrentConsecutiveLosses { get; init; }
    public decimal MaxDrawdown { get; init; }
    public int OpenPositionCount { get; init; }
    public decimal OpenUnrealizedPnl { get; init; }
    public string LastDecision { get; init; } = string.Empty;
    public string LastDecisionReason { get; init; } = string.Empty;
    public IReadOnlyList<CrossMarketTradeRow> Trades { get; init; } = Array.Empty<CrossMarketTradeRow>();
    public IReadOnlyList<CrossMarketEquityPoint> EquityCurve { get; init; } = Array.Empty<CrossMarketEquityPoint>();
}

public sealed class CrossMarketTradeRow
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

public sealed class CrossMarketEquityPoint
{
    public DateTime TimestampUtc { get; init; }
    public decimal TradePnl { get; init; }
    public decimal CumulativePnl { get; init; }
    public decimal Drawdown { get; init; }
}
