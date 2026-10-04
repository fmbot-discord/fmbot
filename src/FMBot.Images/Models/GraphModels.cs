using System.Globalization;
using FMBot.Domain.Models;
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

    public GraphType Style { get; init; } = GraphType.Line;

    public bool ZeroBased { get; init; } = true;
    public bool IntegerValues { get; init; } = true;
    public bool ShowArea { get; init; } = true;
    public bool ShowEndDot { get; init; } = true;

    public int MaxYTicks { get; init; } = 5;

    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;
    public GraphInterval? Interval { get; init; }

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

    public static List<GraphTick> Plan(IReadOnlyList<GraphPoint> points, CultureInfo culture, GraphInterval? interval,
        int currentYear, Func<List<GraphTick>, bool> fits)
    {
        if (points.Count < 2)
        {
            return [];
        }

        List<GraphTick> coarsest = [];
        foreach (var step in Steps)
        {
            if (interval == GraphInterval.Year && step.Unit != GraphTickUnit.Year)
            {
                continue;
            }

            if (interval == GraphInterval.Month && step.Unit == GraphTickUnit.Day)
            {
                continue;
            }

            var ticks = Build(points, step, culture, currentYear);
            if (ticks.Count == 0)
            {
                continue;
            }

            if (fits(ticks))
            {
                return ticks;
            }

            coarsest = ticks;
        }

        return coarsest;
    }

    private static List<GraphTick> Build(IReadOnlyList<GraphPoint> points, (GraphTickUnit Unit, int Amount) step,
        CultureInfo culture, int currentYear)
    {
        var first = points[0].Date;
        var indexes = new List<int>();
        var index = 0;

        foreach (var boundary in Boundaries(first, points[^1].Date, step))
        {
            if (first - boundary >= TimeSpan.FromDays(7))
            {
                continue;
            }

            while (index < points.Count && points[index].Date < boundary)
            {
                index++;
            }

            if (index >= points.Count)
            {
                break;
            }

            if (indexes.Count > 0 && indexes[^1] == index)
            {
                continue;
            }

            indexes.Add(index);
        }

        var dates = indexes.Select(s => points[s].Date).ToList();
        var labels = Labels(dates, step.Unit, culture,
            points[0].Date.Year != currentYear || points[^1].Date.Year != currentYear);

        return indexes.Select((s, i) => new GraphTick(s, labels[i])).ToList();
    }

    private static List<string> Labels(List<DateTime> dates, GraphTickUnit unit, CultureInfo culture,
        bool outsideCurrentYear)
    {
        string Year(DateTime date) => date.ToString("yyyy", culture);
        string ShortYear(DateTime date) => $"'{date.ToString("yy", culture)}";
        string Month(DateTime date) => date.ToString("MMM", culture);

        switch (unit)
        {
            case GraphTickUnit.Year:
                return dates.Select(Year).ToList();
            case GraphTickUnit.Month:
                var labels = dates.Select(s => s.Month == 1 ? Year(s) : Month(s)).ToList();
                if (outsideCurrentYear && labels.Count > 0 && dates.All(a => a.Month != 1))
                {
                    labels[0] = $"{Month(dates[0])} {ShortYear(dates[0])}";
                }

                return labels;
            default:
                var pattern = culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM");
                return dates.Select((s, i) =>
                    outsideCurrentYear && (i == 0 || s.Year != dates[i - 1].Year)
                        ? $"{s.ToString(pattern, culture)} {ShortYear(s)}"
                        : s.ToString(pattern, culture)).ToList();
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
                return first.Date;
        }
    }
}

public static class GraphSeries
{
    private const int MaxDailyPoints = 62;
    private const int MaxPoints = 140;
    private const int MaxRenderPoints = 320;
    private const int MaxBarPoints = 16;

    public static GraphInterval PickInterval(DateTime from, DateTime to, int sampleCount = int.MaxValue,
        GraphType style = GraphType.Line)
    {
        var days = (to.Date - from.Date).TotalDays + 1;

        var bar = style == GraphType.Bar;

        var interval = bar
            ? days <= MaxBarPoints ? GraphInterval.Day :
            days / 7 <= MaxBarPoints ? GraphInterval.Week :
            days / 30.44 <= MaxBarPoints ? GraphInterval.Month : GraphInterval.Year
            : days <= MaxDailyPoints ? GraphInterval.Day :
            days / 7 <= MaxPoints ? GraphInterval.Week :
            days / 30.44 <= MaxRenderPoints ? GraphInterval.Month : GraphInterval.Year;

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
        DateTime from, DateTime to, DateTime? weekAnchor = null)
    {
        DateTime Bucket(DateTime date) => interval == GraphInterval.Week && weekAnchor.HasValue
            ? weekAnchor.Value.Date.AddDays(Math.Floor((date.Date - weekAnchor.Value.Date).TotalDays / 7) * 7)
            : StartOfInterval(date, interval);

        var counts = new Dictionary<DateTime, double>();
        foreach (var day in dailyCounts)
        {
            if (day.Date < from.Date || day.Date > to.Date)
            {
                continue;
            }

            var bucket = Bucket(day.Date);
            counts.TryGetValue(bucket, out var existing);
            counts[bucket] = existing + day.Value;
        }

        var points = new List<GraphPoint>();
        var current = Bucket(from);
        var last = Bucket(to);

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

    public static DateTime LimitToMaxPoints(DateTime from, DateTime to, GraphInterval interval,
        GraphType style = GraphType.Line)
    {
        var maxBuckets = style == GraphType.Bar && interval != GraphInterval.Year
            ? MaxBarPoints
            : interval == GraphInterval.Day
                ? MaxDailyPoints
                : MaxRenderPoints;
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
