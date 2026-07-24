using System.Globalization;
using System.Text.Json;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Percistance.Services.Main;

public sealed class SpotMarketDataClient(HttpClient httpClient) : ISpotMarketDataClient
{
    public async Task<JsonElement> GetKlinesAsync(
        string symbol,
        string interval,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"/api/v3/klines?symbol={Uri.EscapeDataString(symbol)}&interval={Uri.EscapeDataString(interval)}&limit={Math.Clamp(limit, 2, 1000)}");
        using var response = await httpClient.GetAsync(path, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Spot market-data request failed. Status={(int)response.StatusCode} Body={body}");
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }
}
