using System;
using System.Threading.Tasks;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Attributes;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;
using Fergun.Interactive;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

public class GuildSettingSlashCommands(
    GuildSettingBuilder guildSettingBuilder,
    InteractiveService interactivity,
    UserService userService,
    GuildService guildService,
    GuildBuilders guildBuilders)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;

    [SlashCommand("members", "Members in this server that use .fmbot",
        Contexts = [InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.GuildInstall])]
    [RequiresIndex]
    public async Task MemberOverviewAsync(
        [SlashCommandParameter(Name = "view", Description = "Statistic you want to view")]
        GuildViewType viewType)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredMessage());

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

            var response =
                await guildBuilders.MemberOverviewAsync(new ContextModel(this.Context, contextUser), guild,
                    viewType);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("block", "Block a user from appearing in WhoKnows and server-wide charts",
        Contexts = [InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.GuildInstall],
        DefaultGuildPermissions = Permissions.BanUsers)]
    [RequiresIndex]
    public async Task GuildBlockUserAsync(
        [SlashCommandParameter(Name = "user", Description = "The user you want to block")]
        NetCord.User user)
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var response = new ResponseModel
            {
                ResponseType = ResponseType.Embed
            };

            if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, contextUser)))
            {
                response.Embed.WithDescription(GuildSettingBuilder.UserNotAllowedResponseText());
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.CommandResponse = CommandResponse.NoPermission;

                await this.Context.SendResponse(this.Interactivity, response, userService, ephemeral: true);
                await this.Context.LogCommandUsedAsync(response, userService);
                return;
            }

            var userToBlock = await userService.GetUserAsync(user.Id);

            if (userToBlock == null)
            {
                response.Embed.WithDescription("User not found. Are you sure they are registered in .fmbot?");
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.CommandResponse = CommandResponse.NotFound;

                await this.Context.SendResponse(this.Interactivity, response, userService, ephemeral: true);
                await this.Context.LogCommandUsedAsync(response, userService);
                return;
            }

            var guildUsers = await guildService.GetGuildUsers(this.Context.Guild.Id);

            if (guildUsers == null || !guildUsers.ContainsKey(userToBlock.UserId))
            {
                response.Embed.WithDescription("User not found. Are you sure they are in this server?");
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.CommandResponse = CommandResponse.NotFound;

                await this.Context.SendResponse(this.Interactivity, response, userService, ephemeral: true);
                await this.Context.LogCommandUsedAsync(response, userService);
                return;
            }

            if (guildUsers[userToBlock.UserId].BlockedFromWhoKnows)
            {
                response.Embed.WithDescription("The user you're trying to block has already been blocked on this server.");
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.CommandResponse = CommandResponse.WrongInput;

                await this.Context.SendResponse(this.Interactivity, response, userService, ephemeral: true);
                await this.Context.LogCommandUsedAsync(response, userService);
                return;
            }

            var userBlocked = await guildService.BlockGuildUserAsync(this.Context.Guild, userToBlock.UserId);

            if (userBlocked)
            {
                response.Embed.WithTitle("Added blocked user");
                response.Embed.WithDescription(
                    $"Discord user id: `{userToBlock.DiscordUserId}` (<@{userToBlock.DiscordUserId}>)\n" +
                    $"Last.fm username: `{userToBlock.UserNameLastFM}`");
                response.Embed.WithColor(DiscordConstants.InformationColorBlue);
                response.CommandResponse = CommandResponse.Ok;
            }
            else
            {
                response.Embed.WithDescription("Something went wrong while attempting to block user, please contact .fmbot staff.");
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.CommandResponse = CommandResponse.Error;
            }

            await this.Context.SendResponse(this.Interactivity, response, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("unblock", "Remove a block from a user so they appear in WhoKnows and server-wide charts again",
        Contexts = [InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.GuildInstall],
        DefaultGuildPermissions = Permissions.BanUsers)]
    [RequiresIndex]
    public async Task GuildUnBlockUserAsync(
        [SlashCommandParameter(Name = "user", Description = "The user you want to unblock")]
        NetCord.User user)
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var response = new ResponseModel
            {
                ResponseType = ResponseType.Embed
            };

            if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, contextUser)))
            {
                response.Embed.WithDescription(GuildSettingBuilder.UserNotAllowedResponseText());
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.CommandResponse = CommandResponse.NoPermission;

                await this.Context.SendResponse(this.Interactivity, response, userService, ephemeral: true);
                await this.Context.LogCommandUsedAsync(response, userService);
                return;
            }

            var userToUnblock = await userService.GetUserAsync(user.Id);

            if (userToUnblock == null)
            {
                response.Embed.WithDescription("User not found. Are you sure they are registered in .fmbot?");
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.CommandResponse = CommandResponse.NotFound;

                await this.Context.SendResponse(this.Interactivity, response, userService, ephemeral: true);
                await this.Context.LogCommandUsedAsync(response, userService);
                return;
            }

            var guildUsers = await guildService.GetGuildUsers(this.Context.Guild.Id);

            if (guildUsers == null || !guildUsers.ContainsKey(userToUnblock.UserId) ||
                !guildUsers[userToUnblock.UserId].BlockedFromWhoKnows)
            {
                response.Embed.WithDescription("The user you're trying to unblock was not blocked on this server.");
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.CommandResponse = CommandResponse.WrongInput;

                await this.Context.SendResponse(this.Interactivity, response, userService, ephemeral: true);
                await this.Context.LogCommandUsedAsync(response, userService);
                return;
            }

            var userUnblocked = await guildService.UnBlockGuildUserAsync(this.Context.Guild, userToUnblock.UserId);

            if (userUnblocked)
            {
                response.Embed.WithTitle("Unblocked user");
                response.Embed.WithDescription(
                    $"Discord user id: `{userToUnblock.DiscordUserId}` (<@{userToUnblock.DiscordUserId}>)\n" +
                    $"Last.fm username: `{userToUnblock.UserNameLastFM}`");
                response.Embed.WithColor(DiscordConstants.InformationColorBlue);
                response.CommandResponse = CommandResponse.Ok;
            }
            else
            {
                response.Embed.WithDescription("Something went wrong while attempting to unblock user, please contact .fmbot staff.");
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.CommandResponse = CommandResponse.Error;
            }

            await this.Context.SendResponse(this.Interactivity, response, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
