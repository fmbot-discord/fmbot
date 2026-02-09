using System;
using System.Collections.Generic;

namespace FMBot.Domain.Models;

public class GraphDataPoint
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}

public class ArtistGraphData
{
    public string ArtistName { get; set; }
    public int TotalPlays { get; set; }
    public List<GraphDataPoint> DataPoints { get; set; }
}

public enum GraphTimeInterval
{
    Daily,
    Weekly,
    Monthly
}

public enum GraphType
{
    ArtistGrowth
}

public static class GraphHelpers
{
    public static GraphTimeInterval GetIntervalForPlayDays(int playDays)
    {
        return playDays switch
        {
            <= 45 => GraphTimeInterval.Daily,
            <= 180 => GraphTimeInterval.Weekly,
            _ => GraphTimeInterval.Monthly
        };
    }

    public static string GetIntervalString(GraphTimeInterval interval)
    {
        return interval switch
        {
            GraphTimeInterval.Daily => "day",
            GraphTimeInterval.Weekly => "week",
            GraphTimeInterval.Monthly => "month",
            _ => "day"
        };
    }
}
