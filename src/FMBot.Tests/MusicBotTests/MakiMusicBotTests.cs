namespace FMBot.Tests.MusicBotTests;

public class MakiMusicBotTests
{
    [Test]
    [TestCase("**We All Lift Together** — **Warframe**\nRequested by", "Warframe", "We All Lift Together")]
    [TestCase("**Bohemian Rhapsody** — **Queen**\nRequested by", "Queen", "Bohemian Rhapsody")]
    public void GetTrackQuery_ShouldExtractCorrectText(string description, string expectedArtist, string expectedTrack)
    {
        // Act
        var trackQuery = MusicBotTestHelper.ParseMakiFormat(description);

        // Assert
        Assert.That(trackQuery, Contains.Substring(expectedArtist));
        Assert.That(trackQuery, Contains.Substring(expectedTrack));
    }
}
