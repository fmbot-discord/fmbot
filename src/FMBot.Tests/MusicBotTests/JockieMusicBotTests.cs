namespace FMBot.Tests.MusicBotTests;

public class JockieMusicBotTests
{
    [Test]
    [TestCase(":spotify: ​ Started playing The Game of Love by Daft Punk", "Daft Punk", "The Game of Love")]
    [TestCase(":apple_music: ​ Started playing Radio by Alkaline Trio", "Alkaline Trio", "Radio")]
    [TestCase(" ​ Started playing **[Constant Headache by Joyce Manor](https://www.deezer.com/track/67220083)**", "Joyce Manor", "Constant Headache")]
    public void GetTrackQuery_ShouldExtractCorrectText(string description, string expectedArtist, string expectedTrack)
    {
        // Act
        var trackQuery = MusicBotTestHelper.ParseJockieFormat(description);

        // Assert
        Assert.That(trackQuery, Contains.Substring(expectedArtist));
        Assert.That(trackQuery, Contains.Substring(expectedTrack));
    }
}
