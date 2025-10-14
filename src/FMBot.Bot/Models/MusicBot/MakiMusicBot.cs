using System;
using System.Linq;

using Discord.WebSocket;
using NetCord.Gateway;

namespace FMBot.Bot.Models.MusicBot;

internal class MakiMusicBot : MusicBot
{
    private const string StartedPlaying = "Now Playing";
    public MakiMusicBot() : base("Maki", false, false, true)
    {
    }

    public override bool ShouldIgnoreMessage(Message msg)
    {
        if (msg.Embeds.Count != 1)
        {
            return true;
        }

        var title = msg.Embeds.First().Title;
        if (!string.IsNullOrEmpty(title))
        {
            return !title.Contains(StartedPlaying, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    /**
        Example: Love Reigns — Mall Grab
     */
    public override string GetTrackQuery(Message msg)
    {
        return msg.Embeds.First().Description.Replace("**", "");
    }
}
