using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Data.Entities;
using FMBot.Services;
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
using static FMBot.Bot.FMBotModules;
using static FMBot.Bot.FMBotUtil;
using static FMBot.Bot.Models.LastFMModels;

namespace FMBot.Bot.Commands
{
    public class LastFMCommands : ModuleBase
    {
        private readonly TimerService _timer;

        private readonly UserService _userService = new UserService();
        private readonly GuildService _guildService = new GuildService();
        private readonly LastFMService _lastFmService = new LastFMService();
        private readonly FriendsService _friendsService = new FriendsService();

        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;

        public LastFMCommands(TimerService timer)
        {
            _timer = timer;
            _embed = new EmbedBuilder();
            _embedAuthor = new EmbedAuthorBuilder();
            _embedFooter = new EmbedFooterBuilder();
        }

        private async Task SendChartMessage(FMBotChart chart)
        {
            await _lastFmService.GenerateChartAsync(chart).ConfigureAwait(false);

            // Send chart memory stream, remove when finished

            using (MemoryStream memory = await GlobalVars.GetChartStreamAsync(chart.DiscordUser.Id).ConfigureAwait(false))
            {
                await Context.Channel.SendFileAsync(memory, "chart.png").ConfigureAwait(false);
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
            User userSettings = await _userService.GetUserSettingsAsync(Context.User).ConfigureAwait(false);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }
            if (user == "help")
            {
                await ReplyAsync(
                    "Usage: `.fm 'lastfm username/discord user'` \n" +
                    "You can set your default user and your display mode through the `.fmset 'username' 'embedfull/embedmini/textfull/textmini'` command.").ConfigureAwait(false);
                return;
            }

            try
            {
                string lastFMUserName = userSettings.UserNameLastFM;
                bool self = true;

                if (user != null)
                {
                    if (await _lastFmService.LastFMUserExistsAsync(user).ConfigureAwait(false))
                    {
                        lastFMUserName = user;
                        self = false;
                    }
                    else if (!_guildService.CheckIfDM(Context))
                    {
                        IGuildUser guildUser = await _guildService.FindUserFromGuildAsync(Context, user).ConfigureAwait(false);

                        if (guildUser != null)
                        {
                            User guildUserLastFM = await _userService.GetUserSettingsAsync(guildUser).ConfigureAwait(false);

                            if (guildUserLastFM?.UserNameLastFM != null)
                            {
                                lastFMUserName = guildUserLastFM.UserNameLastFM;
                                self = false;
                            }
                        }
                    }
                }


                PageResponse<LastTrack> tracks = await _lastFmService.GetRecentScrobblesAsync(lastFMUserName).ConfigureAwait(false);

                if (tracks?.Any() != true)
                {
                    await NoScrobblesErrorResponseFoundAsync();
                    return;
                }

                LastResponse<LastUser> userinfo = await _lastFmService.GetUserInfoAsync(lastFMUserName);

                LastTrack currentTrack = tracks.Content[0];
                LastTrack lastTrack = tracks.Content[1];

                const string nulltext = "[undefined]";

                if (userSettings.ChartType == ChartType.embedmini || userSettings.ChartType == ChartType.embedfull)
                {
                    if (!_guildService.CheckIfDM(Context))
                    {
                        GuildPermissions perms = await _guildService.CheckSufficientPermissionsAsync(Context).ConfigureAwait(false);
                        if (!perms.EmbedLinks)
                        {
                            await ReplyAsync("Insufficient permissions, I need to the 'Embed links' permission to show you your scrobbles.").ConfigureAwait(false);
                            return;
                        }
                    }

                    var userTitle = await _userService.GetUserTitleAsync(Context);

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
                        : $"Last scrobble {tracks.Content[0].TimePlayed?.ToString("g")}");

                    this._embedAuthor.WithIconUrl(Context.User.GetAvatarUrl());
                    this._embed.WithAuthor(this._embedAuthor);
                    this._embed.WithUrl("https://www.last.fm/user/" + lastFMUserName);

                    this._embedFooter.WithText($"{userinfo.Content.Name} has {userinfo.Content.Playcount} scrobbles.");
                    this._embed.WithFooter(this._embedFooter);

                    this._embed.WithColor(Constants.LastFMColorRed);
                    await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
                }
                else if (userSettings.ChartType == ChartType.textfull)
                {
                    string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                    string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                    string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                    string LastTrackName = string.IsNullOrWhiteSpace(lastTrack.Name) ? nulltext : lastTrack.Name;
                    string LastArtistName = string.IsNullOrWhiteSpace(lastTrack.ArtistName) ? nulltext : lastTrack.ArtistName;
                    string LastAlbumName = string.IsNullOrWhiteSpace(lastTrack.AlbumName) ? nulltext : lastTrack.AlbumName;


                    int playcount = userinfo.Content.Playcount;

                    await Context.Channel.SendMessageAsync(await _userService.GetUserTitleAsync(Context).ConfigureAwait(false)
                                                           + "\n**Current** - "
                                                           + ArtistName
                                                           + " - "
                                                           + TrackName
                                                           + " ["
                                                           + AlbumName
                                                           + "]\n**Previous** - "
                                                           + LastArtistName
                                                           + " - "
                                                           + LastTrackName
                                                           + " ["
                                                           + LastAlbumName
                                                           + "]\n<https://www.last.fm/user/"
                                                           + userSettings.UserNameLastFM
                                                           + ">\n"
                                                           + userSettings.UserNameLastFM
                                                           + "'s total scrobbles: "
                                                           + playcount.ToString("N0")).ConfigureAwait(false);
                }
                else if (userSettings.ChartType == ChartType.textmini)
                {
                    string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                    string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                    string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                    string LastTrackName = string.IsNullOrWhiteSpace(lastTrack.Name) ? nulltext : lastTrack.Name;
                    string LastArtistName = string.IsNullOrWhiteSpace(lastTrack.ArtistName) ? nulltext : lastTrack.ArtistName;
                    string LastAlbumName = string.IsNullOrWhiteSpace(lastTrack.AlbumName) ? nulltext : lastTrack.AlbumName;

                    int playcount = userinfo.Content.Playcount;

                    await Context.Channel.SendMessageAsync(await _userService.GetUserTitleAsync(Context).ConfigureAwait(false)
                                                           + "\n**Current** - "
                                                           + ArtistName
                                                           + " - "
                                                           + TrackName
                                                           + " ["
                                                           + AlbumName
                                                           + "]\n<https://www.last.fm/user/"
                                                           + userSettings.UserNameLastFM
                                                           + ">\n"
                                                           + userSettings.UserNameLastFM
                                                           + "'s total scrobbles: "
                                                           + playcount.ToString("N0")).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);
                await ReplyAsync("Unable to show Last.FM info due to an internal error. Try scrobbling something then use the command again.").ConfigureAwait(false);
            }
        }



