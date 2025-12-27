namespace FMBot.Tests.MusicBotTests;

public class BettyMusicBotTests
{
    // Note: BettyMusicBot now uses MediaGallery components instead of embeds.
    // The parsing logic extracts "artist | track" from MediaGalleryItem descriptions.
    [Test]
    [TestCase("iluv | Effy", "iluv", "Effy")]
    [TestCase("Taylor Swift | Cruel Summer", "Taylor Swift", "Cruel Summer")]
    public void GetTrackQuery_ShouldExtractCorrectText(string description, string expectedArtist, string expectedTrack)
    {
        // Act
        var trackQuery = MusicBotTestHelper.ParseBettyFormat(description);

        // Assert
        Assert.That(trackQuery, Contains.Substring(expectedArtist));
        Assert.That(trackQuery, Contains.Substring(expectedTrack));
    }
}
