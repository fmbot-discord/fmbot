using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Discord;
using Discord.Commands;
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
using FMBot.LastFM.Services;
using Interactivity;
using Interactivity.Pagination;

namespace FMBot.Bot.Commands.LastFM
{
    [Name("Crowns")]
    public class CrownCommands : ModuleBase
    {
        private readonly AdminService _adminService;
        private readonly CrownService _crownService;
        private readonly GuildService _guildService;
        private readonly LastFmService _lastFmService;
        private readonly IPrefixService _prefixService;
        private readonly SettingService _settingService;
        private readonly UserService _userService;

        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedBuilder _embed;
        private readonly EmbedFooterBuilder _embedFooter;

        private InteractivityService Interactivity { get; }

        public CrownCommands(CrownService crownService,
            GuildService guildService,
            IPrefixService prefixService,
            UserService userService,
            AdminService adminService,
            LastFmService lastFmService,
            SettingService settingService,
            InteractivityService interactivity)
        {
            this._crownService = crownService;
            this._guildService = guildService;
            this._prefixService = prefixService;
            this._userService = userService;
            this._adminService = adminService;
            this._lastFmService = lastFmService;
            this._settingService = settingService;
            this.Interactivity = interactivity;

            this._embedAuthor = new EmbedAuthorBuilder();
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
            this._embedFooter = new EmbedFooterBuilder();
        }


