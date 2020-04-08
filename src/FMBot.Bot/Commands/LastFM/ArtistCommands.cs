using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.LastFM.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using SpotifyAPI.Web.Enums;

namespace FMBot.Bot.Commands.LastFM
{
    public class ArtistCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;
        private readonly GuildService _guildService = new GuildService();
        private readonly IndexService _indexService;
        private readonly ArtistsService _artistsService = new ArtistsService();
        private readonly LastFMService _lastFmService = new LastFMService();
        private readonly SpotifyService _spotifyService = new SpotifyService();
        private readonly IPrefixService _prefixService;
        private readonly ILastfmApi _lastfmApi;
        private readonly Logger.Logger _logger;

        private readonly UserService _userService = new UserService();

        public ArtistCommands(Logger.Logger logger, IndexService indexService, ILastfmApi lastfmApi, IPrefixService prefixService)
        {
            this._logger = logger;
            this._indexService = indexService;
            this._lastfmApi = lastfmApi;
            this._prefixService = prefixService;
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("artist", RunMode = RunMode.Async)]
        [Summary("Displays current artist.")]
        [Alias("a")]
        public async Task ArtistsAsync(params string[] artistValues)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.CommandPrefix;

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

            var artist = await GetArtistOrHelp(artistValues, userSettings, "fmartist");
            if (artist == null)
            {
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var queryParams = new Dictionary<string, string>
            {
                {"artist", artist },
                {"username", userSettings.UserNameLastFM }
            };

            var artistCall = await this._lastfmApi.CallApiAsync<ArtistResponse>(queryParams, Call.ArtistInfo);
            var spotifyArtistResultsTask = this._spotifyService.GetSearchResultAsync(artist, SearchType.Artist);

            if (!artistCall.Success)
            {
                this._embed.ErrorResponse(artistCall.Error.Value, artistCall.Message, this.Context, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }

            var artistInfo = artistCall.Content.Artist;
            var spotifyResults = await spotifyArtistResultsTask;

            if (spotifyResults.Artists?.Items?.Any() == true)
            {
                var spotifyArtist = spotifyResults.Artists.Items.FirstOrDefault();
                if (spotifyArtist.Images.Any() && spotifyArtist.Name.ToLower() == artistInfo.Name.ToLower())
                {
                    this._embed.WithThumbnailUrl(spotifyArtist.Images.OrderByDescending(o => o.Height).FirstOrDefault().Url);
                    this._embedFooter.WithText("Image source: Spotify");
                    this._embed.WithFooter(this._embedFooter);
                }
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
                var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);
                if (lastIndex != null)
                {
                    var guildUsers = await this.Context.Guild.GetUsersAsync();
                    var serverListeners = await this._artistsService.GetArtistListenerCountForServer(guildUsers, artistInfo.Name);
                    var serverPlaycount = await this._artistsService.GetArtistPlayCountForServer(guildUsers, artistInfo.Name);
                    var avgServerListenerPlaycount = await this._artistsService.GetArtistAverageListenerPlaycountForServer(guildUsers, artistInfo.Name);

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
            this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                this.Context.Message.Content);
        }

        [Command("artists", RunMode = RunMode.Async)]
        [Summary("Displays top artists.")]
        [Alias("al", "as", "artistlist", "artistslist")]
        public async Task ArtistsAsync(string time = "weekly", int num = 10, string user = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.CommandPrefix;

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

            if (time == "help")
            {
                await ReplyAsync(
                    "Usage: `.fmartists 'weekly/monthly/yearly/alltime' 'number of artists (max 10)' 'lastfm username/discord user'` \n" +
                    "You can set your default user and your display mode through the `.fmset 'username' 'embedfull/embedmini/textfull/textmini'` command.");
                return;
            }

            if (!Enum.TryParse(time, true, out ChartTimePeriod timePeriod))
            {
                await ReplyAsync("Invalid time period. Please use 'weekly', 'monthly', 'yearly', or 'alltime'. \n" +
                                 "Usage: `.fmartists 'weekly/monthly/yearly/alltime' 'number of artists (max 10)' 'lastfm username/discord user'`");
                return;
            }

            if (num > 20)
            {
                num = 20;
            }

            if (num < 1)
            {
                num = 1;
            }

            var timeSpan = this._lastFmService.GetLastStatsTimeSpan(timePeriod);

            try
            {
                var lastFMUserName = userSettings.UserNameLastFM;
                var self = true;

                if (user != null)
                {
                    var alternativeLastFmUserName = await FindUser(user);
                    if (!string.IsNullOrEmpty(alternativeLastFmUserName))
                    {
                        lastFMUserName = alternativeLastFmUserName;
                        self = false;
                    }
                }

                var artists = await this._lastFmService.GetTopArtistsAsync(lastFMUserName, timeSpan, num);

                if (artists?.Any() != true)
                {
                    this._embed.NoScrobblesFoundErrorResponse(artists.Status, this.Context, this._logger);
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
                var artistsString = num == 1 ? "artist" : "artists";
                this._embedAuthor.WithName($"Top {num} {timePeriod} {artistsString} for {userTitle}");
                this._embedAuthor.WithUrl(Constants.LastFMUserUrl + lastFMUserName + "/library/artists");
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
                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);
                await ReplyAsync("Unable to show Last.FM info due to an internal error.");
            }
        }

        [Command("index", RunMode = RunMode.Async)]
        [Summary("Indexes top 2000 artists for every user in your server.")]
        public async Task IndexGuildAsync()
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("This command is not supported in DMs.");
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);

