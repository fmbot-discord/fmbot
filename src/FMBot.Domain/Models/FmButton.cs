using System;
using FMBot.Domain.Attributes;

namespace FMBot.Domain.Models;

[Flags]
public enum FmButton : long
{
    [Option("Last.fm track link")]
    [FmButton(882227627287515166)]
    LastFmTrackLink = 1 << 1,

    [Option("Last.fm album link")]
    [FmButton(882227627287515166)]
    LastFmAlbumLink = 1 << 2,

    [Option("Last.fm artist link")]
    [FmButton(882227627287515166)]
    LastFmArtistLink = 1 << 3,

    [Option("Last.fm track plays link")]
    [FmButton(882227627287515166)]
    LastFmUserLibraryLink = 1 << 4,

    [Option("Spotify link")]
    [FmButton(882221219334725662, requiresDbTrack: true)]
    SpotifyLink = 1 << 5,

    [Option("Apple Music link")]
    [FmButton(1218182727149420544, requiresDbTrack: true)]
    AppleMusicLink = 1 << 6,

    [Option("Rate Your Music link")]
    [FmButton(1183851241151930399)]
    RymLink = 1 << 7,

    [Option("Track details")]
    [FmButton("ℹ️", "fm-track-details", requiresDbTrack: true)]
    TrackDetails = 1 << 8,

    [Option("Track preview")]
    [FmButton(1305607890941378672, "track-preview", requiresDbTrack: true)]
    TrackPreview = 1 << 9,

    [Option("Love track")]
    [FmButton("❤️", "fm-track-love", requiresDbTrack: true)]
    TrackLove = 1 << 10,

    [Option("Unlove track")]
    [FmButton("💔", "fm-track-unlove", requiresDbTrack: true)]
    TrackUnlove = 1 << 11,

    [Option("⭐ Track lyrics", supporterOnly: true)]
    [FmButton("🎤", "track-lyrics", requiresDbTrack: true)]
    TrackLyrics = 1 << 12,

    [Option("⭐ Album cover", supporterOnly: true)]
    [FmButton("🖼️", "album-cover", requiresDbTrack: true)]
    AlbumCover = 1 << 13,

    [Option("⭐ Album tracks", supporterOnly: true)]
    [FmButton("💿", "album-tracks", requiresDbTrack: true)]
    AlbumTracks = 1 << 14,

    [Option("⭐ Artist tracks", supporterOnly: true)]
    [FmButton("🎵", "artist-tracks", requiresDbTrack: true)]
    ArtistTracks = 1 << 15
}
