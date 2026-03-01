using System.Data;
using Dapper;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Percistance.Repositories;

public class BalanceRepository(IDbConnection connection) : IBalanceRepository
{
    public async Task<Guid> InsertAsync(BalanceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot.Id == Guid.Empty)
            snapshot.Id = Guid.NewGuid();

        var now = DateTime.UtcNow;
        snapshot.CreatedAt = now;
        snapshot.UpdatedAt = now;

        const string sql = """
            INSERT INTO balance_snapshots
                (id, asset, symbol, side, free, locked, created_at, updated_at)
            VALUES
                (@Id, @Asset, @Symbol, @Side, @Free, @Locked, @CreatedAt, @UpdatedAt);
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, snapshot, cancellationToken: cancellationToken));
        return snapshot.Id;
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
            new CommandDefinition(sql, new { Asset = asset, Symbol = symbol }, cancellationToken: cancellationToken));
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

