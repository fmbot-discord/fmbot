using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FMBot.LastFM.Domain.Models;

public class TopTracksLfmResponse
{
    public TopTracksLfm TopTracks { get; set; }
}

public class TopTracksLfm
{
    [JsonPropertyName("@attr")]
    public TopTracksAttr Attr { get; set; }
    public List<TopTrackLfm> Track { get; set; }
}

public partial class TopTracksAttr
{
    public long Page { get; set; }
    public long Total { get; set; }
    public string User { get; set; }
    public long PerPage { get; set; }
    public long TotalPages { get; set; }
}

public class TopTrackLfm
{
    public long Duration { get; set; }
    public long Playcount { get; set; }
    public Artist Artist { get; set; }
    public List<ImageLfm> Image { get; set; }
    public string Mbid { get; set; }
    public string Name { get; set; }
    public string Url { get; set; }
}

public class Artist
{
    public Uri Url { get; set; }
    public string Name { get; set; }
    public string Mbid { get; set; }
}