        [Command("crowns", RunMode = RunMode.Async)]
        [Summary("Shows you your crowns")]
        [Alias("cws")]
        [UsernameSetRequired]
        [GuildOnly]
        public async Task UserCrownsAsync([Remainder] string extraOptions = null)
        {
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            if (guild == null)
            {
                await ReplyAsync("This server hasn't been stored yet.\n" +
                                 $"Please run `{prfx}index` to store this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (guild.CrownsDisabled == true)
            {
                await ReplyAsync("Crown functionality has been disabled in this server.");
                this.Context.LogCommandUsed(CommandResponse.Disabled);
                return;
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            var differentUser = false;
            if (!string.IsNullOrWhiteSpace(extraOptions))
            {
                var userSettings = await this._settingService.GetUserFromString(extraOptions);

                if (userSettings != null)
                {
                    user = userSettings;
                    differentUser = true;
                }
            }

            var userCrowns = await this._crownService.GetCrownsForUser(guild, user.UserId);

            var title = differentUser
                ? $"Crowns for {user.UserNameLastFM}, requested by {userTitle}"
                : $"Crowns for {userTitle}";

            if (!userCrowns.Any())
            {
                this._embed.WithDescription($"You or the user you're searching for don't have any crowns yet. \n" +
                                            $"Use `{prfx}whoknows` to start getting crowns!");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                return;
            }

            var footer = $"{userCrowns.Count} total crowns";

            try
            {
                var paginationEnabled = false;
                var maxAmount = userCrowns.Count > 15 ? 15 : userCrowns.Count;
                var pages = new List<PageBuilder>();
                var perms = await this._guildService.CheckSufficientPermissionsAsync(this.Context);
                if (perms.ManageMessages && userCrowns.Count > 15)
                {
                    paginationEnabled = true;
                    maxAmount = userCrowns.Count;
                }

                var embedDescription = new StringBuilder();
                for (var index = 0; index < maxAmount; index++)
                {
                    if (paginationEnabled && (index > 0 && index % 10 == 0 || index == maxAmount - 1))
                    {
                        pages.Add(new PageBuilder().WithDescription(embedDescription.ToString()).WithTitle(title).WithFooter(footer));
                        embedDescription = new StringBuilder();
                    }

                    var userCrown = userCrowns[index];

                    var claimTimeDescription = DateTime.UtcNow.AddDays(-3) < userCrown.Created
                        ? StringExtensions.GetTimeAgo(userCrown.Created)
                        : userCrown.Created.Date.ToString("dddd MMM d", CultureInfo.InvariantCulture);

                    embedDescription.AppendLine($"{index + 1}. **{userCrown.ArtistName}** - **{userCrown.CurrentPlaycount}** plays (claimed {claimTimeDescription})");
                }

                if (paginationEnabled)
                {
                    var paginator = new StaticPaginatorBuilder()
                        .WithPages(pages)
                        .WithFooter(PaginatorFooter.PageNumber)
                        .WithEmotes(DiscordConstants.PaginationEmotes)
                        .WithTimoutedEmbed(null)
                        .WithCancelledEmbed(null)
                        .WithDeletion(DeletionOptions.Valid)
                        .Build();

                    _ = this.Interactivity.SendPaginatorAsync(paginator, this.Context.Channel, TimeSpan.FromSeconds(DiscordConstants.PaginationTimeoutInSeconds));
                }
                else
                {
                    footer += "\nWant pagination? Enable the 'Manage Messages' permission for .fmbot.";
                    this._embed.WithTitle(title);
                    this._embed.WithDescription(embedDescription.ToString());
                    this._embed.WithFooter(footer);
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            this.Context.LogCommandUsed();
        }

        [Command("crown", RunMode = RunMode.Async)]
        [Summary("Shows crown info about current artist")]
        [Alias("cw")]
        [UsernameSetRequired]
        [GuildOnly]
        public async Task CrownAsync([Remainder] string artistValues = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            if (guild == null)
            {
                await ReplyAsync("This server hasn't been stored yet.\n" +
                                 $"Please run `{prfx}index` to store this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (guild.CrownsDisabled == true)
            {
                await ReplyAsync("Crown functionality has been disabled in this server.");
                this.Context.LogCommandUsed(CommandResponse.Disabled);
                return;
            }

            var artist = artistValues;

            if (string.IsNullOrWhiteSpace(artistValues))
            {
                string sessionKey = null;
                if (!string.IsNullOrWhiteSpace(userSettings.SessionKeyLastFm))
                {
                    sessionKey = userSettings.SessionKeyLastFm;
                }

                var recentScrobbles = await this._lastFmService.GetRecentTracksAsync(userSettings.UserNameLastFM, useCache: true, sessionKey: sessionKey);

                if (await ErrorService.RecentScrobbleCallFailedReply(recentScrobbles, userSettings.UserNameLastFM, this.Context))
                {
                    return;
                }

                var currentTrack = recentScrobbles.Content.RecentTracks[0];
                artist = currentTrack.ArtistName;
            }

            var artistCrowns = await this._crownService.GetCrownsForArtist(guild, artist);

            if (!artistCrowns.Any(a => a.Active))
            {
                this._embed.WithDescription($"No known crowns for the artist `{artist}`. \n" +
                                            $"Be the first to claim the crown with `{prfx}whoknows`!");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                return;
            }

            var currentCrown = artistCrowns
                .Where(w => w.Active)
                .OrderByDescending(o => o.CurrentPlaycount)
                .First();

            var name = await this._guildService.GetUserFromGuild(guild, currentCrown.UserId);

            var artistUrl =
                $"{Constants.LastFMUserUrl}{currentCrown.User.UserNameLastFM}/library/music/{HttpUtility.UrlEncode(artist)}";
            this._embed.AddField("Current crown holder",
                $"**[{name?.UserName ?? currentCrown.User.UserNameLastFM}]({artistUrl})** - " +
                $"Since **{currentCrown.Created:MMM dd yyyy}** - " +
                $"`{currentCrown.StartPlaycount}` to `{currentCrown.CurrentPlaycount}` plays");

            var lastCrownCreateDate = currentCrown.Created;
            if (artistCrowns.Count > 1)
            {
                var crownHistory = new StringBuilder();

                foreach (var artistCrown in artistCrowns.Take(10).Where(w => !w.Active))
                {
                    var crownUsername = await this._guildService.GetUserFromGuild(guild, artistCrown.UserId);

                    var toStringFormat = lastCrownCreateDate.Year != artistCrown.Created.Year ? "MMM dd yyyy" : "MMM dd";

                    crownHistory.AppendLine($"**{crownUsername?.UserName ?? artistCrown.User.UserNameLastFM}** - " +
                                            $"**{artistCrown.Created.ToString(toStringFormat)}** to **{lastCrownCreateDate.ToString(toStringFormat)}** - " +
                                            $"`{artistCrown.StartPlaycount}` to `{artistCrown.CurrentPlaycount}` plays");
                    lastCrownCreateDate = artistCrown.Created;

                }

                if (artistCrowns.Count(w => !w.Active) > 10)
                {
                    crownHistory.AppendLine($"*{artistCrowns.Count(w => !w.Active) - 11} more steals hidden..*");

                    var firstCrown = artistCrowns.OrderBy(o => o.Created).First();
                    var crownUsername = await this._guildService.GetUserFromGuild(guild, firstCrown.UserId);
                    this._embed.AddField("First crownholder",
                         $"**{crownUsername?.UserName ?? firstCrown.User.UserNameLastFM}** - " +
                         $"**{firstCrown.Created:MMM dd yyyy}** to **{lastCrownCreateDate:MMM dd yyyy}** - " +
                         $"`{firstCrown.StartPlaycount}` to `{firstCrown.CurrentPlaycount}` plays");
                }

                this._embed.AddField("Crown history", crownHistory.ToString());
            }

            this._embed.WithTitle($"Crown info for {currentCrown.ArtistName}");

            var embedDescription = new StringBuilder();

            this._embed.WithDescription(embedDescription.ToString());

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("crownleaderboard", RunMode = RunMode.Async)]
        [Summary("Shows users with the most crowns in your server")]
        [Alias("cwlb", "crownlb", "cwleaderboard", "crown leaderboard")]
        [UsernameSetRequired]
        [GuildOnly]
        public async Task CrownLeaderboardAsync()
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            if (guild == null)
            {
                await ReplyAsync("This server hasn't been stored yet.\n" +
                                 $"Please run `{prfx}index` to store this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (guild.CrownsDisabled == true)
            {
                await ReplyAsync("Crown functionality has been disabled in this server.");
                this.Context.LogCommandUsed(CommandResponse.Disabled);
                return;
            }

            var topCrownUsers = await this._crownService.GetTopCrownUsersForGuild(guild);
            var guildCrownCount = await this._crownService.GetTotalCrownCountForGuild(guild);

            if (!topCrownUsers.Any())
            {
                this._embed.WithDescription($"No top crown users in this server. Use whoknows to start getting crowns!");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                return;
            }

            var paginationEnabled = false;
            var maxAmount = topCrownUsers.Count > 15 ? 15 : topCrownUsers.Count;
            var pages = new List<PageBuilder>();
            var perms = await this._guildService.CheckSufficientPermissionsAsync(this.Context);
            if (perms.ManageMessages && topCrownUsers.Count > 15)
            {
                paginationEnabled = true;
                maxAmount = topCrownUsers.Count;
            }

            var title = $"Users with most crowns in {this.Context.Guild.Name}";
            var embedDescription = new StringBuilder();
            var footer = $"{guildCrownCount} total active crowns in this server";

            if (topCrownUsers.Count < 15)
            {
                paginationEnabled = false;
            }

            for (var index = 0; index < maxAmount; index++)
            {
                if (paginationEnabled && (index > 0 && index % 10 == 0 || index == maxAmount - 1))
                {
                    pages.Add(new PageBuilder().WithDescription(embedDescription.ToString()).WithTitle(title).WithFooter(footer));
                    embedDescription = new StringBuilder();
                }

                var crownUser = topCrownUsers[index];

                var guildUser = guild
                    .GuildUsers
                    .FirstOrDefault(f => f.UserId == crownUser.Key);

                string name = null;

                if (guildUser != null)
                {
                    name = guildUser.UserName;
                }

                embedDescription.AppendLine($"{index + 1}. **{name ?? crownUser.First().User.UserNameLastFM}** - **{crownUser.Count()}** {StringExtensions.GetCrownsString(crownUser.Count())}");
            }

            if (paginationEnabled)
            {
                var paginator = new StaticPaginatorBuilder()
                    .WithPages(pages)
                    .WithFooter(PaginatorFooter.PageNumber)
                    .WithEmotes(DiscordConstants.PaginationEmotes)
                    .WithTimoutedEmbed(null)
                    .WithCancelledEmbed(null)
                    .WithDeletion(DeletionOptions.Valid)
                    .Build();

                _ = this.Interactivity.SendPaginatorAsync(paginator, this.Context.Channel, TimeSpan.FromSeconds(DiscordConstants.PaginationTimeoutInSeconds));
            }
            else
            {
                this._embed.WithTitle(title);
                this._embed.WithDescription(embedDescription.ToString());
                this._embed.WithFooter(footer);
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            }

            this.Context.LogCommandUsed();
        }

        [Command("killcrown", RunMode = RunMode.Async)]
        [Summary("Removes all crowns from a specific artist")]
        [Alias("kcw", "kcrown", "killcw", "kill crown", "crown kill")]
        [GuildOnly]
        public async Task KillCrownAsync([Remainder] string killCrownValues = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
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

            if (guild == null)
            {
                await ReplyAsync("This server hasn't been stored yet.\n" +
                                 $"Please run `{prfx}index` to store this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
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
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
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
        [Summary("Seeds crowns for a server")]
        [Alias("crownseed", "seedcrowns")]
        [GuildOnly]
        public async Task SeedCrownsAsync([Remainder] string helpValues = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
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

            if (guild == null)
            {
                await ReplyAsync("This server hasn't been stored yet.\n" +
                                 $"Please run `{prfx}index` to store this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
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
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            this._embed.WithDescription($"<a:loading:749715170682470461> Seeding crowns for your server...");
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
        [Summary("Removes all crowns")]
        [Alias("removeallcrowns")]
        [GuildOnly]
        public async Task KillAllCrownsAsync([Remainder] string confirmation = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
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

            if (guild == null)
            {
                await ReplyAsync("This server hasn't been stored yet.\n" +
                                 $"Please run `{prfx}index` to store this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
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
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
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
        [Summary("Removes all seeded crowns")]
        [Alias("removeallseededcrowns")]
        [GuildOnly]
        public async Task KillAllSeededCrownsAsync([Remainder] string confirmation = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
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

            if (guild == null)
            {
                await ReplyAsync("This server hasn't been stored yet.\n" +
                                 $"Please run `{prfx}index` to store this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
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
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
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
