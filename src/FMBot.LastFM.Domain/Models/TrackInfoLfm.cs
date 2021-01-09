using System;

namespace FMBot.LastFM.Domain.Models
{
    public class TrackInfoLfmResponse
    {
        public TrackInfoLfm Track { get; set; }
    }

    public class TrackInfoLfm
    {
        public string Name { get; set; }
        public Guid Mbid { get; set; }
        public string Url { get; set; }
        public long Duration { get; set; }
        public long Listeners { get; set; }
        public long Playcount { get; set; }
        public ChildArtistLfm Artist { get; set; }
        public ChildAlbumLfm Album { get; set; }
        public long Userplaycount { get; set; }
        public long Userloved { get; set; }
        public TrackInfoTopTagsLfm Toptags { get; set; }
        public WikiLfm Wiki { get; set; }
    }

    public class TrackInfoTopTagsLfm
    {
        public Tag[] Tag { get; set; }
    }
}
