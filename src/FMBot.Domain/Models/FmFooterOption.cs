using System;
using FMBot.Domain.Attributes;

namespace FMBot.Domain.Models;


[Flags]
public enum FmFooterOption : long
{
    [Option("None", "")]
    None = 0,

    [Option("Track loved", "Displays a heart if the track is loved")]
    Loved = 1 << 0,
    [Option("Artist plays", "Your all-time artist plays")]
    ArtistPlays = 1 << 1,
    [Option("Album plays", "Your all-time album plays")]
    AlbumPlays = 1 << 2,
    [Option("Track plays", "Your all-time track plays")]
    TrackPlays = 1 << 3,

    [Option("Total scrobbles", "Your all-time scrobble count")]
    TotalScrobbles = 1 << 4,

    [Option("Artist plays last week", "Your 7-day artist plays")]
    ArtistPlaysThisWeek = 1 << 5,

    [Option("Artist country", "Country the artist is from (from MusicBrainz)")]
    ArtistCountry = 1 << 6,
    [Option("Artist birthday", "Shows artist birthday (from MusicBrainz)")]
    ArtistBirthday = 1 << 7,
    [Option("Artist genres", "Artist genres (from Spotify)")]
    ArtistGenres = 1 << 8,

    [Option("Track bpm", "Beats per minute for track (from Spotify)")]
    TrackBpm = 1 << 9,
    [Option("Track duration", "Length of track")]
    TrackDuration = 1 << 10,

    [Option("Discogs collection", "Shows if you have current album in your Discogs collection")]
    DiscogsCollection = 1 << 11,

    [Option("Server artist listeners", "Amount of artist listeners in server")]
    ServerArtistListeners = 1 << 12,
    [Option("Server album listeners", "Amount of album listeners in server")]
    ServerAlbumListeners = 1 << 13,
    [Option("Server track listeners", "Amount of track listeners in server")]
    ServerTrackListeners = 1 << 14,

    [Option("Server artist rank", "Your WhoKnows rank for this artist")]
    ServerArtistRank = 1 << 15,
    [Option("Server album rank", "Your WhoKnows rank for this album")]
    ServerAlbumRank = 1 << 16,
    [Option("Server track rank", "Your WhoKnows rank for this track")]
    ServerTrackRank = 1 << 17,

    [Option("Crown holder", "Current crown holder")]
    CrownHolder = 1 << 18,

    [Option("Global artist rank", "Your Global WhoKnows rank for this artist")]
    GlobalArtistRank = 1 << 19,
    [Option("Global album rank", "Your Global WhoKnows rank for this album")]
    GlobalAlbumRank = 1 << 20,
    [Option("Global track rank", "Your Global WhoKnows rank for this track")]
    GlobalTrackRank = 1 << 21,

    [Option("First artist listen", "Date you first listened to an artist", true)]
    FirstArtistListen = 1 << 22,
    [Option("First album listen", "Date you first listened to an album", true)]
    FirstAlbumListen = 1 << 23,
    [Option("First track listen", "Date you first listened to a track", true)]
    FirstTrackListen = 1 << 24
}
