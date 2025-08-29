using Newtonsoft.Json;

namespace TradingBot.Shared.Shared.Settings;

public class LowerCaseJsonConverter : Newtonsoft.Json.JsonConverter<bool?>
{
    public override bool? ReadJson(Newtonsoft.Json.JsonReader reader, Type objectType, bool? existingValue, bool hasExistingValue, Newtonsoft.Json.JsonSerializer serializer)
    {
        if (reader.Value == null) return null;
        var returnValue = reader.Value.ToString()?.ToLower() == "true";
        return returnValue;
    }

    public override void WriteJson(JsonWriter writer, bool? value, Newtonsoft.Json.JsonSerializer serializer)
    {
        if (value.HasValue)
            writer.WriteValue(value.Value ? "true" : "false");
        else
            writer.WriteNull();
    }
}
