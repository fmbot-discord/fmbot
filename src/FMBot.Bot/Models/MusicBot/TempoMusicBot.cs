using System;
using System.Linq;

using NetCord;
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
        var text = GetFirstTextDisplay(msg);
        if (text != null)
        {
            return !text.Contains(StartedPlaying, StringComparison.OrdinalIgnoreCase);
        }

        if (msg.Embeds.Count == 1)
        {
            var title = msg.Embeds.First().Title;
            return string.IsNullOrEmpty(title) || !title.StartsWith(StartedPlaying, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    public override string GetTrackQuery(Message msg)
    {
        var text = GetFirstTextDisplay(msg);
        if (text != null)
        {
            var stripped = text.Replace("**", "");
            var startIndex = stripped.IndexOf(StartedPlaying, StringComparison.OrdinalIgnoreCase);
            if (startIndex >= 0)
            {
                return stripped[(startIndex + StartedPlaying.Length)..];
            }
        }

        var title = msg.Embeds.First().Title;
        var songByArtist = title[title.IndexOf(StartedPlaying, StringComparison.OrdinalIgnoreCase)..];
        return songByArtist.Replace(StartedPlaying, "", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFirstTextDisplay(Message msg)
    {
        foreach (var component in msg.Components)
        {
            if (component is TextDisplay textDisplay)
            {
                return textDisplay.Content;
            }

            if (component is ComponentContainer container)
            {
                foreach (var inner in container.Components)
                {
                    if (inner is TextDisplay innerText)
                    {
                        return innerText.Content;
                    }
                }
            }
        }

        return null;
    }
}
