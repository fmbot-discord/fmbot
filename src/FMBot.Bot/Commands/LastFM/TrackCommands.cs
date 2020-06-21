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
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Commands.LastFM
{
    public class TrackCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;
        private readonly IGuildService _guildService;
        private readonly LastFMService _lastFmService;
        private readonly Logger.Logger _logger;

        private readonly UserService _userService;

        private readonly IPrefixService _prefixService;

        public TrackCommands(Logger.Logger logger,
            IPrefixService prefixService,
            ILastfmApi lastfmApi,
            IGuildService guildService)
        {
            this._logger = logger;
            this._prefixService = prefixService;
            this._guildService = guildService;
            this._userService = new UserService();
            this._lastFmService = new LastFMService(lastfmApi);
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("fm", RunMode = RunMode.Async)]
        [Summary("Displays what a user is listening to.")]
        [Alias("np", "qm", "wm", "em", "rm", "tm", "ym", "um", "om", "pm", "dm", "gm", "sm", "am", "hm", "jm", "km",
            "lm", "zm", "xm", "cm", "vm", "bm", "nm", "mm", "lastfm")]
        [LoginRequired]
        public async Task FMAsync(params string[] user)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.CommandPrefix;

            if (userSettings?.UserNameLastFM == null)
            {
                this._embed.UsernameNotSetErrorResponse(this.Context, prfx, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }
            if (user.Length > 0 && user.First() == "help")
            {
                var fmString = "fm";
                if (prfx == ".fm")
                {
                    fmString = "";
                }

                var replyString = $"`{prfx}{fmString}` shows you your last scrobble(s). \n " +
                                  $"This command can also be used on others, for example `{prfx}{fmString} lastfmusername` or `{prfx}{fmString} @discorduser`\n \n" +

                                  "You can set your username and you can change the mode with the `.fmset` command.\n";

                var differentMode = userSettings.FmEmbedType == FmEmbedType.embedmini ? "embedfull" : "embedmini";
                replyString += $"`{prfx}set {userSettings.UserNameLastFM} {differentMode}` \n \n" +
                               $"For more info, use `{prfx}set help`.";


                this._embed.WithUrl($"{Constants.DocsUrl}/commands/tracks/");
                this._embed.WithTitle($"Using the {prfx}{fmString} command");
                this._embed.WithDescription(replyString);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                return;
            }

            if (user.Length > 0 && user.First() == "set")
            {
                await ReplyAsync(
                    "Please remove the space between `.fm` and `set` to set your last.fm username.");
                return;
            }

            try
            {
                var lastFMUserName = userSettings.UserNameLastFM;
                var self = true;

                if (user.Length > 0 && !string.IsNullOrEmpty(user.First()))
                {
                    var alternativeLastFmUserName = await FindUser(user.First());
                    if (!string.IsNullOrEmpty(alternativeLastFmUserName) && await this._lastFmService.LastFMUserExistsAsync(alternativeLastFmUserName))
                    {
                        lastFMUserName = alternativeLastFmUserName;
                        self = false;
                    }
                }

                var recentScrobblesTask = this._lastFmService.GetRecentScrobblesAsync(lastFMUserName);
                var userInfoTask = this._lastFmService.GetUserInfoAsync(lastFMUserName);

                Task.WaitAll(recentScrobblesTask, userInfoTask);

                var recentScrobbles = recentScrobblesTask.Result;
                var userInfo = userInfoTask.Result;


                if (recentScrobbles?.Any() != true)
                {
                    this._embed.NoScrobblesFoundErrorResponse(recentScrobbles.Status, this.Context, this._logger);
                    await ReplyAsync("", false, this._embed.Build());
                    return;
                }

                var currentTrack = recentScrobbles.Content[0];
                var previousTrack = recentScrobbles.Content[1];

                var playCount = userInfo.Content.Playcount;

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);
                var embedTitle = self ? userTitle : $"{lastFMUserName}, requested by {userTitle}";

                var fmText = "";

                string footerText = "";

                footerText +=
                    $"{userInfo.Content.Name} has ";

                switch (userSettings.FmCountType)
                {
                    case FmCountType.Track:
                        var trackInfo = await this._lastFmService.GetTrackInfoAsync(currentTrack.Name,
                            currentTrack.ArtistName, lastFMUserName);
                        if (trackInfo != null)
                        {
                            footerText += $"{trackInfo.Userplaycount} scrobbles on this track | ";
                        }
                        break;
                    case FmCountType.Album:
                        if (!string.IsNullOrEmpty(currentTrack.AlbumName))
                        {
                            var albumInfo = await this._lastFmService.GetAlbumInfoAsync(currentTrack.ArtistName, currentTrack.AlbumName, lastFMUserName);
                            if (albumInfo.Success)
                            {
                                footerText += $"{albumInfo.Content.Album.Userplaycount} scrobbles on this album | ";
                            }
                        }
                        break;
                    case FmCountType.Artist:
                        var artistInfo = await this._lastFmService.GetArtistInfoAsync(currentTrack.ArtistName, lastFMUserName);
                        if (artistInfo.Success)
                        {
                            footerText += $"{artistInfo.Content.Artist.Stats.Userplaycount} scrobbles on this artist | ";
                        }
                        break;
                }

                footerText += $"{userInfo.Content.Playcount} total scrobbles";

                switch (userSettings.FmEmbedType)
                {
                    case FmEmbedType.textmini:
                    case FmEmbedType.textfull:
                        if (userSettings.FmEmbedType == FmEmbedType.textmini)
                        {
                            fmText += $"Last track for {embedTitle}: \n";

                            fmText += LastFMService.TrackToString(currentTrack);
                        }
                        else
                        {
                            fmText += $"Last tracks for {embedTitle}: \n";

                            fmText += LastFMService.TrackToString(currentTrack);
                            fmText += LastFMService.TrackToString(previousTrack);
                        }

                        fmText +=
                            $"<{Constants.LastFMUserUrl + userSettings.UserNameLastFM}> has {playCount} scrobbles.";

                        fmText = fmText.FilterOutMentions();

                        await this.Context.Channel.SendMessageAsync(fmText);
                        break;
                    default:
                        var albumImagesTask =
                            this._lastFmService.GetAlbumImagesAsync(currentTrack.ArtistName, currentTrack.AlbumName);

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

                        if (userSettings.FmEmbedType == FmEmbedType.embedmini)
                        {
                            fmText += LastFMService.TrackToLinkedString(currentTrack);
                            this._embed.WithDescription(fmText);
                        }
                        else
                        {
                            this._embed.AddField("Current:", LastFMService.TrackToLinkedString(currentTrack));
                            this._embed.AddField("Previous:", LastFMService.TrackToLinkedString(previousTrack));
                        }

                        string headerText;
                        if (currentTrack.IsNowPlaying == true)
                        {
                            headerText = "Now playing - ";
                        }
                        else
                        {
                            headerText = userSettings.FmEmbedType == FmEmbedType.embedmini
                                ? "Last track for "
                                : "Last tracks for ";
                        }
                        headerText += embedTitle;


                        if (currentTrack.IsNowPlaying != true && currentTrack.TimePlayed.HasValue)
                        {
                            footerText += " | Last scrobble:";
                            this._embed.WithTimestamp(currentTrack.TimePlayed.Value);
                        }

                        this._embedAuthor.WithName(headerText);
                        this._embedAuthor.WithUrl(Constants.LastFMUserUrl + lastFMUserName);

                        this._embedFooter.WithText(footerText);

                        this._embed.WithFooter(this._embedFooter);

                        this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                        this._embed.WithAuthor(this._embedAuthor);
                        this._embed.WithUrl(Constants.LastFMUserUrl + lastFMUserName);

                        if ((await albumImagesTask)?.Large != null)
                        {
                            this._embed.WithThumbnailUrl((await albumImagesTask).Large.ToString());
                        }

                        var message = await ReplyAsync("", false, this._embed.Build());

                        if (!this._guildService.CheckIfDM(this.Context))
                        {
                            await this._guildService.AddReactionsAsync(message, this.Context.Guild);
                        }

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

        [Command("recent", RunMode = RunMode.Async)]
        [Summary("Displays a user's recent tracks.")]
        [Alias("recenttracks", "r")]
        [LoginRequired]
        public async Task RecentAsync(string amount = "5", string user = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.CommandPrefix;

            if (user == "help")
            {
                await ReplyAsync($"{prfx}recent 'number of items (max 10)' 'lastfm username/discord user'");
                return;
            }

            if (!int.TryParse(amount, out var amountOfTracks))
            {
                await ReplyAsync("Please enter a valid amount. \n" +
                                 $"`{prfx}recent 'number of items (max 10)' 'lastfm username/discord user'` \n" +
                                 $"Example: `{prfx}recent 8`");
                return;
            }

            if (amountOfTracks > 10)
            {
                amountOfTracks = 10;
            }

            if (amountOfTracks < 1)
            {
                amountOfTracks = 1;
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

                var tracksTask = this._lastFmService.GetRecentScrobblesAsync(lastFMUserName, amountOfTracks);
                var userInfoTask = this._lastFmService.GetUserInfoAsync(lastFMUserName);

                Task.WaitAll(tracksTask, userInfoTask);

                var tracks = tracksTask.Result;
                var userInfo = userInfoTask.Result;

                if (tracks?.Any() != true)
                {
                    this._embed.NoScrobblesFoundErrorResponse(tracks.Status, this.Context, this._logger);
                    await ReplyAsync("", false, this._embed.Build());
                    return;
                }

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);
                var title = self ? userTitle : $"{lastFMUserName}, requested by {userTitle}";
                this._embedAuthor.WithName($"Latest tracks for {title}");

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                this._embedAuthor.WithUrl(Constants.LastFMUserUrl + lastFMUserName);
                this._embed.WithAuthor(this._embedAuthor);

                var fmRecentText = "";
                for (var i = 0; i < tracks.Content.Count; i++)
                {
                    var track = tracks.Content[i];

                    if (i == 0)
                    {
                        var albumImages =
                            await this._lastFmService.GetAlbumImagesAsync(track.ArtistName, track.AlbumName);

                        if (albumImages?.Medium != null)
                        {
                            this._embed.WithThumbnailUrl(albumImages.Medium.ToString());
                        }
                    }

                    fmRecentText += $"`{i + 1}` {LastFMService.TrackToLinkedString(track)}\n";
                }

                this._embed.WithDescription(fmRecentText);

                string footerText;
                if (tracks.Content[0].IsNowPlaying == true)
                {
                    footerText =
                        $"{userInfo.Content.Name} has {userInfo.Content.Playcount} scrobbles - Now Playing";
                }
                else
                {
                    footerText =
                        $"{userInfo.Content.Name} has {userInfo.Content.Playcount} scrobbles";
                    if (tracks.Content[0].TimePlayed.HasValue)
                    {
                        footerText += " - Last scrobble:";
                        this._embed.WithTimestamp(tracks.Content[0].TimePlayed.Value);
                    }
                }

                this._embedFooter.WithText(footerText);

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

        [Command("toptracks", RunMode = RunMode.Async)]
        [Summary("Displays top tracks.")]
        [Alias("tt", "tl", "tracklist", "tracks", "trackslist")]
        [LoginRequired]
        public async Task TopTracksAsync(string time = "weekly", int num = 8, string user = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.CommandPrefix;

            if (time == "help")
            {
                await ReplyAsync(
                    $"Usage: `.fmtoptracks '{Constants.CompactTimePeriodList}' 'number of tracks (max 12)' 'lastfm username/discord user'`");
                return;
            }

            if (!Enum.TryParse(time, true, out ChartTimePeriod timePeriod))
            {
                await ReplyAsync($"Invalid time period. Please use {Constants.ExpandedTimePeriodList}. \n" +
                                 $"Usage: `.fmtoptracks '{Constants.CompactTimePeriodList}' 'number of tracks (max 12)' 'lastfm username/discord user'`");
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            if (num > 12)
            {
                num = 12;
            }
            if (num < 1)
            {
                num = 1;
            }

            var timeSpan = LastFMService.ChartTimePeriodToCallTimePeriod(timePeriod);

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

                var topTracks = await this._lastFmService.GetTopTracksAsync(lastFMUserName, timeSpan, num);
                var userUrl = $"{Constants.LastFMUserUrl}{lastFMUserName}/library/tracks?date_preset={LastFMService.ChartTimePeriodToSiteTimePeriodUrl(timePeriod)}";

                if (!topTracks.Success )
                {
                    this._embed.ErrorResponse(topTracks.Error.Value, topTracks.Message, this.Context, this._logger);
                    await ReplyAsync("", false, this._embed.Build());
                    return;
                }
                if (!topTracks.Content.TopTracks.Track.Any())
                {
                    this._embed.WithDescription("No top tracks returned for selected time period.\n" +
                                                $"View [track history here]{userUrl}");
                    this._embed.WithColor(Constants.WarningColorOrange);
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
                var artistsString = num == 1 ? "track" : "tracks";
                this._embedAuthor.WithName($"Top {num} {timePeriod} {artistsString} for {userTitle}");
                this._embedAuthor.WithUrl(userUrl);
                this._embed.WithAuthor(this._embedAuthor);

                var description = "";
                for (var i = 0; i < topTracks.Content.TopTracks.Track.Count; i++)
                {
                    var track = topTracks.Content.TopTracks.Track[i];

                    description += $"{i + 1}. [{track.Artist.Name}]({track.Artist.Url}) - [{track.Name}]({track.Url}) ({track.Playcount} plays) \n";
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
