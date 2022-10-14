using System.Collections.Generic;

namespace FMBot.LastFM.Domain.Models;

public class WeeklyArtistChartsResponse
{
    public WeeklyArtistChartsLfm WeeklyArtistChart { get; set; }
}

public class WeeklyArtistChartsLfm
{
    public List<WeeklyArtistChart> Artist { get; set; }
}

public class WeeklyArtistChart
{
    public string Mbid { get; set; }
    public string Name { get; set; }
    public string Url { get; set; }
    public long Playcount { get; set; }
}
