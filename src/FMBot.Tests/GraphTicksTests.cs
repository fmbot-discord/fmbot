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

    private static List<GraphPoint> Weeks(int year, int month, int day, int count) =>
        Points(new DateTime(year, month, day), count, (d, n) => d.AddDays(7 * n));

    private static Func<List<GraphTick>, bool> AtMost(int count) => ticks => ticks.Count <= count;

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
    public void Plan_NothingFits_ReturnsCoarsestNonEmptyStep()
    {
        var ticks = GraphTicks.Plan(Months(2025, 10, 13), English, GraphInterval.Month, 2026, _ => false);

        Assert.That(ticks.Select(s => s.Label), Is.EqualTo(new[] { "2026" }));
    }
}
