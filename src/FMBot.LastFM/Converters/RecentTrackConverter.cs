using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using FMBot.LastFM.Models;

namespace FMBot.LastFM.Converters;

internal class TrackListConverter : JsonConverter<List<RecentTrackLfm>>
{
    public override List<RecentTrackLfm> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            return JsonSerializer.Deserialize<List<RecentTrackLfm>>(ref reader, options);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var singleTrack = JsonSerializer.Deserialize<RecentTrackLfm>(ref reader, options);
            return [singleTrack];
        }

        throw new JsonException($"Unexpected token type {reader.TokenType} for 'track' property.");
    }

    public override void Write(Utf8JsonWriter writer, List<RecentTrackLfm> value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
