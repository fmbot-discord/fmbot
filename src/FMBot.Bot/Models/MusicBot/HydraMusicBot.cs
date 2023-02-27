using System.Linq;
using Discord;
using Discord.WebSocket;

namespace FMBot.Bot.Models.MusicBot;

internal class HydraMusicBot : MusicBot
{
    public HydraMusicBot() : base("Hydra", false)
    {
    }

    public override bool ShouldIgnoreMessage(IUserMessage msg)
    {
        return msg.Embeds == null ||
               !msg.Embeds.Any() ||
               msg.Embeds.Any(a => a.Title == null) ||
               (msg.Embeds.Any(a => a.Title != "Now playing") && msg.Embeds.Any(a => a.Title != "Speelt nu")) ||
               msg.Embeds.Any(a => a.Description == null);
    }

    public override string GetTrackQuery(IUserMessage msg)
    {
        return msg.Embeds.First().Description;
    }
}
