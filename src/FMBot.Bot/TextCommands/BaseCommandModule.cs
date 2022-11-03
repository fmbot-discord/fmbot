using Discord;
using Discord.Commands;
using FMBot.Bot.Resources;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.TextCommands;

public class BaseCommandModule : ModuleBase
{
    internal readonly EmbedAuthorBuilder _embedAuthor;
    internal readonly EmbedBuilder _embed;
    internal readonly EmbedFooterBuilder _embedFooter;

    internal readonly BotSettings _botSettings;

    public BaseCommandModule(IOptions<BotSettings> botSettings)
    {
        this._embedAuthor = new EmbedAuthorBuilder();
        this._embed = new EmbedBuilder()
            .WithColor(DiscordConstants.LastFmColorRed);
        this._embedFooter = new EmbedFooterBuilder();

        this._botSettings = botSettings.Value;
    }
}
