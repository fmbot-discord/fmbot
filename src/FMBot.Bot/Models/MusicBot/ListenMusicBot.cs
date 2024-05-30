using System.Linq;
using Discord;

namespace FMBot.Bot.Models.MusicBot;

internal partial class ListenMusicBot : MusicBot
{
    private const string NowPlaying = "Now Playing";
    public ListenMusicBot() : base("Listen")
    {
    }

    public override bool ShouldIgnoreMessage(IUserMessage msg)
    {
        if (msg.Embeds.Count != 1)
        {
            return true;
        }

        var title = msg.Embeds.First().Title;
        return string.IsNullOrEmpty(title) || !title.Contains(NowPlaying);
    }

    /**
     * Example:
     * Mall Grab - [Menace II Society](https://help.soundcloud.com/hc/en-us/articles/4402636813979-What-are-SoundCloud-s-copyright-policies)
     */
    public override string GetTrackQuery(IUserMessage msg)
    {
        var content = msg.Embeds.First().Description;

        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        return ListenMusicBotRegex().Replace(msg.Embeds.First().Description, "$1");
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\[(.*?)\]\(.*?\)")]
    private static partial System.Text.RegularExpressions.Regex ListenMusicBotRegex();
}
