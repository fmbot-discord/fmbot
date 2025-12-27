using System.Linq;
using NetCord;
using NetCord.Gateway;


namespace FMBot.Bot.Models.MusicBot;

internal class BettyMusicBot : MusicBot
{
    public BettyMusicBot() : this("Betty")
    {
    }

    protected BettyMusicBot(string name) : base(name, false)
    {
    }

    public override bool ShouldIgnoreMessage(Message msg)
    {
        if (msg.Components.Count == 0)
        {
            return true;
        }

        foreach (var component in msg.Components)
        {
            if (component is MediaGallery mediaGallery)
            {
                // Check if the media gallery contains an item with a description including | in the alt text of the image
                if (mediaGallery.Items.Any(item => item.Description != null && item.Description.Contains('|')))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public override string GetTrackQuery(Message msg)
    {
        foreach (var component in msg.Components)
        {
            if (component is MediaGallery mediaGallery)
            {
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
