using TradingBot.Domain.Models.Analytics;

namespace TradingBot.Domain.Interfaces.Services;

public interface ITradeAnalyticsService
{
    Task<decimal> GetTotalPnL(CancellationToken cancellationToken = default);
    Task<decimal> GetWinRate(CancellationToken cancellationToken = default);
    Task<decimal> GetAverageWin(CancellationToken cancellationToken = default);
    Task<decimal> GetAverageLoss(CancellationToken cancellationToken = default);
    Task<int> GetTotalTrades(CancellationToken cancellationToken = default);
    Task<decimal> GetMaxDrawdown(CancellationToken cancellationToken = default);
    Task<TradeAnalyticsSummary> GetSummary(CancellationToken cancellationToken = default);
}
