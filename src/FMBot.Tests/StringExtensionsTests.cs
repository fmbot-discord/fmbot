using FMBot.Bot.Extensions;

namespace FMBot.Tests;

public class StringExtensionsTests
{
    [Test]
    [TestCase("AC/DC", "AC/DC")]
    [TestCase("#1 Crush", "#1 Crush")]
    [TestCase("Song #2", "Song #2")]
    [TestCase("Home ~ Resonance", "Home ~ Resonance")]
    [TestCase("A | B", "A | B")]
    [TestCase("1. Outside", "1. Outside")]
    [TestCase("**bold**", @"\*\*bold\*\*")]
    [TestCase("snake_case", @"snake\_case")]
    [TestCase("`code`", @"\`code\`")]
    [TestCase(@"back\slash", @"back\\slash")]
    [TestCase("~~strike~~", @"\~\~strike\~\~")]
    [TestCase("||spoiler||", @"\|\|spoiler\|\|")]
    [TestCase("<@1337>", @"<@1337\>")]
    [TestCase("<:emote:1337>", @"<:emote:1337\>")]
    [TestCase("# Header", @"\# Header")]
    [TestCase("### Header", @"\### Header")]
    [TestCase("#### Not a header", "#### Not a header")]
    [TestCase("-# Subtext", @"\-# Subtext")]
    [TestCase("line one\n# Header", "line one\n\\# Header")]
    [TestCase("line one\n-# Subtext", "line one\n\\-# Subtext")]
    [TestCase(null, null)]
    public void TestSanitize(string? input, string? expected)
    {
        Assert.That(StringExtensions.Sanitize(input), Is.EqualTo(expected));
    }

    [Test]
    [TestCase("<a href=\"https://www.last.fm/music/Muse\">Muse</a> are a rock band", "Muse are a rock band")]
    [TestCase("Formed in <b>1994</b>", "Formed in 1994")]
    [TestCase("foo<br />bar", "foo bar")]
    [TestCase("foo<br>bar", "foo bar")]
    [TestCase("Simon &amp; Garfunkel", "Simon & Garfunkel")]
    [TestCase("don&#39;t stop", "don't stop")]
    [TestCase("5 < 10 and no closing bracket", "5 < 10 and no closing bracket")]
    [TestCase("spaced     out", "spaced out")]
    [TestCase("one\n\n\n\ntwo", "one\ntwo")]
    [TestCase("  trimmed  ", "trimmed")]
    [TestCase(null, null)]
    [TestCase("", "")]
    public void TestStripHtml(string? input, string? expected)
    {
        Assert.That(StringExtensions.StripHtml(input), Is.EqualTo(expected));
    }
}
