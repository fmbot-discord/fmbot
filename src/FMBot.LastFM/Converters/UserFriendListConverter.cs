using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using FMBot.LastFM.Models;

namespace FMBot.LastFM.Converters;

internal class UserFriendListConverter : JsonConverter<List<UserLfm>>
{
    public override List<UserLfm> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            return JsonSerializer.Deserialize<List<UserLfm>>(ref reader, options);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var singleFriend = JsonSerializer.Deserialize<UserLfm>(ref reader, options);
            return [singleFriend];
        }

        throw new JsonException($"Unexpected token type {reader.TokenType} for 'user' property.");
    }

    public override void Write(Utf8JsonWriter writer, List<UserLfm> value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
