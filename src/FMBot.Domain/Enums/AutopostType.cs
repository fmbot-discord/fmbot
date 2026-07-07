using FMBot.Domain.Attributes;

namespace FMBot.Domain.Enums;

public enum AutopostType
{
    [Option("Server recap", "Top artists, albums, new releases and tracks combined")]
    ServerRecap = 0,
    [Option("Top artists", "Your server's top artists")]
    ServerArtists = 1,
    [Option("Top albums", "Your server's top albums")]
    ServerAlbums = 2,
    [Option("Top tracks", "Your server's top tracks")]
    ServerTracks = 3
}