        [Command("fmartists", RunMode = RunMode.Async), Summary("Displays top artists.")]
        [Alias("fmartist", "fmartistlist", "fmartistslist")]
        public async Task fmArtistsAsync(string time = "weekly", int num = 6, string user = null)
        {
            User userSettings = await _userService.GetUserSettingsAsync(Context.User).ConfigureAwait(false);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }
            if (time == "help")
            {
                await ReplyAsync(
                    "Usage: `.fmartists 'weekly/monthly/yearly/alltime' 'number of artists (max 10)'` \n" +
                    "You can set your default user and your display mode through the `.fmset 'username' 'embedfull/embedmini/textfull/textmini'` command.").ConfigureAwait(false);
                return;
            }

            if (!Enum.TryParse(time, ignoreCase: true, out ChartTimePeriod timePeriod))
            {
                await ReplyAsync("Invalid time period. Please use 'weekly', 'monthly', 'yearly', or 'alltime'.").ConfigureAwait(false);
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
                    if (await _lastFmService.LastFMUserExistsAsync(user).ConfigureAwait(false))
                    {
                        lastFMUserName = user;
                    }
                    else if (!_guildService.CheckIfDM(Context))
                    {
                        IGuildUser guildUser = await _guildService.FindUserFromGuildAsync(Context, user).ConfigureAwait(false);

                        if (guildUser != null)
                        {
                            User guildUserLastFM = await _userService.GetUserSettingsAsync(guildUser).ConfigureAwait(false);

                            if (guildUserLastFM?.UserNameLastFM != null)
                            {
                                lastFMUserName = guildUserLastFM.UserNameLastFM;
                            }
                        }
                    }
                }

