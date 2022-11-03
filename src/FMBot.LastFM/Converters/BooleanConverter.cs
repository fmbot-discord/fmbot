using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FMBot.LastFM.Converters;

public class BooleanConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (bool.TryParse(str, out var value))
            {
                return value;
            }
        }

        return false;
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
