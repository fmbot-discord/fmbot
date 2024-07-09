using System;
using System.Text.Json.Serialization;

namespace FMBot.LastFM.Models;

internal class UserResponseLfm
{
    [JsonPropertyName("user")]
    public UserLfm User { get; set; }
}

internal class UserLfm
{
    public long Playlists { get; set; }

    public long Playcount { get; set; }
    [JsonPropertyName("artist_count")]
    public long ArtistCount { get; set; }
    [JsonPropertyName("album_count")]
    public long AlbumCount { get; set; }
    [JsonPropertyName("track_count")]
    public long TrackCount { get; set; }

    public string Gender { get; set; }

    public string Name { get; set; }

    public long Subscriber { get; set; }

    public Uri Url { get; set; }

    public string Country { get; set; }

    public ImageLfm[] Image { get; set; }

    public Registered Registered { get; set; }

    public string Type { get; set; }

    public long Age { get; set; }

    public long Bootstrap { get; set; }

    public string Realname { get; set; }
}

internal class Registered
{
    public long Unixtime { get; set; }

    [JsonPropertyName("#text")]
    public long Text { get; set; }
}
