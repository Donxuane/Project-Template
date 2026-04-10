using System.Data;
using Dapper;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Percistance.Repositories;

public class OrderRepository(IDbConnection connection) : IOrderRepository
{
    public async Task<long> InsertAsync(Order order, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO orders
                (exchange_order_id, symbol, side, status, processing_status, sync_retry_count, price, quantity, created_at, updated_at)
            VALUES
                (@ExchangeOrderId, @Symbol, @Side, @Status, @ProcessingStatus, @SyncRetryCount, @Price, @Quantity, @CreatedAt, @UpdatedAt)
            RETURNING id;
            """;

        order.CreatedAt = DateTime.UtcNow;
        order.UpdatedAt = order.CreatedAt;

        var param = new
        {
            order.ExchangeOrderId,
            Symbol = (int)order.Symbol,
            Side = (int)order.Side,
            Status = (int)order.Status,
            ProcessingStatus = (int)order.ProcessingStatus,
            order.SyncRetryCount,
            order.Price,
            order.Quantity,
            order.CreatedAt,
            order.UpdatedAt
        };
        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, param, cancellationToken: cancellationToken));
        order.Id = id;
        return id;
    }

    public async Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        order.UpdatedAt = DateTime.UtcNow;

        const string sql = """
            UPDATE orders
            SET
                exchange_order_id = @ExchangeOrderId,
                symbol = @Symbol,
                side = @Side,
                status = @Status,
                processing_status = @ProcessingStatus,
                sync_retry_count = @SyncRetryCount,
                price = @Price,
                quantity = @Quantity,
                updated_at = @UpdatedAt
            WHERE id = @Id;
            """;

        var param = new
        {
            order.Id,
            order.ExchangeOrderId,
            Symbol = (int)order.Symbol,
            Side = (int)order.Side,
            Status = (int)order.Status,
            ProcessingStatus = (int)order.ProcessingStatus,
            order.SyncRetryCount,
            order.Price,
            order.Quantity,
            order.UpdatedAt
        };
        await connection.ExecuteAsync(new CommandDefinition(sql, param, cancellationToken: cancellationToken));
    }

    public async Task<Order?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, exchange_order_id AS ExchangeOrderId, symbol, side, status, processing_status AS ProcessingStatus, sync_retry_count AS SyncRetryCount, price, quantity, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM orders
            WHERE id = @Id;
            """;

        return await connection.QuerySingleOrDefaultAsync<Order>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<Order?> GetByExchangeOrderIdAsync(long exchangeOrderId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, exchange_order_id AS ExchangeOrderId, symbol, side, status, processing_status AS ProcessingStatus, sync_retry_count AS SyncRetryCount, price, quantity, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM orders
            WHERE exchange_order_id = @ExchangeOrderId;
            """;

        return await connection.QuerySingleOrDefaultAsync<Order>(
            new CommandDefinition(sql, new { ExchangeOrderId = exchangeOrderId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Order>> GetOpenOrdersAsync(TradingSymbol? symbol = null, int? limit = null, CancellationToken cancellationToken = default)
    {
        var openStatuses = new[]
        {
            OrderStatuses.NEW,
            OrderStatuses.PARTIALLY_FILLED,
            OrderStatuses.PENDING_NEW,
            OrderStatuses.PENDING_CANCEL
        };

        const string baseSql = """
            SELECT id, exchange_order_id AS ExchangeOrderId, symbol, side, status, processing_status AS ProcessingStatus, sync_retry_count AS SyncRetryCount, price, quantity, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM orders
            WHERE status = ANY(@Statuses)
            """;

        var orderAndLimit = " ORDER BY created_at ASC" + (limit.HasValue ? " LIMIT @Limit" : "");
        var sql = symbol.HasValue
            ? baseSql + " AND symbol = @Symbol" + orderAndLimit + ";"
            : baseSql + orderAndLimit + ";";

        var param = new
        {
            Statuses = openStatuses.Select(x => (int)x).ToArray(),
            Symbol = symbol.HasValue ? (int?)symbol.Value : null,
            Limit = limit
        };

        var result = await connection.QueryAsync<Order>(
            new CommandDefinition(sql, param, cancellationToken: cancellationToken));

        return result.ToList();
    }

    public async Task<IReadOnlyList<Order>> GetFilledOrdersAsync(CancellationToken cancellationToken = default)
    {
        var completedStatuses = new[]
        {
            OrderStatuses.FILLED,
            OrderStatuses.PARTIALLY_FILLED
        };

        const string sql = """
            SELECT id, exchange_order_id AS ExchangeOrderId, symbol, side, status, processing_status AS ProcessingStatus, sync_retry_count AS SyncRetryCount, price, quantity, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM orders
            WHERE status = ANY(@Statuses)
            ORDER BY created_at DESC;
            """;

        var result = await connection.QueryAsync<Order>(
            new CommandDefinition(sql, new { Statuses = completedStatuses.Select(x => (int)x).ToArray() }, cancellationToken: cancellationToken));

        return result.ToList();
    }

    public async Task<IReadOnlyList<Order>> GetOrdersByProcessingStatusAsync(ProcessingStatus processingStatus, int? limit = null, CancellationToken cancellationToken = default)
    {
        var sql = """
            SELECT id, exchange_order_id AS ExchangeOrderId, symbol, side, status, processing_status AS ProcessingStatus, sync_retry_count AS SyncRetryCount, price, quantity, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM orders
            WHERE processing_status = @ProcessingStatus
            ORDER BY created_at ASC
            """ + (limit.HasValue ? " LIMIT @Limit" : "") + ";";

        var result = await connection.QueryAsync<Order>(
            new CommandDefinition(sql, new { ProcessingStatus = (int)processingStatus, Limit = limit }, cancellationToken: cancellationToken));

        return result.ToList();
    }

    public async Task<IReadOnlyList<Order>> GetOpenOrdersForWorkerAsync(IDbTransaction transaction, TradingSymbol? symbol, int limit, CancellationToken cancellationToken = default)
    {
        var openStatuses = new[]
        {
            OrderStatuses.NEW,
            OrderStatuses.PARTIALLY_FILLED,
            OrderStatuses.PENDING_NEW,
            OrderStatuses.PENDING_CANCEL
        };

        var baseSql = """
            SELECT id, exchange_order_id AS ExchangeOrderId, symbol, side, status, processing_status AS ProcessingStatus, sync_retry_count AS SyncRetryCount, price, quantity, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM orders
            WHERE status = ANY(@Statuses)
            """;
        var sql = symbol.HasValue
            ? baseSql + " AND symbol = @Symbol ORDER BY created_at ASC FOR UPDATE SKIP LOCKED LIMIT @Limit;"
            : baseSql + " ORDER BY created_at ASC FOR UPDATE SKIP LOCKED LIMIT @Limit;";

        var param = new
        {
            Statuses = openStatuses.Select(x => (int)x).ToArray(),
            Symbol = symbol.HasValue ? (int?)symbol.Value : null,
            Limit = limit
        };

        var result = await connection.QueryAsync<Order>(
            new CommandDefinition(sql, param, transaction, cancellationToken: cancellationToken));
        return result.ToList();
    }

    public async Task<IReadOnlyList<Order>> GetOrdersByProcessingStatusForWorkerAsync(IDbTransaction transaction, ProcessingStatus processingStatus, int limit, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, exchange_order_id AS ExchangeOrderId, symbol, side, status, processing_status AS ProcessingStatus, sync_retry_count AS SyncRetryCount, price, quantity, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM orders
            WHERE processing_status = @ProcessingStatus
            ORDER BY created_at ASC
            FOR UPDATE SKIP LOCKED
            LIMIT @Limit;
            """;

        var result = await connection.QueryAsync<Order>(
            new CommandDefinition(sql, new { ProcessingStatus = (int)processingStatus, Limit = limit }, transaction, cancellationToken: cancellationToken));
        return result.ToList();
    }
}

