using System;

namespace FMBot.Domain.Models;


[Flags]
public enum FmFooterOption : long
{
    None = 0,
    Loved = 1 << 0,
    ArtistPlays = 1 << 1,
    AlbumPlays = 1 << 2,
    TrackPlays = 1 << 3,
    TotalScrobbles = 1 << 4,
    ArtistPlaysThisWeek = 1 << 5,
    ArtistCountry = 1 << 6,
    ArtistGenres = 1 << 7,
    TrackBpm = 1 << 8,
    TrackDuration = 1 << 9,
    ServerArtistListeners = 1 << 10,
    ServerAlbumListeners = 1 << 11,
    ServerTrackListeners = 1 << 12,
    ServerArtistRank = 1 << 13,
    ServerAlbumRank = 1 << 14,
    ServerTrackRank = 1 << 15,
    CrownHolder = 1 << 16,
    GlobalArtistRank = 1 << 17,
    GlobalAlbumRank = 1 << 18,
    GlobalTrackRank = 1 << 19,
    FirstArtistListen = 1 << 20,
    FirstAlbumListen = 1 << 21,
    FirstTrackListen = 1 << 22
}
