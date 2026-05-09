using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Domain.Interfaces.Repositories;

public interface IPositionRepository
{
    Task<long> UpsertAsync(Position position, CancellationToken cancellationToken = default);
    Task<Position?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Position?> GetOpenPositionAsync(TradingSymbol symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Position>> GetClosedPositionsAsync(CancellationToken cancellationToken = default);
    Task<bool> TryMarkPositionClosingAsync(long positionId, CancellationToken cancellationToken = default);
    Task ClearPositionClosingAsync(long positionId, CancellationToken cancellationToken = default);
}

