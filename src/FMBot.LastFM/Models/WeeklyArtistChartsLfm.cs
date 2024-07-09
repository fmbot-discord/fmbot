using System.Collections.Generic;

namespace FMBot.LastFM.Models;

internal class WeeklyArtistChartsResponse
{
    public WeeklyArtistChartsLfm WeeklyArtistChart { get; set; }
}

internal class WeeklyArtistChartsLfm
{
    public List<WeeklyArtistChart> Artist { get; set; }
}

internal class WeeklyArtistChart
{
    public string Mbid { get; set; }
    public string Name { get; set; }
    public string Url { get; set; }
    public long Playcount { get; set; }
}
