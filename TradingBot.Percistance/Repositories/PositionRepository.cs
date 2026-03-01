using System.Data;
using Dapper;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Percistance.Repositories;

public class PositionRepository(IDbConnection connection) : IPositionRepository
{
    public async Task<Guid> UpsertAsync(Position position, CancellationToken cancellationToken = default)
    {
        if (position.Id == Guid.Empty)
            position.Id = Guid.NewGuid();

        var now = DateTime.UtcNow;
        if (position.CreatedAt == default)
            position.CreatedAt = now;
        position.UpdatedAt = now;

        const string sql = """
            INSERT INTO positions
                (id, symbol, side, quantity, average_price, realized_pnl, unrealized_pnl, is_open, created_at, updated_at)
            VALUES
                (@Id, @Symbol, @Side, @Quantity, @AveragePrice, @RealizedPnl, @UnrealizedPnl, @IsOpen, @CreatedAt, @UpdatedAt)
            ON CONFLICT (id) DO UPDATE SET
                symbol = EXCLUDED.symbol,
                side = EXCLUDED.side,
                quantity = EXCLUDED.quantity,
                average_price = EXCLUDED.average_price,
                realized_pnl = EXCLUDED.realized_pnl,
                unrealized_pnl = EXCLUDED.unrealized_pnl,
                is_open = EXCLUDED.is_open,
                updated_at = EXCLUDED.updated_at;
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, position, cancellationToken: cancellationToken));
        return position.Id;
    }

    public async Task<Position?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, symbol, side, quantity, average_price AS AveragePrice, realized_pnl AS RealizedPnl,
                   unrealized_pnl AS UnrealizedPnl, is_open AS IsOpen, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM positions
            WHERE id = @Id;
            """;

        return await connection.QuerySingleOrDefaultAsync<Position>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<Position?> GetOpenPositionAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, symbol, side, quantity, average_price AS AveragePrice, realized_pnl AS RealizedPnl,
                   unrealized_pnl AS UnrealizedPnl, is_open AS IsOpen, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM positions
            WHERE symbol = @Symbol AND is_open = TRUE
            LIMIT 1;
            """;

        return await connection.QuerySingleOrDefaultAsync<Position>(
            new CommandDefinition(sql, new { Symbol = symbol }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, symbol, side, quantity, average_price AS AveragePrice, realized_pnl AS RealizedPnl,
                   unrealized_pnl AS UnrealizedPnl, is_open AS IsOpen, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM positions
            WHERE is_open = TRUE
            ORDER BY symbol;
            """;

        var result = await connection.QueryAsync<Position>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return result.ToList();
    }
}

