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
    [TestCase("big")]
    [TestCase("x5")]
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
    [TestCase("3x", "3x3")]
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
    [TestCase("")]
    [TestCase("1")]
    [TestCase("1x")]
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
