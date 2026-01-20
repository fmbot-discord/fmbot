namespace FMBot.Tests.MusicBotTests;

public class GreenBotMusicBotTests
{
    [Test]
    [TestCase("[Bangarang (feat. Sirah)](https://deezer.com/track/15781392) by [Skrillex](https://deezer.com/track/15781392), requested by [frikandel](https://deezer.com/track/15781392)", "Skrillex", "Bangarang (feat. Sirah)")]
    [TestCase("[Yesterday (Remastered 2015)](https://deezer.com/track/116348612) by [The Beatles](https://deezer.com/track/116348612), requested by [frikandel](https://deezer.com/track/116348612)", "The Beatles", "Yesterday (Remastered 2015)")]
    public void GetTrackQuery_ShouldExtractCorrectText(string description, string expectedArtist, string expectedTrack)
    {
        // Act
        var trackQuery = MusicBotTestHelper.ParseGreenBotFormat(description);

        // Assert
        Assert.That(trackQuery, Contains.Substring(expectedArtist));
        Assert.That(trackQuery, Contains.Substring(expectedTrack));
    }
}
