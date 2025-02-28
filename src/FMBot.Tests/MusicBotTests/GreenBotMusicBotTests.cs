using Discord;
using FMBot.Bot.Models.MusicBot;
using Moq;
using System.Threading.Tasks;
using FMBot.Bot.Models;

namespace FMBot.Tests.MusicBotTests;

public class GreenBotMusicBotTests
{
    private readonly Mock<IUserMessage> _mockedMessage = new();
    private readonly Mock<IEmbed> _mockedEmbed = new();

    [SetUp]
    public void SetUp()
    {
        _mockedMessage.Setup(m => m.Embeds).Returns(new List<IEmbed>{_mockedEmbed.Object});
    }

    [Test]
    [TestCase("[Bangarang (feat. Sirah)](https://deezer.com/track/15781392) by [Skrillex](https://deezer.com/track/15781392), requested by [frikandel](https://deezer.com/track/15781392)", "Skrillex", "Bangarang (feat. Sirah)")]
    [TestCase("[Yesterday (Remastered 2015)](https://deezer.com/track/116348612) by [The Beatles](https://deezer.com/track/116348612), requested by [frikandel](https://deezer.com/track/116348612)", "The Beatles", "Yesterday (Remastered 2015)")]
    public void GetTrackQuery_ShouldExtractCorrectText(string description, string expectedArtist, string expectedTrack)
    {
        // Arrange
        var bot = new GreenBotMusicBot();
        _mockedEmbed.Setup(m => m.Description).Returns(description);

        // Act
        var trackQuery = bot.GetTrackQuery(_mockedMessage.Object);

        // Assert
        Assert.That(trackQuery, Contains.Substring(expectedArtist));
        Assert.That(trackQuery, Contains.Substring(expectedTrack));
    }
}
