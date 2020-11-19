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
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Commands.Guild
{
    [Summary("Server Staff Only")]
    public class CrownGuildSettingCommands : ModuleBase
    {
        private readonly AdminService _adminService;
        private readonly CrownService _crownService;
        private readonly GuildService _guildService;
        private readonly SettingService _settingService;

        private readonly IPrefixService _prefixService;

        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;

        public CrownGuildSettingCommands(IPrefixService prefixService,
            GuildService guildService,
            AdminService adminService,
            SettingService settingService, CrownService crownService)
        {
            this._prefixService = prefixService;
            this._guildService = guildService;
            this._settingService = settingService;
            this._crownService = crownService;
            this._adminService = adminService;
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }


        [Command("crownthreshold", RunMode = RunMode.Async)]
        [Summary("Sets amount of days to filter out users for inactivity")]
        [Alias("setcrownthreshold", "setcwthreshold", "cwthreshold", "crowntreshold")]
        [GuildOnly]
        public async Task SetCrownPlaycountThresholdAsync([Remainder] string playcount = null)
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

            if (string.IsNullOrWhiteSpace(playcount))
            {
                await this._guildService.SetMinimumCrownPlaycountThresholdAsync(this.Context.Guild, null);

                await ReplyAsync($"Minumum amount of plays for a crown has been set to the default of {Constants.DefaultPlaysForCrown}.");
                this.Context.LogCommandUsed();
                return;
            }

            var maxAmountOfDays = (DateTime.UtcNow - new DateTime(2020, 11, 4)).Days;

            if (int.TryParse(playcount, out var result))
            {
                if (result < 10 || result > 1000)
                {
                    await ReplyAsync(
                        $"Please pick a value between 10 and 1000 plays.");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }
            }
            else
            {
                await ReplyAsync("Please enter a valid amount of plays. \n" +
                                 "Any playcount below the amount you enter will not be enough for a crown.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await this._guildService.SetMinimumCrownPlaycountThresholdAsync(this.Context.Guild, result);

            await ReplyAsync($"Crown playcount threshold has been set for this server.\n" +
                             $"All users that have less then {result} plays for an artist won't able to gain crowns for that artist.");
            this.Context.LogCommandUsed();
        }

        [Command("crownactivitythreshold", RunMode = RunMode.Async)]
        [Summary("Sets amount of days to filter out users for inactivity")]
        [Alias("setcrownactivitythreshold",  "setcwactivitythreshold", "cwactivitythreshold", "crownactivitytreshold")]
        [GuildOnly]
        public async Task SetCrownActivityThresholdAsync([Remainder] string days = null)
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
                await this._guildService.SetCrownActivityThresholdDaysAsync(this.Context.Guild, null);

                await ReplyAsync("All registered users in this server will now be able to gain crowns.");
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
                        $".fmbot only started storing user activity after November 4th 2020. It is not possible to set the crown activity filter before this date.\n");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }
            }
            else
            {
                await ReplyAsync("Please enter a valid amount of days. \n" +
                                 "All users that have not used .fmbot before that will not be able to gain crowns.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await this._guildService.SetCrownActivityThresholdDaysAsync(this.Context.Guild, result);

            await ReplyAsync($"Crown activity threshold has been set for this server.\n" +
                             $"All users that have not used .fmbot in the last {result} days won't able to gain crowns.");
            this.Context.LogCommandUsed();
        }

        [Command("crownblock", RunMode = RunMode.Async)]
        [Summary("Block a user from appearing gaining crowns")]
        [Alias("crownblockuser", "crownban", "cwblock", "cwban", "crownbanuser")]
        [GuildOnly]
        public async Task GuildBlockUserFromCrownsAsync([Remainder] string user = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            if (guild?.LastIndexed == null)
            {
                await ReplyAsync($"Please run `{prfx}index` first.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            if (user == null)
            {
                await ReplyAsync("Please mention a user, enter a discord id or enter a Last.fm username to block from gaining crowns on your server.");
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

            if (guild.GuildBlockedUsers != null && guild.GuildBlockedUsers
                .Where(w => w.BlockedFromCrowns)
                .Select(s => s.UserId)
                .Contains(userToBlock.UserId))
            {
                await ReplyAsync("The user you're trying to block from gaining crowns has already been blocked on this server.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var userBlocked = await this._guildService.CrownBlockGuildUserAsync(this.Context.Guild, userToBlock.UserId);

            if (userBlocked)
            {
                this._embed.WithTitle("Added crownblocked user");
                this._embed.WithDescription($"Discord user id: `{userToBlock.DiscordUserId}` (<@{userToBlock.DiscordUserId}>)\n" +
                                            $"Last.fm username: `{userToBlock.UserNameLastFM}`\n" +
                                            $".fmbot id: `{userToBlock.UserId}`");

                this._embed.WithFooter($"See all crownblocked users with {prfx}crownblockedusers");

                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("Something went wrong while attempting to crownblock user, please contact .fmbot staff.");
                this.Context.LogCommandUsed(CommandResponse.Error);
            }
        }

        [Command("crownblockedusers", RunMode = RunMode.Async)]
        [Summary("Block a user from appearing in server-wide commands")]
        [Alias("crownblocked", "crownbanned", "crownbannedusers")]
        [GuildOnly]
        public async Task BlockedUsersAsync()
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            if (guild?.LastIndexed == null)
            {
                await ReplyAsync($"Please run `{prfx}index` first.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            if (guild.GuildBlockedUsers != null && guild.GuildBlockedUsers.Any(w => w.BlockedFromCrowns))
            {
                this._embed.WithTitle("Crownblocked users");
                var blockedUsers = "";

                foreach (var crownBlockedUser in guild.GuildBlockedUsers.Where(w => w.BlockedFromCrowns))
                {
                    blockedUsers +=
                        $" - `{crownBlockedUser.User.DiscordUserId}` (<@{crownBlockedUser.User.DiscordUserId}>) | Last.fm: `{crownBlockedUser.User.UserNameLastFM}`\n";
                }

                this._embed.WithDescription(blockedUsers);
                this._embed.WithFooter($"To add: {prfx}crownblock mention/user id/last.fm username\n" +
                                        $"To remove: {prfx}unblock mention/user id/last.fm username");
            }
            else
            {
                this._embed.WithDescription("No crownblocked users found for this server.");
            }

            await ReplyAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("togglecrowns", RunMode = RunMode.Async)]
        [Summary("Toggles crowns for your server.")]
        [Alias("togglecrown")]
        [GuildOnly]
        public async Task ToggleCrownsAsync([Remainder] string confirmation = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            if (guild == null)
            {
                await ReplyAsync("This server hasn't been stored yet.\n" +
                                 $"Please run `{prfx}index` to store this server.");
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

            if (guild.CrownsDisabled != true && (confirmation == null || confirmation.ToLower() != "confirm"))
            {
                await ReplyAsync($"Disabling crowns will remove all existing crowns and crown history for this server.\n" +
                                 $"Type `{prfx}togglecrowns confirm` to confirm.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var crownsDisabled = await this._guildService.ToggleCrownsAsync(this.Context.Guild);

            if (crownsDisabled == true)
            {
                await this._crownService.RemoveAllCrownsFromGuild(guild);
                await ReplyAsync("All crowns have been removed and crowns have been disabled for this server.");
            }
            else
            {
                await ReplyAsync($"Crowns have been enabled for this server.");
            }

            this.Context.LogCommandUsed();
        }
    }
}
