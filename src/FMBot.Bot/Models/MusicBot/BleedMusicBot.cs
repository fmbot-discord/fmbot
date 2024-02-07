using System;
using System.Linq;
using System.Text.RegularExpressions;
using Discord;

namespace FMBot.Bot.Models.MusicBot;

internal class BleedMusicBot : MusicBot
{
    private const string StartedPlaying = "Now playing ";
    public BleedMusicBot() : base("bleed", false, trackNameFirst: true)
    {
    }

    public override bool ShouldIgnoreMessage(IUserMessage msg)
    {
        if (msg.Embeds.Count != 1)
        {
            return true;
        }
        var title = msg.Embeds.First().Description;
        return string.IsNullOrEmpty(title) || !title.StartsWith(StartedPlaying, StringComparison.OrdinalIgnoreCase);
    }

    /**
     * Example: Now playing [`Dive (Official Video)`](https://www.youtube.com/watch?v=jetLHzTiTHc) by **Mall Grab**
     * Example output: Dive (Official Video) - Mall Grab
     */
    public override string GetTrackQuery(IUserMessage msg)
    {
        var description = msg.Embeds.First().Description;
        var embedTrack = description.Replace(StartedPlaying, "", StringComparison.OrdinalIgnoreCase);

        const string pattern = @"\[\`(.+?)\`\]\(.+?\) by \*\*(.+?)\*\*";
        const string replacement = "$1 - $2";

        return Regex.Replace(embedTrack, pattern, replacement);
    }
}
