using System.Linq;
using Discord;

namespace FMBot.Bot.Models.MusicBot;

internal class BettyMusicBot : MusicBot
{
    public BettyMusicBot() : base("Betty", false)
    {
    }

    public override bool ShouldIgnoreMessage(IUserMessage msg)
    {
        if (msg.Attachments.Count == 0)
        {
            return true;
        }

        foreach (var attachment in msg.Attachments)
        {
            if (!string.IsNullOrWhiteSpace(attachment.Description) && attachment.Description.Contains(" | "))
            {
                return false;
            }
        }

        return true;
    }

    /**
     * Example:
     * <:voice:1005912303503421471>・Now Playing **iluv - Effy**
     */
    public override string GetTrackQuery(IUserMessage msg)
    {
        foreach (var attachment in msg.Attachments)
        {
            if (!string.IsNullOrWhiteSpace(attachment.Description))
            {
                var artist = attachment.Description.Split(" | ")[0];
                var track = attachment.Description.Split(" | ")[1];
                return $"{artist} - {track}";
            }
        }

        return null;
    }
}
