using System.Data;
using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Interfaces.Services;

/// <summary>
/// Updates order ProcessingStatus with optimistic concurrency. Only forward transitions are allowed.
/// </summary>
public interface IOrderStatusService
{
    /// <summary>
    /// Attempts to set order ProcessingStatus to <paramref name="newStatus"/> only if current status equals <paramref name="expectedStatus"/>.
    /// Validates that the transition is forward (newStatus &gt; expectedStatus).
    /// </summary>
    /// <param name="transaction">When provided, runs in the same transaction (for worker batch locking).</param>
    /// <returns>True if the update succeeded (one row updated), false otherwise.</returns>
    Task<bool> TryUpdateProcessingStatusAsync(long orderId, ProcessingStatus expectedStatus, ProcessingStatus newStatus, CancellationToken cancellationToken = default, IDbTransaction? transaction = null);

    /// <summary>Sets status to TradesSyncFailed and increments SyncRetryCount. Used when trade sync fails.</summary>
    Task<bool> TrySetTradesSyncFailedAsync(long orderId, ProcessingStatus expectedStatus, CancellationToken cancellationToken = default, IDbTransaction? transaction = null);

    /// <summary>Sets status to PositionUpdateFailed. Used when position update fails.</summary>
    Task<bool> TrySetPositionUpdateFailedAsync(long orderId, ProcessingStatus expectedStatus, CancellationToken cancellationToken = default, IDbTransaction? transaction = null);
}