            try
            {
                var guildUsers = await this.Context.Guild.GetUsersAsync();
                var users = await this._indexService.GetUsersToIndex(guildUsers);
                var indexedUserCount = await this._indexService.GetIndexedUsersCount(guildUsers);

                var guildOnCooldown =
                    lastIndex != null && lastIndex > DateTime.UtcNow.Add(-Constants.GuildIndexCooldown);

                var guildRecentlyIndexed =
                    lastIndex != null && lastIndex > DateTime.UtcNow.Add(-TimeSpan.FromMinutes(6));

                if (guildRecentlyIndexed)
                {
                    await ReplyAsync("An index was recently started on this server. Please wait before running this command again.");
                    return;
                }
                if (users.Count == 0 && lastIndex != null)
                {
                    var reply =
                        $"No new registered .fmbot members found on this server or all users have already been indexed in the last {Constants.GuildIndexCooldown.TotalHours} hours.";

                    if (guildOnCooldown)
                    {
                        var timeTillIndex = lastIndex.Value.Add(Constants.GuildIndexCooldown) - DateTime.UtcNow;
                        reply +=
                            $"\nAll users in this server can be updated again in {(int)timeTillIndex.TotalHours} hours and {timeTillIndex:mm} minutes";
                    }
                    await ReplyAsync(reply);
                    return;
                }
                if (users.Count == 0 && lastIndex == null)
                {
                    await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild, DateTime.UtcNow.AddDays(-1));
                    await ReplyAsync("All users on this server have already been indexed or nobody is registered on .fmbot here.\n" +
                                     "The server has now been registered anyway, so you can start using the commands that require indexing.");
                }

                string usersString = "";
                if (guildOnCooldown)
                {
                    usersString = "new ";
                }

                if (users.Count == 1)
                {
                    usersString += "user";
                }
                else
                {
                    usersString += "users";
                }

                this._embed.WithTitle($"Added {users.Count} {usersString} to bot indexing queue");

                var expectedTime = TimeSpan.FromSeconds(2 * users.Count);
                var indexStartedReply =
                    $"Indexing stores users their all time top {Constants.ArtistsToIndex} artists. \n\n" +
                    $"`{users.Count}` new users or users with expired artists added to queue.";

                if (expectedTime.TotalMinutes >= 2)
                {
                    indexStartedReply += $" This will take approximately {(int)expectedTime.TotalMinutes} minutes.";
                }

                indexStartedReply += $"\n`{indexedUserCount}` users already indexed on this server.\n \n" +
                                     "*Note: You will currently not be alerted when the index is finished.*";

                this._embed.WithDescription(indexStartedReply);

