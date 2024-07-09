using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FMBot.LastFM.Models;

internal class WeeklyTrackChartsResponse
{
    public WeeklyTrackChartsLfm WeeklyTrackChart { get; set; }
}

internal class WeeklyTrackChartsLfm
{
    public List<WeeklyTrackChart> Track { get; set; }
}

internal class WeeklyTrackChart
{
    public string Mbid { get; set; }
    public string Name { get; set; }
    public string Url { get; set; }
    public long Playcount { get; set; }
    public WeeklyTrackChartArtist Artist { get; set; }
}

internal class WeeklyTrackChartArtist
{
    public string Mbid { get; set; }
    [JsonPropertyName("#text")]
    public string Text { get; set; }
}
