using System;
using System.Text;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Shared.Domain.Enums;

namespace FMBot.Bot.SlashCommands;

public class StaticSlashCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserService _userService;
    private readonly StaticBuilders _staticBuilders;
    private readonly SupporterService _supporterService;

    private InteractiveService Interactivity { get; }


    public StaticSlashCommands(UserService userService, StaticBuilders staticBuilders, InteractiveService interactivity,
        SupporterService supporterService)
    {
        this._userService = userService;
        this._staticBuilders = staticBuilders;
        this.Interactivity = interactivity;
        this._supporterService = supporterService;
    }

    [SlashCommand("outofsync", "What to do if your Last.fm isn't up to date with Spotify", Contexts =
    [
        InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,
        InteractionContextType.Guild
    ], IntegrationTypes =
    [
        ApplicationIntegrationType.GuildInstall,
        ApplicationIntegrationType.UserInstall
    ])]
    public async Task OutOfSyncAsync([SlashCommandParameter(Name = "private", Description = "Show info privately?")] bool privateResponse = true)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var response = StaticBuilders.OutOfSync(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this.Interactivity, response, ephemeral: privateResponse);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("getsupporter", "⭐ Get supporter or manage your current subscription", Contexts =
    [
        InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,
        InteractionContextType.Guild
    ], IntegrationTypes =
    [
        ApplicationIntegrationType.GuildInstall,
        ApplicationIntegrationType.UserInstall
    ])]
    public async Task GetSupporterAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var response = await this._staticBuilders.SupporterButtons(new ContextModel(this.Context, contextUser),
            false, true, userLocale: this.Context.Interaction.UserLocale, source: "getsupporter");

        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("supporters", "⭐ Shows all current supporters")]
    public async Task SupportersAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var response = await this._staticBuilders.SupportersAsync(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("giftsupporter", "🎁 Gift supporter to another user", Contexts =
    [
        InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,
        InteractionContextType.Guild
    ], IntegrationTypes =
    [
        ApplicationIntegrationType.GuildInstall,
        ApplicationIntegrationType.UserInstall
    ])]
    public async Task GiftSupporterAsync([SlashCommandParameter(Name = "user", Description = "The user you want to gift supporter")] NetCord.User user)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var recipientUser = await this._userService.GetUserAsync(user.Id);
        var response = await this._staticBuilders.BuildGiftSupporterResponse(this.Context.User.Id, recipientUser,
            Context.Interaction.UserLocale);

        await Context.SendFollowUpResponse(this.Interactivity, response, ephemeral: true);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [UserCommand("Gift supporter", Contexts =
    [
        InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,
        InteractionContextType.Guild
    ], IntegrationTypes =
    [
        ApplicationIntegrationType.GuildInstall,
        ApplicationIntegrationType.UserInstall
    ])]
    public async Task GiftSupporterUserCommand(NetCord.User targetUser)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var recipientUser = await this._userService.GetUserAsync(targetUser.Id);
        var response = await this._staticBuilders.BuildGiftSupporterResponse(this.Context.User.Id, recipientUser,
            Context.Interaction.UserLocale);

        await Context.SendFollowUpResponse(this.Interactivity, response, ephemeral: true);
        this.Context.LogCommandUsed(response.CommandResponse);
    }
}
