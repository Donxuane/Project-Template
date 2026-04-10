using System.Data;
using Dapper;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Percistance.Repositories;

public class OrderRepository(IDbConnection connection) : IOrderRepository
{
    public async Task<Guid> InsertAsync(Order order, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO orders
                (id, exchange_order_id, symbol, side, status, price, quantity, created_at, updated_at)
            VALUES
                (@Id, @ExchangeOrderId, @Symbol, @Side, @Status, @Price, @Quantity, @CreatedAt, @UpdatedAt);
            """;

        order.CreatedAt = DateTime.UtcNow;
        order.UpdatedAt = order.CreatedAt;

        await connection.ExecuteAsync(new CommandDefinition(sql, order, cancellationToken: cancellationToken));
        return order.Id;
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
                price = @Price,
                quantity = @Quantity,
                updated_at = @UpdatedAt
            WHERE id = @Id;
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, order, cancellationToken: cancellationToken));
    }

    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, exchange_order_id AS ExchangeOrderId, symbol, side, status, price, quantity, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM orders
            WHERE id = @Id;
            """;

        return await connection.QuerySingleOrDefaultAsync<Order>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<Order?> GetByExchangeOrderIdAsync(long exchangeOrderId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, exchange_order_id AS ExchangeOrderId, symbol, side, status, price, quantity, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM orders
            WHERE exchange_order_id = @ExchangeOrderId;
            """;

        return await connection.QuerySingleOrDefaultAsync<Order>(
            new CommandDefinition(sql, new { ExchangeOrderId = exchangeOrderId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Order>> GetOpenOrdersAsync(TradingSymbol? symbol = null, CancellationToken cancellationToken = default)
    {
        var openStatuses = new[]
        {
            OrderStatuses.NEW,
            OrderStatuses.PARTIALLY_FILLED,
            OrderStatuses.PENDING_NEW,
            OrderStatuses.PENDING_CANCEL
        };

        const string baseSql = """
            SELECT id, exchange_order_id AS ExchangeOrderId, symbol, side, status, price, quantity, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM orders
            WHERE status = ANY(@Statuses)
            """;

        var sql = symbol.HasValue
            ? baseSql + " AND symbol = @Symbol ORDER BY created_at DESC;"
            : baseSql + " ORDER BY created_at DESC;";

        var param = new
        {
            Statuses = openStatuses,
            Symbol = symbol
        };

        var result = await connection.QueryAsync<Order>(
            new CommandDefinition(sql, param, cancellationToken: cancellationToken));

        return result.ToList();
    }
}

