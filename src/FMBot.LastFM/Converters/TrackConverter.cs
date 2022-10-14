using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using FMBot.LastFM.Domain.Models;

namespace FMBot.LastFM.Converters;

public class TrackConverter : JsonConverter<TracksLfm>
{
    public override TracksLfm Read(ref Utf8JsonReader reader, Type typeToConvert,
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
        ChildTrack[] tracks;
        try
        {
            tracks = JsonSerializer.Deserialize<ChildTrack[]>(ref reader, options);
        }
        catch
        {
            tracks = new[] { JsonSerializer.Deserialize<ChildTrack>(ref reader, options) };
        }

        reader.Read(); // Object

        return new TracksLfm { Track = tracks };
    }

    public override void Write(Utf8JsonWriter writer, TracksLfm value,
        JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}
