namespace FMBot.Tests.MusicBotTests;

public class GreenBotMusicBotTests
{
    [Test]
    [TestCase("[**Breathe**](http://www.tidal.com/track/17981365)\nBy **The Prodigy**\n\nRequested by **<@125740103539621888>**", "The Prodigy", "Breathe")]
    [TestCase("[**Bangarang (feat. Sirah)**](http://www.tidal.com/track/123)\nBy **Skrillex**\n\nRequested by **<@123>**", "Skrillex", "Bangarang (feat. Sirah)")]
    [TestCase("[Breathe](http://www.tidal.com/track/17981365)\nBy The Prodigy\n\nRequested by <@125740103539621888>", "The Prodigy", "Breathe")]
    public void GetTrackQuery_ShouldExtractCorrectText(string content, string expectedArtist, string expectedTrack)
    {
        var trackQuery = MusicBotTestHelper.ParseGreenBotComponentsFormat(content);

        Assert.That(trackQuery, Contains.Substring(expectedArtist));
        Assert.That(trackQuery, Contains.Substring(expectedTrack));
    }
}
