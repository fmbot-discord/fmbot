using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.Commands.Guild
{
    [Name("Crown settings")]
    [ServerStaffOnly]
    public class CrownGuildSettingCommands : BaseCommandModule
    {
        private readonly AdminService _adminService;
        private readonly CrownService _crownService;
        private readonly GuildService _guildService;
        private readonly SettingService _settingService;

        private readonly IPrefixService _prefixService;
        private InteractiveService Interactivity { get; }


        public CrownGuildSettingCommands(IPrefixService prefixService,
            GuildService guildService,
            AdminService adminService,
            SettingService settingService,
            CrownService crownService,
            IOptions<BotSettings> botSettings, InteractiveService interactivity) : base(botSettings)
        {
            this._prefixService = prefixService;
            this._guildService = guildService;
            this._settingService = settingService;
            this._crownService = crownService;
            this.Interactivity = interactivity;
            this._adminService = adminService;
        }

        [Command("crownthreshold", RunMode = RunMode.Async)]
        [Summary("Sets amount of plays before someone can earn a crown in your server")]
        [Options("Amount of plays")]
        [Alias("setcrownthreshold", "setcwthreshold", "cwthreshold", "crowntreshold")]
        [GuildOnly]
        [RequiresIndex]
        [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
        public async Task SetCrownPlaycountThresholdAsync([Remainder] string playcount = null)
        {
            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' or server admins can use this command.");
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
        [Summary("Sets amount of days to filter out users from earning crowns for inactivity. " +
                 "Inactivity is counted by the last date that someone has used .fmbot")]
        [Options("Amount of days to filter someone")]
        [Alias("setcrownactivitythreshold", "setcwactivitythreshold", "cwactivitythreshold", "crownactivitytreshold")]
        [GuildOnly]
        [RequiresIndex]
        [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
        public async Task SetCrownActivityThresholdAsync([Remainder] string days = null)
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
        [Summary("Block a user from gaining any crowns in your server")]
        [Options(Constants.UserMentionExample)]
        [Alias("crownblockuser", "crownban", "cwblock", "cwban", "crownbanuser", "crownbanmember")]
        [GuildOnly]
        [RequiresIndex]
        [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
        public async Task GuildBlockUserFromCrownsAsync([Remainder] string user = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' or server admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            if (user == null)
            {
                await ReplyAsync("Please mention a user, enter a discord id or enter a Last.fm username to block from gaining crowns on your server.");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var userToBlock = await this._settingService.GetDifferentUser(user);

            if (userToBlock == null || !guild.GuildUsers.Select(s => s.UserId).Contains(userToBlock.UserId))
            {
                await ReplyAsync("User not found. Are you sure they are registered in .fmbot and in this server?\n" +
                                 $"To refresh the cached memberlist on your server, use `{prfx}index`.");
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

        [Command("removeusercrowns", RunMode = RunMode.Async)]
        [Summary("Block a user from gaining any crowns in your server")]
        [Options(Constants.UserMentionExample)]
        [Alias("deleteusercrowns", "deleteusercrown", "removeusercrowns", "removeusercws", "deleteusercws", "usercrownsdelete", "usercrownsremove")]
        [GuildOnly]
        [RequiresIndex]
        [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
        public async Task RemoveUserCrownsAsync([Remainder] string user = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' or server admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            if (user == null)
            {
                await ReplyAsync("Please mention a user, enter a discord id or enter a Last.fm username to remove their crowns from.");
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

            try
            {
                this._embed.WithTitle("Are you sure you want to delete all crowns for this user?");
                this._embed.WithDescription($"Discord user id: `{userToBlock.DiscordUserId}` (<@{userToBlock.DiscordUserId}>)\n" +
                                            $"Last.fm username: `{userToBlock.UserNameLastFM}`\n" +
                                            $".fmbot id: `{userToBlock.UserId}`");
                this._embed.WithFooter($"Expires in 30 seconds..");

                var builder = new ComponentBuilder()
                    .WithButton("Confirm", "id");

                var msg = await ReplyAsync("", false, this._embed.Build(), component: builder.Build());

                var result = await this.Interactivity.NextInteractionAsync(x => x is SocketMessageComponent c && c.Message.Id == msg.Id && x.User.Id == this.Context.User.Id,
                    timeout: TimeSpan.FromSeconds(30));

                if (result.IsSuccess)
                {
                    await result.Value.DeferAsync();
                    await this._crownService.RemoveAllCrownsFromDiscordUser(userToBlock.DiscordUserId, guild.DiscordGuildId);

                    this._embed.WithTitle("Crowns have been removed for:");
                    await msg.ModifyAsync(x =>
                    {
                        x.Embed = this._embed.Build();
                        x.Components = new ComponentBuilder().Build(); // No components
                        x.AllowedMentions = AllowedMentions.None;
                    });
                }
                else
                {
                    this._embed.WithTitle("Crown removal timed out");
                    await msg.ModifyAsync(x =>
                    {
                        x.Embed = this._embed.Build();
                        x.Components = new ComponentBuilder().Build(); // No components
                    x.AllowedMentions = AllowedMentions.None;
                    });
                }

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                await ReplyAsync("Something went wrong while attempting to remove crowns for user, please contact .fmbot staff.");
                this.Context.LogCommandException(e);
            }
        }

        [Command("crownblockedusers", RunMode = RunMode.Async)]
        [Summary("View all users that are blocked from earning crowns in your server")]
        [Alias("crownblocked", "crownbanned", "crownbannedusers", "crownbannedmembers")]
        [GuildOnly]
        [RequiresIndex]
        [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
        public async Task BlockedUsersAsync()
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' or server admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

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
        [Summary("Completely enables/disables crowns for your server.")]
        [Alias("togglecrown")]
        [GuildOnly]
        [RequiresIndex]
        [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
        public async Task ToggleCrownsAsync([Remainder] string confirmation = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' or server admins can use this command. ");
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

        [Command("killcrown", RunMode = RunMode.Async)]
        [Summary("Server staff command: Removes all crowns from a specific artist for your server.")]
        [Alias("kcw", "kcrown", "killcw", "kill crown", "crown kill")]
        [GuildOnly]
        [RequiresIndex]
        [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
        public async Task KillCrownAsync([Remainder] string killCrownValues = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            if (!string.IsNullOrWhiteSpace(killCrownValues) && killCrownValues.ToLower() == "help")
            {
                this._embed.WithTitle($"{prfx}killcrown");
                this._embed.WithDescription("Allows you to remove a crown and all crown history for a certain artist.");

                this._embed.AddField("Examples",
                    $"`{prfx}killcrown deadmau5` \n" +
                    $"`{prfx}killcrown the beatles`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            if (guild.CrownsDisabled == true)
            {
                await ReplyAsync("Crown functionality has been disabled in this server.");
                this.Context.LogCommandUsed(CommandResponse.Disabled);
                return;
            }

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' or server admins can use this command. ");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            if (string.IsNullOrWhiteSpace(killCrownValues))
            {
                await ReplyAsync("Please enter the artist you want to remove all crowns for.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var artistCrowns = await this._crownService.GetCrownsForArtist(guild, killCrownValues);

            if (!artistCrowns.Any())
            {
                this._embed.WithDescription($"No crowns found for the artist `{killCrownValues}`");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                return;
            }

            await this._crownService.RemoveCrowns(artistCrowns);

            this._embed.WithDescription($"All crowns for `{killCrownValues}` have been removed.");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("crownseeder", RunMode = RunMode.Async)]
        [Summary("Server staff command: Automatically generates or updates all crowns for your server")]
        [Alias("crownseed", "seedcrowns")]
        [GuildOnly]
        [RequiresIndex]
        [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
        public async Task SeedCrownsAsync([Remainder] string helpValues = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            if (!string.IsNullOrWhiteSpace(helpValues) && helpValues.ToLower() == "help")
            {
                this._embed.WithTitle($"{prfx}crownseeder");
                this._embed.WithDescription("Automatically adds crowns for your server. If you've done this before, it will update all automatically seeded crowns.\n\n" +
                                            "Crown seeding only updates automatically seeded crowns, not manually claimed crowns.");

                this._embed.AddField("Examples",
                    $"`{prfx}crownseeder`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            if (guild.CrownsDisabled == true)
            {
                await ReplyAsync("Crown functionality has been disabled in this server.");
                this.Context.LogCommandUsed(CommandResponse.Disabled);
                return;
            }

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' or server admins can use this command. This is because some servers prefer the manual crown claiming process.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            this._embed.WithDescription($"<a:loading:821676038102056991> Seeding crowns for your server...");
            var message = await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            var guildCrowns = await this._crownService.GetAllCrownsForGuild(guild.GuildId);

            var amountOfCrownsSeeded = await this._crownService.SeedCrownsForGuild(guild, guildCrowns).ConfigureAwait(false);

            await message.ModifyAsync(m =>
            {
                m.Embed = new EmbedBuilder()
                    .WithDescription($"Seeded {amountOfCrownsSeeded} crowns for your server.\n\n" +
                                     $"Tip: You can remove all crowns with `{prfx}killallcrowns` or remove all automatically seeded crowns with `{prfx}killallseededcrowns`.")
                    .WithColor(DiscordConstants.SuccessColorGreen)
                    .Build();
            });

            this.Context.LogCommandUsed();
        }

        [Command("killallcrowns", RunMode = RunMode.Async)]
        [Summary("Server Staff Command: Removes all crowns from your server")]
        [Alias("removeallcrowns")]
        [GuildOnly]
        [RequiresIndex]
        [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
        public async Task KillAllCrownsAsync([Remainder] string confirmation = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            if (!string.IsNullOrWhiteSpace(confirmation) && confirmation.ToLower() == "help")
            {
                this._embed.WithTitle($"{prfx}killallcrowns");
                this._embed.WithDescription("Removes all crowns from your server.");

                this._embed.AddField("Examples",
                    $"`{prfx}killallcrowns`\n" +
                    $"`{prfx}killallcrowns confirm`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            if (guild.CrownsDisabled == true)
            {
                await ReplyAsync("Crown functionality has been disabled in this server.");
                this.Context.LogCommandUsed(CommandResponse.Disabled);
                return;
            }

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' or server admins can use this command. ");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var guildCrowns = await this._crownService.GetAllCrownsForGuild(guild.GuildId);
            if (guildCrowns == null || !guildCrowns.Any())
            {
                await ReplyAsync("This server does not have any crowns.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            if (string.IsNullOrWhiteSpace(confirmation) || confirmation.ToLower() != "confirm")
            {
                this._embed.WithDescription($"Are you sure you want to remove all {guildCrowns.Count} crowns from your server?\n\n" +
                                            $"Type `{prfx}killallcrowns confirm` to confirm.");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await this._crownService.RemoveAllCrownsFromGuild(guild);

            this._embed.WithDescription("Removed all crowns for your server.");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("killallseededcrowns", RunMode = RunMode.Async)]
        [Summary("Removes all crowns seeded by the `crownseeder` command. All other manually claimed crowns will remain in place.")]
        [Alias("removeallseededcrowns")]
        [GuildOnly]
        [RequiresIndex]
        [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
        public async Task KillAllSeededCrownsAsync([Remainder] string confirmation = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            if (!string.IsNullOrWhiteSpace(confirmation) && confirmation.ToLower() == "help")
            {
                this._embed.WithTitle($"{prfx}killallseededcrowns");
                this._embed.WithDescription("Removes all automatically seeded crowns from your server.");

                this._embed.AddField("Examples",
                    $"`{prfx}killallseededcrowns`\n" +
                    $"`{prfx}killallseededcrowns confirm`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            if (guild.CrownsDisabled == true)
            {
                await ReplyAsync("Crown functionality has been disabled in this server.");
                this.Context.LogCommandUsed(CommandResponse.Disabled);
                return;
            }

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' or server admins can use this command. ");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var guildCrowns = await this._crownService.GetAllCrownsForGuild(guild.GuildId);
            if (guildCrowns == null || !guildCrowns.Any())
            {
                await ReplyAsync("This server does not have any crowns.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            if (string.IsNullOrWhiteSpace(confirmation) || confirmation.ToLower() != "confirm")
            {
                this._embed.WithDescription($"Are you sure you want to remove all {guildCrowns.Count(c => c.SeededCrown)} automatically seeded crowns from your server?\n\n" +
                                            $"Type `{prfx}killallseededcrowns confirm` to confirm.");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await this._crownService.RemoveAllSeededCrownsFromGuild(guild);

            this._embed.WithDescription("Removed all seeded crowns for your server.");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }
    }
}
