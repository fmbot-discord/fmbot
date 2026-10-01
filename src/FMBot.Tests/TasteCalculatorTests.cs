using FMBot.Core;
using FMBot.Domain.Models;

namespace FMBot.Tests;

public class TasteCalculatorTests
{
    private static TasteItem Item(string name, long playcount) => new(name, playcount);

    [Test]
    public void GetMatches_OrdersByOwnPlaycountDescending_AndKeepsOwnName()
    {
        var own = new List<TasteItem> { Item("a", 1), Item("b", 30), Item("c", 20), Item("d", 30) };
        var other = new List<TasteItem> { Item("d", 4), Item("c", 3), Item("b", 2), Item("a", 1) };

        var matches = TasteCalculator.GetMatches(own, other, StringComparer.Ordinal);

        Assert.That(matches.Select(m => m.Name), Is.EqualTo(new[] { "b", "d", "c", "a" }));
        Assert.That(matches.Select(m => m.OwnPlaycount), Is.EqualTo(new long[] { 30, 30, 20, 1 }));
        Assert.That(matches.Select(m => m.OtherPlaycount), Is.EqualTo(new long[] { 2, 4, 3, 1 }));
    }

    [Test]
    public void GetMatches_DuplicateOtherName_UsesFirstOccurrence()
    {
        var own = new List<TasteItem> { Item("a", 10) };
        var other = new List<TasteItem> { Item("a", 7), Item("a", 3) };

        var matches = TasteCalculator.GetMatches(own, other, StringComparer.Ordinal);

        Assert.That(matches.Single().OtherPlaycount, Is.EqualTo(7));
    }

    [Test]
    public void GetMatches_DuplicateOwnName_KeepsBothRows()
    {
        var own = new List<TasteItem> { Item("a", 10), Item("a", 4) };
        var other = new List<TasteItem> { Item("a", 7) };

        var matches = TasteCalculator.GetMatches(own, other, StringComparer.Ordinal);

        Assert.That(matches, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetMatches_NullNames_MatchEachOther()
    {
        var own = new List<TasteItem> { Item(null!, 10) };
        var other = new List<TasteItem> { Item(null!, 2) };

        var matches = TasteCalculator.GetMatches(own, other, StringComparer.Ordinal);

        Assert.That(matches.Single().OtherPlaycount, Is.EqualTo(2));
    }

    [Test]
    public void SelectRows_PicksSmallestThresholdThatFitsAmount()
    {
        var matches = new List<TasteMatch>
        {
            new("a", 100, 100),
            new("b", 90, 1),
            new("c", 80, 5),
            new("d", 70, 5),
            new("e", 60, 2),
        };

        var rows = TasteCalculator.SelectRows(matches, 3);

        Assert.That(rows.Select(r => r.Name), Is.EqualTo(new[] { "a", "c", "d" }));
    }

    [Test]
    public void SelectRows_ThresholdNeverFits_KeepsOrderAndTakesAmount()
    {
        var matches = Enumerable.Range(0, 5).Select(i => new TasteMatch($"m{i}", 500 - i, 500)).ToList();

        var rows = TasteCalculator.SelectRows(matches, 2);

        Assert.That(rows.Select(r => r.Name), Is.EqualTo(new[] { "m0", "m1" }));
    }

    [Test]
    public void SelectRows_ThresholdAppliesToBothSides()
    {
        var matches = new List<TasteMatch>
        {
            new("a", 100, 100),
            new("b", 2, 100),
            new("c", 100, 2),
            new("d", 50, 50),
        };

        var rows = TasteCalculator.SelectRows(matches, 2);

        Assert.That(rows.Select(r => r.Name), Is.EqualTo(new[] { "a", "d" }));
    }
}
