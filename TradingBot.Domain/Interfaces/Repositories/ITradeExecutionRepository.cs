using TradingBot.Domain.Models.Trading;

namespace TradingBot.Domain.Interfaces.Repositories;

public interface ITradeExecutionRepository
{
    Task<long> InsertAsync(TradeExecution execution, CancellationToken cancellationToken = default);
}
