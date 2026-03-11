using System;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord.Services.Commands;
using Fergun.Interactive;
using NetCord.Rest;

namespace FMBot.Bot.TextCommands.Guild;

[ModuleName("Server member settings")]
[ServerStaffOnly]
public class UserGuildSettingCommands(
    IPrefixService prefixService,
    GuildService guildService,
    AdminService adminService,
    SettingService settingService,
    UserService userService,
    IOptions<BotSettings> botSettings,
    GuildSettingBuilder guildSettingBuilder,
    InteractiveService interactivity)
    : BaseCommandModule(botSettings)
{
    private InteractiveService Interactivity { get; } = interactivity;


    [Command("fmbotactivitythreshold", "setfmbotactivitythreshold", "setfmbotthreshold", "setfmbothreshold")]
    [Summary("Sets amount of days to filter out users for inactivity. Inactivity is counted by the last date that someone has used .fmbot")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.ServerSettings, CommandCategory.WhoKnows)]
    public async Task SetWhoKnowsThresholdAsync([CommandParameter(Remainder = true)] string _ = null)
    {
        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
            var response = await guildSettingBuilder.SetFmbotActivityThreshold(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("block", "blockuser", "blockmember")]
    [Summary("Block a user from appearing in server-wide commands and charts")]
    [Options(Constants.UserMentionExample)]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.ServerSettings, CommandCategory.WhoKnows)]
    public async Task GuildBlockUserAsync([CommandParameter(Remainder = true)] string user = null)
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = GuildSettingBuilder.UserNotAllowedResponseText() });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        if (user == null)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Please mention a user, enter a Discord ID, or enter a Last.fm username to block on your server." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
            return;
        }

        var userToBlock = await settingService.GetDifferentUser(user);

        if (userToBlock == null)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "User not found. Are you sure they are registered in .fmbot?" });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
            return;
        }

        var guildUsers = await guildService.GetGuildUsers(this.Context.Guild.Id);

        if (!guildUsers.ContainsKey(userToBlock.UserId))
        {
            var similarUsers = await adminService.GetUsersWithLfmUsernameAsync(userToBlock.UserNameLastFM);

            var userInThisServer = similarUsers.FirstOrDefault(f =>
                f.UserNameLastFM.ToLower() == userToBlock.UserNameLastFM.ToLower() && guildUsers.ContainsKey(f.UserId));

            if (userInThisServer == null)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = $"User not found. Are you sure they are in this server?\nTo refresh the cached memberlist on your server, use `{prfx}refreshmembers`." });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                return;
            }

            userToBlock = userInThisServer;
        }

        if (guildUsers
                .Where(w => w.Value.BlockedFromWhoKnows)
                .Select(s => s.Key)
                .Contains(userToBlock.UserId))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "The user you're trying to block has already been blocked on this server." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
            return;
        }

        var userBlocked = await guildService.BlockGuildUserAsync(this.Context.Guild, userToBlock.UserId);

        if (userBlocked)
        {
            this._embed.WithTitle("Added blocked user");
            this._embed.WithDescription($"Discord user id: `{userToBlock.DiscordUserId}` (<@{userToBlock.DiscordUserId}>)\n" +
                                        $"Last.fm username: `{userToBlock.UserNameLastFM}`");

            this._embed.WithFooter($"See all blocked users with {prfx}blockedusers");

            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Embeds = [this._embed] });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Something went wrong while attempting to block user, please contact .fmbot staff." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Error }, userService);
        }
    }

    [Command("unblock", "unblockuser", "removeblock", "removeban", "crownunblock", "crownunban")]
    [Summary("Remove block from a user from appearing in server-wide commands")]
    [Options(Constants.UserMentionExample)]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.ServerSettings, CommandCategory.WhoKnows)]
    public async Task GuildUnBlockUserAsync([CommandParameter(Remainder = true)] string user = null)
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = GuildSettingBuilder.UserNotAllowedResponseText() });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        if (user == null)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Please mention a user, enter a Discord ID, or enter a Last.fm username to unblock on your server." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
            return;
        }

        var userToUnblock = await settingService.GetDifferentUser(user);

        if (userToUnblock == null)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "User not found. Are you sure they are registered in .fmbot?" });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
            return;
        }

        var guildUsers = await guildService.GetGuildUsers(this.Context.Guild.Id);

        if (guildUsers == null || !guildUsers.ContainsKey(userToUnblock.UserId))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "The user you're trying to unblock was not blocked on this server." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
            return;
        }

        var userUnblocked = await guildService.UnBlockGuildUserAsync(this.Context.Guild, userToUnblock.UserId);

        if (userUnblocked)
        {
            this._embed.WithTitle("Unblocked user");
            this._embed.WithDescription($"Discord user id: `{userToUnblock.DiscordUserId}` (<@{userToUnblock.DiscordUserId}>)\n" +
                                        $"Last.fm username: `{userToUnblock.UserNameLastFM}`");

            this._embed.WithFooter($"See other blocked users with {prfx}blockedusers");

            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Embeds = [this._embed] });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Something went wrong while attempting to unblock user, please contact .fmbot staff." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Error }, userService);
        }
    }

    [Command("blockedusers", "blocked", "banned", "bannedusers", "blockedmembers", "bannedmembers")]
    [Summary("View all users that are blocked from appearing in server-wide commands")]
    [GuildOnly]
    [RequiresIndex]
    [SupportsPagination]
    [CommandCategories(CommandCategory.ServerSettings, CommandCategory.WhoKnows)]
    public async Task BlockedUsersAsync([CommandParameter(Remainder = true)] string searchValue = null)
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = GuildSettingBuilder.UserNotAllowedResponseText() });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var response = await guildSettingBuilder.BlockedUsersAsync(new ContextModel(this.Context, prfx), searchValue: searchValue);

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }
}
