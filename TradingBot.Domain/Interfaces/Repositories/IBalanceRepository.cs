using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Domain.Interfaces.Repositories;

public interface IBalanceRepository
{
    Task<long> InsertAsync(BalanceSnapshot snapshot, CancellationToken cancellationToken = default);
    Task UpsertLatestAsync(List<BalanceSnapshot> snapshot, CancellationToken cancellationToken = default);
    Task<BalanceSnapshot?> GetLatestAsync(string asset, TradingSymbol symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BalanceSnapshot>> GetLatestForAllAsync(CancellationToken cancellationToken = default);
}

