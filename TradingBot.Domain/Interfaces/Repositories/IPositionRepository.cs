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

    // The environment-scoped reads below isolate non-live (e.g. testnet validation) rows.
    // Default implementations are provided so existing repositories/test doubles that only
    // handle live Spot positions do not need to change.

    /// <summary>Returns the open position for a symbol scoped to a non-live execution environment (e.g. testnet validation).</summary>
    Task<Position?> GetOpenPositionByEnvironmentAsync(TradingSymbol symbol, string executionEnvironment, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("GetOpenPositionByEnvironmentAsync is not supported by this repository.");

    /// <summary>Returns all open positions for a non-live execution environment.</summary>
    Task<IReadOnlyList<Position>> GetOpenPositionsByEnvironmentAsync(string executionEnvironment, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("GetOpenPositionsByEnvironmentAsync is not supported by this repository.");

    /// <summary>Returns all closed positions for a non-live execution environment (basis for testnet PnL/reporting).</summary>
    Task<IReadOnlyList<Position>> GetClosedPositionsByEnvironmentAsync(string executionEnvironment, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("GetClosedPositionsByEnvironmentAsync is not supported by this repository.");
}

