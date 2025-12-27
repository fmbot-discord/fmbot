using System;
using System.Linq;
using NetCord;
using NetCord.Gateway;


namespace FMBot.Bot.Models.MusicBot;

internal class EaraMusicBot : MusicBot
{
    public EaraMusicBot() : base("eara", false)
    {
    }

    public override bool ShouldIgnoreMessage(Message msg)
    {
        if (msg.Components.Count == 0)
        {
            return true;
        }

        var container = msg.Components.OfType<ComponentContainer>().FirstOrDefault();
        if (container is null)
        {
            return true;
        }

        var section = container.Components.OfType<ComponentSection>().FirstOrDefault();
        if (section is null)
        {
            return true;
        }

        var textComponents = section.Components.OfType<TextDisplay>();
        foreach (var textComponent in textComponents)
        {
            if (textComponent.Content.Contains("—"))
            {
                return false;
            }
        }

        return true;
    }

    /*
     * Example:
     * [I Remember](<https://open.spotify.com/track/69Y7bqe3lsESYEqCqJ4eBH>) — *deadmau5, Kaskade*
-# Requested by <@125740103539621888>

00:00<:emoji:1284320259922329725><:emoji:1284320227009761360><:emoji:1284320227009761360><:emoji:1284320227009761360><:emoji:1284320227009761360><:emoji:1284320227009761360><:emoji:1284320227009761360><:emoji:1284320227009761360><:emoji:1284320227009761360><:emoji:1284320227009761360><:emoji:1284320227009761360><:emoji:1284320227009761360><:emoji:1284320227009761360><:emoji:1284320202955161610>09:07
     */
    public override string GetTrackQuery(Message msg)
    {
        var container = msg.Components.OfType<ComponentContainer>().FirstOrDefault();
        if (container is null)
        {
            return string.Empty;
        }

        var section = container.Components.OfType<ComponentSection>().FirstOrDefault();
        if (section is null)
        {
            return string.Empty;
        }

        var textComponents = section.Components.OfType<TextDisplay>();

        foreach (var textComponent in textComponents)
        {
            if (!textComponent.Content.Contains("—"))
            {
                continue;
            }

            var trackLine = textComponent.Content.Split('\n').First();

            if (trackLine.Contains("open.spotify.com"))
            {
                return trackLine;
            }


            var trackParts = trackLine.Split(" — ", StringSplitOptions.TrimEntries);
            var track = trackParts[0];
            var artist = trackParts[1];

            if (track.StartsWith('['))
            {
                track = track.Split('[', ']')[1];
            }
            if (artist.StartsWith('*'))
            {
                artist = artist.Split('*', '*')[1];
            }

            return $"{artist} - {track}";
        }

        return null;
    }
}
