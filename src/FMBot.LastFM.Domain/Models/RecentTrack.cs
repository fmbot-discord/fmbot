using System;
using System.Text.Json.Serialization;


namespace FMBot.LastFM.Domain.Models
{
    public class RecentTracksResponse
    {
        public RecentTracks RecentTracks { get; set; }
    }

    public class RecentTracks
    {
        [JsonPropertyName("@attr")]
        public Attr Attr { get; set; }

        public RecentTrack[] Track { get; set; }
    }

    public class Attr
    {
        public long Page { get; set; }

        public long Total { get; set; }

        public string User { get; set; }

        public long PerPage { get; set; }

        public long TotalPages { get; set; }
    }

    public class RecentTrack
    {
        [JsonPropertyName("@attr")]
        public TrackAttr Attr { get; set; }

        public string Mbid { get; set; }

        public long Loved { get; set; }

        public SmallArtist Artist { get; set; }

        public Image[] Image { get; set; }

        public Date Date { get; set; }

        public long Streamable { get; set; }

        public Uri Url { get; set; }

        public string Name { get; set; }

        public SmallAlbum Album { get; set; }
    }

    public class TrackAttr
    {
        public bool Nowplaying { get; set; }
    }


    public class Date
    {
        public long Uts { get; set; }

        [JsonPropertyName("#text")]
        public string Text { get; set; }
    }

    public partial class SmallAlbum
    {
        public Guid? Mbid { get; set; }

        [JsonPropertyName("#text")]
        public string Text { get; set; }
    }

    public partial class SmallArtist
    {
        public string Url { get; set; }
        public Guid? Mbid { get; set; }

        [JsonPropertyName("#text")]
        public string Text { get; set; }
    }
}
