namespace FMBot.Tests.MusicBotTests;

public class ListenMusicBotTests
{
    [Test]
    [TestCase("Bangarang (feat. Sirah)", "-# Skrillex", "Skrillex", "Bangarang")]
    public void GetTrackQuery_ShouldExtractCorrectText(string title, string description, string expectedArtist, string expectedTrack)
    {
        // Act
        var trackQuery = MusicBotTestHelper.ParseListenFormat(title, description);

        // Assert
        Assert.That(trackQuery, Contains.Substring(expectedArtist));
        Assert.That(trackQuery, Contains.Substring(expectedTrack));
    }
}
