using System;
using System.Text.Json.Serialization;

namespace FMBot.LastFM.Domain.Models;

public partial class ChildTrack
{
    public string Name { get; set; }

    public string Url { get; set; }
    public long? Duration { get; set; }

    public ChildArtistLfm Artist { get; set; }


    [JsonPropertyName("@attr")]
    public ChildTrackAttr Attr { get; set; }
}


public partial class ChildTrackAttr
{
    public long? Rank { get; set; }
}
