namespace TradingBot.Domain.Interfaces.Services;

public interface ITimeSyncService
{
    Task<long> GetAdjustedTimestampAsync(CancellationToken cancellationToken = default);
    Task<long> RefreshOffsetAsync(CancellationToken cancellationToken = default);
}
