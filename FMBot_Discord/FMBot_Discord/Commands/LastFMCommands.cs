using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Data.Entities;
using FMBot.Services;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static FMBot.Bot.FMBotModules;
using static FMBot.Bot.FMBotUtil;
using static FMBot.Bot.Models.LastFMModels;

namespace FMBot.Bot.Commands
{
    public class LastFMCommands : ModuleBase
    {
        private readonly TimerService _timer;

        private readonly UserService userService = new UserService();

        private readonly GuildService guildService = new GuildService();

        private readonly FriendsService friendsService = new FriendsService();

        private readonly LastFMService lastFMService = new LastFMService();

        public LastFMCommands(TimerService timer)
        {
            _timer = timer;
        }


        private async Task SendChartMessage(FMBotChart chart)
        {
            await lastFMService.GenerateChartAsync(chart);

            // Send chart memory stream, remove when finished

            using (MemoryStream memory = GlobalVars.GetChartStream(chart.DiscordUser.Id))
            {
                await Context.Channel.SendFileAsync(memory, "chart.png");
            }

            lock (GlobalVars.charts.SyncRoot)
            {
                // @TODO: remove only once in a while to keep it cached
                GlobalVars.charts.Remove(GlobalVars.GetChartFileName(chart.DiscordUser.Id));
            }
        }

