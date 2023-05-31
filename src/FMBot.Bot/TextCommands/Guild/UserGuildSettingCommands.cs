using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Fergun.Interactive;
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

namespace FMBot.Bot.TextCommands.Guild;

[Name("Server member settings")]
[ServerStaffOnly]
public class UserGuildSettingCommands : BaseCommandModule
{
    private readonly AdminService _adminService;
    private readonly GuildService _guildService;
    private readonly SettingService _settingService;
    private readonly GuildSettingBuilder _guildSettingBuilder;

    private readonly IPrefixService _prefixService;

    private InteractiveService Interactivity { get; }


    public UserGuildSettingCommands(IPrefixService prefixService,
        GuildService guildService,
        AdminService adminService,
        SettingService settingService,
        IOptions<BotSettings> botSettings, GuildSettingBuilder guildSettingBuilder, InteractiveService interactivity) : base(botSettings)
    {
        this._prefixService = prefixService;
        this._guildService = guildService;
        this._settingService = settingService;
        this._guildSettingBuilder = guildSettingBuilder;
        this.Interactivity = interactivity;
        this._adminService = adminService;
    }

    [Command("fmbotactivitythreshold", RunMode = RunMode.Async)]
    [Summary("Sets amount of days to filter out users for inactivity. Inactivity is counted by the last date that someone has used .fmbot")]
    [Alias("setfmbotactivitythreshold", "setfmbotthreshold", "setfmbothreshold")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.ServerSettings, CommandCategory.WhoKnows)]
    public async Task SetWhoKnowsThresholdAsync([Remainder] string _ = null)
    {
        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var response = await this._guildSettingBuilder.SetFmbotActivityThreshold(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("block", RunMode = RunMode.Async)]
    [Summary("Block a user from appearing in server-wide commands and charts")]
    [Options(Constants.UserMentionExample)]
    [Alias("blockuser", "ban", "banuser", "banmember", "blockmember")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.ServerSettings, CommandCategory.WhoKnows)]
    public async Task GuildBlockUserAsync([Remainder] string user = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await ReplyAsync(GuildSettingBuilder.UserNotAllowedResponseText());
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        _ = this.Context.Channel.TriggerTypingAsync();

        if (user == null)
        {
            await ReplyAsync("Please mention a user, enter a discord id or enter a Last.fm username to block on your server.");
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        var userToBlock = await this._settingService.GetDifferentUser(user);

        if (userToBlock == null)
        {
            await ReplyAsync("User not found. Are you sure they are registered in .fmbot?");
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        var guildUsers = await this._guildService.GetGuildUsers(this.Context.Guild.Id);

        if (!guildUsers.ContainsKey(userToBlock.UserId))
        {
            var similarUsers = await this._adminService.GetUsersWithLfmUsernameAsync(userToBlock.UserNameLastFM);

            var userInThisServer = similarUsers.FirstOrDefault(f =>
                f.UserNameLastFM.ToLower() == userToBlock.UserNameLastFM.ToLower() && guildUsers.ContainsKey(f.UserId));

            if (userInThisServer == null)
            {
                await ReplyAsync("User not found. Are you sure they are in this server?\n" +
                                 $"To refresh the cached memberlist on your server, use `{prfx}refreshmembers`.");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            userToBlock = userInThisServer;
        }

        if (guildUsers
                .Where(w => w.Value.BlockedFromWhoKnows)
                .Select(s => s.Key)
                .Contains(userToBlock.UserId))
        {
            await ReplyAsync("The user you're trying to block has already been blocked on this server.");
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var userBlocked = await this._guildService.BlockGuildUserAsync(this.Context.Guild, userToBlock.UserId);

        if (userBlocked)
        {
            this._embed.WithTitle("Added blocked user");
            this._embed.WithDescription($"Discord user id: `{userToBlock.DiscordUserId}` (<@{userToBlock.DiscordUserId}>)\n" +
                                        $"Last.fm username: `{userToBlock.UserNameLastFM}`");

            this._embed.WithFooter($"See all blocked users with {prfx}blockedusers");

            await ReplyAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync("Something went wrong while attempting to block user, please contact .fmbot staff.");
            this.Context.LogCommandUsed(CommandResponse.Error);
        }
    }

    [Command("unblock", RunMode = RunMode.Async)]
    [Summary("Remove block from a user from appearing in server-wide commands")]
    [Options(Constants.UserMentionExample)]
    [Alias("unblockuser", "unban", "unbanuser", "unbanmember", "removeblock", "removeban", "crownunblock")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.ServerSettings, CommandCategory.WhoKnows)]
    public async Task GuildUnBlockUserAsync([Remainder] string user = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await ReplyAsync(GuildSettingBuilder.UserNotAllowedResponseText());
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        _ = this.Context.Channel.TriggerTypingAsync();

        if (user == null)
        {
            await ReplyAsync("Please mention a user, enter a discord id or enter a Last.fm username to unblock on your server.");
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        var userToUnblock = await this._settingService.GetDifferentUser(user);

        if (userToUnblock == null)
        {
            await ReplyAsync("User not found. Are you sure they are registered in .fmbot?");
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        var guildUsers = await this._guildService.GetGuildUsers(this.Context.Guild.Id);

        if (guildUsers == null || !guildUsers.ContainsKey(userToUnblock.UserId))
        {
            await ReplyAsync("The user you're trying to unblock was not blocked on this server.");
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var userUnblocked = await this._guildService.UnBlockGuildUserAsync(this.Context.Guild, userToUnblock.UserId);

        if (userUnblocked)
        {
            this._embed.WithTitle("Unblocked user");
            this._embed.WithDescription($"Discord user id: `{userToUnblock.DiscordUserId}` (<@{userToUnblock.DiscordUserId}>)\n" +
                                        $"Last.fm username: `{userToUnblock.UserNameLastFM}`");

            this._embed.WithFooter($"See other blocked users with {prfx}blockedusers");

            await ReplyAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync("Something went wrong while attempting to unblock user, please contact .fmbot staff.");
            this.Context.LogCommandUsed(CommandResponse.Error);
        }
    }

    [Command("blockedusers", RunMode = RunMode.Async)]
    [Summary("View all users that are blocked from appearing in server-wide commands")]
    [Alias("blocked", "banned", "bannedusers", "blockedmembers", "bannedmembers")]
    [GuildOnly]
    [RequiresIndex]
    [SupportsPagination]
    [CommandCategories(CommandCategory.ServerSettings, CommandCategory.WhoKnows)]
    public async Task BlockedUsersAsync([Remainder] string searchValue = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await ReplyAsync(GuildSettingBuilder.UserNotAllowedResponseText());
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        _ = this.Context.Channel.TriggerTypingAsync();

        var response = await this._guildSettingBuilder.BlockedUsersAsync(new ContextModel(this.Context, prfx), searchValue: searchValue);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }
}
