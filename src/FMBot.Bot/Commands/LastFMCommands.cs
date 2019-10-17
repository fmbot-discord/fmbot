using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Data.Entities;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Configurations;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using static FMBot.Bot.FMBotUtil;
using static FMBot.Bot.Models.LastFMModels;

namespace FMBot.Bot.Commands
{
    public class LastFMCommands : ModuleBase
    {
        private readonly TimerService _timer;
        private readonly Logger.Logger _logger;

        private readonly UserService _userService = new UserService();
        private readonly GuildService _guildService = new GuildService();
        private readonly LastFMService _lastFmService = new LastFMService();
        private readonly FriendsService _friendsService = new FriendsService();

        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;

        public LastFMCommands(TimerService timer, Logger.Logger logger)
        {
            _timer = timer;
            _logger = logger;
            _embed = new EmbedBuilder();
            _embedAuthor = new EmbedAuthorBuilder();
            _embedFooter = new EmbedFooterBuilder();
        }

        private async Task SendChartMessage(FMBotChart chart)
        {
            await _lastFmService.GenerateChartAsync(chart);

            // Send chart memory stream, remove when finished
            using (MemoryStream memory = await GlobalVars.GetChartStreamAsync(chart.DiscordUser.Id))
            {
                await Context.Channel.SendFileAsync(memory, "chart.png");
            }

            lock (GlobalVars.charts.SyncRoot)
            {
                // @TODO: remove only once in a while to keep it cached
                GlobalVars.charts.Remove(GlobalVars.GetChartFileName(chart.DiscordUser.Id));
            }
        }

        [Command("fm", RunMode = RunMode.Async), Summary("Displays what a user is listening to.")]
        [Alias("qm", "wm", "em", "rm", "tm", "ym", "um", "im", "om", "pm", "dm", "gm", "sm", "am", "hm", "jm", "km", "lm", "zm", "xm", "cm", "vm", "bm", "nm", "mm", "lastfm")]
        public async Task fmAsync(string user = null)
        {
            User userSettings = await _userService.GetUserSettingsAsync(Context.User);

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
                this._logger.LogCommandUsed(Context.Guild?.Id, Context.Channel.Id, Context.User.Id, Context.Message.Content);
                return;
            }

