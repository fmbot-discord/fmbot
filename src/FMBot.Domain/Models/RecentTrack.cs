using System;
using System.Collections.Generic;
using FMBot.Domain.Enums;

namespace FMBot.Domain.Models;

public record RecentTrackList
{
    public long TotalAmount { get; init; }
    public long NewRecentTracksAmount { get; init; }
    public long RemovedRecentTracksAmount { get; init; }

    public string UserUrl { get; init; }
    public string UserRecentTracksUrl { get; init; }

    public IReadOnlyList<RecentTrack> RecentTracks { get; init; }
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

    public PlaySource? PlaySource { get; set; }
}
