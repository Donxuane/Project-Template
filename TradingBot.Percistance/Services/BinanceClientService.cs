using System.Net.Http.Json;
using TradingBot.Domain.Enums.General;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.App;

namespace TradingBot.Percistance.Services;

public sealed class BinanceClientService(
    HttpClient httpClient,
    IBinanceEndpointsService binanceEndpointsService) : IBinanceClientService
{
    public async Task<ServerTimeResponse?> GetServerTime(
        CancellationToken cancellationToken = default)
    {
        var endpoint = binanceEndpointsService.GetEndpoint(GeneralApis.CheckServerTime);

        if (string.IsNullOrWhiteSpace(endpoint.API))
            throw new InvalidOperationException("The Binance server-time endpoint is not configured.");

        using var response = await httpClient.GetAsync(endpoint.API, cancellationToken);

        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<ServerTimeResponse>(
                cancellationToken: cancellationToken)
            : null;
    }
}
