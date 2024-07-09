using System.Text.Json.Serialization;

namespace FMBot.LastFM.Models;


internal class NowPlayingLfmResponse
{
    [JsonPropertyName("nowplaying")]
    public NowPlayingLfm Nowplaying { get; set; }
}

internal class NowPlayingLfm
{
    [JsonPropertyName("artist")]
    public Artist Artist { get; set; }

    [JsonPropertyName("track")]
    public NowPlayedTrack Track { get; set; }

    [JsonPropertyName("ignoredMessage")]
    public IgnoredMessage IgnoredMessage { get; set; }

    [JsonPropertyName("albumArtist")]
    public NowPlayedAlbumArtist AlbumArtist { get; set; }

    [JsonPropertyName("album")]
    public NowPlayedAlbum Album { get; set; }
}


internal class NowPlayedAlbum
{
    [JsonPropertyName("corrected")]
    public string Corrected { get; set; }

    [JsonPropertyName("#text")]
    public string Text { get; set; }
}

internal class NowPlayedAlbumArtist
{
    [JsonPropertyName("corrected")]
    public string Corrected { get; set; }

    [JsonPropertyName("#text")]
    public string Text { get; set; }
}

internal class NowPlayedArtist
{
    [JsonPropertyName("corrected")]
    public string Corrected { get; set; }

    [JsonPropertyName("#text")]
    public string Text { get; set; }
}


internal class NowPlayedTrack
{
    [JsonPropertyName("corrected")]
    public string Corrected { get; set; }

    [JsonPropertyName("#text")]
    public string Text { get; set; }
}
