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
        if (!string.IsNullOrEmpty(description))
        {
            return !description.Contains(StartedPlaying, StringComparison.OrdinalIgnoreCase);
        }

        var title = msg.Embeds.First().Author?.Name;
        if (!string.IsNullOrEmpty(title))
        {
            return !title.Contains("Started playing", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    /**
     * Example: :spotify: ​ Started playing Giants by Lights
     * Or extended (m!set text announce extended on/off)
     */
    public override string GetTrackQuery(IUserMessage msg)
    {
        var description = msg.Embeds.First().Description;
        if (description != null &&
            description.Contains(StartedPlaying, StringComparison.OrdinalIgnoreCase))
        {
            var songByArtist = description[description.IndexOf(StartedPlaying, StringComparison.OrdinalIgnoreCase)..];
            return songByArtist.Replace("\\", "");
        }

        if (msg.Embeds.First().Author.HasValue &&
            msg.Embeds.First().Author?.Name != null &&
            msg.Embeds.First().Author.Value.Name.Contains("Started playing", StringComparison.OrdinalIgnoreCase))
        {
            var field = msg.Embeds.First().Fields
                .FirstOrDefault(f => f.Name.Contains("Playing", StringComparison.OrdinalIgnoreCase));
            if (field != null)
            {
                return field.Value.Replace("\\", "");
            }
        }

        return string.Empty;
    }
}
