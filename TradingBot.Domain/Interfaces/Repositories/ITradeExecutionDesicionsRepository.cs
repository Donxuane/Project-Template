using TradingBot.Domain.Models.Decision;

namespace TradingBot.Domain.Interfaces.Repositories;

public interface ITradeExecutionDesicionsRepository
{
    public Task<long> AddDesicionAsync(TradeExecutionDecisions desicion);
    public Task UpdateDesicionAsync(TradeExecutionDecisions desicion);
}
