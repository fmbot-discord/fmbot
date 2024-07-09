using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FMBot.LastFM.Models;

internal class WeeklyAlbumChartsResponse
{
    public WeeklyAlbumChartsLfm WeeklyAlbumChart { get; set; }
}

internal class WeeklyAlbumChartsLfm
{
    public List<WeeklyAlbumChart> Album { get; set; }
}

internal class WeeklyAlbumChart
{
    public string Mbid { get; set; }
    public string Name { get; set; }
    public string Url { get; set; }
    public long Playcount { get; set; }
    public WeeklyAlbumChartArtist Artist { get; set; }
}

internal class WeeklyAlbumChartArtist
{
    public string Mbid { get; set; }
    [JsonPropertyName("#text")]
    public string Text { get; set; }
}
