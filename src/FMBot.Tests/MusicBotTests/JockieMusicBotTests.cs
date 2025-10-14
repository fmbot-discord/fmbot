
using FMBot.Bot.Models.MusicBot;
using Moq;
using System.Threading.Tasks;
using FMBot.Bot.Models;

namespace FMBot.Tests.MusicBotTests;

public class JockieMusicBotTests
{
    private readonly Mock<IUserMessage> _mockedMessage = new();
    private readonly Mock<IEmbed> _mockedEmbed = new();

    [SetUp]
    public void SetUp()
    {
        _mockedMessage.Setup(m => m.Embeds).Returns(new List<IEmbed>{_mockedEmbed.Object});
    }

    [Test]
    [TestCase(":spotify: ​ Started playing The Game of Love by Daft Punk", "Daft Punk", "The Game of Love")]
    [TestCase(":apple_music: ​ Started playing Radio by Alkaline Trio", "Alkaline Trio", "Radio")]
    [TestCase(" ​ Started playing **[Constant Headache by Joyce Manor](https://www.deezer.com/track/67220083)**", "Joyce Manor", "Constant Headache")]
    public void GetTrackQuery_ShouldExtractCorrectText(string description, string expectedArtist, string expectedTrack)
    {
        // Arrange
        var jockieBot = new JockieMusicBot();
        _mockedEmbed.Setup(m => m.Description).Returns(description);

        // Act
        var trackQuery = jockieBot.GetTrackQuery(_mockedMessage.Object);

        // Assert
        Assert.That(trackQuery, Contains.Substring(expectedArtist));
        Assert.That(trackQuery, Contains.Substring(expectedTrack));
    }
}
