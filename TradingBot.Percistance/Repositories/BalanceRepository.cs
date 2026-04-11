using System.Data;
using Dapper;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Percistance.Repositories;

public class BalanceRepository(IDbConnection connection) : IBalanceRepository
{
    public async Task<long> InsertAsync(BalanceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        snapshot.CreatedAt = now;
        snapshot.UpdatedAt = now;

        const string sql = """
            INSERT INTO balance_snapshots
                (asset, symbol, side, free, locked, created_at, updated_at)
            VALUES
                (@Asset, @Symbol, @Side, @Free, @Locked, @CreatedAt, @UpdatedAt)
            RETURNING id;
            """;

        var param = new
        {
            snapshot.Asset,
            Symbol = (int)snapshot.Symbol,
            Side = (int)snapshot.Side,
            snapshot.Free,
            snapshot.Locked,
            snapshot.CreatedAt,
            snapshot.UpdatedAt
        };
        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, param, cancellationToken: cancellationToken));
        snapshot.Id = id;
        return id;
    }

    public async Task UpsertLatestAsync(List<BalanceSnapshot> snapshot, CancellationToken cancellationToken = default)
    {

        const string sql = """
            INSERT INTO balance_snapshots
                (asset, symbol, side, free, locked)
            VALUES
                (@Asset, @Symbol, @Side, @Free, @Locked)
            ON CONFLICT (asset, symbol)
            DO UPDATE SET
                side = EXCLUDED.side,
                free = EXCLUDED.free,
                locked = EXCLUDED.locked,
                updated_at = now()
            RETURNING id;
            """;

        await connection.ExecuteAsync(sql, snapshot);
    }

    public async Task<BalanceSnapshot?> GetLatestAsync(string asset, TradingSymbol symbol, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, asset, symbol, side, free, locked, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM balance_snapshots
            WHERE asset = @Asset AND symbol = @Symbol
            ORDER BY created_at DESC
            LIMIT 1;
            """;

        return await connection.QuerySingleOrDefaultAsync<BalanceSnapshot>(
            new CommandDefinition(sql, new { Asset = asset, Symbol = (int)symbol }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<BalanceSnapshot>> GetLatestForAllAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT DISTINCT ON (asset, symbol)
                   id, asset, symbol, side, free, locked, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM balance_snapshots
            ORDER BY asset, symbol, created_at DESC;
            """;

        var result = await connection.QueryAsync<BalanceSnapshot>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return result.ToList();
    }
}

