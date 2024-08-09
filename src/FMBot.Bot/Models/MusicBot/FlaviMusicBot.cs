using System;
using System.Linq;
using Discord;

namespace FMBot.Bot.Models.MusicBot;

internal class FlaviMusicBot : MusicBot
{
    private const string NowPlaying = "Now playing";
    public FlaviMusicBot() : base("FlaviBot")
    {
    }

    public override bool ShouldIgnoreMessage(IUserMessage msg)
    {
        if (msg.Embeds.Count != 1)
        {
            return true;
        }

        var title = msg.Embeds.First().Author?.Name;
        return string.IsNullOrEmpty(title) || !title.Contains(NowPlaying);
    }

    /**
     * Example:
     * **[Johnny Hallyday - Allumer le feu](https://deezer.com/track/921379)** - `05:29`
     *
     */
    public override string GetTrackQuery(IUserMessage msg)
    {
        var track = msg.Embeds.First().Description;

        if (track == null)
        {
            return string.Empty;
        }

        return track;
    }
}
