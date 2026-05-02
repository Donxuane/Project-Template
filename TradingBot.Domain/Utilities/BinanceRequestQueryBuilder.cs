using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Models.TradingEndpoints;

namespace TradingBot.Domain.Utilities;

public static class BinanceRequestQueryBuilder
{
    public static Dictionary<string, string> BuildRequestDictionary<TRequest>(TRequest? request)
    {
        if (request is null)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        if (request is NewOrderRequest newOrderRequest)
            ValidateNewOrderShape(newOrderRequest);

        var excludedKeys = request is NewOrderRequest order
            ? GetExcludedNewOrderKeys(order)
            : new HashSet<string>(StringComparer.Ordinal);

        var requestDict = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var property in request.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var value = property.GetValue(request);
            if (value is null)
                continue;

            var key = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                      ?? char.ToLowerInvariant(property.Name[0]) + property.Name[1..];

            if (excludedKeys.Contains(key))
                continue;

            requestDict[key] = SerializeValue(value);
        }

        return requestDict;
    }

    public static string BuildQueryString(IReadOnlyDictionary<string, string> parameters)
    {
        return string.Join("&", parameters.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    private static HashSet<string> GetExcludedNewOrderKeys(NewOrderRequest request)
    {
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        if (request.Type == OrderTypes.MARKET)
        {
            excluded.Add("price");
            excluded.Add("timeInForce");
        }
        else if (request.Type == OrderTypes.LIMIT)
        {
            excluded.Add("quoteOrderQty");
        }

        return excluded;
    }

    private static void ValidateNewOrderShape(NewOrderRequest request)
    {
        if (request.Type == OrderTypes.MARKET)
        {
            var hasQty = request.Quantity.HasValue;
            var hasQuoteQty = request.QuoteOrderQty.HasValue;
            if (hasQty == hasQuoteQty)
                throw new InvalidOperationException("MARKET order must include either quantity or quoteOrderQty (exactly one).");
        }

        if (request.Type == OrderTypes.LIMIT)
        {
            if (!request.Quantity.HasValue || request.Quantity.Value <= 0m)
                throw new InvalidOperationException("LIMIT order requires a positive quantity.");
            if (!request.Price.HasValue || request.Price.Value <= 0m)
                throw new InvalidOperationException("LIMIT order requires a positive price.");
            if (!request.TimeInForce.HasValue)
                throw new InvalidOperationException("LIMIT order requires timeInForce.");
        }
    }

    private static string SerializeValue(object value)
    {
        return value switch
        {
            bool b => b ? "true" : "false",
            decimal d => BinanceDecimalFormatter.FormatDecimal(d),
            IEnumerable<string> stringList => JsonSerializer.Serialize(stringList),
            IEnumerable<object> objectList => JsonSerializer.Serialize(objectList),
            Enum e => e.ToString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }
}
