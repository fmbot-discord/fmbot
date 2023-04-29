using System;
using System.Linq;
using Discord;
using Discord.WebSocket;

namespace FMBot.Bot.Models.MusicBot;

internal class JockieMusicBot : MusicBot
{
    private const string StartedPlaying = " ​ Started playing ";
    public JockieMusicBot() : base("Jockie Music", true)
    {
    }

    public override bool ShouldIgnoreMessage(IUserMessage msg)
    {
        if (msg.Embeds.Count != 1)
        {
            return true;
        }
        var description = msg.Embeds.First().Description;
        return string.IsNullOrEmpty(description) || !description.Contains(StartedPlaying);
    }

    /**
     * Example: :spotify: ​ Started playing Giants by Lights
     */
    public override string GetTrackQuery(IUserMessage msg)
    {
        var description = msg.Embeds.First().Description;
        var songByArtist = description[description.IndexOf(StartedPlaying, StringComparison.Ordinal)..];
        return songByArtist.Replace("\\","");
    }
}
