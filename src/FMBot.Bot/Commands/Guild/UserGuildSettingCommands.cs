using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.Commands.Guild
{
    [Name("Server member settings")]
    [ServerStaffOnly]
    public class UserGuildSettingCommands : BaseCommandModule
    {
        private readonly AdminService _adminService;
        private readonly GuildService _guildService;
        private readonly SettingService _settingService;

        private readonly IPrefixService _prefixService;

        public UserGuildSettingCommands(IPrefixService prefixService,
            GuildService guildService,
            AdminService adminService,
            SettingService settingService,
            IOptions<BotSettings> botSettings) : base(botSettings)
        {
            this._prefixService = prefixService;
            this._guildService = guildService;
            this._settingService = settingService;
            this._adminService = adminService;
        }

        [Command("activitythreshold", RunMode = RunMode.Async)]
        [Summary("Sets amount of days to filter out users for inactivity. Inactivity is counted by the last date that someone has used .fmbot")]
        [Options("Amount of days to filter someone")]
        [Alias("setactivitythreshold", "setthreshold")]
        [GuildOnly]
        public async Task SetWhoKnowsThresholdAsync([Remainder] string days = null)
        {
            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);

            if (lastIndex == null)
            {
                await ReplyAsync("This server hasn't been indexed yet.\n" +
                                 $"Please run `.fmindex` to index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            if (string.IsNullOrWhiteSpace(days))
            {
                await this._guildService.SetWhoKnowsActivityThresholdDaysAsync(this.Context.Guild, null);

                await ReplyAsync("All registered users in this server will now be visible again in server-wide commands and be able to gain crowns.");
                this.Context.LogCommandUsed();
                return;
            }

            var maxAmountOfDays = (DateTime.UtcNow - new DateTime(2020, 11, 4)).Days;

            if (int.TryParse(days, out var result))
            {
                if (result <= 1 || result > maxAmountOfDays)
                {
                    await ReplyAsync(
                        $"Please pick a value between 2 and {maxAmountOfDays} days.\n" +
                        $".fmbot only started storing user activity after November 4th 2020. It is not possible to set the activity filter before this date.\n");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }
            }
            else
            {
                await ReplyAsync("Please enter a valid amount of days. \n" +
                                 "All users that have not used .fmbot before that will be filtered from server wide commands.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await this._guildService.SetWhoKnowsActivityThresholdDaysAsync(this.Context.Guild, result);

            await ReplyAsync($"Activity threshold has been set for this server.\n" +
                             $"All users that have not used .fmbot in the last {result} days are now filtered from all server-wide commands and won't able to gain crowns.");
            this.Context.LogCommandUsed();
        }

        [Command("block", RunMode = RunMode.Async)]
        [Summary("Block a user from appearing in server-wide commands and charts")]
        [Options(Constants.UserMentionExample)]
        [Alias("blockuser", "ban", "banuser", "banmember", "blockmember")]
        [GuildOnly]
        public async Task GuildBlockUserAsync([Remainder] string user = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            if (guild?.LastIndexed == null)
            {
                await ReplyAsync($"Please run `{prfx}index` first.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

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

            if (guild.GuildBlockedUsers != null &&
                guild.GuildBlockedUsers
                    .Where(w => w.BlockedFromWhoKnows)
                    .Select(s => s.UserId)
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
                                            $"Last.fm username: `{userToBlock.UserNameLastFM}`\n" +
                                            $".fmbot id: `{userToBlock.UserId}`");

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
        [Alias("unblockuser", "unban", "unbanuser","unbanmember", "removeblock", "removeban", "crownunblock")]
        [GuildOnly]
        public async Task GuildUnBlockUserAsync([Remainder] string user = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            if (guild?.LastIndexed == null)
            {
                await ReplyAsync($"Please run `{prfx}index` first.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

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

            if (guild.GuildBlockedUsers == null || !guild.GuildBlockedUsers.OrderByDescending(o => o.User.LastUsed).Select(s => s.UserId).Contains(userToUnblock.UserId))
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
                                            $"Last.fm username: `{userToUnblock.UserNameLastFM}`\n" +
                                            $".fmbot id: `{userToUnblock.UserId}`");

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
        public async Task BlockedUsersAsync()
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            if (guild?.LastIndexed == null)
            {
                await ReplyAsync($"Please run `{prfx}index` first.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            if (guild.GuildBlockedUsers != null && guild.GuildBlockedUsers.Any(a => a.BlockedFromWhoKnows))
            {
                this._embed.WithTitle("Blocked users");
                var blockedUsers = "";

                foreach (var blockedUser in guild.GuildBlockedUsers.Where(w => w.BlockedFromWhoKnows))
                {
                    blockedUsers +=
                        $" - `{blockedUser.User.DiscordUserId}` (<@{blockedUser.User.DiscordUserId}>) | Last.fm: `{blockedUser.User.UserNameLastFM}`\n";
                }

                this._embed.WithDescription(blockedUsers);
                this._embed.WithFooter($"To add: {prfx}block mention/user id/last.fm username\n" +
                                        $"To remove: {prfx}unblock mention/user id/last.fm username");
            }
            else
            {
                this._embed.WithDescription("No blocked users found for this server.");
            }

            await ReplyAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }


        [Command("whoknowswhitelist", RunMode = RunMode.Async)]
        [Summary("Configures the WhoKnows charts to only show members with a specified role.\n\n" +
                 "After changing your whitelist setting, you will most likely need to index the server again for it to take effect.\n\n" +
                 "To remove the current whitelist setting, use this command without an option")]
        [Examples("whoknowswhitelist", "whoknowswhitelist royals", "whoknowswhitelist 423946236102705154")]
        [Alias("wkwhitelist", "wkwl")]
        [GuildOnly]
        public async Task SetWhoKnowsWhitelistRoleAsync([Remainder] string roleQuery = null)
        {
            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            if (string.IsNullOrEmpty(roleQuery) || roleQuery.ToLower() == "remove" || roleQuery.ToLower() == "delete")
            {
                await this._guildService.SetGuildWhoKnowsWhitelistRoleAsync(this.Context.Guild, null);
                await this._guildService.StaleGuildLastIndexedAsync(this.Context.Guild);
                await ReplyAsync("Removed the server's role whitelist for WhoKnows!");
                this.Context.LogCommandUsed();
                return;
            }

            if (
                !(roleQuery.Length is >= 17 and <= 19) ||
                !ulong.TryParse(roleQuery, out var discordRoleId)
                )
            {
                var roleQueryResult = this.Context.Guild.Roles.FirstOrDefault(r => r.Name.ToLower().Contains(roleQuery.ToLower()));

                if (roleQueryResult == null)
                {
                    await ReplyAsync("Could not find a role with that name! Maybe try again using the role ID");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                discordRoleId = roleQueryResult.Id;
            }

            var role = this.Context.Guild.Roles.FirstOrDefault(r => r.Id == discordRoleId);

            if (role == null)
            {
                await ReplyAsync("Could not find a role with that ID!");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            await this._guildService.SetGuildWhoKnowsWhitelistRoleAsync(this.Context.Guild, role.Id);
            await this._guildService.StaleGuildLastIndexedAsync(this.Context.Guild);

            this._embed.WithTitle("Successfully set the server's whitelist role for WhoKnows!");
            this._embed.WithDescription($"Set to <@&{discordRoleId}>\nKeep in mind that you need to run `{prfx}index` again for the whitelist to work as intended.");

            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();
        }
    }
}
