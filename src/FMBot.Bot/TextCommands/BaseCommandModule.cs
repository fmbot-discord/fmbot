using FMBot.Bot.Resources;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord.Rest;
using NetCord.Services.Commands;

namespace FMBot.Bot.TextCommands;

public class BaseCommandModule : CommandModule<CommandContext>
{
    internal readonly EmbedAuthorProperties _embedAuthor;
    internal readonly EmbedProperties  _embed;
    internal readonly EmbedFooterProperties _embedFooter;

    internal readonly BotSettings _botSettings;

    public BaseCommandModule(IOptions<BotSettings> botSettings)
    {
        this._embedAuthor = new EmbedAuthorProperties();
        this._embed = new EmbedProperties()
            .WithColor(DiscordConstants.LastFmColorRed);
        this._embedFooter = new EmbedFooterProperties();

        this._botSettings = botSettings.Value;
    }
}
