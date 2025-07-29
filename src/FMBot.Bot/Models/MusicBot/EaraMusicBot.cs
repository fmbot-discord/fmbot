using System;
using System.Linq;
using Discord;

namespace FMBot.Bot.Models.MusicBot;

internal class EaraMusicBot : MusicBot
{
    public EaraMusicBot() : base("eara", false)
    {
    }

    public override bool ShouldIgnoreMessage(IUserMessage msg)
    {
        if (msg.Components.Count == 0)
        {
            return true;
        }

        var container = msg.Components.FirstOrDefault(c => c.Type == ComponentType.Container);
        if (container is not ContainerComponent containerComponent)
        {
            return true;
        }

        var section = containerComponent.Components.FirstOrDefault(c => c.Type == ComponentType.Section);
        if (section is not SectionComponent sectionComponent)
        {
            return true;
        }

        var textComponents = sectionComponent.Components.Where(f => f.Type == ComponentType.TextDisplay);
        foreach (var text in textComponents)
        {
            if (text is TextDisplayComponent textComponent && textComponent.Content.Contains("—"))
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
    public override string GetTrackQuery(IUserMessage msg)
    {
        var container = msg.Components.FirstOrDefault(c => c.Type == ComponentType.Container);
        if (container is not ContainerComponent containerComponent)
        {
            return string.Empty;
        }

        var section = containerComponent.Components.FirstOrDefault(c => c.Type == ComponentType.Section);
        if (section is not SectionComponent sectionComponent)
        {
            return string.Empty;
        }

        var textComponents = sectionComponent.Components.Where(f => f.Type == ComponentType.TextDisplay);

        foreach (var text in textComponents)
        {
            if (text is not TextDisplayComponent textComponent || !textComponent.Content.Contains("—"))
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
