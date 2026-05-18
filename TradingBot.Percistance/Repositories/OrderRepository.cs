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
                (exchange_order_id, correlationid, parent_position_id, order_source, close_reason, symbol, side, status, processing_status, sync_retry_count, price, quantity, created_at, updated_at)
            VALUES
                (@ExchangeOrderId, @CorrelationId, @ParentPositionId, @OrderSource, @CloseReason, @Symbol, @Side, @Status, @ProcessingStatus, @SyncRetryCount, @Price, @Quantity, @CreatedAt, @UpdatedAt)
            RETURNING id;
            """;

        order.CreatedAt = DateTime.UtcNow;
        order.UpdatedAt = order.CreatedAt;

        var param = new
        {
            order.ExchangeOrderId,
            order.CorrelationId,
            order.ParentPositionId,
            OrderSource = (int)order.OrderSource,
            CloseReason = (int)order.CloseReason,
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
                correlationid = @CorrelationId,
                parent_position_id = @ParentPositionId,
                order_source = @OrderSource,
                close_reason = @CloseReason,
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
            order.CorrelationId,
            order.ParentPositionId,
            OrderSource = (int)order.OrderSource,
            CloseReason = (int)order.CloseReason,
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
            SELECT id, exchange_order_id AS ExchangeOrderId, correlationid AS CorrelationId, parent_position_id AS ParentPositionId, order_source AS OrderSource, close_reason AS CloseReason, symbol, side, status, processing_status AS ProcessingStatus, sync_retry_count AS SyncRetryCount, price, quantity, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM orders
            WHERE id = @Id;
            """;

        return await connection.QuerySingleOrDefaultAsync<Order>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<Order?> GetByExchangeOrderIdAsync(long exchangeOrderId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, exchange_order_id AS ExchangeOrderId, correlationid AS CorrelationId, parent_position_id AS ParentPositionId, order_source AS OrderSource, close_reason AS CloseReason, symbol, side, status, processing_status AS ProcessingStatus, sync_retry_count AS SyncRetryCount, price, quantity, created_at AS CreatedAt, updated_at AS UpdatedAt
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
            SELECT id, exchange_order_id AS ExchangeOrderId, correlationid AS CorrelationId, parent_position_id AS ParentPositionId, order_source AS OrderSource, close_reason AS CloseReason, symbol, side, status, processing_status AS ProcessingStatus, sync_retry_count AS SyncRetryCount, price, quantity, created_at AS CreatedAt, updated_at AS UpdatedAt
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
            SELECT id, exchange_order_id AS ExchangeOrderId, correlationid AS CorrelationId, parent_position_id AS ParentPositionId, order_source AS OrderSource, close_reason AS CloseReason, symbol, side, status, processing_status AS ProcessingStatus, sync_retry_count AS SyncRetryCount, price, quantity, created_at AS CreatedAt, updated_at AS UpdatedAt
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
            SELECT id, exchange_order_id AS ExchangeOrderId, correlationid AS CorrelationId, parent_position_id AS ParentPositionId, order_source AS OrderSource, close_reason AS CloseReason, symbol, side, status, processing_status AS ProcessingStatus, sync_retry_count AS SyncRetryCount, price, quantity, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM orders
            WHERE processing_status = @ProcessingStatus
            ORDER BY created_at ASC
            """ + (limit.HasValue ? " LIMIT @Limit" : "") + ";";

        var result = await connection.QueryAsync<Order>(
            new CommandDefinition(sql, new { ProcessingStatus = (int)processingStatus, Limit = limit }, cancellationToken: cancellationToken));

        return result.ToList();
    }

    public async Task<int> GetInFlightOpeningOrderCountAsync(CancellationToken cancellationToken = default)
    {
        var terminalStatuses = new[]
        {
            OrderStatuses.CANCELED,
            OrderStatuses.REJECTED,
            OrderStatuses.EXPIRED,
            OrderStatuses.EXPIRED_IN_MATCH
        };

        var terminalProcessingStatuses = new[]
        {
            ProcessingStatus.PositionUpdated,
            ProcessingStatus.Completed
        };

        const string sql = """
            SELECT COUNT(1)
            FROM orders
            WHERE side = @BuySide
              AND close_reason = @CloseReasonNone
              AND status <> ALL(@TerminalStatuses)
              AND processing_status <> ALL(@TerminalProcessingStatuses);
            """;

        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                sql,
                new
                {
                    BuySide = (int)OrderSide.BUY,
                    CloseReasonNone = (int)CloseReason.None,
                    TerminalStatuses = terminalStatuses.Select(x => (int)x).ToArray(),
                    TerminalProcessingStatuses = terminalProcessingStatuses.Select(x => (int)x).ToArray()
                },
                cancellationToken: cancellationToken));
    }

    public async Task<bool> HasActiveCloseOrderForPositionAsync(long parentPositionId, CancellationToken cancellationToken = default)
    {
        return await HasInFlightClosingOrderForPositionAsync(parentPositionId, cancellationToken);
    }

    public async Task<bool> HasInFlightClosingOrderForPositionAsync(long parentPositionId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT 1
            FROM orders
            WHERE parent_position_id = @ParentPositionId
              AND side = @SellSide
              AND close_reason <> @CloseReasonNone
              AND status <> ALL(@TerminalStatuses)
              AND processing_status <> ALL(@AccountedProcessingStatuses)
            LIMIT 1;
            """;

        var result = await connection.QuerySingleOrDefaultAsync<int?>(
            new CommandDefinition(
                sql,
                new
                {
                    ParentPositionId = parentPositionId,
                    SellSide = (int)OrderSide.SELL,
                    CloseReasonNone = (int)CloseReason.None,
                    TerminalStatuses = new[]
                    {
                        (int)OrderStatuses.CANCELED,
                        (int)OrderStatuses.REJECTED,
                        (int)OrderStatuses.EXPIRED,
                        (int)OrderStatuses.EXPIRED_IN_MATCH
                    },
                    AccountedProcessingStatuses = new[]
                    {
                        (int)ProcessingStatus.PositionUpdated,
                        (int)ProcessingStatus.Completed
                    }
                },
                cancellationToken: cancellationToken));

        return result.HasValue;
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
            SELECT id, exchange_order_id AS ExchangeOrderId, correlationid AS CorrelationId, parent_position_id AS ParentPositionId, order_source AS OrderSource, close_reason AS CloseReason, symbol, side, status, processing_status AS ProcessingStatus, sync_retry_count AS SyncRetryCount, price, quantity, created_at AS CreatedAt, updated_at AS UpdatedAt
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
            SELECT id, exchange_order_id AS ExchangeOrderId, correlationid AS CorrelationId, parent_position_id AS ParentPositionId, order_source AS OrderSource, close_reason AS CloseReason, symbol, side, status, processing_status AS ProcessingStatus, sync_retry_count AS SyncRetryCount, price, quantity, created_at AS CreatedAt, updated_at AS UpdatedAt
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

