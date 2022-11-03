using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;


namespace FMBot.LastFM.Domain.Models;

public class RecentTracksListLfmResponseModel
{
    public RecentTracksLfmList RecentTracks { get; set; }
}

public class LovedTracksListLfmResponseModel
{
    public RecentTracksLfmList LovedTracks { get; set; }
}

public class RecentTracksLfmList
{
    [JsonPropertyName("@attr")]
    public AttributesLfm AttributesLfm { get; set; }

    public List<RecentTrackLfm> Track { get; set; }
}

public class AttributesLfm
{
    public long Page { get; set; }

    public long Total { get; set; }

    public string User { get; set; }

    public long PerPage { get; set; }

    public long TotalPages { get; set; }
}

public class RecentTrackLfm
{
    [JsonPropertyName("@attr")]
    public TrackAttributesLfm AttributesLfm { get; set; }

    public string Loved { get; set; }

    public SmallArtist Artist { get; set; }

    public ImageLfm[] Image { get; set; }

    public Date Date { get; set; }

    public Uri Url { get; set; }

    public string Name { get; set; }

    public SmallAlbum Album { get; set; }
}

public class TrackAttributesLfm
{
    public bool Nowplaying { get; set; }
}


public class Date
{
    public long Uts { get; set; }

    [JsonPropertyName("#text")]
    public string Text { get; set; }
}

public partial class SmallAlbum
{
    public Guid? Mbid { get; set; }

    [JsonPropertyName("#text")]
    public string Text { get; set; }
}

public partial class SmallArtist
{
    public string Url { get; set; }
    public Guid? Mbid { get; set; }

    [JsonPropertyName("name")]
    public string Text { get; set; }
}
