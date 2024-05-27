using System;
using System.Linq;
using Discord;

namespace FMBot.Bot.Models.MusicBot;

internal class UzoxMusicBot : MusicBot
{
    private const string NowPlaying = "Now Playing";
    public UzoxMusicBot() : base("Uzox")
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
     * [`DJ BORING - Winona`](https://open.spotify.com/track/4rvnBqTmx66LlCJZLbLAOY)
     */
    public override string GetTrackQuery(IUserMessage msg)
    {
        var track = msg.Embeds.First().Fields.FirstOrDefault(f => f.Name.Contains("Track:", StringComparison.OrdinalIgnoreCase));

        if (track == null || track.Value == null)
        {
            return string.Empty;
        }

        return track.Value.Replace("`", "");
    }
}
