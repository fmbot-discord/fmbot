using System;

namespace FMBot.LastFM.Domain.Models
{
    public class TrackResponse
    {
        public ResponseTrack Track { get; set; }
    }

    public class ResponseTrack
    {
        public string Name { get; set; }
        public Guid Mbid { get; set; }
        public string Url { get; set; }
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

    public class Toptags
    {
        public Tag[] Tag { get; set; }
    }
}
