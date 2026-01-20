using System;
using System.Linq;
using NetCord;
using NetCord.Gateway;


namespace FMBot.Bot.Models.MusicBot;

internal class FlaviMusicBot : MusicBot
{
    private const string NowPlaying = "Now playing";

    public FlaviMusicBot() : base("FlaviBot")
    {
    }

    public override bool ShouldIgnoreMessage(Message msg)
    {
        if (msg.Components.Count == 0)
        {
            return true;
        }

        // Check if this message was created by a "queue" command
        if (msg.Interaction?.Name?.Equals("queue", StringComparison.OrdinalIgnoreCase) == true)
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

        var textComponent = section.Components.OfType<TextDisplay>().FirstOrDefault();
        if (textComponent is null)
        {
            return true;
        }

        return string.IsNullOrEmpty(textComponent.Content) || !textComponent.Content.Contains(NowPlaying);
    }

    /**
     * Example:
     * ### **[Michael Jackson - Billie Jean](https://open.spotify.com/track/7J1uxwnxfQLu4APicE5Rnj)** - `04:54`
     *
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

        var textComponent = section.Components.OfType<TextDisplay>().Skip(1).FirstOrDefault();
        if (textComponent is null)
        {
            return string.Empty;
        }

        var track = textComponent.Content;

        if (track == null)
        {
            return string.Empty;
        }

        return track.Replace("### ", "");
    }
}
