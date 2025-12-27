namespace FMBot.Tests.MusicBotTests;

public class BleedMusicBotTests
{
    [Test]
    [TestCase("Now playing [`Dive (Official Video)`](https://www.youtube.com/watch?v=jetLHzTiTHc) by **Mall Grab**", "Mall Grab", "Dive (Official Video)")]
    [TestCase("Now playing [`HUMBLE.`](https://www.youtube.com/watch?v=tvTRZJ-4EyI) by **Kendrick Lamar**", "Kendrick Lamar", "HUMBLE.")]
    public void GetTrackQuery_ShouldExtractCorrectText(string description, string expectedArtist, string expectedTrack)
    {
        // Act
        var trackQuery = MusicBotTestHelper.ParseBleedFormat(description);

        // Assert
        Assert.That(trackQuery, Contains.Substring(expectedArtist));
        Assert.That(trackQuery, Contains.Substring(expectedTrack));
    }
}