        [Command("fm"), Summary("Displays what a user is listening to.")]
        [Alias("qm", "wm", "em", "rm", "tm", "ym", "um", "im", "om", "pm", "dm", "gm", "sm", "am", "hm", "jm", "km", "lm", "zm", "xm", "cm", "vm", "bm", "nm", "mm", "lastfm")]
        public async Task fmAsync(string user = null)
        {
            Data.Entities.User userSettings = await userService.GetUserSettingsAsync(Context.User);

            if (userSettings == null || userSettings.UserNameLastFM == null)
            {
                await ReplyAsync("Your LastFM username has not been set. Please set your username using the `.fmset 'username' 'embedfull/embedmini/textfull/textmini'` command.");
                return;
            }
            if (user == "help")
            {
                await ReplyAsync(
                    "Usage: `.fm 'lastfm username/discord user'` \n" +
                    "You can set your default user and your display mode through the `.fmset 'username' 'embedfull/embedmini/textfull/textmini'` command.");
                return;
            }


            try
            {
                string lastFMUserName = userSettings.UserNameLastFM;
                bool self = true;

                if (user != null)
                {
                    if (await lastFMService.LastFMUserExistsAsync(user))
                    {
                        lastFMUserName = user;
                        self = false;
                    }
                    else if (!guildService.CheckIfDM(Context))
                    {
                        IGuildUser guildUser = await guildService.FindUserFromGuildAsync(Context, user);

                        if (guildUser != null)
                        {
                            Data.Entities.User guildUserLastFM = await userService.GetUserSettingsAsync(guildUser);

                            if (guildUserLastFM != null && guildUserLastFM.UserNameLastFM != null)
                            {
                                lastFMUserName = guildUserLastFM.UserNameLastFM;
                                self = false;
                            }
                        }
                    }
                }


                PageResponse<LastTrack> tracks = await lastFMService.GetRecentScrobblesAsync(lastFMUserName);

                if (tracks == null || tracks.Count() == 0)
                {
                    await ReplyAsync("No scrobbles found on this profile. (" + lastFMUserName + ")");
                    return;
                }

                LastTrack currentTrack = tracks.Content.ElementAt(0);
                LastTrack lastTrack = tracks.Content.ElementAt(1);

                string nulltext = "[undefined]";

                if (userSettings.ChartType == ChartType.embedmini)
                {
                    EmbedAuthorBuilder eab = new EmbedAuthorBuilder
                    {
                        IconUrl = Context.User.GetAvatarUrl(),
                        Name = lastFMUserName
                    };

                    EmbedBuilder builder = new EmbedBuilder
                    {
                        Color = new Discord.Color(186, 0, 0),
                    };
                    builder.WithAuthor(eab);
                    builder.WithUrl("https://www.last.fm/user/" + lastFMUserName);
                    if (self)
                    {
                        builder.WithAuthor(eab);
                        builder.Title = lastFMUserName + ", " + await userService.GetUserTitleAsync(Context);
                    }
                    else
                    {
                        builder.Title = lastFMUserName + " stats";
                    }

                    if (currentTrack.IsNowPlaying == true)
                    {
                        builder.WithDescription("Now Playing");
                    }

                    string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                    string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                    string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                    LastImageSet AlbumImages = await lastFMService.GetAlbumImagesAsync(currentTrack.ArtistName, currentTrack.AlbumName);

                    if (AlbumImages != null && AlbumImages.Medium != null)
                    {
                        builder.WithThumbnailUrl(AlbumImages.Medium.ToString());
                    }

                    builder.AddField((currentTrack.IsNowPlaying == true ? "Current: " : "Last track: ") + TrackName, ArtistName + " | " + AlbumName);

                    EmbedFooterBuilder efb = new EmbedFooterBuilder();

                    LastResponse<LastUser> userinfo = await lastFMService.GetUserInfoAsync(lastFMUserName);
                    int playcount = userinfo.Content.Playcount;

                    efb.Text = lastFMUserName + "'s Total Tracks: " + playcount.ToString("N0");

                    builder.WithFooter(efb);

                    await Context.Channel.SendMessageAsync("", false, builder.Build());
                }
                else if (userSettings.ChartType == ChartType.embedfull)
                {

                    EmbedAuthorBuilder eab = new EmbedAuthorBuilder
                    {
                        IconUrl = Context.User.GetAvatarUrl(),
                        Name = userSettings.UserNameLastFM
                    };

                    EmbedBuilder builder = new EmbedBuilder();
                    builder.WithAuthor(eab);
                    builder.WithUrl("https://www.last.fm/user/" + lastFMUserName);

                    if (self)
                    {
                        builder.WithAuthor(eab);
                        builder.Title = lastFMUserName + ", " + await userService.GetUserTitleAsync(Context);
                    }
                    else
                    {
                        builder.Title = lastFMUserName + " stats";
                    }

                    if (currentTrack.IsNowPlaying == true)
                    {
                        builder.WithDescription("Now Playing");
                    }

                    string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                    string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                    string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                    string LastTrackName = string.IsNullOrWhiteSpace(lastTrack.Name) ? nulltext : lastTrack.Name;
                    string LastArtistName = string.IsNullOrWhiteSpace(lastTrack.ArtistName) ? nulltext : lastTrack.ArtistName;
                    string LastAlbumName = string.IsNullOrWhiteSpace(lastTrack.AlbumName) ? nulltext : lastTrack.AlbumName;

                    LastImageSet AlbumImages = await lastFMService.GetAlbumImagesAsync(currentTrack.ArtistName, currentTrack.AlbumName);

                    if (AlbumImages != null && AlbumImages.Medium != null)
                    {
                        builder.WithThumbnailUrl(AlbumImages.Medium.ToString());
                    }

                    builder.AddField((currentTrack.IsNowPlaying == true ? "Current: " : "Last track: ") + TrackName, ArtistName + " | " + AlbumName);
                    builder.AddField("Previous: " + LastTrackName, LastArtistName + " | " + LastAlbumName);

                    EmbedFooterBuilder efb = new EmbedFooterBuilder();

                    LastResponse<LastUser> userinfo = await lastFMService.GetUserInfoAsync(lastFMUserName);
                    int playcount = userinfo.Content.Playcount;

                    efb.Text = lastFMUserName + "'s Total Tracks: " + playcount.ToString("N0");

                    builder.WithFooter(efb);

                    await Context.Channel.SendMessageAsync("", false, builder.Build());
                }
                else if (userSettings.ChartType == ChartType.textfull)
                {
                    string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                    string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                    string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                    string LastTrackName = string.IsNullOrWhiteSpace(lastTrack.Name) ? nulltext : lastTrack.Name;
                    string LastArtistName = string.IsNullOrWhiteSpace(lastTrack.ArtistName) ? nulltext : lastTrack.ArtistName;
                    string LastAlbumName = string.IsNullOrWhiteSpace(lastTrack.AlbumName) ? nulltext : lastTrack.AlbumName;

                    LastResponse<LastUser> userinfo = await lastFMService.GetUserInfoAsync(lastFMUserName);

                    int playcount = userinfo.Content.Playcount;

                    await Context.Channel.SendMessageAsync(await userService.GetUserTitleAsync(Context) + "\n" + "**Current** - " + ArtistName + " - " + TrackName + " [" + AlbumName + "]" + "\n" + "**Previous** - " + LastArtistName + " - " + LastTrackName + " [" + LastAlbumName + "]" + "\n" + "<https://www.last.fm/user/" + userSettings.UserNameLastFM + ">\n" + userSettings.UserNameLastFM + "'s Total Tracks: " + playcount.ToString("N0"));
                }
                else if (userSettings.ChartType == ChartType.textmini)
                {
                    string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                    string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                    string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                    string LastTrackName = string.IsNullOrWhiteSpace(lastTrack.Name) ? nulltext : lastTrack.Name;
                    string LastArtistName = string.IsNullOrWhiteSpace(lastTrack.ArtistName) ? nulltext : lastTrack.ArtistName;
                    string LastAlbumName = string.IsNullOrWhiteSpace(lastTrack.AlbumName) ? nulltext : lastTrack.AlbumName;

                    LastResponse<LastUser> userinfo = await lastFMService.GetUserInfoAsync(lastFMUserName);
                    int playcount = userinfo.Content.Playcount;

                    await Context.Channel.SendMessageAsync(await userService.GetUserTitleAsync(Context) + "\n" + "**Current** - " + ArtistName + " - " + TrackName + " [" + AlbumName + "]" + "\n" + "<https://www.last.fm/user/" + userSettings.UserNameLastFM + ">\n" + userSettings.UserNameLastFM + "'s Total Tracks: " + playcount.ToString("N0"));
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);
                await ReplyAsync("Unable to show Last.FM info due to an internal error. Try scrobbling something then use the command again.");
            }
        }



