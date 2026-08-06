using System.Globalization;
using SkiaSharp;

namespace FMBot.Images.Models;

public enum GraphInterval
{
    Day = 1,
    Week = 2,
    Month = 3,
    Year = 4
}

public class GraphPoint
{
    public DateTime Date { get; init; }
    public double Value { get; init; }
}

public class LineGraph
{
    public List<GraphPoint> Points { get; init; } = [];

    public int Width { get; init; } = 520;
    public int Height { get; init; } = 220;

    public SKColor LineColor { get; init; } = GraphColors.FmbotBlue;

    public bool ZeroBased { get; init; } = true;
    public bool IntegerValues { get; init; } = true;
    public bool ShowArea { get; init; } = true;
    public bool ShowEndDot { get; init; } = true;

    public int MaxYTicks { get; init; } = 5;
    public int MaxXTicks { get; init; } = 5;

    public Func<double, string> ValueLabel { get; init; }
    public Func<DateTime, string> DateLabel { get; init; }
}

public class PlayHistoryGraph
{
    public MemoryStream Image { get; init; }
    public GraphInterval Interval { get; init; }
}

public static class GraphColors
{
    public static readonly SKColor FmbotBlue = new(0x56, 0x74, 0xB9);
    public static readonly SKColor Cyan = new(0x68, 0xDD, 0xE4);
}

public static class GraphLabels
{
    public static Func<DateTime, string> ForInterval(GraphInterval interval, CultureInfo culture)
    {
        switch (interval)
        {
            case GraphInterval.Year:
                return date => date.ToString("yyyy", culture);
            case GraphInterval.Month:
                return date => $"{date.ToString("MMM", culture)} '{date.ToString("yy", culture)}";
            case GraphInterval.Week:
                var weekPattern = culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM");
                return date => $"{date.ToString(weekPattern, culture)} '{date.ToString("yy", culture)}";
            default:
                var dayPattern = culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM");
                return date => date.ToString(dayPattern, culture);
        }
    }
}

public static class GraphSeries
{
    private const int MaxDailyPoints = 62;
    private const int MaxPoints = 140;

    public static GraphInterval PickInterval(DateTime from, DateTime to, int sampleCount = int.MaxValue)
    {
        var days = (to.Date - from.Date).TotalDays + 1;

        var interval = days <= MaxDailyPoints ? GraphInterval.Day :
            days / 7 <= MaxPoints ? GraphInterval.Week :
            days / 30.44 <= MaxPoints ? GraphInterval.Month : GraphInterval.Year;

        while (interval < GraphInterval.Year && BucketCount(days, interval) > sampleCount)
        {
            interval++;
        }

        return interval;
    }

    private static double BucketCount(double days, GraphInterval interval)
    {
        return interval switch
        {
            GraphInterval.Week => days / 7,
            GraphInterval.Month => days / 30.44,
            GraphInterval.Year => days / 365.25,
            _ => days
        };
    }

    public static List<GraphPoint> FromDailyCounts(IEnumerable<GraphPoint> dailyCounts, GraphInterval interval,
        DateTime from, DateTime to)
    {
        var counts = new Dictionary<DateTime, double>();
        foreach (var day in dailyCounts)
        {
            if (day.Date < from.Date || day.Date > to.Date)
            {
                continue;
            }

            var bucket = StartOfInterval(day.Date, interval);
            counts.TryGetValue(bucket, out var existing);
            counts[bucket] = existing + day.Value;
        }

        var points = new List<GraphPoint>();
        var current = StartOfInterval(from, interval);
        var last = StartOfInterval(to, interval);

        while (current <= last)
        {
            counts.TryGetValue(current, out var value);
            points.Add(new GraphPoint
            {
                Date = current,
                Value = value
            });
            current = AddIntervals(current, interval, 1);
        }

        return points;
    }

    public static DateTime LimitToMaxPoints(DateTime from, DateTime to, GraphInterval interval)
    {
        var maxBuckets = interval == GraphInterval.Day ? MaxDailyPoints : MaxPoints;
        var oldest = AddIntervals(StartOfInterval(to, interval), interval, -(maxBuckets - 1));

        return from < oldest ? oldest : from;
    }

    public static DateTime EarliestStart(DateTime to, GraphInterval interval)
    {
        var minimumBuckets = interval switch
        {
            GraphInterval.Week => 8,
            GraphInterval.Month => 6,
            GraphInterval.Year => 5,
            _ => 14
        };

        return AddIntervals(StartOfInterval(to, interval), interval, -(minimumBuckets - 1));
    }

    public static DateTime StartOfInterval(DateTime date, GraphInterval interval)
    {
        return interval switch
        {
            GraphInterval.Week => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
            GraphInterval.Month => new DateTime(date.Year, date.Month, 1, 0, 0, 0, date.Kind),
            GraphInterval.Year => new DateTime(date.Year, 1, 1, 0, 0, 0, date.Kind),
            _ => date.Date
        };
    }

    public static DateTime AddIntervals(DateTime date, GraphInterval interval, int amount)
    {
        return interval switch
        {
            GraphInterval.Week => date.AddDays(7 * amount),
            GraphInterval.Month => date.AddMonths(amount),
            GraphInterval.Year => date.AddYears(amount),
            _ => date.AddDays(amount)
        };
    }
}
