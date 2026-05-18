using System.Text.Json;
using System.Text.Json.Serialization;
using TradingBot.Domain.Models;

namespace TradingBot.Domain.Settings;
public class FilterConverter : JsonConverter<Filter>
{
    public override Filter Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
        {
            var root = doc.RootElement;
            var rawJson = root.GetRawText();
            var filterType = ReadString(root, "filterType", "FilterType");

            if (string.IsNullOrWhiteSpace(filterType))
            {
                return new UnknownFilter
                {
                    FilterType = "UNKNOWN",
                    RawJson = rawJson
                };
            }

            return filterType switch
            {
                "PRICE_FILTER" => DeserializeKnown<PriceFilter>(rawJson, options, filterType),
                "LOT_SIZE" => DeserializeKnown<LotSizeFilter>(rawJson, options, filterType),
                "ICEBERG_PARTS" => DeserializeKnown<IcebergPartsFilter>(rawJson, options, filterType),
                "MARKET_LOT_SIZE" => DeserializeKnown<MarketLotSizeFilter>(rawJson, options, filterType),
                "TRAILING_DELTA" => DeserializeKnown<TrailingDeltaFilter>(rawJson, options, filterType),
                "PERCENT_PRICE" => DeserializeKnown<PercentPriceFilter>(rawJson, options, filterType),
                "PERCENT_PRICE_BY_SIDE" => DeserializeKnown<PercentPriceBySideFilter>(rawJson, options, filterType),
                "NOTIONAL" => DeserializeNotional(root, filterType, includeMaxNotional: true),
                "MIN_NOTIONAL" => DeserializeNotional(root, filterType, includeMaxNotional: false),
                "MAX_NUM_ORDERS" => DeserializeKnown<MaxNumOrdersFilter>(rawJson, options, filterType),
                "MAX_NUM_ORDER_LISTS" => DeserializeKnown<MaxNumOrderListsFilter>(rawJson, options, filterType),
                "MAX_NUM_ALGO_ORDERS" => DeserializeKnown<MaxNumAlgoOrdersFilter>(rawJson, options, filterType),
                "MAX_NUM_ORDER_AMENDS" => DeserializeKnown<MaxNumOrderAmendsFilter>(rawJson, options, filterType),
                _ => new UnknownFilter
                {
                    FilterType = filterType,
                    RawJson = rawJson
                }
            };
        }
    }

    public override void Write(Utf8JsonWriter writer, Filter value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }

    private static T DeserializeKnown<T>(string rawJson, JsonSerializerOptions options, string filterType)
        where T : Filter, new()
    {
        var result = JsonSerializer.Deserialize<T>(rawJson, options);
        if (result is null)
        {
            return new T
            {
                FilterType = filterType
            };
        }

        result.FilterType ??= filterType;
        return result;
    }

    private static NotionalFilter DeserializeNotional(JsonElement root, string filterType, bool includeMaxNotional)
    {
        return new NotionalFilter
        {
            FilterType = filterType,
            MinNotional = ReadString(root, "minNotional", "MinNotional") ?? string.Empty,
            ApplyMinToMarket = ReadBool(root, false, "applyMinToMarket", "ApplyMinToMarket", "applyToMarket", "ApplyToMarket"),
            MaxNotional = includeMaxNotional
                ? ReadString(root, "maxNotional", "MaxNotional") ?? string.Empty
                : string.Empty,
            ApplyMaxToMarket = includeMaxNotional && ReadBool(root, false, "applyMaxToMarket", "ApplyMaxToMarket"),
            AvgPriceMins = ReadInt(root, 0, "avgPriceMins", "AvgPriceMins")
        };
    }

    private static string? ReadString(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!root.TryGetProperty(propertyName, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.String)
                return value.GetString();

            return value.GetRawText();
        }

        return null;
    }

    private static bool ReadBool(JsonElement root, bool defaultValue, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!root.TryGetProperty(propertyName, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.True)
                return true;
            if (value.ValueKind == JsonValueKind.False)
                return false;
            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed))
                return parsed;
        }

        return defaultValue;
    }

    private static int ReadInt(JsonElement root, int defaultValue, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!root.TryGetProperty(propertyName, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsedNumber))
                return parsedNumber;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsedString))
                return parsedString;
        }

        return defaultValue;
    }
}