            try
            {
                string lastFMUserName = userSettings.UserNameLastFM;
                bool self = true;

                if (user != null)
                {
                    var alternativeLastFmUserName = await FindUser(user);
                    if (!string.IsNullOrEmpty(alternativeLastFmUserName))
                    {
                        lastFMUserName = alternativeLastFmUserName;
                        self = false;
                    }
                }

                PageResponse<LastTrack> tracks = await _lastFmService.GetRecentScrobblesAsync(lastFMUserName);

                if (tracks?.Any() != true)
                {
                    await NoScrobblesErrorResponseFoundAsync(tracks.Status);
                    return;
                }

                LastResponse<LastUser> userinfo = await _lastFmService.GetUserInfoAsync(lastFMUserName);

                LastTrack currentTrack = tracks.Content[0];
                LastTrack lastTrack = tracks.Content[1];

                const string nullText = "[undefined]";

                string trackName = currentTrack.Name;
                string artistName = currentTrack.ArtistName;
                string albumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nullText : currentTrack.AlbumName;

                string lastTrackName = lastTrack.Name;
                string lastTrackArtistName = lastTrack.ArtistName;
                string lastTrackAlbumName = string.IsNullOrWhiteSpace(lastTrack.AlbumName) ? nullText : lastTrack.AlbumName;

                int playCount = userinfo.Content.Playcount;

                switch (userSettings.ChartType)
                {
                    case ChartType.textmini:
                        await Context.Channel.SendMessageAsync(await _userService.GetUserTitleAsync(Context)
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
                        await Context.Channel.SendMessageAsync(await _userService.GetUserTitleAsync(Context)
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
                        if (!_guildService.CheckIfDM(Context))
                        {
                            GuildPermissions perms = await _guildService.CheckSufficientPermissionsAsync(Context);
                            if (!perms.EmbedLinks)
                            {
                                await ReplyAsync("Insufficient permissions, I need to the 'Embed links' permission to show you your scrobbles.");
                                break;
                            }
                        }

                        string userTitle;
                        if (self)
                        {
                            userTitle = await _userService.GetUserTitleAsync(Context);
                        }
                        else
                        {
                            userTitle = $"{lastFMUserName}, requested by {await _userService.GetUserTitleAsync(Context)}";
                        }

                        this._embed.AddField(
                            $"Current: {tracks.Content[0].Name}",
                            $"By **{tracks.Content[0].ArtistName}**" + (string.IsNullOrEmpty(tracks.Content[0].AlbumName) ? "" : $" | {tracks.Content[0].AlbumName}"));

                        if (userSettings.ChartType == ChartType.embedfull)
                        {
                            this._embedAuthor.WithName("Last tracks for " + userTitle);
                            this._embed.AddField(
                                $"Previous: {tracks.Content[1].Name}",
                                $"By **{tracks.Content[1].ArtistName}**" + (string.IsNullOrEmpty(tracks.Content[1].AlbumName) ? "" : $" | {tracks.Content[1].AlbumName}"));
                        }
                        else
                        {
                            this._embedAuthor.WithName("Last track for " + userTitle);
                        }

                        this._embed.WithTitle(tracks.Content[0].IsNowPlaying == true
                            ? "*Now playing*"
                            : $"Last scrobble {tracks.Content[0].TimePlayed?.ToString("g")} (UTC)");

                        this._embedAuthor.WithIconUrl(Context.User.GetAvatarUrl());
                        this._embed.WithAuthor(this._embedAuthor);
                        this._embed.WithUrl("https://www.last.fm/user/" + lastFMUserName);

                        LastImageSet AlbumImages = await _lastFmService.GetAlbumImagesAsync(currentTrack.ArtistName, currentTrack.AlbumName);

                        if (AlbumImages?.Medium != null)
                        {
                            this._embed.WithThumbnailUrl(AlbumImages.Medium.ToString());
                        }

                        this._embedFooter.WithText($"{userinfo.Content.Name} has {userinfo.Content.Playcount} scrobbles.");
                        this._embed.WithFooter(this._embedFooter);

                        this._embed.WithColor(Constants.LastFMColorRed);
                        await ReplyAsync("", false, this._embed.Build());
                        break;
                }
                this._logger.LogCommandUsed(Context.Guild?.Id, Context.Channel.Id, Context.User.Id, Context.Message.Content);
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message, Context.Message.Content, Context.User.Username, Context.Guild?.Name, Context.Guild?.Id);
                await ReplyAsync("Unable to show Last.FM info due to an internal error. Try scrobbling something then use the command again.");
            }
        }



