using System;
using System.Collections.Generic;

namespace FMBot.Domain.Models;

public class TrackInfo
{
    public string TrackName { get; set; }
    public string TrackUrl { get; set; }

    public string ArtistName { get; set; }
    public string ArtistUrl { get; set; }
    public Guid? ArtistMbid { get; set; }

    public string AlbumName { get; set; }
    public string AlbumArtist { get; set; }
    public string AlbumUrl { get; set; }
    public string AlbumCoverUrl { get; set; }

    public long? Duration { get; set; }
    public long TotalListeners { get; set; }
    public long TotalPlaycount { get; set; }
    public long? UserPlaycount { get; set; }
    public bool Loved { get; set; }

    public string Description { get; set; }

    public Guid? Mbid { get; set; }

    public List<Tag> Tags { get; set; }

}