        [Command("fmchart"), Summary("Generates a chart based on a user's parameters.")]
        public async Task fmchartAsync(string chartsize = "3x3", string time = "weekly", string titlesetting = null, IUser user = null)
        {
            if (chartsize == "help")
            {
                await ReplyAsync("fmchart '3x3-10x10' 'weekly/monthly/yearly/overall' 'notitles/titles' 'user'");
                return;
            }

            User userSettings = await userService.GetUserSettingsAsync(Context.User);

            if (userSettings == null || userSettings.UserNameLastFM == null)
            {
                await ReplyAsync("Your LastFM username has not been set. Please set your username using the `.fmset 'username' 'embedfull/embedmini/textfull/textmini'` command.");
                return;
            }
            if (userSettings.LastGeneratedChartDateTimeUtc > DateTime.UtcNow.AddSeconds(-10) && userSettings.UserType == UserType.User)
            {
                await ReplyAsync("You're requesting too frequently, please try again later");
                return;
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
                else if (chartsize.Equals("7x7"))
                {
                    //chartalbums = "49";
                    //chartrows = "7";
                    await ReplyAsync("Sorry, charts above 6x6 have been temporarily disabled due to performance issues.");
                    return;
                }
                else if (chartsize.Equals("8x8"))
                {
                    //chartalbums = "64";
                    //chartrows = "8";
                    await ReplyAsync("Sorry, charts above 6x6 have been temporarily disabled due to performance issues.");
                    return;
                }
                else if (chartsize.Equals("9x9"))
                {
                    //chartalbums = "81";
                    //chartrows = "9";
                    await ReplyAsync("Sorry, charts above 6x6 have been temporarily disabled due to performance issues.");
                    return;
                }
                else if (chartsize.Equals("10x10"))
                {
                    //chartalbums = "100";
                    //chartrows = "10";
                    await ReplyAsync("Sorry, charts above 6x6 have been temporarily disabled due to performance issues.");
                    return;
                }
                else
                {
                    await ReplyAsync("Your chart's size isn't valid. Sizes supported: 3x3-10x10");
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
                    titles = titlesetting == null ? userSettings.TitlesEnabled.HasValue ? userSettings.TitlesEnabled.Value : true : titlesetting == "titles",
                };

                userService.ResetChartTimer(userSettings);


                await SendChartMessage(chart);

                // Adding extra infobox

                EmbedAuthorBuilder eab = new EmbedAuthorBuilder
                {
                    IconUrl = Context.User.GetAvatarUrl()
                };

                EmbedBuilder builder = new EmbedBuilder();
                builder.WithAuthor(eab);
                string URI = "https://www.last.fm/user/" + userSettings.UserNameLastFM;
                builder.WithUrl(URI);
                builder.Title = await userService.GetUserTitleAsync(Context);

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

                LastResponse<LastUser> userinfo = await lastFMService.GetUserInfoAsync(userSettings.UserNameLastFM);

                EmbedFooterBuilder efb = new EmbedFooterBuilder();

                int playcount = userinfo.Content.Playcount;

                efb.Text = userSettings.UserNameLastFM + "'s Total Tracks: " + playcount.ToString("0");

                builder.WithFooter(efb);

                await Context.Channel.SendMessageAsync("", false, builder.Build());
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                await ReplyAsync("Unable to generate a FMChart due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("fmartistchart"), Summary("Generates an artist chart based on a user's parameters.")]
        public async Task fmartistchartAsync(string chartsize = "3x3", string time = "weekly", string titlesetting = "titles", IUser user = null)
        {
            if (chartsize == "help")
            {
                await ReplyAsync(".fmartistchart [3x3-10x10] [weekly/monthly/yearly/overall] [notitles/titles] [user]");
                return;
            }

            User userSettings = await userService.GetUserSettingsAsync(Context.User);

            if (userSettings == null || userSettings.UserNameLastFM == null)
            {
                await ReplyAsync("Your LastFM username has not been set. Please set your username using the `.fmset 'username' 'embedfull/embedmini/textfull/textmini'` command.");
                return;
            }
            
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
                else if (chartsize.Equals("7x7"))
                {
                    //chartalbums = "49";
                    //chartrows = "7";
                    await ReplyAsync("Sorry, charts above 6x6 have been temporarily disabled due to performance issues.");
                    return;
                }
                else if (chartsize.Equals("8x8"))
                {
                    //chartalbums = "64";
                    //chartrows = "8";
                    await ReplyAsync("Sorry, charts above 6x6 have been temporarily disabled due to performance issues.");
                    return;
                }
                else if (chartsize.Equals("9x9"))
                {
                    //chartalbums = "81";
                    //chartrows = "9";
                    await ReplyAsync("Sorry, charts above 6x6 have been temporarily disabled due to performance issues.");
                    return;
                }
                else if (chartsize.Equals("10x10"))
                {
                    //chartalbums = "100";
                    //chartrows = "10";
                    await ReplyAsync("Sorry, charts above 6x6 have been temporarily disabled due to performance issues.");
                    return;
                }
                else
                {
                    await ReplyAsync("Your chart's size isn't valid. Sizes supported: 3x3-10x10");
                    return;
                }

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
                    mode = 1,
                    titles = titlesetting == null ? userSettings.TitlesEnabled.HasValue ? userSettings.TitlesEnabled.Value : true : titlesetting == "titles",
                };

                userService.ResetChartTimer(userSettings);

                await lastFMService.GenerateChartAsync(chart);

                await SendChartMessage(chart);

                EmbedAuthorBuilder eab = new EmbedAuthorBuilder
                {
                    IconUrl = Context.User.GetAvatarUrl()
                };

                EmbedBuilder builder = new EmbedBuilder();
                builder.WithAuthor(eab);
                string URI = "https://www.last.fm/user/" + userSettings.UserNameLastFM;
                builder.WithUrl(URI);
                builder.Title = await userService.GetUserTitleAsync(Context);

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

                LastResponse<LastUser> userinfo = await lastFMService.GetUserInfoAsync(userSettings.UserNameLastFM);

                EmbedFooterBuilder efb = new EmbedFooterBuilder();

                int playcount = userinfo.Content.Playcount;

                efb.Text = userSettings.UserNameLastFM + "'s Total Tracks: " + playcount.ToString("0");

                builder.WithFooter(efb);

                await Context.Channel.SendMessageAsync("", false, builder.Build());
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                await ReplyAsync("Unable to generate a FMChart due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }



        [Command("fmrecent"), Summary("Displays a user's recent tracks.")]
        [Alias("fmrecenttracks")]
        public async Task fmrecentAsync(string user = null, int num = 5)
        {
            Data.Entities.User userSettings = await userService.GetUserSettingsAsync(Context.User);

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
                    if (await lastFMService.LastFMUserExistsAsync(user))
                    {
                        lastFMUserName = user;
                        self = false;
                    }
                    else if (!guildService.CheckIfDM(Context))
                    {
                        IGuildUser guildUser = await guildService.FindUserFromGuildAsync(Context, user);

                        if (guildUser != null)
                        {
                            Data.Entities.User guildUserLastFM = await userService.GetUserSettingsAsync(guildUser);

                            if (guildUserLastFM != null && guildUserLastFM.UserNameLastFM != null)
                            {
                                lastFMUserName = guildUserLastFM.UserNameLastFM;
                                self = false;
                            }
                        }
                    }
                }


                PageResponse<LastTrack> tracks = await lastFMService.GetRecentScrobblesAsync(lastFMUserName, num);

                EmbedBuilder builder = new EmbedBuilder();

                if (self)
                {
                    builder.WithAuthor(new EmbedAuthorBuilder
                    {
                        IconUrl = Context.User.GetAvatarUrl(),
                        Name = lastFMUserName
                    });
                }


                builder.WithUrl("https://www.last.fm/user/" + lastFMUserName);
                builder.Title = await userService.GetUserTitleAsync(Context);

                builder.WithDescription("Top " + num + " Recent Track List");

                string nulltext = "[undefined]";
                int indexval = (num - 1);
                for (int i = 0; i <= indexval; i++)
                {
                    LastTrack track = tracks.Content.ElementAt(i);

                    string TrackName = string.IsNullOrWhiteSpace(track.Name) ? nulltext : track.Name;
                    string ArtistName = string.IsNullOrWhiteSpace(track.ArtistName) ? nulltext : track.ArtistName;
                    string AlbumName = string.IsNullOrWhiteSpace(track.AlbumName) ? nulltext : track.AlbumName;


                    if (i == 0)
                    {
                        LastResponse<LastAlbum> AlbumInfo = await lastFMService.GetAlbumInfoAsync(ArtistName, AlbumName);

                        LastImageSet AlbumImages = await lastFMService.GetAlbumImagesAsync(ArtistName, AlbumName);

                        if (AlbumImages != null && AlbumImages.Medium != null)
                        {
                            builder.WithThumbnailUrl(AlbumImages.Medium.ToString());
                        }
                    }

                    int correctnum = (i + 1);
                    builder.AddField("Track #" + correctnum.ToString() + ":", TrackName + " - " + ArtistName + " | " + AlbumName);
                }

                EmbedFooterBuilder efb = new EmbedFooterBuilder();

                LastResponse<LastUser> userinfo = await lastFMService.GetUserInfoAsync(lastFMUserName);
                int playcount = userinfo.Content.Playcount;

                efb.Text = lastFMUserName + "'s Total Tracks: " + playcount.ToString("0");

                builder.WithFooter(efb);

                await Context.Channel.SendMessageAsync("", false, builder.Build());

            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                await ReplyAsync("Unable to show your recent tracks on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }



        [Command("fmstats"), Summary("Displays user stats related to Last.FM and FMBot")]
        [Alias("fminfo")]
        public async Task fmstatsAsync(string user = null)
        {
            Data.Entities.User userSettings = await userService.GetUserSettingsAsync(Context.User);

            if (userSettings == null || userSettings.UserNameLastFM == null)
            {
                await ReplyAsync("Your LastFM username has not been set. Please set your username using the `.fmset 'username' 'embedfull/embedmini/textfull/textmini'` command.");
                return;
            }

            try
            {
                string lastFMUserName = userSettings.UserNameLastFM;

                if (user != null)
                {
                    if (await lastFMService.LastFMUserExistsAsync(user))
                    {
                        lastFMUserName = user;
                    }
                    else if (!guildService.CheckIfDM(Context))
                    {
                        IGuildUser guildUser = await guildService.FindUserFromGuildAsync(Context, user);

                        if (guildUser != null)
                        {
                            Data.Entities.User guildUserLastFM = await userService.GetUserSettingsAsync(guildUser);

                            if (guildUserLastFM != null && guildUserLastFM.UserNameLastFM != null)
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

                EmbedBuilder builder = new EmbedBuilder();
                builder.WithAuthor(eab);
                builder.WithUrl("https://www.last.fm/user/" + lastFMUserName);

                builder.Title = await userService.GetUserTitleAsync(Context);
                builder.WithDescription("Last.FM Statistics for " + lastFMUserName);

                LastResponse<LastUser> userinfo = await lastFMService.GetUserInfoAsync(lastFMUserName);

                LastImageSet userinfoImages = (userinfo.Content.Avatar != null) ? userinfo.Content.Avatar : null;
                string userinfoThumbnail = (userinfoImages != null) ? userinfoImages.Large.AbsoluteUri : null;
                string ThumbnailImage = (userinfoThumbnail != null) ? userinfoThumbnail.ToString() : null;

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
                builder.AddField("User Type: ", usertype.ToString());
                builder.AddField("Total Tracks: ", playcount.ToString("0"));
                builder.AddField("Has Last.FM Premium? ", premium.ToString());
                builder.AddField("Bot user type: ", userSettings.UserType);

                await Context.Channel.SendMessageAsync("", false, builder.Build());

            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                await ReplyAsync("Unable to show your stats on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("fmfeatured"), Summary("Displays the featured avatar.")]
        [Alias("fmfeaturedavatar", "fmfeatureduser", "fmfeaturedalbum")]
        public async Task fmfeaturedAsync()
        {
            try
            {
                EmbedBuilder builder = new EmbedBuilder();
                ISelfUser SelfUser = Context.Client.CurrentUser;
                builder.WithThumbnailUrl(SelfUser.GetAvatarUrl());
                builder.AddField("Featured:", _timer.GetTrackString());

                await Context.Channel.SendMessageAsync("", false, builder.Build());
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                await ReplyAsync("Unable to show the featured avatar on FMBot due to an internal error. The timer service cannot be loaded. Please wait for the bot to fully load.");
            }
        }

        [Command("fmset"), Summary("Sets your Last.FM name and FM mode.")]
        [Alias("fmsetname", "fmsetmode")]
        public async Task fmsetAsync([Summary("Your Last.FM name")] string lastFMUserName, [Summary("The mode you want to use.")] string chartType = "embedmini")
        {
            if (lastFMUserName == "help")
            {
                JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();
                await ReplyAsync(cfgjson.CommandPrefix + "fmset 'Last.FM Username' 'embedmini/embedfull/textmini/textfull'");
                return;
            }

            if (!await lastFMService.LastFMUserExistsAsync(lastFMUserName))
            {
                await ReplyAsync("LastFM user could not be found. Please check if the name you entered is correct.");
                return;
            }

            if (!Enum.TryParse(chartType, ignoreCase: true, out ChartType chartTypeEnum))
            {
                await ReplyAsync("Invalid mode. Please use 'embedmini', 'embedfull', 'textfull', or 'textmini'.");
                return;
            }

            userService.SetLastFM(Context.User, lastFMUserName, chartTypeEnum);

            await ReplyAsync("Your Last.FM name has been set to '" + lastFMUserName + "' and your mode has been set to '" + chartType + "'.");
        }



        [Command("fmremove"), Summary("Deletes your FMBot data.")]
        [Alias("fmdelete", "fmremovedata", "fmdeletedata")]
        public async Task fmremoveAsync()
        {
            await userService.DeleteUser(Context.Client.CurrentUser);

            await ReplyAsync("Your settings and data have been successfully deleted.");
        }

    }
}
