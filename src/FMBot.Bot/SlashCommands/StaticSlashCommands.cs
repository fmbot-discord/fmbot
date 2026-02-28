using System;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

public class StaticSlashCommands(
    UserService userService,
    StaticBuilders staticBuilders,
    InteractiveService interactivity)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;


    [SlashCommand("outofsync", "What to do if your Last.fm isn't up to date with Spotify", Contexts =
    [
        InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,
        InteractionContextType.Guild
    ], IntegrationTypes =
    [
        ApplicationIntegrationType.GuildInstall,
        ApplicationIntegrationType.UserInstall
    ])]
    public async Task OutOfSyncAsync(
        [SlashCommandParameter(Name = "private", Description = "Show info privately?")]
        bool privateResponse = true)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var response = StaticBuilders.OutOfSync(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this.Interactivity, response, userService, ephemeral: privateResponse);
        await this.Context.LogCommandUsedAsync(response, userService);
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
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var response = await staticBuilders.SupporterButtons(new ContextModel(this.Context, contextUser),
            false, true, userLocale: this.Context.Interaction.UserLocale, source: "getsupporter");

        await this.Context.SendResponse(this.Interactivity, response, userService, ephemeral: true);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [SlashCommand("supporters", "⭐ Shows all current supporters")]
    public async Task SupportersAsync()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var response = await staticBuilders.SupportersAsync(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
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
    public async Task GiftSupporterAsync(
        [SlashCommandParameter(Name = "user", Description = "The user you want to gift supporter")]
        NetCord.User user)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        try
        {
            var recipientUser = await userService.GetUserAsync(user.Id);
            var response = await staticBuilders.BuildGiftSupporterResponse(this.Context.User.Id, recipientUser,
                Context.Interaction.UserLocale);

            await Context.SendFollowUpResponse(this.Interactivity, response, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("frequently-asked", "Frequently asked questions about .fmbot", Contexts =
    [
        InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,
        InteractionContextType.Guild
    ], IntegrationTypes =
    [
        ApplicationIntegrationType.GuildInstall,
        ApplicationIntegrationType.UserInstall
    ])]
    public async Task FrequentlyAskedAsync()
    {
        var response = staticBuilders.FaqOverview();

        await this.Context.SendResponse(this.Interactivity, response, userService, ephemeral: true);
        await this.Context.LogCommandUsedAsync(response, userService);
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

        try
        {
            var recipientUser = await userService.GetUserAsync(targetUser.Id);
            var response = await staticBuilders.BuildGiftSupporterResponse(this.Context.User.Id, recipientUser,
                Context.Interaction.UserLocale);

            await Context.SendFollowUpResponse(this.Interactivity, response, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
