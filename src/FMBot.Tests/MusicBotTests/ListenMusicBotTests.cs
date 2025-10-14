
using FMBot.Bot.Models.MusicBot;
using Moq;
using System.Threading.Tasks;
using FMBot.Bot.Models;

namespace FMBot.Tests.MusicBotTests;

public class ListenMusicBotTests
{
    private readonly Mock<IUserMessage> _mockedMessage = new();
    private readonly Mock<IEmbed> _mockedEmbed = new();

    [SetUp]
    public void SetUp()
    {
        _mockedMessage.Setup(m => m.Embeds).Returns(new List<IEmbed>{_mockedEmbed.Object});
    }

    [Test]
    [TestCase("Bangarang (feat. Sirah)","-# Skrillex", "Skrillex", "Bangarang")]
    public void GetTrackQuery_ShouldExtractCorrectText(string title, string description, string expectedArtist, string expectedTrack)
    {
        // Arrange
        var bot = new ListenMusicBot();
        _mockedEmbed.Setup(m => m.Title).Returns(title);
        _mockedEmbed.Setup(m => m.Description).Returns(description);

        // Act
        var trackQuery = bot.GetTrackQuery(_mockedMessage.Object);

        // Assert
        Assert.That(trackQuery, Contains.Substring(expectedArtist));
        Assert.That(trackQuery, Contains.Substring(expectedTrack));
    }
}
