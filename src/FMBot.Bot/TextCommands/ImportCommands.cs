using System;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord.Rest;
using NetCord.Services.Commands;

namespace FMBot.Bot.TextCommands;

public class ImportCommands(
    UserService userService,
    ImportBuilders importBuilders,
    IPrefixService prefixService,
    InteractiveService interactivity,
    IOptions<BotSettings> botSettings)
    : BaseCommandModule(botSettings)
{
    private InteractiveService Interactivity { get; } = interactivity;

    [Command("import", "spotifyimport", "importspotify", "appleimport", "applemusicimport", "importapple", "importapplemusic", "imports")]
    [Summary("Imports your Spotify or Apple Music history into .fmbot")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Importing)]
    [SupporterExclusive("Only supporters can import and access their Spotify or Apple Music history")]
    public async Task ImportSpotifyAsync([CommandParameter(Remainder = true)] string _ = null)
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

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
            response = await importBuilders.GetSpotifyImportInstructions(new ContextModel(this.Context, prfx, contextUser), true);
        }
        else if (this.Context.Message.Content.Contains("apple", StringComparison.OrdinalIgnoreCase))
        {
            response = await importBuilders.GetAppleMusicImportInstructions(new ContextModel(this.Context, prfx, contextUser), true);
        }
        else
        {
            response = importBuilders.ImportInstructionsPickSource(new ContextModel(this.Context, prfx, contextUser));
        }

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("importmodify", "modifyimport", "importsmodify", "modifyimports")]
    [Summary("Allows you to modify your .fmbot imports")]
    [CommandCategories(CommandCategory.UserSettings)]
    [UsernameSetRequired]
    [SupporterExclusive("Only supporters can import and access their Spotify or Apple Music history")]
    public async Task ModifyImportAsync([CommandParameter(Remainder = true)] string confirmation = null)
    {
        var contextUser = await userService.GetFullUserAsync(this.Context.User.Id);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

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

            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Embeds = [serverEmbed] });
        }
        else
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;
        }

        try
        {
            var response = await importBuilders.ImportModify(new ContextModel(this.Context, prfx, contextUser), contextUser.UserId);
            var dmChannel = await this.Context.User.GetDMChannelAsync();
            await dmChannel.SendMessageAsync(new MessageProperties
            {
                Embeds = [response.Embed],
                Components = [response.Components]
            });
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }

    }
}
