using System;
using System.Collections.Generic;

namespace FMBot.Domain.Models;

public class AlbumInfo
{
    public string AlbumName { get; set; }
    public string AlbumUrl { get; set; }
    public string AlbumCoverUrl { get; set; }

    public string ArtistName { get; set; }
    public string ArtistUrl { get; set; }

    public long TotalListeners { get; set; }
    public long TotalPlaycount { get; set; }
    public long? UserPlaycount { get; set; }
    public long? TotalDuration { get; set; }

    public string Description { get; set; }

    public Guid? Mbid { get; set; }

    public List<Tag> Tags { get; set; }

    public List<AlbumTrack> AlbumTracks { get; set; }
}
