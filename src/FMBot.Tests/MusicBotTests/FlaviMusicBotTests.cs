namespace FMBot.Tests.MusicBotTests;

public class FlaviMusicBotTests
{
    // Note: FlaviMusicBot now uses Components (ComponentContainer/ComponentSection/TextDisplay)
    // instead of embeds. These tests verify the string parsing logic.
    [Test]
    [TestCase("### **[Michael Jackson - Billie Jean](https://open.spotify.com/track/7J1uxwnxfQLu4APicE5Rnj)** - `04:54`", "Michael Jackson", "Billie Jean")]
    [TestCase("### **[Radiohead - Karma Police](https://open.spotify.com/track/abc123)** - `04:24`", "Radiohead", "Karma Police")]
    public void GetTrackQuery_ShouldExtractCorrectText(string description, string expectedArtist, string expectedTrack)
    {
        // Act
        var trackQuery = MusicBotTestHelper.ParseFlaviFormat(description);

        // Assert
        Assert.That(trackQuery, Contains.Substring(expectedArtist));
        Assert.That(trackQuery, Contains.Substring(expectedTrack));
    }
}
