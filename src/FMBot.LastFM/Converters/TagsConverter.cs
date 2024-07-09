using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using FMBot.LastFM.Models;

namespace FMBot.LastFM.Converters;

internal class TagsConverter : JsonConverter<TagsLfm>
{
    public override TagsLfm Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        // If the token is a string, return null
        if (reader.TokenType == JsonTokenType.String)
        {
            return null;
        }

        // Skip over the object and property name
        reader.Read(); // Object
        reader.Read(); // Property name

        // Read the tags array (or single object)
        TagLfm[] tags;
        try
        {
            tags = JsonSerializer.Deserialize<TagLfm[]>(ref reader, options);
        }
        catch
        {
            tags = new[] { JsonSerializer.Deserialize<TagLfm>(ref reader, options) };
        }

        reader.Read(); // Object

        return new TagsLfm { Tag = tags };
    }

    public override void Write(Utf8JsonWriter writer, TagsLfm value,
        JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}
