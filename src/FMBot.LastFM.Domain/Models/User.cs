using System;
using System.Text.Json.Serialization;

namespace FMBot.LastFM.Domain.Models
{
    public class UserResponse
    {
        [JsonPropertyName("user")]
        public LastFmUser User { get; set; }
    }

    public class LastFmUser
    {
        public long Playlists { get; set; }

        public long Playcount { get; set; }

        public string Gender { get; set; }

        public string Name { get; set; }

        public long Subscriber { get; set; }

        public Uri Url { get; set; }

        public string Country { get; set; }

        public Image[] Image { get; set; }

        public Registered Registered { get; set; }

        public string Type { get; set; }

        public long Age { get; set; }

        public long Bootstrap { get; set; }

        public string Realname { get; set; }
    }

    public class Registered
    {
        public long Unixtime { get; set; }

        [JsonPropertyName("#text")]
        public long Text { get; set; }
    }
}
