using FMBot.Bot.Extensions;

namespace FMBot.Tests;

public class ColorExtensionsTests
{
    [Test]
    [TestCase("#FF5733", "#FF5733")]
    [TestCase("ff5733", "#FF5733")]
    [TestCase(" #ff5733 ", "#FF5733")]
    [TestCase("#FFF", "#FFFFFF")]
    [TestCase("abc", "#AABBCC")]
    [TestCase("#ABCD", null)]
    [TestCase("#ABCDE", null)]
    [TestCase("#GGGGGG", null)]
    [TestCase("#FF5733FF", null)]
    [TestCase("", null)]
    [TestCase(null, null)]
    public void TestNormalizeHexColor(string? input, string? expected)
    {
        Assert.That(ColorExtensions.NormalizeHexColor(input), Is.EqualTo(expected));
    }

    [Test]
    [TestCase("#FF5733", 0xFF5733)]
    [TestCase("#FFF", 0xFFFFFF)]
    [TestCase("#0000FF", 0x0000FF)]
    public void TestTryParseHexColor(string input, int expectedRawValue)
    {
        Assert.That(ColorExtensions.TryParseHexColor(input, out var color), Is.True);
        Assert.That(color.RawValue, Is.EqualTo(expectedRawValue));
    }

    [Test]
    [TestCase("#ABCD")]
    [TestCase("nope")]
    [TestCase(null)]
    public void TestTryParseHexColorRejectsInvalid(string? input)
    {
        Assert.That(ColorExtensions.TryParseHexColor(input, out _), Is.False);
    }
}
