using System;

namespace FMBot.LastFM.Domain.Models
{
    public partial class ChildTrack
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public long Duration { get; set; }
        public ChildArtistLfm Artist { get; set; }
    }
}
