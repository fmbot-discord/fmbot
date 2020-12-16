using System.Linq;
using System.Text;
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
using FMBot.Domain.Models;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;

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

        public CrownCommands(CrownService crownService,
            GuildService guildService,
            IPrefixService prefixService,
            UserService userService,
            AdminService adminService,
            LastFmService lastFmService,
            SettingService settingService)
        {
            this._crownService = crownService;
            this._guildService = guildService;
            this._prefixService = prefixService;
            this._userService = userService;
            this._adminService = adminService;
            this._lastFmService = lastFmService;
            this._settingService = settingService;

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
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

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

            this._embed.WithTitle(differentUser
                ? $"Crowns for {user.UserNameLastFM}, requested by {userTitle}"
                : $"Crowns for {userTitle}");

            if (!userCrowns.Any())
            {
                this._embed.WithDescription($"You or the user you're searching for don't have any crowns yet. \n" +
                                            $"Use `{prfx}whoknows` to start getting crowns!");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                return;
            }

            var embedDescription = new StringBuilder();
            for (var index = 0; index < userCrowns.Count && index < 15; index++)
            {
                var userCrown = userCrowns[index];
                embedDescription.AppendLine($"{index + 1}. **{userCrown.ArtistName}** - **{userCrown.CurrentPlaycount}** plays (claimed {StringExtensions.GetTimeAgo(userCrown.Created)})");
            }

            this._embed.WithDescription(embedDescription.ToString());

            this._embed.WithFooter($"{userCrowns.Count} total crowns");

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
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
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

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
                var recentScrobbles = await this._lastFmService.GetRecentTracksAsync(userSettings.UserNameLastFM, useCache: true);

                if (!recentScrobbles.Success || recentScrobbles.Content == null)
                {
                    this._embed.ErrorResponse(recentScrobbles.Error, recentScrobbles.Message, this.Context);
                    this.Context.LogCommandUsed(CommandResponse.LastFmError);
                    await ReplyAsync("", false, this._embed.Build());
                    return;
                }

                if (!recentScrobbles.Content.RecentTracks.Track.Any())
                {
                    this._embed.NoScrobblesFoundErrorResponse(userSettings.UserNameLastFM);
                    this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                    await ReplyAsync("", false, this._embed.Build());
                    return;
                }

                var currentTrack = recentScrobbles.Content.RecentTracks.Track[0];
                artist = currentTrack.Artist.Text;
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

            this._embed.AddField("Current crown holder",
                $"**{name?.UserName ?? currentCrown.User.UserNameLastFM}** - **{currentCrown.CurrentPlaycount}** plays");

            var lastCrownCreateDate = currentCrown.Created;
            if (artistCrowns.Count > 1)
            {
                var crownHistory = new StringBuilder();
                foreach (var artistCrown in artistCrowns.Where(w => !w.Active))
                {
                    var crownUsername = await this._guildService.GetUserFromGuild(guild, artistCrown.UserId);

                    crownHistory.AppendLine($"**{crownUsername?.UserName ?? artistCrown.User.UserNameLastFM}** - " +
                                            $"**{artistCrown.Created:MMMM dd yyyy}** to **{lastCrownCreateDate:MMMM dd yyyy}** - " +
                                            $"**{artistCrown.CurrentPlaycount}** to **{artistCrown.StartPlaycount}** plays");
                    lastCrownCreateDate = artistCrown.Created;
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
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

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

            var embedDescription = new StringBuilder();

            for (var index = 0; index < topCrownUsers.Count && index < 15; index++)
            {
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

            this._embed.WithTitle($"Users with most crowns in {this.Context.Guild.Name}");

            this._embed.WithDescription(embedDescription.ToString());

            this._embed.WithFooter($"{guildCrownCount} total active crowns in this server");

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("killcrown", RunMode = RunMode.Async)]
        [Summary("Removes all crowns from a specific artist")]
        [Alias("kcw", "kcrown", "killcw", "kill crown", "crown kill")]
        [GuildOnly]
        public async Task KillCrownAsync([Remainder] string artistValues = null)
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

            if (string.IsNullOrWhiteSpace(artistValues))
            {
                await ReplyAsync("Please enter the artist you want to remove all crowns for.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var artistCrowns = await this._crownService.GetCrownsForArtist(guild, artistValues);

            if (!artistCrowns.Any())
            {
                this._embed.WithDescription($"No crowns found for the artist `{artistValues}`");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                return;
            }

            await this._crownService.RemoveCrowns(artistCrowns);

            this._embed.WithDescription($"All crowns for `{artistValues}` have been removed.");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }
    }
}
