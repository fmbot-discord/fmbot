using Discord;
using FMBot.Bot.Models.MusicBot;
using Moq;

namespace FMBot.Tests;

public class JockieMusicBotTest
{
    private static readonly MusicBot Jockie = new JockieMusicBot();
    private readonly Mock<IUserMessage> _mockedMessage = new();
    private readonly Mock<IEmbed> _mockedEmbed = new();

    [SetUp]
    public void SetUp()
    {
        _mockedMessage.Setup(m => m.Embeds).Returns(new List<IEmbed>{_mockedEmbed.Object});
    }

    [Test]
    [TestCase("This is not a music bot message.")]
    [TestCase("test")]
    [TestCase("")]
    [TestCase(null)]
    public void TestShouldIgnoreMessage_NotStartedPlaying(string message)
    {
        _mockedEmbed.Setup(m => m.Description).Returns(message);
        Assert.That(Jockie.ShouldIgnoreMessage(_mockedMessage.Object), Is.True);
    }

    [Test]
    [TestCase(":spotify: ​ Started playing The Game of Love by Daft Punk")]
    [TestCase(":apple_music: ​ Started playing Radio by Alkaline Trio")]
    public void TestShouldIgnoreMessage_StartedPlaying(string message)
    {
        this._mockedEmbed.Setup(m => m.Description).Returns(message);
        Assert.That(Jockie.ShouldIgnoreMessage(this._mockedMessage.Object), Is.False);
    }

}
