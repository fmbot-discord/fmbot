using System;
using Newtonsoft.Json;

namespace FMBot.LastFM.Domain.Models
{
    public class UserResponse
    {
        [JsonProperty("user")]
        public LastFmUser User { get; set; }
    }

    public class LastFmUser
    {
        [JsonProperty("playlists")]
        [JsonConverter(typeof(ParseStringConverter))]
        public long Playlists { get; set; }

        [JsonProperty("playcount")]
        [JsonConverter(typeof(ParseStringConverter))]
        public long Playcount { get; set; }

        [JsonProperty("gender")]
        public string Gender { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("subscriber")]
        [JsonConverter(typeof(ParseStringConverter))]
        public long Subscriber { get; set; }

        [JsonProperty("url")]
        public Uri Url { get; set; }

        [JsonProperty("country")]
        public string Country { get; set; }

        [JsonProperty("image")]
        public Image[] Image { get; set; }

        [JsonProperty("registered")]
        public Registered Registered { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("age")]
        [JsonConverter(typeof(ParseStringConverter))]
        public long Age { get; set; }

        [JsonProperty("bootstrap")]
        [JsonConverter(typeof(ParseStringConverter))]
        public long Bootstrap { get; set; }

        [JsonProperty("realname")]
        public string Realname { get; set; }
    }

    public class Registered
    {
        [JsonProperty("unixtime")]
        [JsonConverter(typeof(ParseStringConverter))]
        public long Unixtime { get; set; }

        [JsonProperty("#text")]
        public long Text { get; set; }
    }

    internal class ParseStringConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(long) || t == typeof(long?);

        public override object ReadJson(JsonReader reader, Type t, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var value = serializer.Deserialize<string>(reader);
            if (long.TryParse(value, out var l))
            {
                return l;
            }
            throw new Exception("Cannot unmarshal type long");
        }

        public override void WriteJson(JsonWriter writer, object untypedValue, JsonSerializer serializer)
        {
            if (untypedValue == null)
            {
                serializer.Serialize(writer, null);
                return;
            }
            var value = (long)untypedValue;
            serializer.Serialize(writer, value.ToString());
        }

        public static readonly ParseStringConverter Singleton = new ParseStringConverter();
    }
}
