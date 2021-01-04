using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using FMBot.LastFM.Domain.Models;

namespace FMBot.LastFM.Domain.ResponseModels
{
    public class TopTracksResponse
    {
        public TopTracks TopTracks { get; set; }
    }

    public class TopTracks
    {
        [JsonPropertyName("@attr")]
        public TopTracksAttr Attr { get; set; }
        public List<Track> Track { get; set; }
    }

    public partial class TopTracksAttr
    {
        public long Page { get; set; }
        public long Total { get; set; }
        public string User { get; set; }
        public long PerPage { get; set; }
        public long TotalPages { get; set; }
    }

    public class Track
    {
        public long Duration { get; set; }
        public long Playcount { get; set; }
        public Artist Artist { get; set; }
        public List<Image> Image { get; set; }
        public string Mbid { get; set; }
        public string Name { get; set; }
        public Uri Url { get; set; }
    }

    public class Artist
    {
        public Uri Url { get; set; }
        public string Name { get; set; }
        public string Mbid { get; set; }
    }
}
