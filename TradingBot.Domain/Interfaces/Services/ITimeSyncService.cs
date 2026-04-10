namespace TradingBot.Domain.Interfaces.Services;

/// <summary>
/// Provides timestamp adjusted for Binance server time offset (stored in Redis by TimeSyncWorker).
/// Signed requests should use this to avoid timestamp drift errors.
/// </summary>
public interface ITimeSyncService
{
    /// <summary>Returns current time in milliseconds (Unix) adjusted by the cached offset, or local time if no offset yet.</summary>
    Task<long> GetAdjustedTimestampAsync(CancellationToken cancellationToken = default);
}
