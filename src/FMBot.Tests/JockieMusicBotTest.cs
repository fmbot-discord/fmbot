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

    [Test]
    [TestCase(" ​ Started playing **[Constant Headache by Joyce Manor](https://www.deezer.com/track/67220083)**")]
    [TestCase("<:deezer:837244412492513312> ​ Started playing **[UWU by KEAN DYSSO](https://www.deezer.com/track/1659395942)**")]
    [TestCase("<:apple_music:837244414531076106> ​ Started playing **[River by Anonymouz](https://music.apple.com/us/album/river/1658961980?i=1658961985)**")]
    [TestCase("<:apple_music:837244414531076106> ​ Started playing **[Modern Love by All Time Low](https://music.apple.com/us/album/modern-love/1664275076?i=1664275308)**")]
    public void TestGetTrackQuery(string description)
    {
        this._mockedEmbed.Setup(m => m.Description).Returns(description);
        var expected = description[description.IndexOf(" ​ Started playing ", StringComparison.Ordinal)..];
        Assert.That(expected, Is.EqualTo(Jockie.GetTrackQuery(this._mockedMessage.Object)));
    }

}
