using System.Text.Json;

namespace TradingBot.Domain.Interfaces.Services;

public interface ISpotMarketDataClient
{
    Task<JsonElement> GetKlinesAsync(
        string symbol,
        string interval,
        int limit,
        CancellationToken cancellationToken = default);
}
