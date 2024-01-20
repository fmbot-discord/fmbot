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

        // Find the index of the newline character and truncate the string if it exists because this would display the next song which we dont need.
        int newlineIndex = description.IndexOf('\n');
        if (newlineIndex != -1)
        {
            description = description.Substring(0, newlineIndex);
        }

        // Find the start and end indices of the song info within **[ ]**.
        int startIndex = description.IndexOf("**[", StringComparison.Ordinal) + 3;
        int endIndex = description.IndexOf("](", StringComparison.Ordinal);

        if (startIndex < 0 || endIndex < 0 || endIndex <= startIndex)
        {
            return string.Empty;
        }

        var songByArtist = description.Substring(startIndex, endIndex - startIndex);
        return songByArtist;
    }
}
