using System;

namespace FMBot.LastFM.Domain.Models;

public class AlbumInfoLfmResponse
{
    public AlbumInfoLfm Album { get; set; }
}

public class AlbumInfoLfm
{
    public string Name { get; set; }
    public string Artist { get; set; }
    public string Mbid { get; set; }
    public string Url { get; set; }
    public ImageLfm[] Image { get; set; }
    public long? Listeners { get; set; }
    public long? Playcount { get; set; }
    public long? Userplaycount { get; set; }
    public TracksLfm Tracks { get; set; }
    public TagsLfm Tags { get; set; }
    public WikiLfm Wiki { get; set; }
}

public class TagsLfm
{
    public TagLfm[] Tag { get; set; }
}

public class TracksLfm
{
    public ChildTrack[] Track { get; set; }
}
