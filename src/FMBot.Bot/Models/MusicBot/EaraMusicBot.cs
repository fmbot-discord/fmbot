using System;
using System.Linq;
using Discord;

namespace FMBot.Bot.Models.MusicBot;

internal class EaraMusicBot : MusicBot
{
    public EaraMusicBot() : this("Eara")
    {
    }

    protected EaraMusicBot(string name) : base(name, false)
    {
    }

    public override bool ShouldIgnoreMessage(IUserMessage msg)
    {
        if (msg.Components.Count == 0)
        {
            return true;
        }

        var component = msg.Components.First();
        if (component.Type != ComponentType.Container)
        {
            return true;
        }

        var container = (ContainerComponent)component;
        if (container.Components.Count == 0)
        {
            return true;
        }

        var first = container.Components.First();
        if (first.Type != ComponentType.Section)
        {
            return true;
        }

        var section = (SectionComponent)first;

        // Section components *should* only contain text displays
        var text = (TextDisplayComponent)section.Components.First();

        // The now-playing message contains an em-dash that separates title/artist
        return !text.Content.Contains('—');
    }

    public override string GetTrackQuery(IUserMessage msg)
    {
        foreach (var component in msg.Components)
        {
            if (component.Type != ComponentType.Container)
            {
                continue;
            }

            var container = (ContainerComponent)component;

            var section = (SectionComponent)container.Components.First();

            var text = (TextDisplayComponent)section.Components.First();

            // section is made up of 3 parts: track line, requester, and the progress bar...
            var trackLine = text.Content.Split('\n').First();

            // track and artist is delimited by an em-dash
            var trackParts = trackLine.Split(" — ", StringSplitOptions.TrimEntries);
            var track = trackParts[0];
            var artist = trackParts[1];

            // the track title may be a hyperlink.
            if (track.StartsWith('['))
            {
                track = track.Split('[', ']')[1];
            }

            return $"{artist} - {track}";
        }

        return null;
    }
}
