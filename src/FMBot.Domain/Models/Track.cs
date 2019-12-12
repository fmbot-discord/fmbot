using System;
using System.Net.Mime;

namespace FMBot.Domain.Models
{
    public partial class Track
    {
        public string Name { get; set; }
        public Guid Mbid { get; set; }
        public Uri Url { get; set; }
        public long Duration { get; set; }
        public long Listeners { get; set; }
        public long Playcount { get; set; }
        public ChildArtist Artist { get; set; }
        public ChildAlbum Album { get; set; }
        public long Userplaycount { get; set; }
        public long Userloved { get; set; }
        public Toptags Toptags { get; set; }
        public Wiki Wiki { get; set; }
    }

    public partial class Toptags
    {
        public Tag[] Tag { get; set; }
    }
}
