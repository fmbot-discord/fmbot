using System.Globalization;
using FMBot.Domain.Models;
using FMBot.Images.Models;

namespace FMBot.Tests;

public class GraphSeriesTests
{
    private static readonly DateTime Until = new(2026, 10, 1);

    [Test]
    [TestCase(7, GraphInterval.Day)]
    [TestCase(30, GraphInterval.Week)]
    [TestCase(90, GraphInterval.Week)]
    [TestCase(180, GraphInterval.Month)]
    [TestCase(365, GraphInterval.Month)]
    [TestCase(729, GraphInterval.Quarter)]
    [TestCase(3000, GraphInterval.Year)]
    public void PickInterval_Bars_KeepAtMostSixteenBuckets(int days, GraphInterval expected)
    {
        Assert.That(GraphSeries.PickInterval(Until.AddDays(-days), Until, style: GraphType.Bar),
            Is.EqualTo(expected));
    }

    [Test]
    [TestCase(30, GraphInterval.Day)]
    [TestCase(365, GraphInterval.Week)]
    [TestCase(729, GraphInterval.Week)]
    [TestCase(3000, GraphInterval.Month)]
    public void PickInterval_Lines_AreUnchanged(int days, GraphInterval expected)
    {
        Assert.That(GraphSeries.PickInterval(Until.AddDays(-days), Until), Is.EqualTo(expected));
    }

    [Test]
    public void PickInterval_LinesWithFewSamples_SkipQuarter()
    {
        Assert.That(GraphSeries.PickInterval(Until.AddDays(-729), Until, sampleCount: 5),
            Is.EqualTo(GraphInterval.Year));
    }

    [Test]
    public void FromDailyCounts_AnchoredWeeks_StartAtWindowStart()
    {
        var from = new DateTime(2026, 9, 1);
        var days = Enumerable.Range(0, 30).Select(s => new GraphPoint { Date = from.AddDays(s), Value = 1 }).ToList();

        var points = GraphSeries.FromDailyCounts(days, GraphInterval.Week, from, new DateTime(2026, 9, 30), from);

        Assert.Multiple(() =>
        {
            Assert.That(points.Select(s => s.Date.Day), Is.EqualTo(new[] { 1, 8, 15, 22, 29 }));
            Assert.That(points.Select(s => s.Value), Is.EqualTo(new double[] { 7, 7, 7, 7, 2 }));
        });
    }

    [Test]
    public void FromDailyCounts_Quarters_GroupByCalendarQuarter()
    {
        var days = new List<GraphPoint>
        {
            new() { Date = new DateTime(2025, 2, 10), Value = 3 },
            new() { Date = new DateTime(2025, 3, 31), Value = 2 },
            new() { Date = new DateTime(2025, 4, 1), Value = 5 },
            new() { Date = new DateTime(2025, 12, 31), Value = 1 }
        };

        var points = GraphSeries.FromDailyCounts(days, GraphInterval.Quarter, new DateTime(2025, 1, 1),
            new DateTime(2025, 12, 31));

        Assert.Multiple(() =>
        {
            Assert.That(points.Select(s => s.Date.Month), Is.EqualTo(new[] { 1, 4, 7, 10 }));
            Assert.That(points.Select(s => s.Value), Is.EqualTo(new double[] { 5, 5, 0, 1 }));
        });
    }

    [Test]
    public void Plan_Quarters_UseQuarterLabelsWithYearAtQ1()
    {
        var points = Enumerable.Range(0, 8)
            .Select(s => new GraphPoint { Date = new DateTime(2024, 10, 1).AddMonths(3 * s), Value = 1 })
            .ToList();

        var ticks = GraphTicks.Plan(points, CultureInfo.GetCultureInfo("en-US"), GraphInterval.Quarter, 2026,
            _ => true);

        Assert.That(ticks.Select(s => s.Label),
            Is.EqualTo(["Q4", "2025", "Q2", "Q3", "Q4", "2026", "Q2", "Q3"]));
    }

    [Test]
    public void Plan_QuartersOutsideCurrentYearWithoutQ1_AddYearToFirstLabel()
    {
        var points = Enumerable.Range(0, 3)
            .Select(s => new GraphPoint { Date = new DateTime(2024, 4, 1).AddMonths(3 * s), Value = 1 })
            .ToList();

        var ticks = GraphTicks.Plan(points, CultureInfo.GetCultureInfo("fr-FR"), GraphInterval.Quarter, 2026,
            _ => true, quarter => $"T{quarter}");

        Assert.That(ticks.Select(s => s.Label), Is.EqualTo(new[] { "T2 '24", "T3", "T4" }));
    }
}
