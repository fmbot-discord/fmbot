using System;
using System.Linq;
using Discord;
using Discord.WebSocket;

namespace FMBot.Bot.Models.MusicBot;

internal class BettyMusicBot : MusicBot
{
    private const string NowPlaying = "・Now Playing";

    public BettyMusicBot() : base("Betty", false, trackNameFirst: true)
    {
    }

    public override bool ShouldIgnoreMessage(IUserMessage msg)
    {
        if (!msg.Embeds.Any())
        {
            return true;
        }

        foreach (var embed in msg.Embeds)
        {
            if (!string.IsNullOrEmpty(embed.Description) &&
                embed.Description.Contains(NowPlaying, StringComparison.OrdinalIgnoreCase))
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
        foreach (var embed in msg.Embeds)
        {
            if (string.IsNullOrEmpty(embed.Description) ||
                !embed.Description.Contains(NowPlaying, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Look for the start and end indices of the song info within **SONGTITLE - AUTHOR**.
            var startIndex = embed.Description.IndexOf("Now Playing **", StringComparison.OrdinalIgnoreCase) +
                             "Now Playing **".Length;
            var endIndex = embed.Description.IndexOf("**", startIndex, StringComparison.OrdinalIgnoreCase);

            if (startIndex < "Now Playing **".Length || endIndex < 0 || endIndex <= startIndex)
            {
                return string.Empty;
            }

            // Extract the song info "SONGTITLE - AUTHOR".
            var songByArtist = embed.Description.Substring(startIndex, endIndex - startIndex);
            return songByArtist;
        }

        return string.Empty;
    }
}
