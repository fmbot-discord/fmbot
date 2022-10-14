using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FMBot.LastFM.Domain.Models;

public class WeeklyTrackChartsResponse
{
    public WeeklyTrackChartsLfm WeeklyTrackChart { get; set; }
}

public class WeeklyTrackChartsLfm
{
    public List<WeeklyTrackChart> Track { get; set; }
}

public class WeeklyTrackChart
{
    public string Mbid { get; set; }
    public string Name { get; set; }
    public string Url { get; set; }
    public long Playcount { get; set; }
    public WeeklyTrackChartArtist Artist { get; set; }
}

public class WeeklyTrackChartArtist
{
    public string Mbid { get; set; }
    [JsonPropertyName("#text")]
    public string Text { get; set; }
}
