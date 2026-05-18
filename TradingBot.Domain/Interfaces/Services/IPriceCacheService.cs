using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.MarketData;

namespace TradingBot.Domain.Interfaces.Services;

/// <summary>
/// Provides cached ticker prices (updated by MarketDataWorker).
/// </summary>
public interface IPriceCacheService
{
    /// <summary>Returns the cached price for the symbol, or null if not available.</summary>
    Task<decimal?> GetCachedPriceAsync(TradingSymbol symbol, CancellationToken cancellationToken = default);

    /// <summary>Returns cached price metadata snapshot for the symbol, or null if unavailable.</summary>
    Task<PriceSnapshot?> GetCachedPriceSnapshotAsync(TradingSymbol symbol, CancellationToken cancellationToken = default);

    /// <summary>Sets the cached price (used by MarketDataWorker).</summary>
    Task SetCachedPriceAsync(TradingSymbol symbol, decimal price, CancellationToken cancellationToken = default);
}
