using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Discord.Commands;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.Commands.LastFM
{
    [Name("Crowns")]
    public class CrownCommands : BaseCommandModule
    {
        private readonly AdminService _adminService;
        private readonly CrownService _crownService;
        private readonly GuildService _guildService;
        private readonly LastFmRepository _lastFmRepository;
        private readonly IPrefixService _prefixService;
        private readonly SettingService _settingService;
        private readonly UserService _userService;

        private InteractiveService Interactivity { get; }

        public CrownCommands(CrownService crownService,
            GuildService guildService,
            IPrefixService prefixService,
            UserService userService,
            AdminService adminService,
            LastFmRepository lastFmRepository,
            SettingService settingService,
            InteractiveService interactivity,
            IOptions<BotSettings> botSettings) : base(botSettings)
        {
            this._crownService = crownService;
            this._guildService = guildService;
            this._prefixService = prefixService;
            this._userService = userService;
            this._adminService = adminService;
            this._lastFmRepository = lastFmRepository;
            this._settingService = settingService;
            this.Interactivity = interactivity;
        }


        [Command("crowns", RunMode = RunMode.Async)]
        [Summary("Shows you your crowns for this server.")]
        [Alias("cws")]
        [UsernameSetRequired]
        [GuildOnly]
        [SupportsPagination]
        [RequiresIndex]
        public async Task UserCrownsAsync([Remainder] string extraOptions = null)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

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
                var userSettings = await this._settingService.StringWithDiscordIdForUser(extraOptions);

                if (userSettings != null)
                {
                    contextUser = userSettings;
                    differentUser = true;
                }
            }

            var userCrowns = await this._crownService.GetCrownsForUser(guild, contextUser.UserId);

            var title = differentUser
                ? $"Crowns for {contextUser.UserNameLastFM}, requested by {userTitle}"
                : $"Crowns for {userTitle}";

            if (!userCrowns.Any())
            {
                this._embed.WithDescription($"You or the user you're searching for don't have any crowns yet. \n" +
                                            $"Use `{prfx}whoknows` to start getting crowns!");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
                return;
            }

            try
            {
                var pages = new List<PageBuilder>();

                var crownPages = userCrowns.ChunkBy(10);

                var counter = 1;
                var pageCounter = 1;
                foreach (var crownPage in crownPages)
                {
                    var crownPageString = new StringBuilder();
                    foreach (var userCrown in crownPage)
                    {
                        crownPageString.AppendLine($"{counter}. **{userCrown.ArtistName}** - **{userCrown.CurrentPlaycount}** plays (claimed <t:{((DateTimeOffset)userCrown.Created).ToUnixTimeSeconds()}:R>)");
                        counter++;
                    }

                    var footer = $"Page {pageCounter}/{crownPages.Count} - {userCrowns.Count} total crowns";

                    pages.Add(new PageBuilder()
                        .WithDescription(crownPageString.ToString())
                        .WithTitle(title)
                        .WithFooter(footer));
                    pageCounter++;
                }

                var paginator = StringService.BuildStaticPaginator(pages);

                _ = this.Interactivity.SendPaginatorAsync(
                    paginator,
                    this.Context.Channel,
                    TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds),
                    resetTimeoutOnInput: true);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            this.Context.LogCommandUsed();
        }

        [Command("crown", RunMode = RunMode.Async)]
        [Summary("Shows crown history for the artist you're currently listening to or searching for")]
        [Alias("cw")]
        [UsernameSetRequired]
        [GuildOnly]
        [RequiresIndex]
        public async Task CrownAsync([Remainder] string artistValues = null)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

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
                if (!string.IsNullOrWhiteSpace(contextUser.SessionKeyLastFm))
                {
                    sessionKey = contextUser.SessionKeyLastFm;
                }

                var recentScrobbles = await this._lastFmRepository.GetRecentTracksAsync(contextUser.UserNameLastFM, useCache: true, sessionKey: sessionKey);

                if (await GenericEmbedService.RecentScrobbleCallFailedReply(recentScrobbles, contextUser.UserNameLastFM, this.Context))
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
        [SupportsPagination]
        [RequiresIndex]
        public async Task CrownLeaderboardAsync()
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

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

            var pages = new List<PageBuilder>();

            var title = $"Users with most crowns in {this.Context.Guild.Name}";

            var crownPages = topCrownUsers.ChunkBy(10);

            var counter = 1;
            var pageCounter = 1;
            foreach (var crownPage in crownPages)
            {
                var crownPageString = new StringBuilder();
                foreach (var crownUser in crownPage)
                {
                    var guildUser = guild
                        .GuildUsers
                        .FirstOrDefault(f => f.UserId == crownUser.Key);

                    string name = null;

                    if (guildUser != null)
                    {
                        name = guildUser.UserName;
                    }

                    crownPageString.AppendLine($"{counter}. **{name ?? crownUser.First().User.UserNameLastFM}** - **{crownUser.Count()}** {StringExtensions.GetCrownsString(crownUser.Count())}");
                    counter++;
                }

                var footer = $"Page {pageCounter}/{crownPages.Count} - {guildCrownCount} total active crowns in this server";

                pages.Add(new PageBuilder()
                    .WithDescription(crownPageString.ToString())
                    .WithTitle(title)
                    .WithFooter(footer));
                pageCounter++;
            }

            var paginator = StringService.BuildStaticPaginator(pages);

            _ = this.Interactivity.SendPaginatorAsync(
                paginator,
                this.Context.Channel,
                TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds),
                resetTimeoutOnInput: true);

            this.Context.LogCommandUsed();
        }
    }
}
