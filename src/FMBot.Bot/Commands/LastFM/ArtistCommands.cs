using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using IF.Lastfm.Core.Api.Enums;

namespace FMBot.Bot.Commands.LastFM
{
    public class ArtistCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;
        private readonly GuildService _guildService;
        private readonly ArtistsService _artistsService;
        private readonly WhoKnowsArtistService _whoKnowArtistService;
        private readonly LastFMService _lastFmService;
        private readonly SpotifyService _spotifyService = new SpotifyService();
        private readonly IPrefixService _prefixService;
        private readonly ILastfmApi _lastfmApi;
        private readonly Logger.Logger _logger;

        private readonly UserService _userService;

        public ArtistCommands(Logger.Logger logger,
            ILastfmApi lastfmApi,
            IPrefixService prefixService,
            ArtistsService artistsService,
            WhoKnowsArtistService whoKnowsArtistService,
            GuildService guildService,
            UserService userService,
            LastFMService lastFmService)
        {
            this._logger = logger;
            this._lastfmApi = lastfmApi;
            this._lastFmService = lastFmService;
            this._prefixService = prefixService;
            this._artistsService = artistsService;
            this._whoKnowArtistService = whoKnowsArtistService;
            this._guildService = guildService;
            this._userService = userService;
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("artist", RunMode = RunMode.Async)]
        [Summary("Displays artist info and stats.")]
        [Alias("a")]
        [UsernameSetRequired]
        public async Task ArtistAsync(params string[] artistValues)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.Bot.Prefix;

