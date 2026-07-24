using System.Data;
using Dapper;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Percistance.Repositories;

public sealed class TradeExecutionRepository(IDbConnection connection) : ITradeExecutionRepository
{
    public async Task<long> InsertAsync(
        TradeExecution execution,
        CancellationToken cancellationToken = default)
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
        var parameters = new
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
        execution.Id = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        return execution.Id;
    }
}
