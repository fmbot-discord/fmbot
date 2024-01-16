using System;
using System.Linq;
using Discord;
using Discord.WebSocket;

namespace FMBot.Bot.Models.MusicBot;

internal class GreenBotMusicBot : MusicBot
{
    private const string NowPlayingStatus = "- Now Playing";
    public GreenBotMusicBot() : base("Green-bot", true)
    {
    }

    public override bool ShouldIgnoreMessage(IUserMessage msg)
    {
        var embed = msg.Embeds.FirstOrDefault();
        if (embed == null)
        {
            return true;
        }

        var authorName = embed.Author?.Name ?? string.Empty;
        return !authorName.Contains(NowPlayingStatus);
    }

    public override string GetTrackQuery(IUserMessage msg)
    {
        var embed = msg.Embeds.FirstOrDefault();
        if (embed == null)
        {
            return string.Empty;
        }

        var description = embed.Description ?? string.Empty;
        var songDetails = description.Split(" by ", StringSplitOptions.None);

        if (songDetails.Length < 2)
        {
            return string.Empty; 
        }

        var songName = songDetails[0].Trim();
        var author = songDetails[1].Trim();

        return $"{songName} - {author}";
    }
}
