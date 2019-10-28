using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Configurations;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Data.Entities;
using IF.Lastfm.Core.Api.Enums;
using static FMBot.Bot.FMBotUtil;
using static FMBot.Bot.Models.LastFMModels;

namespace FMBot.Bot.Commands
{
    public class LastFMCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;
        private readonly FriendsService _friendsService = new FriendsService();
        private readonly GuildService _guildService = new GuildService();
        private readonly LastFMService _lastFmService = new LastFMService();
        private readonly Logger.Logger _logger;
        private readonly TimerService _timer;

        private readonly UserService _userService = new UserService();

        public LastFMCommands(TimerService timer, Logger.Logger logger)
        {
            this._timer = timer;
            this._logger = logger;
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        private async Task SendChartMessage(FMBotChart chart)
        {
            await this._lastFmService.GenerateChartAsync(chart);

            // Send chart memory stream, remove when finished
            using (var memory = await GlobalVars.GetChartStreamAsync(chart.DiscordUser.Id))
            {
                await this.Context.Channel.SendFileAsync(memory, "chart.png");
            }

            lock (GlobalVars.charts.SyncRoot)
            {
                // @TODO: remove only once in a while to keep it cached
                GlobalVars.charts.Remove(GlobalVars.GetChartFileName(chart.DiscordUser.Id));
            }
        }

        [Command("fm", RunMode = RunMode.Async)]
        [Summary("Displays what a user is listening to.")]
        [Alias("qm", "wm", "em", "rm", "tm", "ym", "um", "im", "om", "pm", "dm", "gm", "sm", "am", "hm", "jm", "km",
            "lm", "zm", "xm", "cm", "vm", "bm", "nm", "mm", "lastfm")]
        public async Task FMAsync(string user = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

            if (user == "help")
            {
                await ReplyAsync(
                    "Usage: `.fm 'lastfm username/discord user'` \n" +
                    "You can set your default user and your display mode through the `.fmset 'username' 'embedfull/embedmini/textfull/textmini'` command.");
                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);
                return;
            }

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

                var trackTask = this._lastFmService.GetRecentScrobblesAsync(lastFMUserName);
                var userInfoTask = this._lastFmService.GetUserInfoAsync(lastFMUserName);

                Task.WaitAll(trackTask, userInfoTask);

                var tracks = trackTask.Result;
                var userInfo = userInfoTask.Result;

                if (tracks?.Any() != true)
                {
                    await NoScrobblesErrorResponseFoundAsync(tracks.Status);
                    return;
                }

                var currentTrack = tracks.Content[0];
                var lastTrack = tracks.Content[1];

                const string nullText = "[undefined]";

                var trackName = currentTrack.Name;
                var artistName = currentTrack.ArtistName;
                var albumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nullText : currentTrack.AlbumName;

                var lastTrackName = lastTrack.Name;
                var lastTrackArtistName = lastTrack.ArtistName;
                var lastTrackAlbumName =
                    string.IsNullOrWhiteSpace(lastTrack.AlbumName) ? nullText : lastTrack.AlbumName;

                var playCount = userInfo.Content.Playcount;

                switch (userSettings.ChartType)
                {
                    case ChartType.textmini:
                        await this.Context.Channel.SendMessageAsync(
                            await this._userService.GetUserTitleAsync(this.Context)
                            + "\n**Current** - "
                            + artistName
                            + " - "
                            + trackName
                            + " ["
                            + albumName
                            + "]\n<https://www.last.fm/user/"
                            + userSettings.UserNameLastFM
                            + ">\n"
                            + userSettings.UserNameLastFM
                            + "'s total scrobbles: "
                            + playCount.ToString("N0"));
                        break;
                    case ChartType.textfull:
                        await this.Context.Channel.SendMessageAsync(
                            await this._userService.GetUserTitleAsync(this.Context)
                            + "\n**Current** - "
                            + artistName
                            + " - "
                            + trackName
                            + " ["
                            + albumName
                            + "]\n**Previous** - "
                            + lastTrackArtistName
                            + " - "
                            + lastTrackName
                            + " ["
                            + lastTrackAlbumName
                            + "]\n<https://www.last.fm/user/"
                            + userSettings.UserNameLastFM
                            + ">\n"
                            + userSettings.UserNameLastFM
                            + "'s total scrobbles: "
                            + playCount.ToString("N0"));
                        break;
                    default:
                        if (!this._guildService.CheckIfDM(this.Context))
                        {
                            var perms = await this._guildService.CheckSufficientPermissionsAsync(this.Context);
                            if (!perms.EmbedLinks)
                            {
                                await ReplyAsync(
                                    "Insufficient permissions, I need to the 'Embed links' permission to show you your scrobbles.");
                                break;
                            }
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

                        this._embed.AddField(
                            $"Current: {tracks.Content[0].Name}",
                            $"By **{tracks.Content[0].ArtistName}**" +
                            (string.IsNullOrEmpty(tracks.Content[0].AlbumName)
                                ? ""
                                : $" | {tracks.Content[0].AlbumName}"));

                        if (userSettings.ChartType == ChartType.embedfull)
                        {
                            this._embedAuthor.WithName("Last tracks for " + userTitle);
                            this._embed.AddField(
                                $"Previous: {tracks.Content[1].Name}",
                                $"By **{tracks.Content[1].ArtistName}**" +
                                (string.IsNullOrEmpty(tracks.Content[1].AlbumName)
                                    ? ""
                                    : $" | {tracks.Content[1].AlbumName}"));
                        }
                        else
                        {
                            this._embedAuthor.WithName("Last track for " + userTitle);
                        }

                        this._embed.WithTitle(tracks.Content[0].IsNowPlaying == true
                            ? "*Now playing*"
                            : $"Last scrobble {tracks.Content[0].TimePlayed?.ToString("g")} (UTC)");

                        this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                        this._embed.WithAuthor(this._embedAuthor);
                        this._embed.WithUrl(Constants.LastFMUserUrl + lastFMUserName);

                        var albumImages =
                            await this._lastFmService.GetAlbumImagesAsync(currentTrack.ArtistName,
                                currentTrack.AlbumName);

                        if (albumImages?.Medium != null)
                        {
                            this._embed.WithThumbnailUrl(albumImages.Medium.ToString());
                        }

                        this._embedFooter.WithText(
                            $"{userInfo.Content.Name} has {userInfo.Content.Playcount} scrobbles.");
                        this._embed.WithFooter(this._embedFooter);

                        this._embed.WithColor(Constants.LastFMColorRed);
                        await ReplyAsync("", false, this._embed.Build());
                        break;
                }

                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);
                await ReplyAsync(
                    "Unable to show Last.FM info due to an internal error. Try scrobbling something then use the command again.");
            }
        }


        [Command("fmartists", RunMode = RunMode.Async)]
        [Summary("Displays top artists.")]
        [Alias("fmartist", "fmartistlist", "fmartistslist")]
        public async Task ArtistsAsync(string time = "weekly", int num = 6, string user = null)
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
                    "Usage: `.fmartists 'weekly/monthly/yearly/alltime' 'number of artists (max 10)'` \n" +
                    "You can set your default user and your display mode through the `.fmset 'username' 'embedfull/embedmini/textfull/textmini'` command.");
                return;
            }

            if (!Enum.TryParse(time, true, out ChartTimePeriod timePeriod))
            {
                await ReplyAsync("Invalid time period. Please use 'weekly', 'monthly', 'yearly', or 'alltime'. \n" +
                                 "Usage: `.fmartists 'weekly/monthly/yearly/alltime' 'number of artists (max 10)'`");
                return;
            }

            if (num > 10)
            {
                num = 10;
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
                    await NoScrobblesErrorResponseFoundAsync(artists.Status);
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
                this._embedAuthor.WithName($"Artists for {userTitle}");
                this._embed.WithAuthor(this._embedAuthor);

                this._embed.WithTitle($"Top {num} artists ({timePeriod})");
                this._embed.WithUrl(Constants.LastFMUserUrl + lastFMUserName + "/library/artists");

                var indexVal = num - 1;
                for (var i = 0; i <= indexVal; i++)
                {
                    var artist = artists.Content[i];

                    var correctNum = i + 1;
                    this._embed.AddField("#" + correctNum + ": " + artist.Name,
                        artist.PlayCount.Value.ToString("N0") + " times scrobbled");
                }

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

        [Command("fmchart", RunMode = RunMode.Async)]
        [Summary("Generates a chart based on a user's parameters.")]
        public async Task ChartAsync(string chartSize = "3x3", string time = "weekly", string titleSetting = null,
            string user = null)
        {
            if (chartSize == "help")
            {
                await ReplyAsync("fmchart '2x2-8x8' 'weekly/monthly/yearly/overall' 'notitles/titles' 'user'");
                return;
            }

            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

            var lastFMUserName = userSettings.UserNameLastFM;
            var self = true;

            if (!this._guildService.CheckIfDM(this.Context))
            {
                var perms = await this._guildService.CheckSufficientPermissionsAsync(this.Context);
                if (!perms.AttachFiles)
                {
                    await ReplyAsync(
                        "I'm missing the 'Attach files' permission in this server, so I can't post a chart.");
                    return;
                }
            }

            // @TODO: change to intparse or the likes
            try
            {
                int chartAlbums;
                int chartRows;

                switch (chartSize)
                {
                    case "2x2":
                        chartAlbums = 4;
                        chartRows = 2;
                        break;
                    case "3x3":
                        chartAlbums = 9;
                        chartRows = 3;
                        break;
                    case "4x4":
                        chartAlbums = 16;
                        chartRows = 4;
                        break;
                    case "5x5":
                        chartAlbums = 25;
                        chartRows = 5;
                        break;
                    case "6x6":
                        chartAlbums = 36;
                        chartRows = 6;
                        break;
                    case "7x7":
                        chartAlbums = 49;
                        chartRows = 7;
                        break;
                    case "8x8":
                        chartAlbums = 64;
                        chartRows = 8;
                        break;
                    default:
                        await ReplyAsync("Your chart's size isn't valid. Sizes supported: 3x3-8x8. \n" +
                                         $"Example: `{ConfigData.Data.CommandPrefix}fmchart 5x5 monthly titles`. For more info, use `.fmchart help`");
                        return;
                }

                _ = this.Context.Channel.TriggerTypingAsync();

                if (user != null)
                {
                    var alternativeLastFmUserName = await FindUser(user);
                    if (!string.IsNullOrEmpty(alternativeLastFmUserName))
                    {
                        lastFMUserName = alternativeLastFmUserName;
                        self = false;
                    }
                }

                // Generating image
                var timespan = this._lastFmService.StringToLastStatsTimeSpan(time);
                var albums = await this._lastFmService.GetTopAlbumsAsync(lastFMUserName, timespan, chartAlbums);

                if (albums.Count() < chartAlbums)
                {
                    await ReplyAsync(
                        $"User hasn't listened to enough albums ({albums.Count()} of required {chartAlbums}) for a chart this size. " +
                        "Please try a smaller chart or a bigger time period (weekly/monthly/yearly/overall)'.");
                    return;
                }

                var chart = new FMBotChart
                {
                    albums = albums,
                    LastFMName = lastFMUserName,
                    max = chartAlbums,
                    rows = chartRows,
                    images = new List<ChartImage>(),
                    DiscordUser = this.Context.User,
                    disclient = this.Context.Client as DiscordSocketClient,
                    mode = 0,
                    titles = titleSetting == null ? userSettings.TitlesEnabled ?? true : titleSetting == "titles"
                };

                await this._userService.ResetChartTimerAsync(userSettings);

                await SendChartMessage(chart);

                // Adding extra infobox
                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());

                this._embed.WithColor(Constants.LastFMColorRed);
                this._embed.WithAuthor(this._embedAuthor);
                var URI = Constants.LastFMUserUrl + lastFMUserName;
                this._embed.WithUrl(URI);

                string chartDescription;
                if (time.Equals("weekly") || time.Equals("week") || time.Equals("w"))
                {
                    chartDescription = chartSize + " Weekly Chart";
                }
                else if (time.Equals("monthly") || time.Equals("month") || time.Equals("m"))
                {
                    chartDescription = chartSize + " Monthly Chart";
                }
                else if (time.Equals("yearly") || time.Equals("year") || time.Equals("y"))
                {
                    chartDescription = chartSize + " Yearly Chart";
                }
                else if (time.Equals("overall") || time.Equals("alltime") || time.Equals("o") || time.Equals("at"))
                {
                    chartDescription = chartSize + " Overall Chart";
                }
                else
                {
                    chartDescription = chartSize + " Chart";
                }

                if (self)
                {
                    this._embedAuthor.WithName(chartDescription + " for " +
                                               await this._userService.GetUserTitleAsync(this.Context));
                }
                else
                {
                    this._embedAuthor.WithName(
                        $"{chartDescription} for {lastFMUserName}, requested by {await this._userService.GetUserTitleAsync(this.Context)}");
                }

                var userInfo = await this._lastFmService.GetUserInfoAsync(lastFMUserName);

                var playCount = userInfo.Content.Playcount;

                this._embedFooter.Text = $"{lastFMUserName} has {playCount} scrobbles.";
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
                    "Sorry, but I was unable to generate a FMChart due to an internal error. Make sure you have scrobbles and Last.FM isn't having issues, and try again later.");
            }
        }

        [Command("fmrecent", RunMode = RunMode.Async)]
        [Summary("Displays a user's recent tracks.")]
        [Alias("fmrecenttracks")]
        public async Task RecentAsync(string user = null, int num = 5)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

            if (user == "help")
            {
                await ReplyAsync(".fmrecent 'user' 'number of items (max 10)'");
                return;
            }

            if (num > 10)
            {
                num = 10;
            }

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

                var tracksTask = this._lastFmService.GetRecentScrobblesAsync(lastFMUserName, num);
                var userInfoTask = this._lastFmService.GetUserInfoAsync(lastFMUserName);

                Task.WaitAll(tracksTask, userInfoTask);

                var tracks = tracksTask.Result;
                var userInfo = userInfoTask.Result;

                if (tracks?.Any() != true)
                {
                    await NoScrobblesErrorResponseFoundAsync(tracks.Status);
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
                this._embedAuthor.WithName($"Latest tracks for {userTitle}");
                this._embed.WithAuthor(this._embedAuthor);

                this._embed.WithTitle(tracks.Content[0].IsNowPlaying == true
                    ? "*Now playing*"
                    : $"Last scrobble {tracks.Content[0].TimePlayed?.ToString("g")} (UTC)");
                this._embed.WithUrl(Constants.LastFMUserUrl + lastFMUserName);

                var indexval = num - 1;
                for (var i = 0; i <= indexval; i++)
                {
                    var track = tracks.Content[i];

                    var TrackName = track.Name;
                    var ArtistName = track.ArtistName;
                    var AlbumName = track.AlbumName;

                    if (i == 0)
                    {
                        var albumImages = await this._lastFmService.GetAlbumImagesAsync(ArtistName, AlbumName);

                        if (albumImages?.Medium != null)
                        {
                            this._embed.WithThumbnailUrl(albumImages.Medium.ToString());
                        }
                    }

                    var correctNum = i + 1;
                    this._embed.AddField("#" + correctNum + ": " + TrackName,
                        $"By **{ArtistName}**" + (string.IsNullOrEmpty(AlbumName) ? "" : $" | {AlbumName}"));
                }

                var playcount = userInfo.Content.Playcount;

                this._embedFooter.WithText(lastFMUserName + "'s total scrobbles: " + playcount.ToString("0"));
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
                    "Unable to show your recent tracks on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("fmstats", RunMode = RunMode.Async)]
        [Summary("Displays user stats related to Last.FM and FMBot")]
        public async Task StatsAsync(string user = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

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
                this._embedAuthor.WithName("Last.FM & fmbot user data for " + userTitle);
                this._embed.WithAuthor(this._embedAuthor);

                this._embed.WithUrl(Constants.LastFMUserUrl + lastFMUserName);
                this._embed.WithTitle("Click here to go to profile");

                this._embedFooter.WithText(
                    "To see info for other users, use .fmstats 'Discord username/ Last.FM username'");
                this._embed.WithFooter(this._embedFooter);

                var userInfo = await this._lastFmService.GetUserInfoAsync(lastFMUserName);

                var userImages = userInfo.Content.Avatar;
                var userAvatar = userImages?.Large.AbsoluteUri;

                if (!string.IsNullOrWhiteSpace(userAvatar))
                {
                    this._embed.WithThumbnailUrl(userAvatar);
                }

                this._embed.AddField("Last.FM Name", lastFMUserName, true);
                this._embed.AddField("User Type", userInfo.Content.Type, true);
                this._embed.AddField("Total scrobbles", userInfo.Content.Playcount, true);
                this._embed.AddField("Country", userInfo.Content.Country, true);
                this._embed.AddField("Is subscriber?", userInfo.Content.IsSubscriber.ToString(), true);
                this._embed.AddField("Bot Chart Mode", userSettings.ChartType, true);
                this._embed.AddField("Bot user type", userSettings.UserType, true);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);
                await ReplyAsync(
                    "Unable to show your stats on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("fmfeatured", RunMode = RunMode.Async)]
        [Summary("Displays the featured avatar.")]
        [Alias("fmfeaturedavatar", "fmfeatureduser", "fmfeaturedalbum")]
        public async Task FeaturedAsync()
        {
            try
            {
                var selfUser = this.Context.Client.CurrentUser;
                this._embed.WithThumbnailUrl(selfUser.GetAvatarUrl());
                this._embed.AddField("Featured:", this._timer.GetTrackString());

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);
                await ReplyAsync(
                    "Unable to show the featured avatar on FMBot due to an internal error. The timer service cannot be loaded. Please wait for the bot to fully load.");
            }
        }

        [Command("fmset", RunMode = RunMode.Async)]
        [Summary("Sets your Last.FM name and FM mode. Please note that users in shared servers will be able to see and request your Last.FM username.")]
        [Alias("fmsetname", "fmsetmode")]
        public async Task fmsetAsync([Summary("Your Last.FM name")] string lastFMUserName,
            [Summary("The mode you want to use.")] string chartType = "embedfull")
        {
            if (lastFMUserName == "help")
            {
                await ReplyAsync(ConfigData.Data.CommandPrefix +
                                 "fmset 'Last.FM Username' 'embedmini/embedfull/textmini/textfull'");
                return;
            }

            if (!await this._lastFmService.LastFMUserExistsAsync(lastFMUserName.Replace("'", "")))
            {
                await ReplyAsync("LastFM user could not be found. Please check if the name you entered is correct.");
                return;
            }

            if (!Enum.TryParse(chartType.Replace("'", ""), true, out ChartType chartTypeEnum))
            {
                await ReplyAsync("Invalid mode. Please use 'embedmini', 'embedfull', 'textfull', or 'textmini'.");
                return;
            }

            this._userService.SetLastFM(this.Context.User, lastFMUserName.Replace("'", ""), chartTypeEnum);

            await ReplyAsync("Your Last.FM name has been set to '" + lastFMUserName.Replace("'", "") +
                             "' and your mode has been set to '" + chartType + "'.");
            this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                this.Context.Message.Content);

            if (!this._guildService.CheckIfDM(this.Context))
            {
                var perms = await this._guildService.CheckSufficientPermissionsAsync(this.Context);
                if (!perms.EmbedLinks || !perms.AttachFiles)
                {
                    await ReplyAsync(
                        "Please note that the bot also needs the 'Attach files' and 'Embed links' permissions for most commands. One or both of these permissions are currently missing.");
                }
            }
        }

        [Command("fmremove", RunMode = RunMode.Async)]
        [Summary("Deletes your FMBot data.")]
        [Alias("fmdelete", "fmremovedata", "fmdeletedata")]
        public async Task RemoveAsync()
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            if (userSettings == null)
            {
                await ReplyAsync("Sorry, but we don't have any data from you in our database.");
                return;
            }

            await this._friendsService.RemoveAllLastFMFriendsAsync(userSettings.UserID);
            await this._userService.DeleteUser(userSettings.UserID);

            await ReplyAsync("Your settings, friends and any other data have been successfully deleted.");
            this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                this.Context.Message.Content);
        }

        [Command("fmsuggest", RunMode = RunMode.Async)]
        [Summary("Suggest features you want to see in the bot, or report inappropriate images.")]
        [Alias("fmreport", "fmsuggestion", "fmsuggestions")]
        public async Task Suggest(string suggestion = null)
        {
            try
            {
                /*
                if (string.IsNullOrWhiteSpace(suggestion))
                {
                    await ReplyAsync(cfgjson.CommandPrefix + "fmsuggest 'text in quotes'");
                    return;
                }
                else
                {
                */
                var client = this.Context.Client as DiscordSocketClient;

                var BroadcastServerID = Convert.ToUInt64(ConfigData.Data.BaseServer);
                var BroadcastChannelID = Convert.ToUInt64(ConfigData.Data.SuggestionsChannel);

                var guild = client.GetGuild(BroadcastServerID);
                var channel = guild.GetTextChannel(BroadcastChannelID);

                var builder = new EmbedBuilder();
                var eab = new EmbedAuthorBuilder
                {
                    IconUrl = this.Context.User.GetAvatarUrl(),
                    Name = this.Context.User.Username
                };
                builder.WithAuthor(eab);
                builder.WithTitle(this.Context.User.Username + "'s suggestion:");
                builder.WithDescription(suggestion);

                await channel.SendMessageAsync("", false, builder.Build());

                await ReplyAsync("Your suggestion has been sent to the .fmbot server!");
                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);

                //}
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);
            }
        }

        private async Task UsernameNotSetErrorResponseAsync()
        {
            this._embed.WithTitle("Error while attempting get Last.FM information");
            this._embed.WithDescription("Last.FM username has not been set. \n" +
                                        "To setup your Last.FM account with this bot, please use the `.fmset` command. \n" +
                                        $"Usage: `{ConfigData.Data.CommandPrefix}fmset username mode`\n" +
                                        "Possible modes: embedmini/embedfull/textmini/textfull. \n" +
                                        "Please note that users in shared servers will be able to see and request your Last.FM username.");

            this._embed.WithColor(Constants.WarningColorOrange);
            await ReplyAsync("", false, this._embed.Build());
            this._logger.LogError("Last.FM username not set", this.Context.Message.Content, this.Context.User.Username,
                this.Context.Guild?.Name, this.Context.Guild?.Id);
        }

        private async Task NoScrobblesErrorResponseFoundAsync(LastResponseStatus apiResponse)
        {
            this._embed.WithTitle("Error while attempting get Last.FM information");
            switch (apiResponse)
            {
                case LastResponseStatus.Failure:
                    this._embed.WithDescription("Last.FM has issues and/or is down. Please try again later.");
                    break;
                default:
                    this._embed.WithDescription(
                        "You have no scrobbles/artists on your profile, or Last.FM is having issues. Please try again later.");
                    break;
            }

            this._embed.WithColor(Constants.WarningColorOrange);
            await ReplyAsync("", false, this._embed.Build());
            this._logger.LogError("No scrobbles found for user", this.Context.Message.Content,
                this.Context.User.Username, this.Context.Guild?.Name, this.Context.Guild?.Id);
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