                PageResponse<LastArtist> artists = await _lastFmService.GetTopArtistsAsync(lastFMUserName, timeSpan, num).ConfigureAwait(false);

                if (artists?.Any() != true)
                {
                    await ReplyAsync("No artists found on this profile. (" + lastFMUserName + ")").ConfigureAwait(false);
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

                const string nulltext = "[undefined]";
                int indexval = (num - 1);
                for (int i = 0; i <= indexval; i++)
                {
                    LastArtist artist = artists.Content[i];

                    string ArtistName = string.IsNullOrWhiteSpace(artist.Name) ? nulltext : artist.Name;

                    int correctnum = (i + 1);
                    builder.AddField("#" + correctnum + ": " + artist.Name, artist.PlayCount.Value.ToString("N0") + " times scrobbled");
                }

                EmbedFooterBuilder embedFooter = new EmbedFooterBuilder();

                LastResponse<LastUser> userinfo = await _lastFmService.GetUserInfoAsync(lastFMUserName).ConfigureAwait(false);

                embedFooter.Text = lastFMUserName + "'s total scrobbles: " + userinfo.Content.Playcount.ToString("N0");

                builder.WithFooter(embedFooter);

                await Context.Channel.SendMessageAsync("", false, builder.Build()).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);
                await ReplyAsync("Unable to show Last.FM info due to an internal error.").ConfigureAwait(false);
            }
        }


        [Command("fmchart", RunMode = RunMode.Async), Summary("Generates a chart based on a user's parameters.")]
        public async Task fmchartAsync(string chartsize = "3x3", string time = "weekly", string titlesetting = null, IUser user = null)
        {
            if (chartsize == "help")
            {
                await ReplyAsync("fmchart '3x3-10x10' 'weekly/monthly/yearly/overall' 'notitles/titles' 'user'").ConfigureAwait(false);
                return;
            }

            User userSettings = await _userService.GetUserSettingsAsync(Context.User).ConfigureAwait(false);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

            if (!_guildService.CheckIfDM(Context))
            {
                GuildPermissions perms = await _guildService.CheckSufficientPermissionsAsync(Context).ConfigureAwait(false);
                if (!perms.AttachFiles)
                {
                    await ReplyAsync("I'm missing the 'Attach files' permission in this server, so I can't post a chart.").ConfigureAwait(false);
                    return;
                }
            }


            // @TODO: change to intparse or the likes
            try
            {
                string chartalbums = "";
                string chartrows = "";

                if (chartsize.Equals("3x3"))
                {
                    chartalbums = "9";
                    chartrows = "3";
                }
                else if (chartsize.Equals("4x4"))
                {
                    chartalbums = "16";
                    chartrows = "4";
                }
                else if (chartsize.Equals("5x5"))
                {
                    chartalbums = "25";
                    chartrows = "5";
                }
                else if (chartsize.Equals("6x6"))
                {
                    chartalbums = "36";
                    chartrows = "6";
                }
                else if (chartsize.Equals("7x7") || chartsize.Equals("8x8") || chartsize.Equals("9x9") || chartsize.Equals("10x10"))
                {
                    await ReplyAsync("Sorry, charts above 6x6 have been disabled due to performance issues.").ConfigureAwait(false);
                    return;
                }
                else
                {
                    await ReplyAsync("Your chart's size isn't valid. Sizes supported: 3x3-6x6").ConfigureAwait(false);
                    return;
                }

                // Generating image
                int max = int.Parse(chartalbums);
                int rows = int.Parse(chartrows);

                List<Bitmap> images = new List<Bitmap>();

                FMBotChart chart = new FMBotChart
                {
                    time = time,
                    LastFMName = userSettings.UserNameLastFM,
                    max = max,
                    rows = rows,
                    images = images,
                    DiscordUser = Context.User,
                    disclient = Context.Client as DiscordSocketClient,
                    mode = 0,
                    titles = titlesetting == null ? userSettings.TitlesEnabled ?? true : titlesetting == "titles",
                };

                await _userService.ResetChartTimerAsync(userSettings).ConfigureAwait(false);

                await SendChartMessage(chart).ConfigureAwait(false);

                // Adding extra infobox
                EmbedAuthorBuilder eab = new EmbedAuthorBuilder
                {
                    IconUrl = Context.User.GetAvatarUrl()
                };

                EmbedBuilder builder = new EmbedBuilder
                {
                    Color = new Discord.Color(186, 0, 0)
                };
                builder.WithAuthor(eab);
                string URI = "https://www.last.fm/user/" + userSettings.UserNameLastFM;
                builder.WithUrl(URI);
                builder.Title = await _userService.GetUserTitleAsync(Context).ConfigureAwait(false);

                // @TODO: clean up
                if (time.Equals("weekly") || time.Equals("week") || time.Equals("w"))
                {
                    builder.WithDescription("Last.FM " + chartsize + " Weekly Chart for " + userSettings.UserNameLastFM);
                }
                else if (time.Equals("monthly") || time.Equals("month") || time.Equals("m"))
                {
                    builder.WithDescription("Last.FM " + chartsize + " Monthly Chart for " + userSettings.UserNameLastFM);
                }
                else if (time.Equals("yearly") || time.Equals("year") || time.Equals("y"))
                {
                    builder.WithDescription("Last.FM " + chartsize + " Yearly Chart for " + userSettings.UserNameLastFM);
                }
                else if (time.Equals("overall") || time.Equals("alltime") || time.Equals("o") || time.Equals("at"))
                {
                    builder.WithDescription("Last.FM " + chartsize + " Overall Chart for " + userSettings.UserNameLastFM);
                }
                else
                {
                    builder.WithDescription("Last.FM " + chartsize + " Chart for " + userSettings.UserNameLastFM);
                }

                LastResponse<LastUser> userinfo = await _lastFmService.GetUserInfoAsync(userSettings.UserNameLastFM).ConfigureAwait(false);

                EmbedFooterBuilder efb = new EmbedFooterBuilder();

                int playcount = userinfo.Content.Playcount;

                efb.Text = userSettings.UserNameLastFM + "'s total scrobbles: " + playcount.ToString("0");

                builder.WithFooter(efb);

                await Context.Channel.SendMessageAsync("", false, builder.Build()).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                await ReplyAsync("Unable to generate a FMChart due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.").ConfigureAwait(false);
            }
        }

        [Command("fmrecent", RunMode = RunMode.Async), Summary("Displays a user's recent tracks.")]
        [Alias("fmrecenttracks")]
        public async Task fmrecentAsync(string user = null, int num = 5)
        {
            User userSettings = await _userService.GetUserSettingsAsync(Context.User).ConfigureAwait(false);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

            if (user == "help")
            {
                await ReplyAsync(".fmrecent 'user' 'number of items (max 10)'").ConfigureAwait(false);
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
                    if (await _lastFmService.LastFMUserExistsAsync(user).ConfigureAwait(false))
                    {
                        lastFMUserName = user;
                        self = false;
                    }
                    else if (!_guildService.CheckIfDM(Context))
                    {
                        IGuildUser guildUser = await _guildService.FindUserFromGuildAsync(Context, user).ConfigureAwait(false);

                        if (guildUser != null)
                        {
                            User guildUserLastFM = await _userService.GetUserSettingsAsync(guildUser).ConfigureAwait(false);

                            if (guildUserLastFM?.UserNameLastFM != null)
                            {
                                lastFMUserName = guildUserLastFM.UserNameLastFM;
                                self = false;
                            }
                        }
                    }
                }


                PageResponse<LastTrack> tracks = await _lastFmService.GetRecentScrobblesAsync(lastFMUserName, num).ConfigureAwait(false);

                EmbedBuilder builder = new EmbedBuilder
                {
                    Color = new Discord.Color(186, 0, 0)
                };

                if (self)
                {
                    builder.WithAuthor(new EmbedAuthorBuilder
                    {
                        IconUrl = Context.User.GetAvatarUrl(),
                        Name = lastFMUserName
                    });
                }

                builder.WithUrl("https://www.last.fm/user/" + lastFMUserName);
                builder.Title = await _userService.GetUserTitleAsync(Context).ConfigureAwait(false);

                builder.WithDescription("Top " + num + " Recent Track List");

                const string nulltext = "[undefined]";
                int indexval = (num - 1);
                for (int i = 0; i <= indexval; i++)
                {
                    LastTrack track = tracks.Content[i];

                    string TrackName = string.IsNullOrWhiteSpace(track.Name) ? nulltext : track.Name;
                    string ArtistName = string.IsNullOrWhiteSpace(track.ArtistName) ? nulltext : track.ArtistName;
                    string AlbumName = string.IsNullOrWhiteSpace(track.AlbumName) ? nulltext : track.AlbumName;


                    if (i == 0)
                    {
                        LastResponse<LastAlbum> AlbumInfo = await _lastFmService.GetAlbumInfoAsync(ArtistName, AlbumName).ConfigureAwait(false);

                        LastImageSet AlbumImages = await _lastFmService.GetAlbumImagesAsync(ArtistName, AlbumName).ConfigureAwait(false);

                        if (AlbumImages?.Medium != null)
                        {
                            builder.WithThumbnailUrl(AlbumImages.Medium.ToString());
                        }
                    }

                    int correctnum = (i + 1);
                    builder.AddField("Track #" + correctnum.ToString() + ":", TrackName + " - " + ArtistName + " | " + AlbumName);
                }

                EmbedFooterBuilder efb = new EmbedFooterBuilder();

                LastResponse<LastUser> userinfo = await _lastFmService.GetUserInfoAsync(lastFMUserName).ConfigureAwait(false);
                int playcount = userinfo.Content.Playcount;

                efb.Text = lastFMUserName + "'s total scrobbles: " + playcount.ToString("0");

                builder.WithFooter(efb);

                await Context.Channel.SendMessageAsync("", false, builder.Build()).ConfigureAwait(false);

            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                await ReplyAsync("Unable to show your recent tracks on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.").ConfigureAwait(false);
            }
        }



        [Command("fmstats", RunMode = RunMode.Async), Summary("Displays user stats related to Last.FM and FMBot")]
        [Alias("fminfo")]
        public async Task fmstatsAsync(string user = null)
        {
            User userSettings = await _userService.GetUserSettingsAsync(Context.User).ConfigureAwait(false);

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
                    if (await _lastFmService.LastFMUserExistsAsync(user).ConfigureAwait(false))
                    {
                        lastFMUserName = user;
                    }
                    else if (!_guildService.CheckIfDM(Context))
                    {
                        IGuildUser guildUser = await _guildService.FindUserFromGuildAsync(Context, user).ConfigureAwait(false);

                        if (guildUser != null)
                        {
                            User guildUserLastFM = await _userService.GetUserSettingsAsync(guildUser).ConfigureAwait(false);

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

                builder.Title = await _userService.GetUserTitleAsync(Context).ConfigureAwait(false);
                builder.WithDescription("Last.FM Statistics for " + lastFMUserName);

                LastResponse<LastUser> userinfo = await _lastFmService.GetUserInfoAsync(lastFMUserName).ConfigureAwait(false);

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

                await Context.Channel.SendMessageAsync("", false, builder.Build()).ConfigureAwait(false);

            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                await ReplyAsync("Unable to show your stats on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.").ConfigureAwait(false);
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

                await Context.Channel.SendMessageAsync("", false, builder.Build()).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                await ReplyAsync("Unable to show the featured avatar on FMBot due to an internal error. The timer service cannot be loaded. Please wait for the bot to fully load.").ConfigureAwait(false);
            }
        }

        [Command("fmset", RunMode = RunMode.Async), Summary("Sets your Last.FM name and FM mode.")]
        [Alias("fmsetname", "fmsetmode")]
        public async Task fmsetAsync([Summary("Your Last.FM name")] string lastFMUserName, [Summary("The mode you want to use.")] string chartType = "embedfull")
        {
            if (lastFMUserName == "help")
            {
                await ReplyAsync(ConfigData.Data.CommandPrefix + "fmset 'Last.FM Username' 'embedmini/embedfull/textmini/textfull'").ConfigureAwait(false);
                return;
            }

            if (!await _lastFmService.LastFMUserExistsAsync(lastFMUserName.Replace("'", "")).ConfigureAwait(false))
            {
                await ReplyAsync("LastFM user could not be found. Please check if the name you entered is correct.").ConfigureAwait(false);
                return;
            }

            if (!Enum.TryParse(chartType.Replace("'", ""), ignoreCase: true, out ChartType chartTypeEnum))
            {
                await ReplyAsync("Invalid mode. Please use 'embedmini', 'embedfull', 'textfull', or 'textmini'.").ConfigureAwait(false);
                return;
            }

            _userService.SetLastFM(Context.User, lastFMUserName.Replace("'", ""), chartTypeEnum);

            await ReplyAsync("Your Last.FM name has been set to '" + lastFMUserName.Replace("'", "") + "' and your mode has been set to '" + chartType + "'.").ConfigureAwait(false);

            if (!_guildService.CheckIfDM(Context))
            {
                GuildPermissions perms = await _guildService.CheckSufficientPermissionsAsync(Context).ConfigureAwait(false);
                if (!perms.EmbedLinks || !perms.AttachFiles)
                {
                    await ReplyAsync("Please note that the bot also needs the 'Attach files' and 'Embed links' permissions for most commands. One or both of these permissions are currently missing.").ConfigureAwait(false);
                    return;
                }
            }
        }

        [Command("fmremove", RunMode = RunMode.Async), Summary("Deletes your FMBot data.")]
        [Alias("fmdelete", "fmremovedata", "fmdeletedata")]
        public async Task fmremoveAsync()
        {
            User userSettings = await _userService.GetUserSettingsAsync(Context.User).ConfigureAwait(false);

            if (userSettings == null)
            {
                await ReplyAsync("Sorry, but we don't have any data from you in our database.").ConfigureAwait(false);
                return;
            }

            await _friendsService.RemoveAllLastFMFriendsAsync(userSettings.UserID).ConfigureAwait(false);
            await _userService.DeleteUser(userSettings.UserID).ConfigureAwait(false);

            await ReplyAsync("Your settings, friends and any other data have been successfully deleted.").ConfigureAwait(false);
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
                    await ReplyAsync(cfgjson.CommandPrefix + "fmsuggest 'text in quotes'").ConfigureAwait(false);
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

                    await channel.SendMessageAsync("", false, builder.Build()).ConfigureAwait(false);

                    await ReplyAsync("Your suggestion has been sent to the .fmbot server!").ConfigureAwait(false);
                //}
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);
            }
        }

        private async Task UsernameNotSetErrorResponseAsync()
        {
            this._embed.WithTitle("Error while attempting get latest tracks");
            this._embed.WithDescription("Last.FM username has not been set. \n" +
                                        "To setup your Last.FM account with this bot, please use the `.fmset username` command.");
            this._embed.WithColor(Constants.WarningColorOrange);
            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
        }

        private async Task NoScrobblesErrorResponseFoundAsync()
        {
            this._embed.WithTitle("Error while attempting to get latest tracks");
            this._embed.WithDescription($"No scrobbles were found on your profile.");
            this._embed.WithColor(Constants.WarningColorOrange);
            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
        }
    }
}