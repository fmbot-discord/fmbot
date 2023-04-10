using System;
using System.Linq;
using Discord;

namespace FMBot.Bot.Models.MusicBot;

internal class CakeyBotMusicBot : MusicBot
{
    public CakeyBotMusicBot() : base("Cakey Bot", true)
    {
    }

    public override bool ShouldIgnoreMessage(IUserMessage msg)
    {
        return msg.Embeds == null ||
            !msg.Embeds.Any() ||
            msg.Embeds.Any(a => a.Title == null) ||
            msg.Embeds.Any(a => a.Description == null) ||
            !msg.Embeds.Any(a => a.Description.Contains("Now playing:"));
    }

    public override string GetTrackQuery(IUserMessage msg)
    {
        var pFrom = msg.Embeds.First().Description.IndexOf("Now playing: ", StringComparison.Ordinal) + "Now playing: ".Length;
        var pTo = msg.Embeds.First().Description.LastIndexOf(" [", StringComparison.Ordinal);
        var result = msg.Embeds.First().Description.Substring(pFrom, pTo - pFrom);
        return result;
    }
}
