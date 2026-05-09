using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Analytics;
using TradingBot.Domain.Models.Trading;
using Microsoft.Extensions.Logging;

namespace TradingBot.Application.Services;

public class TradeAnalyticsService(
    IPositionRepository positionRepository,
    ILogger<TradeAnalyticsService> logger) : ITradeAnalyticsService
{

    public async Task<TradeAnalyticsSummary> GetSummary(CancellationToken cancellationToken = default)
    {
        var closed = await GetClosedPositionsAsync(cancellationToken);

        if (closed.Count == 0)
        {
            return new TradeAnalyticsSummary();
        }

        var totalTrades = closed.Count;
        var totalPnl = closed.Sum(x => x.RealizedPnl);

        var wins = closed.Where(x => x.RealizedPnl > 0).Select(x => x.RealizedPnl).ToList();
        var losses = closed.Where(x => x.RealizedPnl < 0).Select(x => x.RealizedPnl).ToList();

        var winRate = (decimal)wins.Count / totalTrades * 100m;

        var avgWin = wins.Count == 0 ? 0m : wins.Average();
        var avgLoss = losses.Count == 0 ? 0m : Math.Abs(losses.Average());

        var maxDrawdown = CalculateMaxDrawdown(closed);

        return new TradeAnalyticsSummary
        {
            TotalPnl = totalPnl,
            WinRate = winRate,
            AverageWin = avgWin,
            AverageLoss = avgLoss,
            TotalTrades = totalTrades,
            MaxDrawdown = maxDrawdown
        };
    }

    public async Task<decimal> GetTotalPnL(CancellationToken cancellationToken = default)
        => (await GetSummary(cancellationToken)).TotalPnl;

    public async Task<decimal> GetWinRate(CancellationToken cancellationToken = default)
        => (await GetSummary(cancellationToken)).WinRate;

    public async Task<decimal> GetAverageWin(CancellationToken cancellationToken = default)
        => (await GetSummary(cancellationToken)).AverageWin;

    public async Task<decimal> GetAverageLoss(CancellationToken cancellationToken = default)
        => (await GetSummary(cancellationToken)).AverageLoss;

    public async Task<int> GetTotalTrades(CancellationToken cancellationToken = default)
        => (await GetSummary(cancellationToken)).TotalTrades;

    public async Task<decimal> GetMaxDrawdown(CancellationToken cancellationToken = default)
        => (await GetSummary(cancellationToken)).MaxDrawdown;

    private static decimal CalculateMaxDrawdown(IReadOnlyList<Position> positions)
    {
        decimal equity = 0m;
        decimal peak = 0m;
        decimal maxDrawdown = 0m;

        foreach (var p in positions)
        {
            equity += p.RealizedPnl;

            if (equity > peak)
                peak = equity;

            var drawdown = peak - equity;

            if (drawdown > maxDrawdown)
                maxDrawdown = drawdown;
        }

        return maxDrawdown;
    }

    private async Task<IReadOnlyList<Position>> GetClosedPositionsAsync(CancellationToken cancellationToken)
    {
        var closed = await positionRepository.GetClosedPositionsAsync(cancellationToken);

        var included = closed
            .Where(x => !x.IsOpen && x.ClosedAt.HasValue && x.ExitPrice.HasValue)
            .OrderBy(x => x.ClosedAt ?? x.UpdatedAt)
            .ThenBy(x => x.Id)
            .ToList();

        foreach (var position in included.Where(x => !x.ExitReason.HasValue))
        {
            logger.LogWarning(
                "Trade analytics included closed position with null exit reason. PositionId={PositionId}, Symbol={Symbol}, ClosedAt={ClosedAt}, ExitPrice={ExitPrice}",
                position.Id,
                position.Symbol,
                position.ClosedAt,
                position.ExitPrice);
        }

        return included;
    }

}
