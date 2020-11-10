using System;
using System.Text.Json.Serialization;


namespace FMBot.LastFM.Domain.Models
{
    public class PlayResponse
    {
        [JsonPropertyName("recenttracks")]
        public Recenttracks Recenttracks { get; set; }
    }

    public class Recenttracks
    {
        [JsonPropertyName("@attr")]
        public Attr Attr { get; set; }

        [JsonPropertyName("track")]
        public RecentTrack[] Track { get; set; }
    }

    public class Attr
    {
        [JsonPropertyName("page")]
        public long Page { get; set; }

        [JsonPropertyName("total")]
        public long Total { get; set; }

        [JsonPropertyName("user")]
        public string User { get; set; }

        [JsonPropertyName("perPage")]
        public long PerPage { get; set; }

        [JsonPropertyName("totalPages")]
        public long TotalPages { get; set; }
    }

    public class RecentTrack
    {
        [JsonPropertyName("mbid")]
        public string Mbid { get; set; }

        [JsonPropertyName("loved")]
        public long Loved { get; set; }

        [JsonPropertyName("artist")]
        public ChildArtist Artist { get; set; }

        [JsonPropertyName("image")]
        public Image[] Image { get; set; }

        [JsonPropertyName("date")]
        public Date Date { get; set; }

        [JsonPropertyName("streamable")]
        public long Streamable { get; set; }

        [JsonPropertyName("url")]
        public Uri Url { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("album")]
        public ChildAlbum Album { get; set; }
    }


    public partial class Date
    {
        [JsonPropertyName("uts")]
        public long Uts { get; set; }

        [JsonPropertyName("#text")]
        public string Text { get; set; }
    }
}
