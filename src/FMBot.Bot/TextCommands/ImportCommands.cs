using System.Threading.Tasks;
using Discord.Commands;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.TextCommands;

public class ImportCommands : BaseCommandModule
{
    private readonly UserService _userService;
    private readonly ImportBuilders _importBuilders;
    private readonly IPrefixService _prefixService;

    private InteractiveService Interactivity { get; }

    public ImportCommands(UserService userService,
        ImportBuilders importBuilders,
        IPrefixService prefixService,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        this._userService = userService;
        this._importBuilders = importBuilders;
        this._prefixService = prefixService;
        this.Interactivity = interactivity;
    }

    [Command("import")]
    [Summary("Imports your Spotify history into .fmbot")]
    [Alias("import spotify", "spotifyimport", "spotifyimport")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Importing)]
    public async Task ImportSpotifyAsync([Remainder] string _ = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        var supporterRequiredResponse =
            ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, prfx, userSettings));

        if (supporterRequiredResponse != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse);
            this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
            return;
        }

        var response = await this._importBuilders.GetImportInstructions(new ContextModel(this.Context, prfx, userSettings));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }
}
