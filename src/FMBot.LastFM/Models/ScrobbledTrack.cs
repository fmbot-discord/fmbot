using System.Text.Json.Serialization;

namespace FMBot.LastFM.Models;

internal class ScrobbledTrack
{
    public Scrobbles Scrobbles { get; set; }
}

internal class Scrobbles
{
    [JsonPropertyName("@attr")]

    public ScrobbledTrackAttr Attr { get; set; }

    public Scrobble Scrobble { get; set; }
}

internal class ScrobbledTrackAttr
{
    public long Accepted { get; set; }

    public long Ignored { get; set; }
}

internal class Scrobble
{
    public ScrobbledItem Artist { get; set; }

    public IgnoredMessage IgnoredMessage { get; set; }

    public ScrobbledItem AlbumArtist { get; set; }

    public string Timestamp { get; set; }

    public ScrobbledItem Album { get; set; }

    public ScrobbledItem Track { get; set; }
}

internal class ScrobbledItem
{
    public long Corrected { get; set; }

    [JsonPropertyName("#text")]
    public string Text { get; set; }
}

internal class IgnoredMessage
{
    public long Code { get; set; }

    [JsonPropertyName("#text")]
    public string Text { get; set; }
}
