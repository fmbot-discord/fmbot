using System;

namespace FMBot.Domain.Models
{
    public class Album
    {
        public string Name { get; set; }
        public ChildArtist Artist { get; set; }
        public Guid Mbid { get; set; }
        public Uri Url { get; set; }
        public Image[] Image { get; set; }
        public long Listeners { get; set; }
        public long Playcount { get; set; }
        public long? Userplaycount { get; set; }
        public Tracks Tracks { get; set; }
        public Tags Tags { get; set; }
        public Wiki Wiki { get; set; }
    }

    public partial class Tags
    {
        public Tag[] Tag { get; set; }
    }

    public partial class Tracks
    {
        public ChildTrack[] Track { get; set; }
    }
}
