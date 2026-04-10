
using TradingBot.Domain.Models.Analytics;

namespace TradingBot.Domain.Interfaces.Repositories;
public interface IAnalyticsRepository
{
    public Task StoreAnalytics(TradeAnalyticsSummary tradeAnalyticsSummary);
}
