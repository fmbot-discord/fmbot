using FMBot.Images.Models;

namespace FMBot.Tests;

public class GraphSeriesTests
{
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
}