        [Command("fmartists", RunMode = RunMode.Async), Summary("Displays top artists.")]
        [Alias("fmartist", "fmartistlist", "fmartistslist")]
        public async Task fmArtistsAsync(string time = "weekly", int num = 6, string user = null)
        {
            User userSettings = await _userService.GetUserSettingsAsync(Context.User);

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

            if (!Enum.TryParse(time, ignoreCase: true, out ChartTimePeriod timePeriod))
            {
                await ReplyAsync("Invalid time period. Please use 'weekly', 'monthly', 'yearly', or 'alltime'.");
                return;
            }

            if (num > 10)
            {
                num = 10;
            }

            LastStatsTimeSpan timeSpan = _lastFmService.GetLastStatsTimeSpan(timePeriod);

            try
            {
                string lastFMUserName = userSettings.UserNameLastFM;

                if (user != null)
                {
                    if (await _lastFmService.LastFMUserExistsAsync(user))
                    {
                        lastFMUserName = user;
                    }
                    else if (!_guildService.CheckIfDM(Context))
                    {
                        IGuildUser guildUser = await _guildService.FindUserFromGuildAsync(Context, user);

                        if (guildUser != null)
                        {
                            User guildUserLastFM = await _userService.GetUserSettingsAsync(guildUser);

                            if (guildUserLastFM?.UserNameLastFM != null)
                            {
                                lastFMUserName = guildUserLastFM.UserNameLastFM;
                            }
                        }
                    }
                }

                PageResponse<LastArtist> artists = await _lastFmService.GetTopArtistsAsync(lastFMUserName, timeSpan, num);

                if (artists?.Any() != true)
                {
                    await ReplyAsync("No artists found on this profile. (" + lastFMUserName + ")");
                    return;
                }

                EmbedAuthorBuilder eab = new EmbedAuthorBuilder
                {
                    IconUrl = Context.User.GetAvatarUrl(),
                    Name = userSettings.UserNameLastFM
                };

                EmbedBuilder builder = new EmbedBuilder
                {
                    Color = new Discord.Color(186, 0, 0),
                };

                builder.WithUrl("https://www.last.fm/user/" + lastFMUserName + "/library/artists");
                builder.Title = lastFMUserName + " top " + num + " artists (" + timePeriod + ")";

                int indexval = (num - 1);
                for (int i = 0; i <= indexval; i++)
                {
                    LastArtist artist = artists.Content[i];

                    string artistName = artist.Name;

                    int correctnum = (i + 1);
                    builder.AddField("#" + correctnum + ": " + artist.Name, artist.PlayCount.Value.ToString("N0") + " times scrobbled");
                }

                EmbedFooterBuilder embedFooter = new EmbedFooterBuilder();

                LastResponse<LastUser> userinfo = await _lastFmService.GetUserInfoAsync(lastFMUserName);

                embedFooter.Text = lastFMUserName + "'s total scrobbles: " + userinfo.Content.Playcount.ToString("N0");

                builder.WithFooter(embedFooter);

                await Context.Channel.SendMessageAsync("", false, builder.Build());
                this._logger.LogCommandUsed(Context.Guild?.Id, Context.Channel.Id, Context.User.Id, Context.Message.Content);
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message, Context.Message.Content, Context.User.Username, Context.Guild?.Name, Context.Guild?.Id);
                await ReplyAsync("Unable to show Last.FM info due to an internal error.");
            }
        }


