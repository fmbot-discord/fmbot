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

    public List<GraphTick> Ticks { get; init; } = [];

    public Func<double, string> ValueLabel { get; init; }
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

public record GraphTick(int Index, string Label);

public enum GraphTickUnit
{
    Day = 1,
    Month = 2,
    Year = 3
}

public static class GraphTicks
{
    private static readonly (GraphTickUnit Unit, int Amount)[] Steps =
    [
        (GraphTickUnit.Day, 1), (GraphTickUnit.Day, 2), (GraphTickUnit.Day, 7),
        (GraphTickUnit.Month, 1), (GraphTickUnit.Month, 2), (GraphTickUnit.Month, 3), (GraphTickUnit.Month, 6),
        (GraphTickUnit.Year, 1), (GraphTickUnit.Year, 2), (GraphTickUnit.Year, 5), (GraphTickUnit.Year, 10)
    ];

    public static List<GraphTick> Plan(IReadOnlyList<GraphPoint> points, int maxTicks, CultureInfo culture)
    {
        if (points.Count < 2 || maxTicks < 2)
        {
            return [];
        }

        var first = points[0].Date;
        var last = points[^1].Date;

        var step = Steps[^1];
        foreach (var candidate in Steps)
        {
            if (CountBoundaries(first, last, candidate) <= maxTicks)
            {
                step = candidate;
                break;
            }
        }

        var label = LabelFor(step.Unit, first.Year != last.Year, culture);

        var ticks = new List<GraphTick>();
        var index = 0;

        foreach (var boundary in Boundaries(first, last, step))
        {
            while (index < points.Count && points[index].Date < boundary)
            {
                index++;
            }

            if (index >= points.Count)
            {
                break;
            }

            if (ticks.Count > 0 && ticks[^1].Index == index)
            {
                continue;
            }

            ticks.Add(new GraphTick(index, label(points[index].Date)));
        }

        if (ticks.Count >= 3 && ticks[0].Index == 0 &&
            (ticks[1].Index - ticks[0].Index) * 2 < ticks[2].Index - ticks[1].Index)
        {
            ticks.RemoveAt(0);
        }

        return ticks;
    }

    private static Func<DateTime, string> LabelFor(GraphTickUnit unit, bool multipleYears, CultureInfo culture)
    {
        switch (unit)
        {
            case GraphTickUnit.Year:
                return date => date.ToString("yyyy", culture);
            case GraphTickUnit.Month:
                return multipleYears
                    ? date => $"{date.ToString("MMM", culture)} '{date.ToString("yy", culture)}"
                    : date => date.ToString("MMM", culture);
            default:
                var pattern = culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM");
                return multipleYears
                    ? date => $"{date.ToString(pattern, culture)} '{date.ToString("yy", culture)}"
                    : date => date.ToString(pattern, culture);
        }
    }

    private static IEnumerable<DateTime> Boundaries(DateTime first, DateTime last,
        (GraphTickUnit Unit, int Amount) step)
    {
        var current = AlignedStart(first, step);

        while (current.Date <= last.Date)
        {
            yield return current;

            current = step.Unit switch
            {
                GraphTickUnit.Year => current.AddYears(step.Amount),
                GraphTickUnit.Month => current.AddMonths(step.Amount),
                _ => current.AddDays(step.Amount)
            };
        }
    }

    private static int CountBoundaries(DateTime first, DateTime last, (GraphTickUnit Unit, int Amount) step)
    {
        var start = AlignedStart(first, step);

        if (last.Date < start.Date)
        {
            return 0;
        }

        return step.Unit switch
        {
            GraphTickUnit.Year => (last.Year - start.Year) / step.Amount + 1,
            GraphTickUnit.Month => ((last.Year - start.Year) * 12 + last.Month - start.Month) / step.Amount + 1,
            _ => (int)((last.Date - start.Date).TotalDays / step.Amount) + 1
        };
    }

    private static DateTime AlignedStart(DateTime first, (GraphTickUnit Unit, int Amount) step)
    {
        switch (step.Unit)
        {
            case GraphTickUnit.Year:
                return new DateTime(first.Year - first.Year % step.Amount, 1, 1, 0, 0, 0, first.Kind);
            case GraphTickUnit.Month:
                var month = first.Month - 1;
                return new DateTime(first.Year, month - month % step.Amount + 1, 1, 0, 0, 0, first.Kind);
            default:
                return step.Amount % 7 == 0
                    ? GraphSeries.StartOfInterval(first, GraphInterval.Week)
                    : first.Date;
        }
    }
}

public static class GraphSeries
{
    private const int MaxDailyPoints = 62;
    private const int MaxPoints = 140;
    private const int MaxRenderPoints = 320;

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

    public static (DateTime From, DateTime To) TrimToWholeIntervals(DateTime from, DateTime to, GraphInterval interval)
    {
        var firstStart = StartOfInterval(from, interval);
        if (firstStart < from.Date)
        {
            from = AddIntervals(firstStart, interval, 1);
        }

        var lastStart = StartOfInterval(to, interval);
        if (AddIntervals(lastStart, interval, 1) > to.Date.AddDays(1))
        {
            to = lastStart.AddDays(-1);
        }

        return (from, to);
    }

    public static DateTime LimitToMaxPoints(DateTime from, DateTime to, GraphInterval interval)
    {
        var maxBuckets = interval == GraphInterval.Day ? MaxDailyPoints : MaxRenderPoints;
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
