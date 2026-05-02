using System.Data;
using Dapper;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Percistance.Repositories;

public class TradeExecutionRepository(IDbConnection connection) : ITradeExecutionRepository
{
    public async Task<long> InsertAsync(TradeExecution execution, CancellationToken cancellationToken = default)
    {
        execution.CreatedAt = DateTime.UtcNow;
        execution.UpdatedAt = execution.CreatedAt;

        const string sql = """
            INSERT INTO trade_executions
                (order_id, exchange_order_id, exchange_trade_id, symbol, side, price, quantity, quote_quantity, fee, fee_asset, position_processed_at, executed_at, created_at, updated_at)
            VALUES
                (@OrderId, @ExchangeOrderId, @ExchangeTradeId, @Symbol, @Side, @Price, @Quantity, @QuoteQuantity, @Fee, @FeeAsset, @PositionProcessedAt, @ExecutedAt, @CreatedAt, @UpdatedAt)
            RETURNING id;
            """;

        var param = new
        {
            execution.OrderId,
            execution.ExchangeOrderId,
            execution.ExchangeTradeId,
            Symbol = (int)execution.Symbol,
            Side = (int)execution.Side,
            execution.Price,
            execution.Quantity,
            execution.QuoteQuantity,
            execution.Fee,
            execution.FeeAsset,
            execution.PositionProcessedAt,
            execution.ExecutedAt,
            execution.CreatedAt,
            execution.UpdatedAt
        };
        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, param, cancellationToken: cancellationToken));
        execution.Id = id;
        return id;
    }

    public async Task<TradeExecution?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, order_id AS OrderId, exchange_order_id AS ExchangeOrderId, exchange_trade_id AS ExchangeTradeId,
                   symbol, side, price, quantity, quote_quantity AS QuoteQuantity, fee, fee_asset AS FeeAsset,
                   position_processed_at AS PositionProcessedAt, executed_at AS ExecutedAt, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM trade_executions
            WHERE id = @Id;
            """;

        return await connection.QuerySingleOrDefaultAsync<TradeExecution>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<TradeExecution?> GetByExchangeTradeIdAsync(long exchangeTradeId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, order_id AS OrderId, exchange_order_id AS ExchangeOrderId, exchange_trade_id AS ExchangeTradeId,
                   symbol, side, price, quantity, quote_quantity AS QuoteQuantity, fee, fee_asset AS FeeAsset,
                   position_processed_at AS PositionProcessedAt, executed_at AS ExecutedAt, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM trade_executions
            WHERE exchange_trade_id = @ExchangeTradeId;
            """;

        return await connection.QuerySingleOrDefaultAsync<TradeExecution>(
            new CommandDefinition(sql, new { ExchangeTradeId = exchangeTradeId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<TradeExecution>> GetByOrderIdAsync(long orderId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, order_id AS OrderId, exchange_order_id AS ExchangeOrderId, exchange_trade_id AS ExchangeTradeId,
                   symbol, side, price, quantity, quote_quantity AS QuoteQuantity, fee, fee_asset AS FeeAsset,
                   position_processed_at AS PositionProcessedAt, executed_at AS ExecutedAt, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM trade_executions
            WHERE order_id = @OrderId
            ORDER BY executed_at ASC;
            """;

        var result = await connection.QueryAsync<TradeExecution>(
            new CommandDefinition(sql, new { OrderId = orderId }, cancellationToken: cancellationToken));
        return result.ToList();
    }

    public async Task<IReadOnlyList<TradeExecution>> GetBySymbolAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, order_id AS OrderId, exchange_order_id AS ExchangeOrderId, exchange_trade_id AS ExchangeTradeId,
                   symbol, side, price, quantity, quote_quantity AS QuoteQuantity, fee, fee_asset AS FeeAsset,
                   position_processed_at AS PositionProcessedAt, executed_at AS ExecutedAt, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM trade_executions
            WHERE symbol = @Symbol
            ORDER BY executed_at DESC;
            """;

        var result = await connection.QueryAsync<TradeExecution>(
            new CommandDefinition(sql, new { Symbol = (int)symbol }, cancellationToken: cancellationToken));
        return result.ToList();
    }

    public async Task MarkPositionProcessedByOrderAsync(long orderId, DateTime processedAtUtc, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE trade_executions
            SET position_processed_at = COALESCE(position_processed_at, @ProcessedAt),
                updated_at = @ProcessedAt
            WHERE order_id = @OrderId;
            """;

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    OrderId = orderId,
                    ProcessedAt = processedAtUtc
                },
                cancellationToken: cancellationToken));
    }
}

