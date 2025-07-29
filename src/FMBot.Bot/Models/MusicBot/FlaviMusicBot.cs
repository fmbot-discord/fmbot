using System;
using System.Linq;
using Discord;

namespace FMBot.Bot.Models.MusicBot;

internal class FlaviMusicBot : MusicBot
{
    private const string NowPlaying = "Now playing";
    public FlaviMusicBot() : base("FlaviBot")
    {
    }

    public override bool ShouldIgnoreMessage(IUserMessage msg)
    {
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

        var text = sectionComponent.Components.FirstOrDefault(f => f.Type == ComponentType.TextDisplay);
        if (text is not TextDisplayComponent textComponent)
        {
            return true;
        }

        return string.IsNullOrEmpty(textComponent?.Content) || !textComponent.Content.Contains(NowPlaying);
    }

    /**
     * Example:
     * ### **[Michael Jackson - Billie Jean](https://open.spotify.com/track/7J1uxwnxfQLu4APicE5Rnj)** - `04:54`
     *
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

        var text = sectionComponent.Components.Skip(1).FirstOrDefault(f => f.Type == ComponentType.TextDisplay);
        if (text is not TextDisplayComponent textComponent)
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
