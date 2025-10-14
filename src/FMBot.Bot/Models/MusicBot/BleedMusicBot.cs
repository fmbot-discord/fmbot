using System;
using System.Linq;
using NetCord.Gateway;


namespace FMBot.Bot.Models.MusicBot;

internal partial class BleedMusicBot : MusicBot
{
    private const string StartedPlaying = "Now playing ";
    public BleedMusicBot() : base("bleed", false, trackNameFirst: true)
    {
    }

    public override bool ShouldIgnoreMessage(Message msg)
    {
        if (msg.Embeds.Count != 1)
        {
            return true;
        }
        var description = msg.Embeds.First().Description;
        return string.IsNullOrEmpty(description) || !description.StartsWith(StartedPlaying, StringComparison.OrdinalIgnoreCase);
    }

    /**
     * Example: Now playing [`Dive (Official Video)`](https://www.youtube.com/watch?v=jetLHzTiTHc) by **Mall Grab**
     * Example output: Dive (Official Video) - Mall Grab
     */
    public override string GetTrackQuery(Message msg)
    {
        var description = msg.Embeds.First().Description;
        var embedTrack = description.Replace(StartedPlaying, "", StringComparison.OrdinalIgnoreCase);

        const string replacement = "$1 - $2";

        return BleedRegex().Replace(embedTrack, replacement);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\[\`(.+?)\`\]\(.+?\) by \*\*(.+?)\*\*")]
    private static partial System.Text.RegularExpressions.Regex BleedRegex();
}
