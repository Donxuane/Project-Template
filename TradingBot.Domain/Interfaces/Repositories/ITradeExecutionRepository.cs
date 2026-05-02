using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Domain.Interfaces.Repositories;

public interface ITradeExecutionRepository
{
    Task<long> InsertAsync(TradeExecution execution, CancellationToken cancellationToken = default);
    Task<TradeExecution?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<TradeExecution?> GetByExchangeTradeIdAsync(long exchangeTradeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TradeExecution>> GetByOrderIdAsync(long orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TradeExecution>> GetBySymbolAsync(TradingSymbol symbol, CancellationToken cancellationToken = default);
    Task MarkPositionProcessedByOrderAsync(long orderId, DateTime processedAtUtc, CancellationToken cancellationToken = default);
}

