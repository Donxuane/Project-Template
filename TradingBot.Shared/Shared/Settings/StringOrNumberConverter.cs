using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TradingBot.Shared.Shared.Settings;

public class StringOrNumberConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return reader.GetDecimal().ToString(CultureInfo.InvariantCulture);
        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString()!;
        throw new JsonException($"Unexpected token type {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
