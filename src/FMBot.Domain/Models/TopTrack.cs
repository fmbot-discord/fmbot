using System;
using System.Collections.Generic;

namespace FMBot.Domain.Models;

public record TopTrackList
{
    public long? TotalAmount { get; init; }

    public string UserUrl { get; init; }
    public string UserTopTracksUrl { get; init; }

    public IReadOnlyList<TopTrack> TopTracks { get; init; }
}

public class TopTrack
{
    public string AlbumName { get; set; }
    public string AlbumUrl { get; set; }
    public string AlbumCoverUrl { get; set; }

    public string TrackName { get; set; }
    public string TrackUrl { get; set; }
    
    public string ArtistName { get; set; }
    public string ArtistUrl { get; set; }

    public long? Rank { get; set; }

    public long? UserPlaycount { get; set; }

    public DateTime? FirstPlay { get; set; }

    public TopTimeListened TimeListened { get; set; }

    public Guid? Mbid { get; set; }
}
