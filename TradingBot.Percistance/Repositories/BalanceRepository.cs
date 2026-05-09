using System.Data;
using System.Data.Common;
using Dapper;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Percistance.Repositories;

public class BalanceRepository(IDbConnection connection) : IBalanceRepository
{
    // NOTE: balance_snapshots.symbol is a legacy DB column name that stores Assets enum ids.
    // Repository APIs should treat it as asset id, not TradingSymbol.

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
            Symbol = (int)snapshot.AssetId,
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
                (asset, symbol, side, free, locked, updated_at)
            VALUES
                (@Asset, @Symbol, @Side, @Free, @Locked, now())
            ON CONFLICT (asset, symbol)
            DO UPDATE SET
                side = EXCLUDED.side,
                free = EXCLUDED.free,
                locked = EXCLUDED.locked,
                updated_at = now()
            RETURNING id;
            """;

        var payload = snapshot.Select(x => new
        {
            x.Asset,
            Symbol = (int)x.AssetId,
            Side = (int)x.Side,
            x.Free,
            x.Locked
        });

        await connection.ExecuteAsync(new CommandDefinition(sql, payload, cancellationToken: cancellationToken));
    }

    public async Task<BalanceSyncWriteResult> UpsertLatestAndAppendHistoryAsync(
        IReadOnlyList<BalanceSnapshot> snapshots,
        string source,
        string? syncCorrelationId,
        bool forceSnapshot = false,
        CancellationToken cancellationToken = default)
    {
        if (snapshots.Count == 0)
        {
            return new BalanceSyncWriteResult();
        }

        using var transaction = await BeginTransactionWithOpenConnectionAsync(cancellationToken);
        var latestRowsUpserted = 0;
        var historyRowsInserted = 0;

        try
        {
            foreach (var snapshot in snapshots)
            {
                var existingLatest = await GetLatestByAssetAndAssetIdAsync(snapshot.Asset, snapshot.AssetId, transaction, cancellationToken);
                var hasHistory = await HasHistoryForAssetAsync(snapshot.Asset, snapshot.AssetId, transaction, cancellationToken);

                await UpsertLatestRowAsync(snapshot, transaction, cancellationToken);
                latestRowsUpserted++;

                if (ShouldInsertHistory(existingLatest, snapshot, hasHistory, forceSnapshot))
                {
                    await InsertHistoryRowAsync(snapshot, source, syncCorrelationId, transaction, cancellationToken);
                    historyRowsInserted++;
                }
            }

            await CommitTransactionAsync(transaction, cancellationToken);
        }
        catch (Exception ex)
        {
            try
            {
                await RollbackTransactionAsync(transaction, cancellationToken);
            }
            catch (Exception rollbackEx)
            {
                throw new AggregateException(ex, rollbackEx);
            }

            throw;
        }

        return new BalanceSyncWriteResult
        {
            AssetsFetched = snapshots.Count,
            LatestRowsUpserted = latestRowsUpserted,
            HistoryRowsInserted = historyRowsInserted
        };
    }

    public async Task<BalanceSnapshot?> GetLatestByAssetAsync(string asset, Assets assetId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, asset, symbol AS AssetId, side, free, locked, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM balance_snapshots
            WHERE asset = @Asset AND symbol = @AssetId
            ORDER BY COALESCE(updated_at, created_at) DESC, created_at DESC
            LIMIT 1;
            """;

