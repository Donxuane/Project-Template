using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace TradingBot.Backtest;

public sealed record FuturesSymbolFilters(
    string Symbol,
    decimal? TickSize,
    decimal? StepSize,
    decimal? MinQty,
    decimal? MinNotional);

/// <summary>
/// Loads public Binance USD-M futures exchange filters (no API key required).
/// </summary>
public static class FuturesTestnetShadowExchangeInfo
{
    private const string FuturesExchangeInfoUrl = "https://fapi.binance.com/fapi/v1/exchangeInfo";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static readonly Dictionary<string, FuturesSymbolFilters> FallbackFilters =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["BNBUSDT"] = new("BNBUSDT", 0.01m, 0.01m, 0.01m, 5m),
            ["SOLUSDT"] = new("SOLUSDT", 0.001m, 0.1m, 0.1m, 5m)
        };

    public static async Task<FuturesSymbolFilters> GetFiltersAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HttpClient.GetAsync(FuturesExchangeInfoUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            foreach (var sym in doc.RootElement.GetProperty("symbols").EnumerateArray())
            {
                if (!string.Equals(sym.GetProperty("symbol").GetString(), symbol, StringComparison.OrdinalIgnoreCase))
                    continue;

                decimal? tick = null, step = null, minQty = null, minNotional = null;
                foreach (var filter in sym.GetProperty("filters").EnumerateArray())
                {
                    var type = filter.GetProperty("filterType").GetString();
                    switch (type)
                    {
                        case "PRICE_FILTER":
                            tick = ParseDecimal(filter.GetProperty("tickSize").GetString());
                            break;
                        case "LOT_SIZE":
                            step = ParseDecimal(filter.GetProperty("stepSize").GetString());
                            minQty = ParseDecimal(filter.GetProperty("minQty").GetString());
                            break;
                        case "MIN_NOTIONAL":
                            minNotional = ParseDecimal(filter.GetProperty("notional").GetString());
                            break;
                    }
                }

                return new FuturesSymbolFilters(symbol, tick, step, minQty, minNotional ?? 5m);
            }
        }
        catch
        {
            // Fall through to static defaults.
        }

        return FallbackFilters.TryGetValue(symbol, out var fallback)
            ? fallback
            : new FuturesSymbolFilters(symbol, 0.01m, 0.01m, 0.01m, 5m);
    }

    public static (decimal Rounded, bool Valid) RoundQuantity(decimal raw, FuturesSymbolFilters filters)
    {
        var step = filters.StepSize ?? 0.01m;
        var minQty = filters.MinQty ?? step;
        if (raw <= 0m)
            return (0m, false);
        var rounded = FloorToStep(raw, step);
        return (rounded, rounded >= minQty);
    }

    public static decimal FloorToStep(decimal value, decimal step)
    {
        if (step <= 0m)
            return value;
        return Math.Floor(value / step) * step;
    }

    private static decimal? ParseDecimal(string? value)
        => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}