        [Command("fmchart", RunMode = RunMode.Async), Summary("Generates a chart based on a user's parameters.")]
        public async Task fmchartAsync(string chartSize = "3x3", string time = "weekly", string titleSetting = null, string user = null)
        {
            if (chartSize == "help")
            {
                await ReplyAsync("fmchart '2x2-8x8' 'weekly/monthly/yearly/overall' 'notitles/titles' 'user'");
                return;
            }

            User userSettings = await _userService.GetUserSettingsAsync(Context.User);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

            var lastFMUserName = userSettings.UserNameLastFM;
            bool self = true;

            if (!_guildService.CheckIfDM(Context))
            {
                GuildPermissions perms = await _guildService.CheckSufficientPermissionsAsync(Context);
                if (!perms.AttachFiles)
                {
                    await ReplyAsync("I'm missing the 'Attach files' permission in this server, so I can't post a chart.");
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
                        await ReplyAsync($"Your chart's size isn't valid. Sizes supported: 3x3-8x8. \n" +
                                         $"Example: `{ConfigData.Data.CommandPrefix}fmchart 5x5 monthly titles`. For more info, use `.fmchart help`");
                        return;
                }

                _ = Context.Channel.TriggerTypingAsync();

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
                List<Bitmap> images = new List<Bitmap>();

                var timespan = _lastFmService.StringToLastStatsTimeSpan(time);
                PageResponse<LastAlbum> albums = await _lastFmService.GetTopAlbumsAsync(lastFMUserName, timespan, chartAlbums);

                if (albums.Count() < chartAlbums)
                {
                    await ReplyAsync($"User hasn't listened to enough albums ({albums.Count()} of required {chartAlbums}) for a chart this size. " +
                                     "Please try a smaller chart or a bigger time period (weekly/monthly/yearly/overall)'.");
                    return;
                }

                FMBotChart chart = new FMBotChart
                {
                    albums = albums,
                    time = time,
                    LastFMName = lastFMUserName,
                    max = chartAlbums,
                    rows = chartRows,
                    images = images,
                    DiscordUser = Context.User,
                    disclient = Context.Client as DiscordSocketClient,
                    mode = 0,
                    titles = titleSetting == null ? userSettings.TitlesEnabled ?? true : titleSetting == "titles",
                };

                await _userService.ResetChartTimerAsync(userSettings);

                await SendChartMessage(chart);

                // Adding extra infobox
                this._embedAuthor.WithIconUrl(Context.User.GetAvatarUrl());

                this._embed.WithColor(Constants.LastFMColorRed);
                this._embed.WithAuthor(this._embedAuthor);
                var URI = "https://www.last.fm/user/" + lastFMUserName;
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
                    this._embedAuthor.WithName(chartDescription + " for " + await _userService.GetUserTitleAsync(Context));
                }
                else
                {
                    this._embedAuthor.WithName($"{chartDescription} for {lastFMUserName}, requested by {await _userService.GetUserTitleAsync(Context)}");
                }

                LastResponse<LastUser> userInfo = await _lastFmService.GetUserInfoAsync(lastFMUserName);

                int playCount = userInfo.Content.Playcount;

                this._embedFooter.Text = $"{lastFMUserName} has {playCount} scrobbles.";
                this._embed.WithFooter(this._embedFooter);

                await Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this._logger.LogCommandUsed(Context.Guild?.Id, Context.Channel.Id, Context.User.Id, Context.Message.Content);
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message, Context.Message.Content, Context.User.Username, Context.Guild?.Name, Context.Guild?.Id);
                await ReplyAsync("Sorry, but I was unable to generate a FMChart due to an internal error. Make sure you have scrobbles and Last.FM isn't having issues, and try again later.");
            }
        }

        [Command("fmrecent", RunMode = RunMode.Async), Summary("Displays a user's recent tracks.")]
        [Alias("fmrecenttracks")]
        public async Task fmrecentAsync(string user = null, int num = 5)
        {
            User userSettings = await _userService.GetUserSettingsAsync(Context.User);

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
                string lastFMUserName = userSettings.UserNameLastFM;
                bool self = true;

                if (user != null)
                {
                    if (await _lastFmService.LastFMUserExistsAsync(user))
                    {
                        lastFMUserName = user;
                        self = false;
                    }
                    else if (!_guildService.CheckIfDM(Context))
                    {
                        IGuildUser guildUser = await _guildService.FindUserFromGuildAsync(Context, user);

                        if (guildUser != null)
                        {
                            User guildUserLastFM = await _userService.GetUserSettingsAsync(guildUser);

                            if (guildUserLastFM?.UserNameLastFM != null)
                            {
                                lastFMUserName = guildUserLastFM.UserNameLastFM;
                                self = false;
                            }
                        }
                    }
                }

                PageResponse<LastTrack> tracks = await _lastFmService.GetRecentScrobblesAsync(lastFMUserName, num);

                EmbedBuilder builder = new EmbedBuilder
                {
                    Color = new Discord.Color(186, 0, 0)
                };

                if (self)
                {
                    builder.WithAuthor(new EmbedAuthorBuilder
                    {
                        IconUrl = Context.User.GetAvatarUrl(),
                        Name = $"Last {num} tracks for {await _userService.GetUserTitleAsync(Context)}"
                    });
                }
                else
                {
                    builder.WithAuthor(new EmbedAuthorBuilder
                    {
                        Name = $"Last {num} tracks for {lastFMUserName} requested by {await _userService.GetUserTitleAsync(Context)}"
                    });
                }

                builder.WithTitle(tracks.Content[0].IsNowPlaying == true
                    ? "*Now playing*"
                    : $"Last scrobble {tracks.Content[0].TimePlayed?.ToString("g")} (UTC)");
                builder.WithUrl("https://www.last.fm/user/" + lastFMUserName);

                int indexval = (num - 1);
                for (int i = 0; i <= indexval; i++)
                {
                    LastTrack track = tracks.Content[i];

                    string TrackName = track.Name;
                    string ArtistName =  track.ArtistName;
                    string AlbumName = track.AlbumName;

                    if (i == 0)
                    {
                        LastImageSet AlbumImages = await _lastFmService.GetAlbumImagesAsync(ArtistName, AlbumName);

                        if (AlbumImages?.Medium != null)
                        {
                            builder.WithThumbnailUrl(AlbumImages.Medium.ToString());
                        }
                    }

                    int correctnum = (i + 1);
                    builder.AddField("#" + correctnum + ": " + TrackName, $"By **{ArtistName}**" + (string.IsNullOrEmpty(AlbumName) ? "" :$" | {AlbumName}"));
                }

                EmbedFooterBuilder efb = new EmbedFooterBuilder();

                LastResponse<LastUser> userinfo = await _lastFmService.GetUserInfoAsync(lastFMUserName);
                int playcount = userinfo.Content.Playcount;

                efb.Text = lastFMUserName + "'s total scrobbles: " + playcount.ToString("0");

                builder.WithFooter(efb);

                await Context.Channel.SendMessageAsync("", false, builder.Build());
                this._logger.LogCommandUsed(Context.Guild?.Id, Context.Channel.Id, Context.User.Id, Context.Message.Content);
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message, Context.Message.Content, Context.User.Username, Context.Guild?.Name, Context.Guild?.Id);
                await ReplyAsync("Unable to show your recent tracks on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("fmstats", RunMode = RunMode.Async), Summary("Displays user stats related to Last.FM and FMBot")]
        [Alias("fminfo")]
        public async Task fmstatsAsync(string user = null)
        {
            User userSettings = await _userService.GetUserSettingsAsync(Context.User);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

            try
            {
                string lastFMUserName = userSettings.UserNameLastFM;

                if (user != null)
                {
                    if (await _lastFmService.LastFMUserExistsAsync(user))
                    {
                        lastFMUserName = user;
                    }
                    else if (!_guildService.CheckIfDM(Context))
                    {
                        IGuildUser guildUser = await _guildService.FindUserFromGuildAsync(Context, user);

                        if (guildUser != null)
                        {
                            User guildUserLastFM = await _userService.GetUserSettingsAsync(guildUser);

                            if (guildUserLastFM?.UserNameLastFM != null)
                            {
                                lastFMUserName = guildUserLastFM.UserNameLastFM;
                            }
                        }
                    }
                }

                EmbedAuthorBuilder eab = new EmbedAuthorBuilder
                {
                    IconUrl = Context.User.GetAvatarUrl(),
                    Name = lastFMUserName
                };

                EmbedBuilder builder = new EmbedBuilder
                {
                    Color = new Discord.Color(186, 0, 0)
                };

                builder.WithAuthor(eab);
                builder.WithUrl("https://www.last.fm/user/" + lastFMUserName);

                builder.Title = await _userService.GetUserTitleAsync(Context);
                builder.WithDescription("Last.FM Statistics for " + lastFMUserName);

                LastResponse<LastUser> userinfo = await _lastFmService.GetUserInfoAsync(lastFMUserName);

                LastImageSet userinfoImages = userinfo.Content.Avatar;
                string userinfoThumbnail = userinfoImages?.Large.AbsoluteUri;
                string ThumbnailImage = userinfoThumbnail ?? null;

                if (!string.IsNullOrWhiteSpace(ThumbnailImage))
                {
                    builder.WithThumbnailUrl(ThumbnailImage);
                }


                int playcount = userinfo.Content.Playcount;
                string usertype = userinfo.Content.Type;
                int playlists = userinfo.Content.Playlists;
                bool premium = userinfo.Content.IsSubscriber;


                builder.AddField("Last.FM Name: ", lastFMUserName);
                builder.AddField("Chart Mode: ", userSettings.ChartType);
                builder.AddField("User Type: ", usertype);
                builder.AddField("Total scrobbles: ", playcount.ToString("0"));
                builder.AddField("Has Last.FM Premium? ", premium.ToString());
                builder.AddField("Bot user type: ", userSettings.UserType);

                await Context.Channel.SendMessageAsync("", false, builder.Build());
                this._logger.LogCommandUsed(Context.Guild?.Id, Context.Channel.Id, Context.User.Id, Context.Message.Content);
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message, Context.Message.Content, Context.User.Username, Context.Guild?.Name, Context.Guild?.Id);
                await ReplyAsync("Unable to show your stats on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("fmfeatured", RunMode = RunMode.Async), Summary("Displays the featured avatar.")]
        [Alias("fmfeaturedavatar", "fmfeatureduser", "fmfeaturedalbum")]
        public async Task fmfeaturedAsync()
        {
            try
            {
                EmbedBuilder builder = new EmbedBuilder
                {
                    Color = new Discord.Color(186, 0, 0)
                };

                ISelfUser SelfUser = Context.Client.CurrentUser;
                builder.WithThumbnailUrl(SelfUser.GetAvatarUrl());
                builder.AddField("Featured:", _timer.GetTrackString());

                await Context.Channel.SendMessageAsync("", false, builder.Build());
                this._logger.LogCommandUsed(Context.Guild?.Id, Context.Channel.Id, Context.User.Id, Context.Message.Content);
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message, Context.Message.Content, Context.User.Username, Context.Guild?.Name, Context.Guild?.Id);
                await ReplyAsync("Unable to show the featured avatar on FMBot due to an internal error. The timer service cannot be loaded. Please wait for the bot to fully load.");
            }
        }

        [Command("fmset", RunMode = RunMode.Async), Summary("Sets your Last.FM name and FM mode.")]
        [Alias("fmsetname", "fmsetmode")]
        public async Task fmsetAsync([Summary("Your Last.FM name")] string lastFMUserName, [Summary("The mode you want to use.")] string chartType = "embedfull")
        {
            if (lastFMUserName == "help")
            {
                await ReplyAsync(ConfigData.Data.CommandPrefix + "fmset 'Last.FM Username' 'embedmini/embedfull/textmini/textfull'");
                return;
            }

            if (!await _lastFmService.LastFMUserExistsAsync(lastFMUserName.Replace("'", "")))
            {
                await ReplyAsync("LastFM user could not be found. Please check if the name you entered is correct.");
                return;
            }

            if (!Enum.TryParse(chartType.Replace("'", ""), ignoreCase: true, out ChartType chartTypeEnum))
            {
                await ReplyAsync("Invalid mode. Please use 'embedmini', 'embedfull', 'textfull', or 'textmini'.");
                return;
            }

            _userService.SetLastFM(Context.User, lastFMUserName.Replace("'", ""), chartTypeEnum);

            await ReplyAsync("Your Last.FM name has been set to '" + lastFMUserName.Replace("'", "") + "' and your mode has been set to '" + chartType + "'.");
            this._logger.LogCommandUsed(Context.Guild?.Id, Context.Channel.Id, Context.User.Id, Context.Message.Content);

            if (!_guildService.CheckIfDM(Context))
            {
                GuildPermissions perms = await _guildService.CheckSufficientPermissionsAsync(Context);
                if (!perms.EmbedLinks || !perms.AttachFiles)
                {
                    await ReplyAsync("Please note that the bot also needs the 'Attach files' and 'Embed links' permissions for most commands. One or both of these permissions are currently missing.");
                    return;
                }
            }
        }

        [Command("fmremove", RunMode = RunMode.Async), Summary("Deletes your FMBot data.")]
        [Alias("fmdelete", "fmremovedata", "fmdeletedata")]
        public async Task fmremoveAsync()
        {
            User userSettings = await _userService.GetUserSettingsAsync(Context.User);

            if (userSettings == null)
            {
                await ReplyAsync("Sorry, but we don't have any data from you in our database.");
                return;
            }

            await _friendsService.RemoveAllLastFMFriendsAsync(userSettings.UserID);
            await _userService.DeleteUser(userSettings.UserID);

            await ReplyAsync("Your settings, friends and any other data have been successfully deleted.");
            this._logger.LogCommandUsed(Context.Guild?.Id, Context.Channel.Id, Context.User.Id, Context.Message.Content);
        }

        [Command("fmsuggest", RunMode = RunMode.Async), Summary("Suggest features you want to see in the bot, or report inappropriate images.")]
        [Alias("fmreport", "fmsuggestion", "fmsuggestions")]
        public async Task fmsuggest(string suggestion = null)
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
                DiscordSocketClient client = Context.Client as DiscordSocketClient;

                ulong BroadcastServerID = Convert.ToUInt64(ConfigData.Data.BaseServer);
                ulong BroadcastChannelID = Convert.ToUInt64(ConfigData.Data.SuggestionsChannel);

                SocketGuild guild = client.GetGuild(BroadcastServerID);
                SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                EmbedBuilder builder = new EmbedBuilder();
                EmbedAuthorBuilder eab = new EmbedAuthorBuilder
                {
                    IconUrl = Context.User.GetAvatarUrl(),
                    Name = Context.User.Username
                };
                builder.WithAuthor(eab);
                builder.WithTitle(Context.User.Username + "'s suggestion:");
                builder.WithDescription(suggestion);

                await channel.SendMessageAsync("", false, builder.Build());

                await ReplyAsync("Your suggestion has been sent to the .fmbot server!");
                this._logger.LogCommandUsed(Context.Guild?.Id, Context.Channel.Id, Context.User.Id, Context.Message.Content);

                //}
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message, Context.Message.Content, Context.User.Username, Context.Guild?.Name, Context.Guild?.Id);
            }
        }

        private async Task UsernameNotSetErrorResponseAsync()
        {
            this._embed.WithTitle("Error while attempting get Last.FM information");
            this._embed.WithDescription("Last.FM username has not been set. \n" +
                                        "To setup your Last.FM account with this bot, please use the `.fmset` command. \n" +
                                        $"Usage: `{ConfigData.Data.CommandPrefix}fmset username mode`\n" +
                                        "Possible modes: embedmini/embedfull/textmini/textfull");

            this._embed.WithColor(Constants.WarningColorOrange);
            await ReplyAsync("", false, this._embed.Build());
            this._logger.LogError("Last.FM username not set", Context.Message.Content, Context.User.Username, Context.Guild?.Name, Context.Guild?.Id);
        }

        private async Task NoScrobblesErrorResponseFoundAsync(LastResponseStatus apiResponse)
        {
            this._embed.WithTitle("Error while attempting get Last.FM information");
            switch (apiResponse)
            {
                case LastResponseStatus.Failure:
                    this._embed.WithDescription("Last.FM is having issues. Please try again later.");
                    break;
                default:
                    this._embed.WithDescription("You have no scrobbles on your profile, or Last.FM is having issues. Please try again later.");
                    break;
            }

            this._embed.WithColor(Constants.WarningColorOrange);
            await ReplyAsync("", false, this._embed.Build());
            this._logger.LogError("No scrobbles found for user", Context.Message.Content, Context.User.Username, Context.Guild?.Name, Context.Guild?.Id);
        }

        private async Task<string> FindUser(string user)
        {
            if (await _lastFmService.LastFMUserExistsAsync(user))
            {
                return user;
            }

            if (!_guildService.CheckIfDM(Context))
            {
                IGuildUser guildUser = await _guildService.FindUserFromGuildAsync(Context, user);

                if (guildUser != null)
                {
                    User guildUserLastFm = await _userService.GetUserSettingsAsync(guildUser);

                    return guildUserLastFm?.UserNameLastFM;
                }
            }

            return null;
        }
    }
}
