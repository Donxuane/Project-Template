using TradingBot.Domain.Models.Trading;

namespace TradingBot.Domain.Interfaces.Repositories;

public interface IPositionRepository
{
    Task<long> UpsertAsync(Position position, CancellationToken cancellationToken = default);
    Task<bool> TryMarkPositionClosingAsync(long positionId, CancellationToken cancellationToken = default);
    Task ClearPositionClosingAsync(long positionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Position>> GetOpenPositionsByEnvironmentAsync(
        string executionEnvironment,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Position>> GetClosedPositionsByEnvironmentAsync(
        string executionEnvironment,
        CancellationToken cancellationToken = default);
}
