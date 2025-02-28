using Discord;
using FMBot.Bot.Models.MusicBot;
using Moq;
using System.Threading.Tasks;
using FMBot.Bot.Models;

namespace FMBot.Tests.MusicBotTests;

public class CakeyBotMusicBotTests
{
    private readonly Mock<IUserMessage> _mockedMessage = new();
    private readonly Mock<IEmbed> _mockedEmbed = new();

    [SetUp]
    public void SetUp()
    {
        _mockedMessage.Setup(m => m.Embeds).Returns(new List<IEmbed>{_mockedEmbed.Object});
    }

    [Test]
    [TestCase("<:CB_NowPlaying:1271328269416796251> Now playing: [Skrillex - Rumble](https://cakey.bot) [<@125740103539621888>]\n<:CB_Volume:1271328279898357760> Volume: `50`", "Skrillex", "Rumble")]
    [TestCase("<:CB_NowPlaying:1271328269416796251> Now playing: [The Weeknd - Blinding Lights](https://cakey.bot) [<@125740103539621888>]\n<:CB_Volume:1271328279898357760> Volume: `50`", "The Weeknd", "Blinding Lights")]
    public void GetTrackQuery_ShouldExtractCorrectText(string description, string expectedArtist, string expectedTrack)
    {
        // Arrange
        var bot = new CakeyBotMusicBot();
        _mockedEmbed.Setup(m => m.Description).Returns(description);

        // Act
        var trackQuery = bot.GetTrackQuery(_mockedMessage.Object);

        // Assert
        Assert.That(trackQuery, Contains.Substring(expectedArtist));
        Assert.That(trackQuery, Contains.Substring(expectedTrack));
    }
}
