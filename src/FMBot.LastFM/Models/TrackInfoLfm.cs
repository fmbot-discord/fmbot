namespace FMBot.LastFM.Models;

internal class TrackInfoLfmResponse
{
    public TrackInfoLfm Track { get; set; }
}

internal class TrackInfoLfm
{
    public string Name { get; set; }
    public string Mbid { get; set; }
    public string Url { get; set; }
    public long? Duration { get; set; }
    public long? Listeners { get; set; }
    public long? Playcount { get; set; }
    public ChildArtistLfm Artist { get; set; }
    public ChildAlbumLfm Album { get; set; }
    public long? Userplaycount { get; set; }
    public string Userloved { get; set; }
    public TagsLfm Toptags { get; set; }
    public WikiLfm Wiki { get; set; }
}
