using System;
using System.Linq;
using Discord;
using Discord.WebSocket;
using System.Text.RegularExpressions; 

namespace FMBot.Bot.Models.MusicBot;

internal class GreenBotMusicBot : MusicBot
{
    private const string NowPlayingStatus = "- Now Playing";
    public GreenBotMusicBot() : base("Green-bot", false)
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
        var matches = Regex.Matches(description, @"\[(.*?)\]\(.*?\)");

        if (matches.Count < 2)
        {
            return string.Empty; 
        }

        var songName = matches[0].Groups[1].Value.Trim();
        var artist = matches[1].Groups[1].Value.Trim();

        return $"{artist} - {songName}";
    }
}
