using System.Data;
using Dapper;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Percistance.Repositories;

public sealed class OrderRepository(IDbConnection connection) : IOrderRepository
{
    public async Task<long> InsertAsync(Order order, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO orders
                (exchange_order_id, correlationid, parent_position_id, order_source, close_reason, symbol, side, status, processing_status, price, quantity, execution_environment, created_at, updated_at)
            VALUES
                (@ExchangeOrderId, @CorrelationId, @ParentPositionId, @OrderSource, @CloseReason, @Symbol, @Side, @Status, @ProcessingStatus, @Price, @Quantity, @ExecutionEnvironment, @CreatedAt, @UpdatedAt)
            RETURNING id;
            """;

        order.CreatedAt = DateTime.UtcNow;
        order.UpdatedAt = order.CreatedAt;
        var parameters = ToParameters(order);
        order.Id = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        return order.Id;
    }

    public async Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        order.UpdatedAt = DateTime.UtcNow;
        const string sql = """
            UPDATE orders
            SET exchange_order_id = @ExchangeOrderId,
                correlationid = @CorrelationId,
                parent_position_id = @ParentPositionId,
                order_source = @OrderSource,
                close_reason = @CloseReason,
                symbol = @Symbol,
                side = @Side,
                status = @Status,
                processing_status = @ProcessingStatus,
                price = @Price,
                quantity = @Quantity,
                execution_environment = @ExecutionEnvironment,
                updated_at = @UpdatedAt
            WHERE id = @Id;
            """;
        await connection.ExecuteAsync(
            new CommandDefinition(sql, ToParameters(order), cancellationToken: cancellationToken));
    }

    public async Task<bool> HasActiveCloseOrderForPositionByEnvironmentAsync(
        long parentPositionId,
        string executionEnvironment,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT 1
            FROM orders
            WHERE parent_position_id = @ParentPositionId
              AND close_reason <> @CloseReasonNone
              AND execution_environment = @ExecutionEnvironment
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
                    CloseReasonNone = (int)CloseReason.None,
                    ExecutionEnvironment = executionEnvironment,
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

    private static object ToParameters(Order order) => new
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
        order.Price,
        order.Quantity,
        order.ExecutionEnvironment,
        order.CreatedAt,
        order.UpdatedAt
    };
}
