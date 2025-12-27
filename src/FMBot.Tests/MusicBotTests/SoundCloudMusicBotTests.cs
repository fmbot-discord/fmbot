namespace FMBot.Tests.MusicBotTests;

public class SoundCloudMusicBotTests
{
    [Test]
    [TestCase("SoundCloud - Original - Some Great Track", "Original", "Some Great Track")]
    [TestCase("SoundCloud - Led Zeppelin - Stairway to Heaven", "Led Zeppelin", "Stairway to Heaven")]
    public void GetTrackQuery_ShouldExtractCorrectText(string description, string expectedArtist, string expectedTrack)
    {
        // Act
        var trackQuery = MusicBotTestHelper.ParseSoundCloudFormat(description);

        // Assert
        Assert.That(trackQuery, Contains.Substring(expectedArtist));
        Assert.That(trackQuery, Contains.Substring(expectedTrack));
    }
}
