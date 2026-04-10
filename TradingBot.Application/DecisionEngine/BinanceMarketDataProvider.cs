using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;
using TradingBot.Domain.Models.MarketData;

namespace TradingBot.Application.DecisionEngine;

public class BinanceMarketDataProvider(
    IToolService toolService,
    IPriceCacheService priceCacheService,
    ILogger<BinanceMarketDataProvider> logger) : IMarketDataProvider
{
    private const int DefaultKlineLimit = 50;
    private const string DefaultKlineInterval = "1m";

    public async Task<MarketSnapshot?> GetLatestAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var currentPrice = await priceCacheService.GetCachedPriceAsync(symbol, cancellationToken);
            if (!currentPrice.HasValue || currentPrice.Value <= 0)
            {
                logger.LogWarning("DecisionEngine market data missing cached price for {Symbol}.", symbol);
                return null;
            }

            var endpoint = toolService.BinanceEndpointsService.GetEndpoint(MarketData.CandlestickDataKline);
            var request = new KlineRequest
            {
                Symbol = symbol.ToString(),
                Interval = DefaultKlineInterval,
                Limit = DefaultKlineLimit
            };

            var raw = await toolService.BinanceClientService.Call<JsonElement, KlineRequest>(request, endpoint, false);
            var closes = ParseClosePrices(raw);
            var volumes = ParseVolumes(raw);
            if (closes.Count == 0)
            {
                logger.LogWarning("DecisionEngine received no close prices for {Symbol}.", symbol);
                return null;
            }
            if (volumes.Count == 0)
            {
                logger.LogWarning("DecisionEngine received no volume data for {Symbol}.", symbol);
                return null;
            }

            return new MarketSnapshot
            {
                Symbol = symbol,
                CurrentPrice = currentPrice.Value,
                ClosePrices = closes,
                Volumes = volumes,
                TimestampUtc = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DecisionEngine failed to fetch market data for {Symbol}.", symbol);
            return null;
        }
    }

    private static IReadOnlyList<decimal> ParseClosePrices(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Array)
            return [];

        var closes = new List<decimal>();
        foreach (var candle in raw.EnumerateArray())
        {
            if (candle.ValueKind != JsonValueKind.Array || candle.GetArrayLength() <= 4)
                continue;

            var closeElement = candle[4];
            if (TryParseDecimal(closeElement, out var close))
                closes.Add(close);
        }

        return closes;
    }

    private static IReadOnlyList<decimal> ParseVolumes(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Array)
            return [];

        var volumes = new List<decimal>();
        foreach (var candle in raw.EnumerateArray())
        {
            if (candle.ValueKind != JsonValueKind.Array || candle.GetArrayLength() <= 5)
                continue;

            var volumeElement = candle[5];
            if (TryParseDecimal(volumeElement, out var volume))
                volumes.Add(volume);
        }

        return volumes;
    }

    private static bool TryParseDecimal(JsonElement element, out decimal value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDecimal(out value);
            case JsonValueKind.String:
                return decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
            default:
                value = default;
                return false;
        }
    }
}
