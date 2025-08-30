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

            var filterType = root.GetProperty("filterType").GetString();

            return filterType switch
            {
                "PRICE_FILTER" => JsonSerializer.Deserialize<PriceFilter>(root.GetRawText(), options),
                "LOT_SIZE" => JsonSerializer.Deserialize<LotSizeFilter>(root.GetRawText(), options),
                "ICEBERG_PARTS" => JsonSerializer.Deserialize<IcebergPartsFilter>(root.GetRawText(), options),
                "MARKET_LOT_SIZE" => JsonSerializer.Deserialize<MarketLotSizeFilter>(root.GetRawText(), options),
                "TRAILING_DELTA" => JsonSerializer.Deserialize<TrailingDeltaFilter>(root.GetRawText(), options),
                "PERCENT_PRICE_BY_SIDE" => JsonSerializer.Deserialize<PercentPriceBySideFilter>(root.GetRawText(), options),
                "NOTIONAL" => JsonSerializer.Deserialize<NotionalFilter>(root.GetRawText(), options),
                "MAX_NUM_ORDERS" => JsonSerializer.Deserialize<MaxNumOrdersFilter>(root.GetRawText(), options),
                "MAX_NUM_ORDER_LISTS" => JsonSerializer.Deserialize<MaxNumOrderListsFilter>(root.GetRawText(), options),
                "MAX_NUM_ALGO_ORDERS" => JsonSerializer.Deserialize<MaxNumAlgoOrdersFilter>(root.GetRawText(), options),
                "MAX_NUM_ORDER_AMENDS" => JsonSerializer.Deserialize<MaxNumOrderAmendsFilter>(root.GetRawText(), options),
                _ => throw new NotSupportedException($"Unknown filter type: {filterType}")
            };
        }
    }

    public override void Write(Utf8JsonWriter writer, Filter value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
