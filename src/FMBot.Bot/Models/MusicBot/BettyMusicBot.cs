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
        if (msg.Embeds.Count != 1)
        {
            return true;
        }

        var description = msg.Embeds.First().Description;
        return string.IsNullOrEmpty(description) || !description.Contains(NowPlaying);
    }

    /**
     * Example: 
     * :voice:・Now Playing **[Escape - Jacob Vallen](https://discord.gg/XXXXX 'An invite link to the support server')**
     */
    public override string GetTrackQuery(IUserMessage msg)
    {
        var description = msg.Embeds.First().Description;

        // Look for the start and end indices of the song info within **SONGTITLE - AUTHOR**.
        int startIndex = description.IndexOf("Now Playing **", StringComparison.Ordinal) + "Now Playing **".Length;
        int endIndex = description.IndexOf("**", startIndex, StringComparison.Ordinal);

        if (startIndex < "Now Playing **".Length || endIndex < 0 || endIndex <= startIndex)
        {
            return string.Empty;
        }

        // Extract the song info "SONGTITLE - AUTHOR".
        var songByArtist = description.Substring(startIndex, endIndex - startIndex);
        return songByArtist;
    }
}
