using System.Data;
using Dapper;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Percistance.Repositories;

public class PositionRepository(IDbConnection connection) : IPositionRepository
{
    public async Task<long> UpsertAsync(Position position, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        if (position.CreatedAt == default)
            position.CreatedAt = now;
        position.UpdatedAt = now;

        if (position.Id == 0)
        {
            const string insertSql = """
                INSERT INTO positions
                    (symbol, side, quantity, average_price, stop_loss_price, take_profit_price, exit_price, exit_reason, opened_at, closed_at, realized_pnl, unrealized_pnl, is_open, is_closing, created_at, updated_at)
                VALUES
                    (@Symbol, @Side, @Quantity, @AveragePrice, @StopLossPrice, @TakeProfitPrice, @ExitPrice, @ExitReason, @OpenedAt, @ClosedAt, @RealizedPnl, @UnrealizedPnl, @IsOpen, @IsClosing, @CreatedAt, @UpdatedAt)
                RETURNING id;
                """;

            var insertParam = new
            {
                Symbol = (int)position.Symbol,
                Side = (int)position.Side,
                position.Quantity,
                position.AveragePrice,
                position.StopLossPrice,
                position.TakeProfitPrice,
                position.ExitPrice,
                ExitReason = position.ExitReason.HasValue ? (int)position.ExitReason.Value : (int?)null,
                position.OpenedAt,
                position.ClosedAt,
                position.RealizedPnl,
                position.UnrealizedPnl,
                position.IsOpen,
                position.IsClosing,
                position.CreatedAt,
                position.UpdatedAt
            };
            var id = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(insertSql, insertParam, cancellationToken: cancellationToken));
            position.Id = id;
            return id;
        }
        else
        {
            const string updateSql = """
                UPDATE positions
                SET
                    symbol = @Symbol,
                    side = @Side,
                    quantity = @Quantity,
                    average_price = @AveragePrice,
                    stop_loss_price = @StopLossPrice,
                    take_profit_price = @TakeProfitPrice,
                    exit_price = @ExitPrice,
                    exit_reason = @ExitReason,
                    opened_at = @OpenedAt,
                    closed_at = @ClosedAt,
                    realized_pnl = @RealizedPnl,
                    unrealized_pnl = @UnrealizedPnl,
                    is_open = @IsOpen,
                    is_closing = @IsClosing,
                    updated_at = @UpdatedAt
                WHERE id = @Id;
                """;

            var updateParam = new
            {
                position.Id,
                Symbol = (int)position.Symbol,
                Side = (int)position.Side,
                position.Quantity,
                position.AveragePrice,
                position.StopLossPrice,
                position.TakeProfitPrice,
                position.ExitPrice,
                ExitReason = position.ExitReason.HasValue ? (int)position.ExitReason.Value : (int?)null,
                position.OpenedAt,
                position.ClosedAt,
                position.RealizedPnl,
                position.UnrealizedPnl,
                position.IsOpen,
                position.IsClosing,
                position.UpdatedAt
            };
            await connection.ExecuteAsync(new CommandDefinition(updateSql, updateParam, cancellationToken: cancellationToken));
            return position.Id;
        }
    }

    public async Task<Position?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, symbol, side, quantity, average_price AS AveragePrice, stop_loss_price AS StopLossPrice,
                   take_profit_price AS TakeProfitPrice, exit_price AS ExitPrice, exit_reason AS ExitReason, opened_at AS OpenedAt,
                   closed_at AS ClosedAt, realized_pnl AS RealizedPnl, unrealized_pnl AS UnrealizedPnl,
                   is_open AS IsOpen, is_closing AS IsClosing, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM positions
            WHERE id = @Id;
            """;

        return await connection.QuerySingleOrDefaultAsync<Position>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<Position?> GetOpenPositionAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, symbol, side, quantity, average_price AS AveragePrice, stop_loss_price AS StopLossPrice,
                   take_profit_price AS TakeProfitPrice, exit_price AS ExitPrice, exit_reason AS ExitReason, opened_at AS OpenedAt,
                   closed_at AS ClosedAt, realized_pnl AS RealizedPnl, unrealized_pnl AS UnrealizedPnl,
                   is_open AS IsOpen, is_closing AS IsClosing, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM positions
            WHERE symbol = @Symbol AND is_open = TRUE
            LIMIT 1;
            """;

        return await connection.QuerySingleOrDefaultAsync<Position>(
            new CommandDefinition(sql, new { Symbol = (int)symbol }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, symbol, side, quantity, average_price AS AveragePrice, stop_loss_price AS StopLossPrice,
                   take_profit_price AS TakeProfitPrice, exit_price AS ExitPrice, exit_reason AS ExitReason, opened_at AS OpenedAt,
                   closed_at AS ClosedAt, realized_pnl AS RealizedPnl, unrealized_pnl AS UnrealizedPnl,
                   is_open AS IsOpen, is_closing AS IsClosing, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM positions
            WHERE is_open = TRUE
            ORDER BY symbol;
            """;

        var result = await connection.QueryAsync<Position>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return result.ToList();
    }

    public async Task<IReadOnlyList<Position>> GetClosedPositionsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, 
            symbol, 
            side, 
            quantity,
            average_price AS AveragePrice, 
            stop_loss_price AS StopLossPrice,
            take_profit_price AS TakeProfitPrice, 
            exit_price AS ExitPrice, 
            exit_reason AS ExitReason, 
            opened_at AS OpenedAt,
            closed_at AS ClosedAt,
            realized_pnl AS RealizedPnl, 
            unrealized_pnl AS UnrealizedPnl,
            is_open AS IsOpen,
            is_closing AS IsClosing,
            created_at AS CreatedAt, 
            updated_at AS UpdatedAt
            FROM positions
            WHERE is_open = FALSE
            ORDER BY COALESCE(closed_at, updated_at, created_at), id;
            """;

        var result = await connection.QueryAsync<Position>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return result.ToList();
    }

    public async Task<bool> TryMarkPositionClosingAsync(long positionId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE positions
            SET is_closing = TRUE,
                updated_at = @UpdatedAt
            WHERE id = @PositionId
              AND is_open = TRUE
              AND is_closing = FALSE
              AND quantity > 0
            RETURNING id;
            """;

        var updatedId = await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(
                sql,
                new
                {
                    PositionId = positionId,
                    UpdatedAt = DateTime.UtcNow
                },
                cancellationToken: cancellationToken));

        return updatedId.HasValue;
    }

    public async Task ClearPositionClosingAsync(long positionId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE positions
            SET is_closing = FALSE,
                updated_at = @UpdatedAt
            WHERE id = @PositionId;
            """;

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    PositionId = positionId,
                    UpdatedAt = DateTime.UtcNow
                },
                cancellationToken: cancellationToken));
    }
}

