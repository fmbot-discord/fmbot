using System.Globalization;
using FMBot.Images.Models;

namespace FMBot.Tests;

public class GraphTicksTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static List<GraphPoint> Points(DateTime first, int count, Func<DateTime, int, DateTime> next)
    {
        var points = new List<GraphPoint>();
        var date = first;
        for (var i = 0; i < count; i++)
        {
            points.Add(new GraphPoint { Date = date, Value = 1 });
            date = next(date, 1);
        }

        return points;
    }

    private static List<GraphPoint> Months(int year, int month, int count) =>
        Points(new DateTime(year, month, 1), count, (d, n) => d.AddMonths(n));

    private static List<GraphPoint> Days(int year, int month, int day, int count) =>
        Points(new DateTime(year, month, day), count, (d, n) => d.AddDays(n));

    private static List<GraphPoint> Weeks(int year, int month, int day, int count) =>
        Points(new DateTime(year, month, day), count, (d, n) => d.AddDays(7 * n));

    private static Func<List<GraphTick>, bool> AtMost(int count) => ticks => ticks.Count <= count;

    [Test]
    public void Plan_CalendarYear_LabelsEveryMonthWithYearAtJanuary()
    {
        var ticks = GraphTicks.Plan(Months(2021, 1, 12), English, GraphInterval.Month, 2026, AtMost(12));

        Assert.Multiple(() =>
        {
            Assert.That(ticks.Select(s => s.Index), Is.EqualTo(Enumerable.Range(0, 12)));
            Assert.That(ticks.Select(s => s.Label),
                Is.EqualTo(new[] { "2021", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" }));
        });
    }

    [Test]
    public void Plan_RollingYear_ShowsYearOnlyWhereItChanges()
    {
        var ticks = GraphTicks.Plan(Months(2025, 10, 13), English, GraphInterval.Month, 2026, AtMost(13));

        Assert.That(ticks.Select(s => s.Label), Is.EqualTo(new[]
        {
            "Oct", "Nov", "Dec", "2026", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct"
        }));
    }

    [Test]
    public void Plan_CoarserMonthStep_StaysOnCalendarGridWithoutSnappingFirstPoint()
    {
        var ticks = GraphTicks.Plan(Months(2025, 10, 13), English, GraphInterval.Month, 2026, AtMost(7));

        Assert.Multiple(() =>
        {
            Assert.That(ticks.Select(s => s.Index), Is.EqualTo(new[] { 1, 3, 5, 7, 9, 11 }));
            Assert.That(ticks.Select(s => s.Label), Is.EqualTo(new[] { "Nov", "2026", "Mar", "May", "Jul", "Sep" }));
        });
    }

    [Test]
    public void Plan_CoarserYearStep_SkipsBoundaryBeforeFirstYear()
    {
        var points = Points(new DateTime(2015, 1, 1), 12, (d, n) => d.AddYears(n));

        var ticks = GraphTicks.Plan(points, English, GraphInterval.Year, 2026, AtMost(6));

        Assert.That(ticks.Select(s => s.Label), Is.EqualTo(new[] { "2016", "2018", "2020", "2022", "2024", "2026" }));
    }

    [Test]
    public void Plan_DailyMonth_CountsWeeksFromFirstDay()
    {
        var ticks = GraphTicks.Plan(Days(2026, 9, 1, 30), English, GraphInterval.Day, 2026, AtMost(5));

        Assert.That(ticks.Select(s => s.Label), Is.EqualTo(new[] { "Sep 1", "Sep 8", "Sep 15", "Sep 22", "Sep 29" }));
    }

    [Test]
    public void Plan_DailyOutsideCurrentYear_AddsYearToFirstLabel()
    {
        var ticks = GraphTicks.Plan(Days(2024, 3, 1, 31), English, GraphInterval.Day, 2026, AtMost(5));

        Assert.That(ticks.Select(s => s.Label),
            Is.EqualTo(new[] { "Mar 1 '24", "Mar 8", "Mar 15", "Mar 22", "Mar 29" }));
    }

    [Test]
    public void Plan_DailyAcrossNewYear_AddsYearWhereItChanges()
    {
        var ticks = GraphTicks.Plan(Days(2025, 12, 20, 28), English, GraphInterval.Day, 2026, AtMost(4));

        Assert.That(ticks.Select(s => s.Label),
            Is.EqualTo(new[] { "Dec 20 '25", "Dec 27", "Jan 3 '26", "Jan 10" }));
    }

    [Test]
    public void Plan_WeeklyPointsStartingJustAfterNewYear_KeepJanuaryTick()
    {
        var ticks = GraphTicks.Plan(Weeks(2021, 1, 4, 52), English, GraphInterval.Week, 2026, AtMost(12));

        Assert.Multiple(() =>
        {
            Assert.That(ticks[0].Index, Is.EqualTo(0));
            Assert.That(ticks[0].Label, Is.EqualTo("2021"));
            Assert.That(ticks, Has.Count.EqualTo(12));
        });
    }

    [Test]
    public void Plan_WeeklyPointsStartingLateDecember_SkipDecemberTick()
    {
        var ticks = GraphTicks.Plan(Weeks(2025, 12, 29, 40), English, GraphInterval.Week, 2026, AtMost(10));

        Assert.Multiple(() =>
        {
            Assert.That(ticks[0].Index, Is.EqualTo(1));
            Assert.That(ticks[0].Label, Is.EqualTo("2026"));
        });
    }

    [Test]
    public void Plan_MonthsWithoutJanuaryOutsideCurrentYear_AddsYearToFirstLabel()
    {
        var ticks = GraphTicks.Plan(Months(2024, 3, 7), English, GraphInterval.Month, 2026, AtMost(7));

        Assert.That(ticks.Select(s => s.Label),
            Is.EqualTo(new[] { "Mar '24", "Apr", "May", "Jun", "Jul", "Aug", "Sep" }));
    }

    [Test]
    public void Plan_MonthsWithinCurrentYear_HaveNoYear()
    {
        var ticks = GraphTicks.Plan(Months(2026, 4, 6), English, GraphInterval.Month, 2026, AtMost(6));

        Assert.That(ticks.Select(s => s.Label), Is.EqualTo(new[] { "Apr", "May", "Jun", "Jul", "Aug", "Sep" }));
    }

    [Test]
    public void Plan_NothingFits_ReturnsCoarsestNonEmptyStep()
    {
        var ticks = GraphTicks.Plan(Months(2025, 10, 13), English, GraphInterval.Month, 2026, _ => false);

        Assert.That(ticks.Select(s => s.Label), Is.EqualTo(new[] { "2026" }));
    }
}
