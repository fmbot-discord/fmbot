namespace FMBot.Tests.MusicBotTests;

public class CakeyBotMusicBotTests
{
    [Test]
    [TestCase("<:CB_NowPlaying:1271328269416796251> Now playing: [Skrillex - Rumble](https://cakey.bot) [<@125740103539621888>]\n<:CB_Volume:1271328279898357760> Volume: `50`", "Skrillex", "Rumble")]
    [TestCase("<:CB_NowPlaying:1271328269416796251> Now playing: [The Weeknd - Blinding Lights](https://cakey.bot) [<@125740103539621888>]\n<:CB_Volume:1271328279898357760> Volume: `50`", "The Weeknd", "Blinding Lights")]
    public void GetTrackQuery_ShouldExtractCorrectText(string description, string expectedArtist, string expectedTrack)
    {
        // Act
        var trackQuery = MusicBotTestHelper.ParseCakeyFormat(description);

        // Assert
        Assert.That(trackQuery, Contains.Substring(expectedArtist));
        Assert.That(trackQuery, Contains.Substring(expectedTrack));
    }
}
