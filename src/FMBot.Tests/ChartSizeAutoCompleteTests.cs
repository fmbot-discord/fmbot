using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Services;

namespace FMBot.Tests;

public class ChartSizeAutoCompleteTests
{
    private static List<string> Suggest(string? input) =>
        ChartSizeAutoComplete.GetSuggestions(input!, ChartService.MaxImages)
            .Select(s => $"{s.Width}x{s.Height}")
            .ToList();

    [Test]
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("big")]
    [TestCase("x5")]
    [TestCase("0")]
    [TestCase("00")]
    [TestCase("0x5")]
    [TestCase("٣")]
    [TestCase("４x４")]
    public void EmptyOrUnparsableInputReturnsDefaults(string? input)
    {
        var results = Suggest(input);

        Assert.That(results.Take(3), Is.EqualTo(new[] { "3x3", "4x4", "5x5" }));
        Assert.That(results, Does.Contain("10x10"));
        Assert.That(results, Does.Contain("8x5"));
    }

    [Test]
    [TestCase("5", "5x5")]
    [TestCase("10", "10x10")]
    [TestCase("15", "15x15")]
    [TestCase("3x", "3x3")]
    [TestCase("3X", "3x3")]
    [TestCase("4x4", "4x4")]
    [TestCase("4X4", "4x4")]
    [TestCase("4×4", "4x4")]
    [TestCase("4*4", "4x4")]
    [TestCase("4 by 4", "4x4")]
    [TestCase("4 4", "4x4")]
    [TestCase(" 4 x 4 ", "4x4")]
    public void FirstSuggestionMatchesIntent(string input, string expectedFirst)
    {
        Assert.That(Suggest(input).First(), Is.EqualTo(expectedFirst));
    }

    [Test]
    public void SingleDigitPrefixesLargerDefaultSizes()
    {
        var results = Suggest("1");

        Assert.That(results.First(), Is.EqualTo("1x1"));
        Assert.That(results.IndexOf("10x10"), Is.LessThan(results.IndexOf("1x3")));
        Assert.That(results.IndexOf("15x15"), Is.LessThan(results.IndexOf("1x3")));
    }

    [Test]
    [TestCase("5x")]
    [TestCase("5 ")]
    [TestCase("5 x ")]
    public void WidthWithSeparatorListsSquareThenAscendingHeights(string input)
    {
        var results = Suggest(input);

        Assert.That(results.Take(6), Is.EqualTo(new[] { "5x5", "5x1", "5x2", "5x3", "5x4", "5x6" }));
    }

    [Test]
    public void CompleteSizeIsFollowedByLongerHeights()
    {
        var results = Suggest("2x1");

        Assert.That(results.First(), Is.EqualTo("2x1"));
        Assert.That(results, Does.Contain("2x10"));
        Assert.That(results, Does.Contain("2x19"));
        Assert.That(results, Does.Not.Contain("2x20"));
    }

    [Test]
    public void OversizedInputSuggestsLargestValidAlternatives()
    {
        var results = Suggest("20x20");

        Assert.That(results, Is.EqualTo(new[] { "20x11", "11x20", "15x15" }));
    }

    [Test]
    public void WidthWiderThanMaxReturnsDefaults()
    {
        var results = Suggest("300x");

        Assert.That(results.First(), Is.EqualTo("3x3"));
    }

    [Test]
    [TestCase("")]
    [TestCase("1")]
    [TestCase("1x")]
    [TestCase("2")]
    [TestCase("15x")]
    [TestCase("225x")]
    public void NeverExceedsDiscordChoiceLimitOrMaxImages(string input)
    {
        var results = ChartSizeAutoComplete.GetSuggestions(input, ChartService.MaxImages);

        Assert.That(results.Count, Is.LessThanOrEqualTo(25));
        Assert.That(results.Count, Is.GreaterThan(0));
        Assert.That(results.All(r => r.Width * r.Height <= ChartService.MaxImages), Is.True);
        Assert.That(results.Distinct().Count(), Is.EqualTo(results.Count));
    }
}
