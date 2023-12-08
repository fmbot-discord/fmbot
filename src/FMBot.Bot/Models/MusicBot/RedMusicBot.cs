using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Services.Guild;

namespace FMBot.Bot.Models.MusicBot;

internal class RedMusicBot : MusicBot
{
    public RedMusicBot() : base("Red", true, false)
    {
    }

    public override bool ShouldIgnoreMessage(IUserMessage msg)
    {
        // Bot sends only single embed per request
        if (msg.Embeds.Count != 1)
        {
            return true;
        }

        var targetEmbed = msg.Embeds.First();

        return targetEmbed.Title == null ||
               !targetEmbed.Title.Contains("Now Playing") ||
               string.IsNullOrEmpty(targetEmbed.Description);
    }

    public override string GetTrackQuery(IUserMessage msg)
    {
        return msg.Embeds.First().Description.Replace(@"\","");
    }

    public override async Task<bool> IsAuthor(SocketUser user, ICommandContext context, GuildService guildService)
    {
        var guild = await guildService.GetGuildAsync(context.Guild.Id);
        string redBotName = guild.RedBotName;

        if (redBotName != null)
        {
            return user?.Username?.StartsWith(redBotName, StringComparison.OrdinalIgnoreCase) ?? false;
        }

        return false;
    }
}