                await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);

                this._indexService.IndexGuild(users);
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);
                await ReplyAsync(
                    "Something went wrong while indexing users. Please let us know as this feature is in beta.");
                await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild, DateTime.UtcNow);
            }
        }

        [Command("whoknows", RunMode = RunMode.Async)]
        [Summary("Shows what other users listen to the same artist in your server")]
        [Alias("w", "wk")]
        public async Task WhoKnowsAsync(params string[] artistValues)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("This command is not supported in DMs.");
                return;
            }

            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(Context.Guild);

            if (lastIndex == null)
            {
                await ReplyAsync("This server hasn't been indexed yet.\n" +
                                 "Please run `.fmindex` to index this server.");
                return;
            }
            if (lastIndex < DateTime.UtcNow.AddDays(-60))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 60 days ago.\n" +
                                 "Please run `.fmindex` to re-index this server.");
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var artistQuery = await GetArtistOrHelp(artistValues, userSettings, "fmartist");
            if (artistQuery == null)
            {
                return;
            }

            var queryParams = new Dictionary<string, string>
            {
                {"artist", artistQuery },
                {"username", userSettings.UserNameLastFM }
            };

            var artistCall = await this._lastfmApi.CallApiAsync<ArtistResponse>(queryParams, Call.ArtistInfo);

            if (!artistCall.Success)
            {
                this._embed.ErrorResponse(artistCall.Error.Value, artistCall.Message, this.Context, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }
            Statistics.LastfmApiCalls.Inc();

            var artist = artistCall.Content;

            try
            {
                var guildUsers = await this.Context.Guild.GetUsersAsync();
                var usersWithArtist = await this._artistsService.GetIndexedUsersForArtist(guildUsers, artist.Artist.Name);

                if (usersWithArtist.Count == 0 && artist.Artist.Stats.Userplaycount != 0)
                {
                    var guildUser = await Context.Guild.GetUserAsync(Context.User.Id);
                    usersWithArtist =
                        ArtistsService.AddUserToIndexList(usersWithArtist, userSettings, guildUser, artist);
                }
                if (!usersWithArtist.Select(s => s.UserId).Contains(userSettings.UserId) && usersWithArtist.Count != 14 && artist.Artist.Stats.Userplaycount != 0)
                {
                    var guildUser = await Context.Guild.GetUserAsync(Context.User.Id);
                    usersWithArtist =
                        ArtistsService.AddUserToIndexList(usersWithArtist, userSettings, guildUser, artist);
                }

                var serverUsers = ArtistsService.ArtistWithUserToStringList(usersWithArtist, artist, userSettings.UserId);
                if (usersWithArtist.Count == 0)
                {
                    serverUsers = "Nobody in this server (not even you) has listened to this artist.";
                }

                this._embed.WithDescription(serverUsers);
                var footer = "";

                var timeTillIndex = DateTime.UtcNow - lastIndex.Value;
                footer += $"Last updated {(int)timeTillIndex.TotalHours}h{timeTillIndex:mm}m ago";
                if (lastIndex < DateTime.UtcNow.Add(-Constants.GuildIndexCooldown))
                {
                    footer += " - Update data with `.fmindex`";
                }

                if (guildUsers.Count < 100)
                {
                    var serverListeners = await this._artistsService.GetArtistListenerCountForServer(guildUsers, artist.Artist.Name);
                    var serverPlaycount = await this._artistsService.GetArtistPlayCountForServer(guildUsers, artist.Artist.Name);
                    var avgServerListenerPlaycount = await this._artistsService.GetArtistAverageListenerPlaycountForServer(guildUsers, artist.Artist.Name);

                    footer += $"\n{serverListeners} listeners - ";
                    footer += $"{serverPlaycount} total plays - ";
                    footer += $"{(int)avgServerListenerPlaycount} median plays";
                }
                else if (guildUsers.Count < 125)
                {
                    footer += "\nView server artist averages in `.fmartist`";
                }

                this._embed.WithTitle($"Who knows {artist.Artist.Name} in {Context.Guild.Name}");
                this._embed.WithUrl(artist.Artist.Url);

                this._embedFooter.WithText(footer);
                this._embed.WithFooter(this._embedFooter);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);
                await ReplyAsync(
                    "Something went wrong while using whoknows. Please let us know as this feature is in beta.");
            }
        }

        private async Task<string> GetArtistOrHelp(string[] artistValues, User userSettings, string command)
        {
            string artist;
            if (artistValues.Length > 0)
            {
                if (artistValues.First() == "help")
                {
                    await ReplyAsync(
                        $"Usage: `.{command} 'name'\n" +
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
                    this._embed.NoScrobblesFoundErrorResponse(track.Status, this.Context, this._logger);
                    await ReplyAsync("", false, this._embed.Build());
                    return null;
                }

                artist = track.Content.First().ArtistName;
            }

            return artist;
        }

        private async Task UsernameNotSetErrorResponseAsync()
        {
            this._embed.UsernameNotSetErrorResponse(this.Context, this._logger);
            await ReplyAsync("", false, this._embed.Build());
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
    }
}
