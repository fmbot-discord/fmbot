using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FMBot.LastFM.Converters;

public class GuidConverter : JsonConverter<Guid?>
{
    public override Guid? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                var str = reader.GetString();
                if (string.IsNullOrEmpty(str))
                {
                    return null;
                }
                else
                {
                    return new Guid(str);
                }
            default:
                throw new ArgumentException("Invalid token type");
        }

    }

    public override void Write(Utf8JsonWriter writer, Guid? value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
