using System;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord.Services.Commands;

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
    [Summary("Imports your Spotify or Apple Music history into .fmbot")]
    [Alias("import spotify", "import apple", "import applemusic", "spotifyimport", "spotifyimport")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Importing)]
    [SupporterExclusive("Only supporters can import and access their Spotify or Apple Music history")]
    public async Task ImportSpotifyAsync([CommandParameter(Remainder = true)] string _ = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var supporterRequiredResponse =
            ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, prfx, contextUser));

        if (supporterRequiredResponse != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse);
            this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
            return;
        }

        ResponseModel response;
        if (this.Context.Message.Content.Contains("spotify", StringComparison.OrdinalIgnoreCase))
        {
            response = await this._importBuilders.GetSpotifyImportInstructions(new ContextModel(this.Context, prfx, contextUser), true);
        }
        else if (this.Context.Message.Content.Contains("apple", StringComparison.OrdinalIgnoreCase))
        {
            response = await this._importBuilders.GetAppleMusicImportInstructions(new ContextModel(this.Context, prfx, contextUser), true);
        }
        else
        {
            response = this._importBuilders.ImportInstructionsPickSource(new ContextModel(this.Context, prfx, contextUser));
        }

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("importmodify")]
    [Summary("Allows you to modify your .fmbot imports")]
    [Alias("modifyimport", "importsmodify", "modifyimports", "import modify")]
    [CommandCategories(CommandCategory.UserSettings)]
    [UsernameSetRequired]
    [SupporterExclusive("Only supporters can import and access their Spotify or Apple Music history")]
    public async Task ModifyImportAsync([CommandParameter(Remainder = true)] string confirmation = null)
    {
        var contextUser = await this._userService.GetFullUserAsync(this.Context.User.Id);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var supporterRequiredResponse =
            ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, prfx, contextUser));

        if (supporterRequiredResponse != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse);
            this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
            return;
        }

        if (this.Context.Guild != null)
        {
            var serverEmbed = new EmbedProperties()
                .WithColor(DiscordConstants.InformationColorBlue)
                .WithDescription("Check your DMs to continue with modifying your .fmbot imports.");

            await ReplyAsync(embed: serverEmbed.Build());
        }
        else
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;
        }

        try
        {
            var response = await this._importBuilders.ImportModify(new ContextModel(this.Context, prfx, contextUser), contextUser.UserId);
            await this.Context.User.SendMessageAsync("", false, response.Embed, components: response.Components);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }

    }
}
