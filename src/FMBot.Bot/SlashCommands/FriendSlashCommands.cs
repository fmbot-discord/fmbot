using System;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

public class FriendSlashCommands(
    UserService userService,
    InteractiveService interactivity,
    FriendBuilders friendBuilders)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;

    [SlashCommand("friends", "Displays your friends and what they're listening to",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task FriendsAsync()
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await friendBuilders.FriendsAsync(new ContextModel(this.Context, contextUser));

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("addfriend", "Add a friend to your .fmbot friends",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task AddFriendAsync(
        [SlashCommandParameter(Name = "user", Description = "The user to add")]
        NetCord.User user)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response =
                await friendBuilders.AddFriendsAsync(new ContextModel(this.Context, contextUser), [user.Id.ToString()]);

            await this.Context.SendResponse(this.Interactivity, response, true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
        }
    }

    [UserCommand("Add as friend",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task AddFriendUserCommandAsync(NetCord.User user)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response =
                await friendBuilders.AddFriendsAsync(new ContextModel(this.Context, contextUser), [user.Id.ToString()]);

            await this.Context.SendResponse(this.Interactivity, response, true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
        }
    }

    [SlashCommand("removefriend", "Remove a friend from your .fmbot friends",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task RemoveFriendAsync(
        [SlashCommandParameter(Name = "user", Description = "The user to remove")]
        NetCord.User user)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response =
                await friendBuilders.RemoveFriendsAsync(new ContextModel(this.Context, contextUser),
                    [user.Id.ToString()]);

            await this.Context.SendResponse(this.Interactivity, response, true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
        }
    }

    [UserCommand("Remove friend",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task RemoveFriendUserCommandAsync(NetCord.User user)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await friendBuilders.RemoveFriendsAsync(new ContextModel(this.Context, contextUser),
                new[] { user.Id.ToString() }, true);

            await this.Context.SendResponse(this.Interactivity, response, true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
        }
    }
}