        return await connection.QuerySingleOrDefaultAsync<BalanceSnapshot>(
            new CommandDefinition(sql, new { Asset = asset, AssetId = (int)assetId }, cancellationToken: cancellationToken));
    }

    [Obsolete("Legacy API: balance_snapshots.symbol stores asset id. Use GetLatestByAssetAsync.")]
    public Task<BalanceSnapshot?> GetLatestAsync(string asset, TradingSymbol symbol, CancellationToken cancellationToken = default)
    {
        return GetLatestByAssetAsync(asset, (Assets)(int)symbol, cancellationToken);
    }

    public async Task<IReadOnlyList<BalanceSnapshot>> GetLatestForAllAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT DISTINCT ON (asset, symbol)
                   id, asset, symbol AS AssetId, side, free, locked, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM balance_snapshots
            ORDER BY asset, symbol, created_at DESC;
            """;

        var result = await connection.QueryAsync<BalanceSnapshot>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return result.ToList();
    }

    public async Task<IReadOnlyList<BalanceSnapshot>> GetStaleBalancesAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.Subtract(maxAge <= TimeSpan.Zero ? TimeSpan.Zero : maxAge);
        const string sql = """
            SELECT DISTINCT ON (asset, symbol)
                   id, asset, symbol AS AssetId, side, free, locked, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM balance_snapshots
            WHERE COALESCE(updated_at, created_at) < @Cutoff
            ORDER BY asset, symbol, created_at DESC;
            """;

        var result = await connection.QueryAsync<BalanceSnapshot>(
            new CommandDefinition(sql, new { Cutoff = cutoff }, cancellationToken: cancellationToken));
        return result.ToList();
    }

    public static bool ShouldInsertHistory(
        BalanceSnapshot? existingLatest,
        BalanceSnapshot incoming,
        bool hasHistory,
        bool forceSnapshot)
    {
        if (forceSnapshot)
            return true;
        if (!hasHistory)
            return true;
        if (existingLatest is null)
            return true;

        return existingLatest.Free != incoming.Free || existingLatest.Locked != incoming.Locked;
    }

    public static bool IsSnapshotStale(BalanceSnapshot snapshot, DateTime cutoffUtc)
    {
        var timestamp = snapshot.UpdatedAt == default ? snapshot.CreatedAt : snapshot.UpdatedAt;
        if (timestamp == default)
            return true;

        return timestamp < cutoffUtc;
    }

    private async Task<BalanceSnapshot?> GetLatestByAssetAndAssetIdAsync(
        string asset,
        Assets assetId,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, asset, symbol AS AssetId, side, free, locked, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM balance_snapshots
            WHERE asset = @Asset AND symbol = @AssetId
            LIMIT 1;
            """;

        return await connection.QuerySingleOrDefaultAsync<BalanceSnapshot>(
            new CommandDefinition(sql, new { Asset = asset, AssetId = (int)assetId }, transaction, cancellationToken: cancellationToken));
    }

    private async Task<bool> HasHistoryForAssetAsync(
        string asset,
        Assets assetId,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT 1
            FROM balance_snapshot_history
            WHERE asset = @Asset
              AND (asset_id = @AssetId OR (asset_id IS NULL AND @AssetId IS NULL))
            LIMIT 1;
            """;

        var exists = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(sql, new { Asset = asset, AssetId = (int)assetId }, transaction, cancellationToken: cancellationToken));
        return exists.HasValue;
    }

    private async Task UpsertLatestRowAsync(BalanceSnapshot snapshot, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO balance_snapshots
                (asset, symbol, side, free, locked, updated_at)
            VALUES
                (@Asset, @Symbol, @Side, @Free, @Locked, now())
            ON CONFLICT (asset, symbol)
            DO UPDATE SET
                side = EXCLUDED.side,
                free = EXCLUDED.free,
                locked = EXCLUDED.locked,
                updated_at = now();
            """;

        var parameters = new
        {
            snapshot.Asset,
            Symbol = (int)snapshot.AssetId,
            Side = (int)snapshot.Side,
            snapshot.Free,
            snapshot.Locked
        };

        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));
    }

    private async Task InsertHistoryRowAsync(
        BalanceSnapshot snapshot,
        string source,
        string? syncCorrelationId,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO balance_snapshot_history
                (asset, asset_id, free, locked, source, sync_correlation_id, captured_at, created_at)
            VALUES
                (@Asset, @AssetId, @Free, @Locked, @Source, @SyncCorrelationId, now(), now());
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                snapshot.Asset,
                AssetId = (int)snapshot.AssetId,
                snapshot.Free,
                snapshot.Locked,
                Source = string.IsNullOrWhiteSpace(source) ? "BinanceAccount" : source,
                SyncCorrelationId = syncCorrelationId
            },
            transaction,
            cancellationToken: cancellationToken));
    }

    private async Task<IDbTransaction> BeginTransactionWithOpenConnectionAsync(CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            if (connection is DbConnection dbConnection)
            {
                await dbConnection.OpenAsync(cancellationToken);
            }
            else
            {
                connection.Open();
            }
        }

        if (connection is DbConnection asyncCapableConnection)
        {
            return await asyncCapableConnection.BeginTransactionAsync(cancellationToken);
        }

        return connection.BeginTransaction();
    }

    private static Task CommitTransactionAsync(IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (transaction is DbTransaction dbTransaction)
            return dbTransaction.CommitAsync(cancellationToken);

        transaction.Commit();
        return Task.CompletedTask;
    }

    private static Task RollbackTransactionAsync(IDbTransaction transaction, CancellationToken cancellationToken)
    {
        if (transaction is DbTransaction dbTransaction)
            return dbTransaction.RollbackAsync(cancellationToken);

        transaction.Rollback();
        return Task.CompletedTask;
    }
}

