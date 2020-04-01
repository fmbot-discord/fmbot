using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Data.Entities;
using FMBot.LastFM.Models;
using FMBot.LastFM.Services;
using Artist = FMBot.Data.Entities.Artist;

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
        private readonly LastfmApi _lastfmApi;
        private readonly Logger.Logger _logger;

        private readonly UserService _userService = new UserService();

        public ArtistCommands(Logger.Logger logger, IndexService indexService)
        {
            this._logger = logger;
            this._indexService = indexService;
            this._lastfmApi = new LastfmApi(ConfigData.Data.FMKey, ConfigData.Data.FMSecret);
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("fmartist", RunMode = RunMode.Async)]
        [Summary("Displays current artist.")]
        [Alias("fma")]
        public async Task ArtistsAsync(params string[] artistValues)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

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

            var queryParams = new Dictionary<string, string>
            {
                {"artist", artist },
                {"username", userSettings.UserNameLastFM }
            };

            var artistCall = await this._lastfmApi.CallApiAsync<ArtistResponse>(queryParams, Call.ArtistInfo);

            if (!artistCall.Success)
            {
                this._embed.ErrorResponse(artistCall.Error.Value, artistCall.Message, this.Context, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }

            var artistInfo = artistCall.Content.Artist;

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            this._embedAuthor.WithName($"Artist info about {artistInfo.Name} for {userTitle}");
            this._embedAuthor.WithUrl(artistInfo.Url);
            this._embed.WithAuthor(this._embedAuthor);

            this._embed.AddField("Listeners", artistInfo.Stats.Listeners, true);
            this._embed.AddField("Global playcount", artistInfo.Stats.Playcount, true);
            if (artistInfo.Stats.Userplaycount.HasValue)
            {
                this._embed.AddField("Your playcount", artistInfo.Stats.Userplaycount, true);
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

        [Command("fmartists", RunMode = RunMode.Async)]
        [Summary("Displays top artists.")]
        [Alias("fmal","fmas", "fmartistlist", "fmartistslist")]
        public async Task ArtistsAsync(string time = "weekly", int num = 10, string user = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

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

        [Command("fmindex", RunMode = RunMode.Async)]
        [Summary("GIndexes top 1000 artists for every user in your server.")]
        public async Task IndexGuildAsync()
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("This command is not supported in DMs.");
                return;
            }

            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(Context.Guild);

            try
            {
                var users = await this._indexService.GetUsersForContext(this.Context);

                var guildOnCooldown =
                    lastIndex != null && lastIndex > DateTime.UtcNow.Add(-Constants.GuildIndexCooldown);

                if (users.Count == 0)
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

                var expectedTime = TimeSpan.FromSeconds(1.5 * users.Count);
                var indexStartedReply = $"{users.Count} {usersString} have been added to the queue for adding or updating their top 1000 artists.";
                if (users.Count >= 60)
                {
                    indexStartedReply += "\nPlease note that this can take a while since this server has a lot of registered members. \n" +
                                         $"Estimated time for indexing all users: {(int)expectedTime.TotalMinutes} minutes.";
                }

                indexStartedReply +=
                    "\n*Note: Due to technical limitations, you will currently not be alerted when the server index is finished*";

                await ReplyAsync(indexStartedReply);

                await this._guildService.UpdateGuildIndexTimestampAsync(Context.Guild);
                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);

                var serverUsers = await this._indexService.IndexGuild(users);
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);
                await ReplyAsync(
                    "Something went wrong while indexing users. Please let us know as this feature is in beta.");
                await this._guildService.UpdateGuildIndexTimestampAsync(Context.Guild, DateTime.UtcNow);
            }
        }

        [Command("fmwhoknows", RunMode = RunMode.Async)]
        [Summary("Shows what other users listen to the same artist in your server")]
        [Alias("fmw", "fmwk")]
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
                await ReplyAsync("This server hasn't been indexed yet (or it isn't finished).\n" +
                                 "Please run `.fmindex` to index this server.");
                return;
            }
            if (lastIndex < DateTime.UtcNow.AddDays(-60))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 60 days ago.\n" +
                                 "Please run `.fmindex` to re-index this server.");
                return;
            }

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
                var artists = await this._artistsService.GetUsersForArtist(Context, artist.Artist.Name);

                switch (artists.Count)
                {
                    case 0 when artist.Artist.Stats.Userplaycount == 0:
                        await ReplyAsync("Sorry, but nobody has this artists in their top 1000 artists and you don't have any plays.");
                        return;
                    case 0:
                        artists = this._artistsService.AddArtistToIndexList(artists, userSettings, artist);
                        break;
                }

                var serverUsers = await this._artistsService.UserListToIndex(artists, artistCall.Content, userSettings.UserId);

                this._embed.WithDescription(serverUsers);
                var footer = "";
                if (lastIndex < DateTime.UtcNow.Add(-Constants.GuildIndexCooldown))
                {
                    footer += "Update data with `.fmindex` | ";
                }

                this._embed.WithTitle($"Who knows {artist.Artist.Name} in {Context.Guild.Name}");
                this._embed.WithUrl(artist.Artist.Url);

                var timeTillIndex = DateTime.UtcNow - lastIndex.Value;

                footer += $"Last updated {(int)timeTillIndex.TotalHours}h{timeTillIndex:mm}m ago";
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
