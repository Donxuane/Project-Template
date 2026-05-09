using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Domain.Interfaces.Repositories;

public interface IBalanceRepository
{
    Task<long> InsertAsync(BalanceSnapshot snapshot, CancellationToken cancellationToken = default);
    Task UpsertLatestAsync(List<BalanceSnapshot> snapshot, CancellationToken cancellationToken = default);
    Task<BalanceSyncWriteResult> UpsertLatestAndAppendHistoryAsync(
        IReadOnlyList<BalanceSnapshot> snapshots,
        string source,
        string? syncCorrelationId,
        bool forceSnapshot = false,
        CancellationToken cancellationToken = default);
    Task<BalanceSnapshot?> GetLatestByAssetAsync(string asset, Assets assetId, CancellationToken cancellationToken = default);
    [Obsolete("Legacy API: balance_snapshots.symbol stores asset id. Use GetLatestByAssetAsync.")]
    Task<BalanceSnapshot?> GetLatestAsync(string asset, TradingSymbol symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BalanceSnapshot>> GetLatestForAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BalanceSnapshot>> GetStaleBalancesAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);
}

