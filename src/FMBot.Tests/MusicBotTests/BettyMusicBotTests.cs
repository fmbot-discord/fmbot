using Discord;
using FMBot.Bot.Models.MusicBot;
using Moq;
using System.Threading.Tasks;
using FMBot.Bot.Models;

namespace FMBot.Tests.MusicBotTests;

public class BettyMusicBotTests
{
    private readonly Mock<IUserMessage> _mockedMessage = new();
    private readonly Mock<IEmbed> _mockedEmbed = new();

    [SetUp]
    public void SetUp()
    {
        _mockedMessage.Setup(m => m.Embeds).Returns(new List<IEmbed>{_mockedEmbed.Object});
    }

    [Test]
    [TestCase("<:voice:1005912303503421471>・Now Playing **iluv - Effy**", "iluv", "Effy")]
    [TestCase("<:voice:1005912303503421471>・Now Playing **Taylor Swift - Cruel Summer**", "Taylor Swift", "Cruel Summer")]
    public void GetTrackQuery_ShouldExtractCorrectText(string description, string expectedArtist, string expectedTrack)
    {
        // Arrange
        var bettyBot = new BettyMusicBot();
        _mockedEmbed.Setup(m => m.Description).Returns(description);

        // Act
        var trackQuery = bettyBot.GetTrackQuery(_mockedMessage.Object);

        // Assert
        Assert.That(trackQuery, Contains.Substring(expectedArtist));
        Assert.That(trackQuery, Contains.Substring(expectedTrack));
    }
}
