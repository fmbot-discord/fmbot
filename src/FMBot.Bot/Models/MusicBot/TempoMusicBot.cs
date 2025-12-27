using System;
using System.Linq;

using NetCord.Gateway;

namespace FMBot.Bot.Models.MusicBot;

internal class TempoMusicBot : MusicBot
{
    private const string StartedPlaying = "Playing: ";
    public TempoMusicBot() : base("Tempo", false, trackNameFirst: true)
    {
    }

    public override bool ShouldIgnoreMessage(Message msg)
    {
        if (msg.Embeds.Count != 1)
        {
            return true;
        }
        var title = msg.Embeds.First().Title;
        return string.IsNullOrEmpty(title) || !title.StartsWith(StartedPlaying, StringComparison.OrdinalIgnoreCase);
    }

    /**
     * Example: Playing: Liverpool Street In The Rain by Mall Grab
     */
    public override string GetTrackQuery(Message msg)
    {
        var title = msg.Embeds.First().Title;
        var songByArtist = title[title.IndexOf(StartedPlaying, StringComparison.OrdinalIgnoreCase)..];
        return songByArtist.Replace(StartedPlaying, "", StringComparison.OrdinalIgnoreCase);
    }
}
