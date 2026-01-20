
using System.Linq;
using NetCord.Gateway;

namespace FMBot.Bot.Models.MusicBot;

internal partial class ListenMusicBot : MusicBot
{
    private const string ListenBotUrl = "https://listenbot.site";
    private const string ListenBotImageUrl = "https://i.imgur.com/Bes7wpk.png";

    public ListenMusicBot() : base("Listen")
    {
    }

    public override bool ShouldIgnoreMessage(Message msg)
    {
        if (msg.Embeds.Count != 1)
        {
            return true;
        }

        var embed = msg.Embeds.First();

        // Check if it's a Listen bot embed based on the constant URL and image
        return embed.Url != ListenBotUrl ||
               embed.Image?.Url != ListenBotImageUrl;
    }

    /**
     * Example:
     * Title: "I Feel It Coming (feat. Daft Punk)"
     * Description: "-# The Weeknd"
     * Returns: "I Feel It Coming The Weeknd"
     */
    public override string GetTrackQuery(Message msg)
    {
        var embed = msg.Embeds.First();

        if (string.IsNullOrWhiteSpace(embed.Title) ||
            string.IsNullOrWhiteSpace(embed.Description))
        {
            return string.Empty;
        }

        // Remove the "-# " prefix from the description to get the artist name
        var artist = embed.Description.Replace("-# ", "").Trim();

        return $"{artist} - {embed.Title}";
    }
}
