using FMBot.Domain.Attributes;

namespace FMBot.Domain.Enums;

public enum DataSource
{
    [Option("Last.fm", "Only use Last.fm")]
    LastFm = 1,
    [Option("Full Spotify, then Last.fm", "Use your full Spotify history and add Last.fm afterwards")]
    FullSpotifyThenLastFm = 2,
    [Option("Spotify until full Last.fm", "Use your Spotify history up until you started scrobbling")]
    SpotifyThenFullLastFm = 3
}
