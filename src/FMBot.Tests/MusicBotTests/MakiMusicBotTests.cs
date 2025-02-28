using Discord;
using FMBot.Bot.Models.MusicBot;
using Moq;
using System.Threading.Tasks;
using FMBot.Bot.Models;

namespace FMBot.Tests.MusicBotTests;

public class MakiMusicBotTests
{
    private readonly Mock<IUserMessage> _mockedMessage = new();
    private readonly Mock<IEmbed> _mockedEmbed = new();

    [SetUp]
    public void SetUp()
    {
        _mockedMessage.Setup(m => m.Embeds).Returns(new List<IEmbed>{_mockedEmbed.Object});
    }

    [Test]
    [TestCase("**We All Lift Together** — **Warframe**\nRequested by", "Warframe", "We All Lift Together")]
    [TestCase("**Bohemian Rhapsody** — **Queen**\nRequested by", "Queen", "Bohemian Rhapsody")]
    public void GetTrackQuery_ShouldExtractCorrectText(string description, string expectedArtist, string expectedTrack)
    {
        // Arrange
        var bot = new MakiMusicBot();
        _mockedEmbed.Setup(m => m.Description).Returns(description);

        // Act
        var trackQuery = bot.GetTrackQuery(_mockedMessage.Object);

        // Assert
        Assert.That(trackQuery, Contains.Substring(expectedArtist));
        Assert.That(trackQuery, Contains.Substring(expectedTrack));
    }
}
