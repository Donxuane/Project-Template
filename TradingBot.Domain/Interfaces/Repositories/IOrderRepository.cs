using System.Data;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Domain.Interfaces.Repositories;

public interface IOrderRepository
{
    Task<long> InsertAsync(Order order, CancellationToken cancellationToken = default);
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
    Task<Order?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Order?> GetByExchangeOrderIdAsync(long exchangeOrderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetOpenOrdersAsync(TradingSymbol? symbol = null, int? limit = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetFilledOrdersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetOrdersByProcessingStatusAsync(ProcessingStatus processingStatus, int? limit = null, CancellationToken cancellationToken = default);

    /// <summary>For workers: locks rows with FOR UPDATE SKIP LOCKED, batch limited. Must run inside a transaction.</summary>
    Task<IReadOnlyList<Order>> GetOpenOrdersForWorkerAsync(IDbTransaction transaction, TradingSymbol? symbol, int limit, CancellationToken cancellationToken = default);

    /// <summary>For workers: locks rows with FOR UPDATE SKIP LOCKED, batch limited. Must run inside a transaction.</summary>
    Task<IReadOnlyList<Order>> GetOrdersByProcessingStatusForWorkerAsync(IDbTransaction transaction, ProcessingStatus processingStatus, int limit, CancellationToken cancellationToken = default);
}

