using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FMBot.LastFM.Domain.Models;

public class WeeklyAlbumChartsResponse
{
    public WeeklyAlbumChartsLfm WeeklyAlbumChart { get; set; }
}

public class WeeklyAlbumChartsLfm
{
    public List<WeeklyAlbumChart> Album { get; set; }
}

public class WeeklyAlbumChart
{
    public string Mbid { get; set; }
    public string Name { get; set; }
    public string Url { get; set; }
    public long Playcount { get; set; }
    public WeeklyAlbumChartArtist Artist { get; set; }
}

public class WeeklyAlbumChartArtist
{
    public string Mbid { get; set; }
    [JsonPropertyName("#text")]
    public string Text { get; set; }
}
