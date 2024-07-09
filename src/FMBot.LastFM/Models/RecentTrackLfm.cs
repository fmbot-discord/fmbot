using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using FMBot.LastFM.Converters;

namespace FMBot.LastFM.Models;

internal class RecentTracksListLfmResponseModel
{
    public RecentTracksLfmList RecentTracks { get; set; }
}

internal class LovedTracksListLfmResponseModel
{
    public RecentTracksLfmList LovedTracks { get; set; }
}

internal class RecentTracksLfmList
{
    [JsonPropertyName("@attr")]
    public AttributesLfm AttributesLfm { get; set; }

    [JsonPropertyName("track")]
    [JsonConverter(typeof(TrackListConverter))]
    public List<RecentTrackLfm> Track { get; set; }
}

internal class AttributesLfm
{
    public long Page { get; set; }

    public long Total { get; set; }

    public string User { get; set; }

    public long PerPage { get; set; }

    public long TotalPages { get; set; }
}

internal class RecentTrackLfm
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

internal class TrackAttributesLfm
{
    public bool Nowplaying { get; set; }
}


internal class Date
{
    public long Uts { get; set; }

    [JsonPropertyName("#text")]
    public string Text { get; set; }
}

internal class SmallAlbum
{
    public Guid? Mbid { get; set; }

    [JsonPropertyName("#text")]
    public string Text { get; set; }
}

internal class SmallArtist
{
    public string Url { get; set; }
    public Guid? Mbid { get; set; }

    [JsonPropertyName("name")]
    public string Text { get; set; }
}
