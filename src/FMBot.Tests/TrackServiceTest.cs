using FMBot.Bot.Services;

namespace FMBot.Tests;

public class TrackServiceTest
{

    [Test]
    [TestCase("**bye** **by** **bye**", "bye", "bye")]
    [TestCase("**bye** **by** **by bye**", "bye", "by bye")]
    [TestCase("**bye by** **by** **bye**", "bye by", "bye")]
    [TestCase("**bye by** **by** **by bye**", "bye by", "by bye")]
    [TestCase("**All by Yourself** **by** **Panic! At the Disco**", "All by Yourself", "Panic! At the Disco")]
    [TestCase("**The Black Parade** **by** **My Chemical Romance**", "The Black Parade", "My Chemical Romance")]
    [TestCase("**The Black Parade** *by** **My Chemical Romance**", null, null)]
    [TestCase("*The Black Parade** **by** **My Chemical Romance**", null, null)]
    [TestCase("The Black Parade **by** My Chemical Romance", null, null)]
    [TestCase("**The Black Parade by My Chemical Romance**", null, null)]
    public void TestParseBoldDelimitedTrackAndArtist(string description, string? track, string? artist)
    {
        var expectedTrackAndArtist = track != null && artist != null
            ? new TrackService.TrackAndArtist() { Track = track, Artist = artist }
            : null;
        var trackAndArtist = TrackService.ParseBoldDelimitedTrackAndArtist(description);
        Assert.That(trackAndArtist, Is.EqualTo(expectedTrackAndArtist));
    }
}