            var artist = await GetArtistOrHelp(artistValues, userSettings, "artist", prfx);
            if (artist == null)
            {
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var queryParams = new Dictionary<string, string>
            {
                {"artist", artist },
                {"username", userSettings.UserNameLastFM },
                {"autocorrect", "1"}
            };

            var artistCall = await this._lastfmApi.CallApiAsync<ArtistResponse>(queryParams, Call.ArtistInfo);

            if (!artistCall.Success)
            {
                this._embed.ErrorResponse(artistCall.Error.Value, artistCall.Message, this.Context, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandWithLastFmError(artistCall.Error);
                return;
            }

            var spotifyImageSearchTask = this._spotifyService.GetOrStoreArtistImageAsync(artistCall.Content, artist);

            var artistInfo = artistCall.Content.Artist;
            var spotifyImage = await spotifyImageSearchTask;

            if (spotifyImage != null)
            {
                this._embed.WithThumbnailUrl(spotifyImage);
                this._embedFooter.WithText("Image source: Spotify");
                this._embed.WithFooter(this._embedFooter);
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            this._embedAuthor.WithName($"Artist info about {artistInfo.Name} for {userTitle}");
            this._embedAuthor.WithUrl(artistInfo.Url);
            this._embed.WithAuthor(this._embedAuthor);

            string globalStats = "";
            globalStats += $"`{artistInfo.Stats.Listeners}` listeners";
            globalStats += $"\n`{artistInfo.Stats.Playcount}` global plays";
            if (artistInfo.Stats.Userplaycount.HasValue)
            {
                globalStats += $"\n`{artistInfo.Stats.Userplaycount}` plays by you";
            }
            this._embed.AddField("Global stats", globalStats, true);

            if (!this._guildService.CheckIfDM(this.Context))
            {
                string serverStats = "";
                var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

                if (guild.LastIndexed != null)
                {
                    var guildUsers = guild.GuildUsers.Select(s => s.User).ToList();
                    var serverListeners = await this._whoKnowArtistService.GetArtistListenerCountForServer(guildUsers, artistInfo.Name);
                    var serverPlaycount = await this._whoKnowArtistService.GetArtistPlayCountForServer(guildUsers, artistInfo.Name);
                    var avgServerListenerPlaycount = await this._whoKnowArtistService.GetArtistAverageListenerPlaycountForServer(guildUsers, artistInfo.Name);

                    serverStats += $"`{serverListeners}` listeners";
                    serverStats += $"\n`{serverPlaycount}` total plays";
                    serverStats += $"\n`{(int)avgServerListenerPlaycount}` median plays";
                }
                else
                {
                    serverStats += "Run `.fmindex` to get server stats";
                }

                this._embed.AddField("Server stats", serverStats, true);
            }

            if (!string.IsNullOrWhiteSpace(artistInfo.Bio.Content))
            {
                var linktext = $"<a href=\"{artistInfo.Url}\">Read more on Last.fm</a>";
                this._embed.AddField("Summary", artistInfo.Bio.Summary.Replace(linktext, ""));
            }

            if (artistInfo.Tags.Tag.Any())
            {
                var tags = this._lastFmService.TagsToLinkedString(artistInfo.Tags);

                this._embed.AddField("Tags", tags);
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("artistplays", RunMode = RunMode.Async)]
        [Summary("Displays artist playcount.")]
        [Alias("ap")]
        [UsernameSetRequired]
        public async Task ArtistPlaysAsync(params string[] artistValues)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.Bot.Prefix;

            var artist = await GetArtistOrHelp(artistValues, userSettings, "artistplays", prfx);
            if (artist == null)
            {
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var queryParams = new Dictionary<string, string>
            {
                {"artist", artist },
                {"username", userSettings.UserNameLastFM },
                {"autocorrect", "1"}
            };
            var artistCall = await this._lastfmApi.CallApiAsync<ArtistResponse>(queryParams, Call.ArtistInfo);
            if (!artistCall.Success)
            {
                this._embed.ErrorResponse(artistCall.Error.Value, artistCall.Message, this.Context, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandWithLastFmError(artistCall.Error);
                return;
            }

            var artistInfo = artistCall.Content.Artist;

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            var playstring = artistInfo.Stats.Userplaycount == 1 ? "play" : "plays";
            this._embedAuthor.WithName($"{userTitle} has {artistInfo.Stats.Userplaycount} {playstring} for {artistInfo.Name}");
            this._embedAuthor.WithUrl(artistInfo.Url);
            this._embed.WithAuthor(this._embedAuthor);

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("topartists", RunMode = RunMode.Async)]
        [Summary("Displays top artists.")]
        [Alias("al", "as", "ta", "artistlist", "artists", "artistslist")]
        [UsernameSetRequired]
        public async Task TopArtistsAsync(params string[] extraOptions)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.Bot.Prefix;

            if (extraOptions.Any() && extraOptions.First() == "help")
            {
                this._embed.WithTitle($"{prfx}topartists options");
                this._embed.WithDescription($"- `{Constants.CompactTimePeriodList}`\n" +
                                            $"- `number of artists (max 16)`\n" +
                                            $"- `user mention/id`");

                this._embed.AddField("Example",
                    $"`{prfx}topartists @drasil alltime 11`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var settings = LastFMService.StringOptionsToSettings(extraOptions);

            try
            {
                var lastFMUserName = userSettings.UserNameLastFM;
                var self = true;

                if (settings.OtherDiscordUserId.HasValue)
                {
                    var alternativeLastFmUserName = await FindUserFromId(settings.OtherDiscordUserId.Value);
                    if (!string.IsNullOrEmpty(alternativeLastFmUserName))
                    {
                        lastFMUserName = alternativeLastFmUserName;
                        self = false;
                    }
                }

                var artists = await this._lastFmService.GetTopArtistsAsync(lastFMUserName, settings.LastStatsTimeSpan, settings.Amount);

                if (artists?.Any() != true)
                {
                    this._embed.NoScrobblesFoundErrorResponse(artists.Status, prfx);
                    this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                    await ReplyAsync("", false, this._embed.Build());
                    return;
                }

                string userTitle;
                if (self)
                {
                    userTitle = await this._userService.GetUserTitleAsync(this.Context);
                }
                else
                {
                    userTitle =
                        $"{lastFMUserName}, requested by {await this._userService.GetUserTitleAsync(this.Context)}";
                }

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                var artistsString = settings.Amount == 1 ? "artist" : "artists";
                this._embedAuthor.WithName($"Top {settings.Amount} {settings.Description.ToLower()} {artistsString} for {userTitle}");
                this._embedAuthor.WithUrl($"{Constants.LastFMUserUrl}{lastFMUserName}/library/artists?date_preset={settings.UrlParameter}");
                this._embed.WithAuthor(this._embedAuthor);

                var description = "";
                for (var i = 0; i < artists.Count(); i++)
                {
                    var artist = artists.Content[i];

                    description += $"{i + 1}. [{artist.Name}]({artist.Url}) ({artist.PlayCount} plays) \n";
                }

                this._embed.WithDescription(description);

                var userInfo = await this._lastFmService.GetUserInfoAsync(lastFMUserName);

                this._embedFooter.WithText(lastFMUserName + "'s total scrobbles: " +
                                           userInfo.Content.Playcount.ToString("N0"));
                this._embed.WithFooter(this._embedFooter);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to show Last.FM info due to an internal error.");
            }
        }

        [Command("taste", RunMode = RunMode.Async)]
        [Summary("Compare taste to other user.")]
        [UsernameSetRequired]
        [Alias("t")]
        public async Task TasteAsync(string user = null, params string[] extraOptions)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.Bot.Prefix;

            if (user == "help")
            {
                await ReplyAsync(
                    $"Usage: `{prfx}taste 'last.fm username/ discord mention' '{Constants.CompactTimePeriodList}' 'table/embed'`");
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var timeType = LastFMService.StringOptionsToSettings(
                extraOptions,
                LastStatsTimeSpan.Overall,
                ChartTimePeriod.AllTime,
                "ALL",
                "overall");

            var tasteSettings = new TasteSettings
            {
                ChartTimePeriod = timeType.ChartTimePeriod
            };

            tasteSettings = this._artistsService.SetTasteSettings(tasteSettings, extraOptions);

            try
            {
                var ownLastFmUsername = userSettings.UserNameLastFM;
                string lastfmToCompare = null;

                if (user != null)
                {
                    var alternativeLastFmUserName = await FindUser(user);
                    if (!string.IsNullOrEmpty(alternativeLastFmUserName))
                    {
                        lastfmToCompare = alternativeLastFmUserName;
                    }
                }

                if (lastfmToCompare == null)
                {
                    await ReplyAsync(
                        $"Please enter a valid user to compare your top artists to. \n" +
                        $"Example: `{prfx}taste lastfmusername` or `{prfx}taste @user`");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }
                if (lastfmToCompare.ToLower() == userSettings.UserNameLastFM.ToLower())
                {
                    await ReplyAsync(
                        $"You can't compare your own taste with yourself. For viewing your top artists, use `fmtopartists`\n" +
                        $"Please enter a different last.fm username or mention another user.");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                tasteSettings.OtherUserLastFmUsername = lastfmToCompare;

                var ownArtistsTask = this._lastFmService.GetTopArtistsAsync(ownLastFmUsername, timeType.LastStatsTimeSpan, 1000);
                var otherArtistsTask = this._lastFmService.GetTopArtistsAsync(lastfmToCompare, timeType.LastStatsTimeSpan, 1000);

                Task.WaitAll(ownArtistsTask, otherArtistsTask);

                var ownArtists = await ownArtistsTask;
                var otherArtists = await otherArtistsTask;

                if (ownArtists?.Any() != true || otherArtists?.Any() != true)
                {
                    await ReplyAsync(
                        $"You or the other user don't have any artist plays in the selected time period.");
                    this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                    return;
                }

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                this._embedAuthor.WithName($"Top artist comparison - {ownLastFmUsername} vs {lastfmToCompare}");
                this._embedAuthor.WithUrl($"{Constants.LastFMUserUrl}{lastfmToCompare}/library/artists?date_preset={timeType.UrlParameter}");
                this._embed.WithAuthor(this._embedAuthor);

                int amount = 14;
                if (tasteSettings.TasteType == TasteType.FullEmbed)
                {
                    var taste = await this._artistsService.GetEmbedTasteAsync(ownArtists, otherArtists, amount, timeType.ChartTimePeriod);

                    this._embed.WithDescription(taste.Description);
                    this._embed.AddField("Artist", taste.LeftDescription, true);
                    this._embed.AddField("Plays", taste.RightDescription, true);
                }
                else
                {
                    var taste = await this._artistsService.GetTableTasteAsync(ownArtists, otherArtists, amount, timeType.ChartTimePeriod, ownLastFmUsername, lastfmToCompare);

                    this._embed.WithDescription(taste);
                }

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to show Last.FM info due to an internal error.");
            }
        }

        [Command("whoknows", RunMode = RunMode.Async)]
        [Summary("Shows what other users listen to the same artist in your server")]
        [Alias("w", "wk")]
        [UsernameSetRequired]
        public async Task WhoKnowsAsync(params string[] artistValues)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("This command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.Bot.Prefix;

            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);

            if (lastIndex == null)
            {
                await ReplyAsync("This server hasn't been indexed yet.\n" +
                                 $"Please run `{prfx}index` to index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (lastIndex < DateTime.UtcNow.AddDays(-50))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 50 days ago.\n" +
                                 $"Please run `{prfx}index` to re-index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            var guildTask = this._guildService.GetGuildAsync(this.Context.Guild.Id);

            _ = this.Context.Channel.TriggerTypingAsync();

            var artistQuery = await GetArtistOrHelp(artistValues, userSettings, "whoknows", prfx);
            if (artistQuery == null)
            {
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var queryParams = new Dictionary<string, string>
            {
                {"artist", artistQuery },
                {"username", userSettings.UserNameLastFM },
                {"autocorrect", "1"}
            };

            try
            {
                var artistCall = await this._lastfmApi.CallApiAsync<ArtistResponse>(queryParams, Call.ArtistInfo);

                if (!artistCall.Success)
                {
                    this._embed.ErrorResponse(artistCall.Error.Value, artistCall.Message, this.Context, this._logger);
                    await ReplyAsync("", false, this._embed.Build());
                    this.Context.LogCommandWithLastFmError(artistCall.Error);
                    return;
                }

                var spotifyArtistResultsTask = this._spotifyService.GetOrStoreArtistImageAsync(artistCall.Content, artistQuery);

                var guild = await guildTask;
                var users = guild.GuildUsers.Select(s => s.User).ToList();

                var artist = artistCall.Content;

                var usersWithArtist = await this._whoKnowArtistService.GetIndexedUsersForArtist(this.Context, users, artist.Artist.Name);

                Statistics.LastfmApiCalls.Inc();

                if (artist.Artist.Stats.Userplaycount != 0)
                {
                    var guildUser = await this.Context.Guild.GetUserAsync(this.Context.User.Id);
                    usersWithArtist = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithArtist, userSettings, guildUser, artist.Artist.Name, artist.Artist.Stats.Userplaycount);
                }

                var serverUsers = WhoKnowsService.WhoKnowsListToString(usersWithArtist);
                if (usersWithArtist.Count == 0)
                {
                    serverUsers = "Nobody in this server (not even you) has listened to this artist.";
                }

                this._embed.WithDescription(serverUsers);

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);
                var footer = $"WhoKnows artist requested by {userTitle}";

                var rnd = new Random();
                if (rnd.Next(0, 4) == 1 && lastIndex < DateTime.UtcNow.AddDays(-3))
                {
                    footer += $"\nMissing members? Update with {prfx}index";
                }

                if (guild.GuildUsers.Count < 400)
                {
                    var serverListeners = await this._whoKnowArtistService.GetArtistListenerCountForServer(users, artist.Artist.Name);
                    var serverPlaycount = await this._whoKnowArtistService.GetArtistPlayCountForServer(users, artist.Artist.Name);
                    var avgServerListenerPlaycount = await this._whoKnowArtistService.GetArtistAverageListenerPlaycountForServer(users, artist.Artist.Name);

                    footer += $"\n{serverListeners} listeners - ";
                    footer += $"{serverPlaycount} total plays - ";
                    footer += $"{(int)avgServerListenerPlaycount} median plays";
                }
                else if (guild.GuildUsers.Count < 450)
                {
                    footer += $"\nView server artist averages in `{prfx}artist`";
                }

                this._embed.WithTitle($"Who knows {artist.Artist.Name} in {this.Context.Guild.Name}");

                if (Uri.IsWellFormedUriString(artist.Artist.Url, UriKind.Absolute))
                {
                    this._embed.WithUrl(artist.Artist.Url);
                }

                this._embedFooter.WithText(footer);
                this._embed.WithFooter(this._embedFooter);

                var spotifyImage = await spotifyArtistResultsTask;
                if (spotifyImage != null)
                {
                    this._embed.WithThumbnailUrl(spotifyImage);
                }

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Something went wrong while using whoknows. Please let us know as this feature is in beta.");
            }
        }

        [Command("serverartists", RunMode = RunMode.Async)]
        [Summary("Shows top artists for your server")]
        [Alias("sa")]
        public async Task ServerArtistsAsync(params string[] artistValues)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("This command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.Bot.Prefix;
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            if (guild.LastIndexed == null)
            {
                await ReplyAsync("This server hasn't been indexed yet.\n" +
                                 $"Please run `{prfx}index` to index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (guild.LastIndexed < DateTime.UtcNow.AddDays(-60))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 60 days ago.\n" +
                                 $"Please run `{prfx}index` to re-index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            try
            {
                var topGuildArtists = await this._whoKnowArtistService.GetTopArtistsForGuild(guild.GuildUsers.Select(s => s.User).ToList());

                var description = "";
                for (var i = 0; i < topGuildArtists.Count(); i++)
                {
                    var artist = topGuildArtists[i];

                    description += $"{i + 1}. {artist.ArtistName} - **{artist.Playcount}** plays - **{artist.ListenerCount}** listeners\n";
                }

                this._embed.WithDescription(description);

                var footer = "";

                if (guild.LastIndexed < DateTime.UtcNow.AddDays(-3))
                {
                    footer += $"Missing members? Update with .fmindex";
                }

                footer += "\nView specific artist info with .fmartist";

                this._embed.WithTitle($"Top alltime artists in {this.Context.Guild.Name}");

                this._embedFooter.WithText(footer);
                this._embed.WithFooter(this._embedFooter);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Something went wrong while using fmserverartists. Please let us know as this feature is in beta.");
            }
        }

        private async Task<string> GetArtistOrHelp(string[] artistValues, User userSettings, string command, string prfx)
        {
            string artist;
            if (artistValues.Length > 0)
            {
                if (artistValues.First() == "help")
                {
                    await ReplyAsync(
                        $"Usage: `.fm{command} 'name'`\n" +
                        "If you don't enter any artists name, it will get the info from the artist you're currently listening to.");
                    return null;
                }

                artist = string.Join(" ", artistValues);
            }
            else
            {
                var track = await this._lastFmService.GetRecentScrobblesAsync(userSettings.UserNameLastFM, 1);

                if (track == null)
                {
                    this._embed.NoScrobblesFoundErrorResponse(track.Status, prfx);
                    this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                    await ReplyAsync("", false, this._embed.Build());
                    return null;
                }

                artist = track.Content.First().ArtistName;
            }

            return artist;
        }

        private async Task<string> FindUser(string user)
        {
            if (await this._lastFmService.LastFMUserExistsAsync(user))
            {
                return user;
            }

            if (!this._guildService.CheckIfDM(this.Context))
            {
                var guildUser = await this._guildService.FindUserFromGuildAsync(this.Context, user);

                if (guildUser != null)
                {
                    var guildUserLastFm = await this._userService.GetUserSettingsAsync(guildUser);

                    return guildUserLastFm?.UserNameLastFM;
                }
            }

            return null;
        }

        private async Task<string> FindUserFromId(ulong userId)
        {
            if (!this._guildService.CheckIfDM(this.Context))
            {
                var guildUser = await this._guildService.FindUserFromGuildAsync(this.Context, userId);

                if (guildUser != null)
                {
                    var guildUserLastFm = await this._userService.GetUserSettingsAsync(guildUser);

                    return guildUserLastFm?.UserNameLastFM;
                }
            }

            return null;
        }
    }
}
