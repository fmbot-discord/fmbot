using System;
using System.Net.Mime;
using Newtonsoft.Json;

namespace FMBot.LastFM.Domain.Models
{
    public class PlayResponse
    {
        [JsonProperty("recenttracks")]
        public Recenttracks Recenttracks { get; set; }
    }

    public class Recenttracks
    {
        [JsonProperty("@attr")]
        public Attr Attr { get; set; }

        [JsonProperty("track")]
        public RecentTrack[] Track { get; set; }
    }

    public class Attr
    {
        [JsonProperty("page")]
        [JsonConverter(typeof(ParseStringConverter))]
        public long Page { get; set; }

        [JsonProperty("total")]
        [JsonConverter(typeof(ParseStringConverter))]
        public long Total { get; set; }

        [JsonProperty("user")]
        public string User { get; set; }

        [JsonProperty("perPage")]
        [JsonConverter(typeof(ParseStringConverter))]
        public long PerPage { get; set; }

        [JsonProperty("totalPages")]
        [JsonConverter(typeof(ParseStringConverter))]
        public long TotalPages { get; set; }
    }

    public class RecentTrack
    {
        [JsonProperty("mbid")]
        public string Mbid { get; set; }

        [JsonProperty("loved")]
        [JsonConverter(typeof(ParseStringConverter))]
        public long Loved { get; set; }

        [JsonProperty("artist")]
        public ChildArtist Artist { get; set; }

        [JsonProperty("image")]
        public Image[] Image { get; set; }

        [JsonProperty("date")]
        public Date Date { get; set; }

        [JsonProperty("streamable")]
        [JsonConverter(typeof(ParseStringConverter))]
        public long Streamable { get; set; }

        [JsonProperty("url")]
        public Uri Url { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("album")]
        public ChildAlbum Album { get; set; }
    }


    public partial class Date
    {
        [JsonProperty("uts")]
        [JsonConverter(typeof(ParseStringConverter))]
        public long Uts { get; set; }

        [JsonProperty("#text")]
        public string Text { get; set; }
    }
}
