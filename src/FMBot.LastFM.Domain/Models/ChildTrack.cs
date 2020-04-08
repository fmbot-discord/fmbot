using System;

namespace FMBot.LastFM.Domain.Models
{
    public partial class ChildTrack
    {
        public string Name { get; set; }
        public Uri Url { get; set; }
        public long Duration { get; set; }
        public ChildArtist Artist { get; set; }
    }
}
