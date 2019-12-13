using System;
using Newtonsoft.Json;

namespace FMBot.LastFM.Models
{
    public class ArtistResponse
    {
        public Artist Artist { get; set; }
    }

    public class Artist
    {
        public string Name { get; set; }
        public Guid Mbid { get; set; }
        public Uri Url { get; set; }
        public Image[] Image { get; set; }
        public long Streamable { get; set; }
        public long Ontour { get; set; }
        public Stats Stats { get; set; }
        public Tags Tags { get; set; }
        public Bio Bio { get; set; }
    }

    public class Bio
    {
        public string Published { get; set; }
        public string Summary { get; set; }
        public string Content { get; set; }
    }

    public class Stats
    {
        public long Listeners { get; set; }
        public long Playcount { get; set; }
        public long? Userplaycount { get; set; }
    }

    public partial class Tags
    {
        [JsonProperty("Tag")]
        public Tag[] TagArray { get; set; }
    }
}
