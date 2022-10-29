using System;
using System.Collections.Generic;

namespace FMBot.Domain.Models;

public class RecentTrackList
{
    public long TotalAmount { get; set; }
    public long NewRecentTracksAmount { get; set; }
    public long RemovedRecentTracksAmount { get; set; }

    public string UserUrl { get; set; }
    public string UserRecentTracksUrl { get; set; }
        
    public List<RecentTrack> RecentTracks { get; set; }
}

public class RecentTrack
{
    public bool NowPlaying { get; set; }
    public DateTime? TimePlayed { get; set; }

    public bool Loved { get; set; }

    public string TrackName { get; set; }
    public string TrackUrl { get; set; }

    public string ArtistName { get; set; }
    public string ArtistUrl { get; set; }

    public string AlbumName { get; set; }
    public string AlbumUrl { get; set; }
    public string AlbumCoverUrl { get; set; }
}
