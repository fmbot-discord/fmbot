using System.Linq;
using Discord;

namespace FMBot.Bot.Models.MusicBot;

internal class BettyMusicBot : MusicBot
{
    public BettyMusicBot() : this("Betty")
    {
    }

    protected BettyMusicBot(string name) : base(name, false)
    {
    }

    public override bool ShouldIgnoreMessage(IUserMessage msg)
    {
        if (msg.Components.Count == 0)
        {
            return true;
        }

        foreach (var component in msg.Components)
        {
            if (component.Type == ComponentType.MediaGallery)
            {
                // Check if the media gallery contains an item with a description including | in the alt text of the image
                var mediaGallery = (MediaGalleryComponent)component;
                if (mediaGallery.Items.Any(item => item.Description != null && item.Description.Contains('|')))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public override string GetTrackQuery(IUserMessage msg)
    {
        foreach (var component in msg.Components)
        {
            if (component.Type == ComponentType.MediaGallery)
            {
                var mediaGallery = (MediaGalleryComponent)component;
                var item = mediaGallery.Items.FirstOrDefault(i => i.Description != null && i.Description.Contains('|'));
                var parts = item.Description.Split('|');
                if (parts.Length == 2)
                {
                    var artist = parts[0].Trim();
                    var track = parts[1].Trim();
                    return $"{artist} - {track}";
                }
            }
        }

        return null;
    }
}
