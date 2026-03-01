using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Domain.Interfaces.Repositories;

public interface ITradeExecutionRepository
{
    Task<Guid> InsertAsync(TradeExecution execution, CancellationToken cancellationToken = default);
    Task<TradeExecution?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TradeExecution?> GetByExchangeTradeIdAsync(long exchangeTradeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TradeExecution>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TradeExecution>> GetBySymbolAsync(TradingSymbol symbol, CancellationToken cancellationToken = default);
}

