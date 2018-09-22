using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Data.Entities;
using FMBot.Services;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using SpotifyAPI.Web.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YoutubeSearch;
using static FMBot.Bot.FMBotModules;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Bot
{
    public class FMBotCommands : ModuleBase
    {
        #region Constructor

        private readonly CommandService _service;
        private readonly TimerService _timer;

        private UserService userService = new UserService();
        private GuildService guildService = new GuildService();

        private LastFMService lastFMService = new LastFMService();
        private SpotifyService spotifyService = new SpotifyService();
        private YoutubeService youtubeService = new YoutubeService();

        public FMBotCommands(CommandService service, TimerService timer)
        {
            _service = service;
            _timer = timer;
        }

        #endregion

        #region Last.FM Commands

        [Command("fm"), Summary("Displays what a user is listening to.")]
        [Alias("qm", "wm", "em", "rm", "tm", "ym", "um", "im", "om", "pm", "dm", "gm", "sm", "am", "hm", "jm", "km", "lm", "zm", "xm", "cm", "vm", "bm", "nm", "mm", "lastfm")]
        public async Task fmAsync(IUser user = null)
        {
            Settings userSettings = await userService.GetUserSettingsAsync(Context.User);

            if (userSettings == null || userSettings.UserNameLastFM == null)
            {
                await ReplyAsync("Your LastFM username has not been set. Please set your username using the `.fmset [username] [embedfull/embedmini/textfull/textmini]` command.");
                return;
            }

            try
            {
                PageResponse<LastTrack> tracks = await lastFMService.GetRecentScrobblesAsync(userSettings.UserNameLastFM);

                if (tracks == null || tracks.Count() == 0)
                {
                    await ReplyAsync("No scrobbles found on your LastFM profile. (" + userSettings.UserNameLastFM + ")");
                    return;
                }

                LastTrack currentTrack = tracks.Content.ElementAt(0);
                LastTrack lastTrack = tracks.Content.ElementAt(1);

                string nulltext = "[undefined]";

                if (userSettings.ChartType == ChartType.embedmini)
                {
                    EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                    eab.IconUrl = Context.User.GetAvatarUrl();
                    eab.Name = userSettings.UserNameLastFM;

                    EmbedBuilder builder = new EmbedBuilder();
                    builder.WithAuthor(eab);
                    string URI = "https://www.last.fm/user/" + userSettings.UserNameLastFM;
                    builder.WithUrl(URI);
                    builder.Title = userSettings.UserNameLastFM + ", " + await userService.GetUserTitleAsync(Context);
                    builder.WithDescription("Now Playing");

                    string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                    string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                    string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                    LastResponse<LastAlbum> AlbumInfo = await lastFMService.GetAlbumInfoAsync(ArtistName, AlbumName);
                    LastImageSet AlbumImages = (AlbumInfo.Content.Images != null) ? AlbumInfo.Content.Images : null;
                    string AlbumThumbnail = (AlbumImages != null) ? AlbumImages.Large != null ? AlbumImages.Large.AbsoluteUri : null : null;
                    string ThumbnailImage = (AlbumThumbnail != null) ? AlbumThumbnail.ToString() : null;

                    if (!string.IsNullOrWhiteSpace(ThumbnailImage))
                    {
                        builder.WithThumbnailUrl(ThumbnailImage);
                    }

                    builder.AddField("Current: " + TrackName, ArtistName + " | " + AlbumName);

                    EmbedFooterBuilder efb = new EmbedFooterBuilder();

                    LastResponse<LastUser> userinfo = await lastFMService.GetUserInfoAsync(userSettings.UserNameLastFM);
                    int playcount = userinfo.Content.Playcount;

                    efb.Text = userSettings.UserNameLastFM + "'s Total Tracks: " + playcount.ToString("0");

                    builder.WithFooter(efb);

                    await Context.Channel.SendMessageAsync("", false, builder.Build());
                }
                else if (userSettings.ChartType == ChartType.embedfull)
                {

                    EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                    eab.IconUrl = Context.User.GetAvatarUrl();
                    eab.Name = userSettings.UserNameLastFM;

                    EmbedBuilder builder = new EmbedBuilder();
                    builder.WithAuthor(eab);
                    string URI = "https://www.last.fm/user/" + userSettings.UserNameLastFM;
                    builder.WithUrl(URI);
                    builder.Title = userSettings.UserNameLastFM + ", " + await userService.GetUserTitleAsync(Context);
                    builder.WithDescription("Now Playing");

                    string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                    string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                    string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                    string LastTrackName = string.IsNullOrWhiteSpace(lastTrack.Name) ? nulltext : lastTrack.Name;
                    string LastArtistName = string.IsNullOrWhiteSpace(lastTrack.ArtistName) ? nulltext : lastTrack.ArtistName;
                    string LastAlbumName = string.IsNullOrWhiteSpace(lastTrack.AlbumName) ? nulltext : lastTrack.AlbumName;

                    LastResponse<LastAlbum> AlbumInfo = await lastFMService.GetAlbumInfoAsync(ArtistName, AlbumName);
                    LastImageSet AlbumImages = (AlbumInfo.Content.Images != null) ? AlbumInfo.Content.Images : null;
                    string AlbumThumbnail = (AlbumImages != null) ? AlbumImages.Large != null ? AlbumImages.Large.AbsoluteUri : null : null;
                    string ThumbnailImage = (AlbumThumbnail != null) ? AlbumThumbnail.ToString() : null;

                    if (!string.IsNullOrWhiteSpace(ThumbnailImage))
                    {
                        builder.WithThumbnailUrl(ThumbnailImage);
                    }

                    builder.AddField("Current: " + TrackName, ArtistName + " | " + AlbumName);
                    builder.AddField("Previous: " + LastTrackName, LastArtistName + " | " + LastAlbumName);

                    EmbedFooterBuilder efb = new EmbedFooterBuilder();

                    LastResponse<LastUser> userinfo = await lastFMService.GetUserInfoAsync(userSettings.UserNameLastFM);
                    int playcount = userinfo.Content.Playcount;

                    efb.Text = userSettings.UserNameLastFM + "'s Total Tracks: " + playcount.ToString("0");

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

                    LastResponse<LastUser> userinfo = await lastFMService.GetUserInfoAsync(userSettings.UserNameLastFM);

                    int playcount = userinfo.Content.Playcount;

                    await Context.Channel.SendMessageAsync(await userService.GetUserTitleAsync(Context) + "\n" + "**Current** - " + ArtistName + " - " + TrackName + " [" + AlbumName + "]" + "\n" + "**Previous** - " + LastArtistName + " - " + LastTrackName + " [" + LastAlbumName + "]" + "\n" + "<https://www.last.fm/user/" + userSettings.UserNameLastFM + ">\n" + userSettings.UserNameLastFM + "'s Total Tracks: " + playcount.ToString("0"));
                }
                else if (userSettings.ChartType == ChartType.textmini)
                {
                    string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                    string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                    string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                    string LastTrackName = string.IsNullOrWhiteSpace(lastTrack.Name) ? nulltext : lastTrack.Name;
                    string LastArtistName = string.IsNullOrWhiteSpace(lastTrack.ArtistName) ? nulltext : lastTrack.ArtistName;
                    string LastAlbumName = string.IsNullOrWhiteSpace(lastTrack.AlbumName) ? nulltext : lastTrack.AlbumName;

                    LastResponse<LastUser> userinfo = await lastFMService.GetUserInfoAsync(userSettings.UserNameLastFM);
                    int playcount = userinfo.Content.Playcount;

                    await Context.Channel.SendMessageAsync(await userService.GetUserTitleAsync(Context) + "\n" + "**Current** - " + ArtistName + " - " + TrackName + " [" + AlbumName + "]" + "\n" + "<https://www.last.fm/user/" + userSettings.UserNameLastFM + ">\n" + userSettings.UserNameLastFM + "'s Total Tracks: " + playcount.ToString("0"));
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);
                await ReplyAsync("Unable to show Last.FM info due to an internal error. Try scrobbling something then use the command again.");
            }
        }

        [Command("fmyt"), Summary("Shares a link to a YouTube video based on what a user is listening to")]
        [Alias("fmyoutube")]
        public async Task fmytAsync(IUser user = null)
        {
            Settings userSettings = await userService.GetUserSettingsAsync(Context.User);

            if (userSettings == null || userSettings.UserNameLastFM == null)
            {
                await ReplyAsync("Your LastFM username has not been set. Please set your username using the `.fmset [username] [embedfull/embedmini/textfull/textmini]` command.");
                return;
            }

            try
            {
                LastTrack track = await lastFMService.GetLastScrobbleAsync(userSettings.UserNameLastFM);

                if (track == null)
                {
                    await ReplyAsync("No scrobbles found on your LastFM profile. (" + userSettings.UserNameLastFM + ")");
                    return;
                }

                try
                {
                    string querystring = track.Name + " - " + track.ArtistName + " " + track.AlbumName;

                    VideoInformation youtubeResult = youtubeService.GetSearchResult(querystring);

                    await ReplyAsync(youtubeResult.Url);
                }
                catch (Exception e)
                {
                    DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                    ExceptionReporter.ReportException(disclient, e);
                    await ReplyAsync("No results have been found for this track.");
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);
                await ReplyAsync("Unable to show Last.FM info via YouTube due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("fmspotify"), Summary("Shares a link to a Spotify track based on what a user is listening to")]
        public async Task fmspotifyAsync(IUser user = null)
        {
            Settings userSettings = await userService.GetUserSettingsAsync(Context.User);

            if (userSettings == null || userSettings.UserNameLastFM == null)
            {
                await ReplyAsync("Your LastFM username has not been set. Please set your username using the `.fmset [username] [embedfull/embedmini/textfull/textmini]` command.");
                return;
            }

            try
            {
                PageResponse<LastTrack> tracks = await lastFMService.GetRecentScrobblesAsync(userSettings.UserNameLastFM, 1);
                LastTrack currentTrack = tracks.Content.ElementAt(0);

                string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? null : currentTrack.Name;
                string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? null : currentTrack.ArtistName;
                string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? null : currentTrack.AlbumName;

                string querystring = null;

                querystring = TrackName + " - " + ArtistName + " " + AlbumName;

                SearchItem item = await spotifyService.GetSearchResultAsync(querystring);

                if (item.Tracks.Items.Any())
                {
                    FullTrack track = item.Tracks.Items.FirstOrDefault();
                    SimpleArtist trackArtist = track.Artists.FirstOrDefault();

                    await ReplyAsync("https://open.spotify.com/track/" + track.Id);
                }
                else
                {
                    await ReplyAsync("No results have been found for this track.");
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);
                await ReplyAsync("Unable to show Last.FM info via Spotify due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("fmspotifysearch"), Summary("Shares a link to a Spotify track based on a user's search parameters")]
        [Alias("fmspotifyfind")]
        public async Task fmspotifysearchAsync(params string[] searchterms)
        {
            try
            {
                string querystring = null;

                if (searchterms.Any())
                {
                    querystring = string.Join(" ", searchterms);

                    SearchItem item = await spotifyService.GetSearchResultAsync(querystring);

                    if (item.Tracks.Items.Any())
                    {
                        FullTrack track = item.Tracks.Items.FirstOrDefault();
                        SimpleArtist trackArtist = track.Artists.FirstOrDefault();

                        await ReplyAsync("https://open.spotify.com/track/" + track.Id);
                    }
                    else
                    {
                        await ReplyAsync("No results have been found for this track.");
                    }
                }
                else
                {
                    await ReplyAsync("Please specify what you want to search for.");
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);
                await ReplyAsync("Unable to search for music via Spotify due to an internal error.");
            }
        }

        [Command("fmchart"), Summary("Generates a chart based on a user's parameters.")]
        public async Task fmchartAsync(string time = "weekly", string chartsize = "3x3", string titlesetting = "titles", IUser user = null)
        {
            JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

            if (time == "help")
            {
                await ReplyAsync(cfgjson.CommandPrefix + "fmchart [weekly/monthly/yearly/overall] [3x3-10x10] [notitles/titles] [user]");
                return;
            }

            Settings userSettings = await userService.GetUserSettingsAsync(Context.User);

            if (userSettings == null || userSettings.UserNameLastFM == null)
            {
                await ReplyAsync("Your LastFM username has not been set. Please set your username using the `.fmset <username> <embedfull/embedmini/textfull/textmini>` command.");
                return;
            }

            ISelfUser SelfUser = Context.Client.CurrentUser;

            string loadingText = "";

            string titletext = "(With Titles)";

            if (titlesetting == "titles")
            {
                titletext = "(With Titles)";
            }
            else if (titlesetting == "notitles")
            {
                titletext = "(Without Titles)";
            }

            if (time.Equals("weekly") || time.Equals("week") || time.Equals("w"))
            {
                loadingText = "Loading your Weekly " + chartsize + " " + SelfUser.Username + " chart " + titletext + "... (may take a while depending on the size of your chart)";
            }
            else if (time.Equals("monthly") || time.Equals("month") || time.Equals("m"))
            {
                loadingText = "Loading your Monthly " + chartsize + " " + SelfUser.Username + " chart " + titletext + "... (may take a while depending on the size of your chart)";
            }
            else if (time.Equals("yearly") || time.Equals("year") || time.Equals("y"))
            {
                loadingText = "Loading your Yearly " + chartsize + " " + SelfUser.Username + " chart " + titletext + "... (may take a while depending on the size of your chart)";
            }
            else if (time.Equals("overall") || time.Equals("alltime") || time.Equals("o") || time.Equals("at"))
            {
                loadingText = "Loading your Overall " + chartsize + " " + SelfUser.Username + " chart " + titletext + "... (may take a while depending on the size of your chart)";
            }
            else
            {
                loadingText = "Loading your " + chartsize + " " + SelfUser.Username + " chart " + titletext + "... (may take a while depending on the size of your chart)";
            }

            IUserMessage loadingmsg = await Context.Channel.SendMessageAsync(loadingText);

            try
            {
                IUser DiscordUser = GlobalVars.CheckIfDM(user, Context);
                string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
                if (LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("Unable to generate a FMChart due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
                }
                else
                {
                    LastfmClient client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

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
                        chartalbums = "49";
                        chartrows = "7";
                    }
                    else if (chartsize.Equals("8x8"))
                    {
                        chartalbums = "64";
                        chartrows = "8";
                    }
                    else if (chartsize.Equals("9x9"))
                    {
                        chartalbums = "81";
                        chartrows = "9";
                    }
                    else if (chartsize.Equals("10x10"))
                    {
                        chartalbums = "100";
                        chartrows = "10";
                    }
                    else
                    {
                        await ReplyAsync("Your chart's size isn't valid. Sizes supported: 3x3-10x10");
                        return;
                    }

                    int max = int.Parse(chartalbums);
                    int rows = int.Parse(chartrows);

                    List<Bitmap> images = new List<Bitmap>();

                    bool TitleBool = true;

                    if (titlesetting == "titles")
                    {
                        TitleBool = true;
                    }
                    else if (titlesetting == "notitles")
                    {
                        TitleBool = false;
                    }

                    FMBotChart chart = new FMBotChart();
                    chart.time = time;
                    chart.client = client;
                    chart.LastFMName = LastFMName;
                    chart.max = max;
                    chart.rows = rows;
                    chart.images = images;
                    chart.DiscordUser = DiscordUser;
                    chart.disclient = Context.Client as DiscordSocketClient;
                    chart.mode = 0;
                    chart.titles = TitleBool;
                    await chart.ChartGenerate();

                    await Context.Channel.SendFileAsync(GlobalVars.UsersFolder + DiscordUser.Id + "-chart.png");

                    EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                    eab.IconUrl = DiscordUser.GetAvatarUrl();
                    eab.Name = GlobalVars.GetNameString(DiscordUser, Context);

                    EmbedBuilder builder = new EmbedBuilder();
                    builder.WithAuthor(eab);
                    string URI = "https://www.last.fm/user/" + LastFMName;
                    builder.WithUrl(URI);
                    builder.Title = await userService.GetUserTitleAsync(Context);

                    if (time.Equals("weekly") || time.Equals("week") || time.Equals("w"))
                    {
                        builder.WithDescription("Last.FM " + chartsize + " Weekly Chart for " + LastFMName);
                    }
                    else if (time.Equals("monthly") || time.Equals("month") || time.Equals("m"))
                    {
                        builder.WithDescription("Last.FM " + chartsize + " Monthly Chart for " + LastFMName);
                    }
                    else if (time.Equals("yearly") || time.Equals("year") || time.Equals("y"))
                    {
                        builder.WithDescription("Last.FM " + chartsize + " Yearly Chart for " + LastFMName);
                    }
                    else if (time.Equals("overall") || time.Equals("alltime") || time.Equals("o") || time.Equals("at"))
                    {
                        builder.WithDescription("Last.FM " + chartsize + " Overall Chart for " + LastFMName);
                    }
                    else
                    {
                        builder.WithDescription("Last.FM " + chartsize + " Chart for " + LastFMName);
                    }

                    LastResponse<LastUser> userinfo = await client.User.GetInfoAsync(LastFMName);
                    EmbedFooterBuilder efb = new EmbedFooterBuilder();
                    int playcount = userinfo.Content.Playcount;
                    efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString("0");

                    builder.WithFooter(efb);

                    await loadingmsg.DeleteAsync();
                    await Context.Channel.SendMessageAsync("", false, builder.Build());
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                await loadingmsg.DeleteAsync();
                await ReplyAsync("Unable to generate a FMChart due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("fmartistchart"), Summary("Generates an artist chart based on a user's parameters.")]
        public async Task fmartistchartAsync(string time = "weekly", string chartsize = "3x3", string titlesetting = "titles", IUser user = null)
        {
            JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

            if (time == "help")
            {
                await ReplyAsync(cfgjson.CommandPrefix + "fmartistchart [weekly/monthly/yearly/overall] [3x3-10x10] [notitles/titles] [user]");
                return;
            }

            ISelfUser SelfUser = Context.Client.CurrentUser;


            string loadingText = "";

            string titletext = "(With Titles)";

            if (titlesetting == "titles")
            {
                titletext = "(With Titles)";
            }
            else if (titlesetting == "notitles")
            {
                titletext = "(Without Titles)";
            }

            if (time.Equals("weekly") || time.Equals("week") || time.Equals("w"))
            {
                loadingText = "Loading your Weekly " + chartsize + " " + SelfUser.Username + " artist chart " + titletext + "... (may take a while depending on the size of your chart)";
            }
            else if (time.Equals("monthly") || time.Equals("month") || time.Equals("m"))
            {
                loadingText = "Loading your Monthly " + chartsize + " " + SelfUser.Username + " artist chart " + titletext + "... (may take a while depending on the size of your chart)";
            }
            else if (time.Equals("yearly") || time.Equals("year") || time.Equals("y"))
            {
                loadingText = "Loading your Yearly " + chartsize + " " + SelfUser.Username + " artist chart " + titletext + "... (may take a while depending on the size of your chart)";
            }
            else if (time.Equals("overall") || time.Equals("alltime") || time.Equals("o") || time.Equals("at"))
            {
                loadingText = "Loading your Overall " + chartsize + " " + SelfUser.Username + " artist chart " + titletext + "... (may take a while depending on the size of your chart)";
            }
            else
            {
                loadingText = "Loading your " + chartsize + " " + SelfUser.Username + " artist chart " + titletext + "... (may take a while depending on the size of your chart)";
            }

            IUserMessage loadingmsg = await Context.Channel.SendMessageAsync(loadingText);

            try
            {
                IUser DiscordUser = GlobalVars.CheckIfDM(user, Context);
                string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
                if (LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("Unable to generate a FMChart due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
                }
                else
                {
                    LastfmClient client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

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
                        chartalbums = "49";
                        chartrows = "7";
                    }
                    else if (chartsize.Equals("8x8"))
                    {
                        chartalbums = "64";
                        chartrows = "8";
                    }
                    else if (chartsize.Equals("9x9"))
                    {
                        chartalbums = "81";
                        chartrows = "9";
                    }
                    else if (chartsize.Equals("10x10"))
                    {
                        chartalbums = "100";
                        chartrows = "10";
                    }
                    else
                    {
                        await ReplyAsync("Your artist chart's size isn't valid. Sizes supported: 3x3-10x10");
                        return;
                    }

                    int max = int.Parse(chartalbums);
                    int rows = int.Parse(chartrows);

                    List<Bitmap> images = new List<Bitmap>();

                    bool TitleBool = true;

                    if (titlesetting == "titles")
                    {
                        TitleBool = true;
                    }
                    else if (titlesetting == "notitles")
                    {
                        TitleBool = false;
                    }

                    FMBotChart chart = new FMBotChart();
                    chart.time = time;
                    chart.client = client;
                    chart.LastFMName = LastFMName;
                    chart.max = max;
                    chart.rows = rows;
                    chart.images = images;
                    chart.DiscordUser = DiscordUser;
                    chart.disclient = Context.Client as DiscordSocketClient;
                    chart.mode = 1;
                    chart.titles = TitleBool;
                    await chart.ChartGenerate();

                    await Context.Channel.SendFileAsync(GlobalVars.UsersFolder + DiscordUser.Id + "-chart.png");

                    EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                    eab.IconUrl = DiscordUser.GetAvatarUrl();
                    eab.Name = GlobalVars.GetNameString(DiscordUser, Context);

                    EmbedBuilder builder = new EmbedBuilder();
                    builder.WithAuthor(eab);
                    string URI = "https://www.last.fm/user/" + LastFMName;
                    builder.WithUrl(URI);
                    builder.Title = await userService.GetUserTitleAsync(Context);

                    if (time.Equals("weekly") || time.Equals("week") || time.Equals("w"))
                    {
                        builder.WithDescription("Last.FM " + chartsize + " Weekly Artist Chart for " + LastFMName);
                    }
                    else if (time.Equals("monthly") || time.Equals("month") || time.Equals("m"))
                    {
                        builder.WithDescription("Last.FM " + chartsize + " Monthly Artist Chart for " + LastFMName);
                    }
                    else if (time.Equals("yearly") || time.Equals("year") || time.Equals("y"))
                    {
                        builder.WithDescription("Last.FM " + chartsize + " Yearly Artist Chart for " + LastFMName);
                    }
                    else if (time.Equals("overall") || time.Equals("alltime") || time.Equals("o") || time.Equals("at"))
                    {
                        builder.WithDescription("Last.FM " + chartsize + " Overall Artist Chart for " + LastFMName);
                    }
                    else
                    {
                        builder.WithDescription("Last.FM " + chartsize + " Artist Chart for " + LastFMName);
                    }

                    LastResponse<LastUser> userinfo = await client.User.GetInfoAsync(LastFMName);
                    EmbedFooterBuilder efb = new EmbedFooterBuilder();
                    int playcount = userinfo.Content.Playcount;
                    efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString("0");

                    builder.WithFooter(efb);

                    await loadingmsg.DeleteAsync();
                    await Context.Channel.SendMessageAsync("", false, builder.Build());
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                await loadingmsg.DeleteAsync();
                await ReplyAsync("Unable to generate a FMChart due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("fmfriends"), Summary("Displays a user's friends and what they are listening to.")]
        [Alias("fmrecentfriends", "fmfriendsrecent")]
        public async Task fmfriendsrecentAsync(IUser user = null)
        {
            try
            {
                JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

                IUser DiscordUser = GlobalVars.CheckIfDM(user, Context);
                ISelfUser SelfUser = Context.Client.CurrentUser;
                string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
                if (LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("Unable to load your FMBot Friends due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
                }
                else
                {
                    string[] LastFMFriends = DBase.GetFriendsForID(DiscordUser.Id.ToString());
                    if (LastFMFriends == null || !LastFMFriends.Any())
                    {
                        await ReplyAsync("Your FMBot Friends were unable to be found. Please use fmsetfriends with your friends' names to set your friends.");
                    }
                    else
                    {
                        LastfmClient client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                        try
                        {
                            EmbedAuthorBuilder eab = new EmbedAuthorBuilder();

                            eab.IconUrl = DiscordUser.GetAvatarUrl();

                            eab.Name = GlobalVars.GetNameString(DiscordUser, Context);

                            EmbedBuilder builder = new EmbedBuilder();
                            builder.WithAuthor(eab);
                            string URI = "https://www.last.fm/user/" + LastFMName;
                            builder.WithUrl(URI);
                            builder.Title = await userService.GetUserTitleAsync(Context);

                            string amountOfScrobbles = "Amount of scrobbles of all your friends together: ";

                            if (LastFMFriends.Count() > 1)
                            {
                                builder.WithDescription("Songs from " + LastFMFriends.Count() + " friends");
                            }
                            else
                            {
                                builder.WithDescription("Songs from your friend");
                                amountOfScrobbles = "Amount of scrobbles from your friend: ";
                            }

                            string nulltext = "[undefined]";
                            int indexval = (LastFMFriends.Count() - 1);
                            int playcount = 0;

                            try
                            {
                                foreach (string friend in LastFMFriends)
                                {
                                    PageResponse<LastTrack> tracks = await client.User.GetRecentScrobbles(friend, null, 1, 1);

                                    string TrackName = string.IsNullOrWhiteSpace(tracks.FirstOrDefault().Name) ? nulltext : tracks.FirstOrDefault().Name;
                                    string ArtistName = string.IsNullOrWhiteSpace(tracks.FirstOrDefault().ArtistName) ? nulltext : tracks.FirstOrDefault().ArtistName;
                                    string AlbumName = string.IsNullOrWhiteSpace(tracks.FirstOrDefault().AlbumName) ? nulltext : tracks.FirstOrDefault().AlbumName;

                                    builder.AddField(friend.ToString() + ":", TrackName + " - " + ArtistName + " | " + AlbumName);

                                    // count how many scrobbles everyone has together (if the bot is too slow, consider removing this?)
                                    if (LastFMFriends.Count() <= 8)
                                    {
                                        LastResponse<LastUser> userinfo = await client.User.GetInfoAsync(friend);
                                        playcount = playcount + userinfo.Content.Playcount;
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                                ExceptionReporter.ReportException(disclient, e);
                            }

                            if (LastFMFriends.Count() <= 8)
                            {
                                EmbedFooterBuilder efb = new EmbedFooterBuilder();
                                efb.Text = amountOfScrobbles + playcount.ToString("0");
                                builder.WithFooter(efb);
                            }

                            await Context.Channel.SendMessageAsync("", false, builder.Build());
                        }
                        catch (Exception e)
                        {
                            DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                            ExceptionReporter.ReportException(disclient, e);

                            await ReplyAsync("Unable to load your FMBot Friends due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again. If nothing's fixed, see if your friends have done the same thing.");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                await ReplyAsync("Your FMBot Friends were unable to be found. Please use fmsetfriends with your friends' names to set your friends.");
            }
        }

        [Command("fmrecent"), Summary("Displays a user's recent tracks.")]
        [Alias("fmrecenttracks")]
        public async Task fmrecentAsync(string list = "5", IUser user = null)
        {
            JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

            if (list == "help")
            {
                await ReplyAsync(cfgjson.CommandPrefix + "fmrecent [number of items] [user]");
                return;
            }

            try
            {
                int num = int.Parse(list);

                IUser DiscordUser = GlobalVars.CheckIfDM(user, Context);
                ISelfUser SelfUser = Context.Client.CurrentUser;
                string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
                if (LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("Unable to show your recent tracks on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
                }
                else
                {
                    LastfmClient client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                    try
                    {
                        PageResponse<LastTrack> tracks = await client.User.GetRecentScrobbles(LastFMName, null, 1, num);

                        EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                        eab.IconUrl = DiscordUser.GetAvatarUrl();
                        eab.Name = GlobalVars.GetNameString(DiscordUser, Context);

                        EmbedBuilder builder = new EmbedBuilder();
                        builder.WithAuthor(eab);
                        string URI = "https://www.last.fm/user/" + LastFMName;
                        builder.WithUrl(URI);
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

                            try
                            {
                                if (i == 0)
                                {
                                    LastResponse<LastAlbum> AlbumInfo = await client.Album.GetInfoAsync(ArtistName, AlbumName);
                                    LastImageSet AlbumImages = (AlbumInfo.Content.Images != null) ? AlbumInfo.Content.Images : null;
                                    string AlbumThumbnail = (AlbumImages != null) ? AlbumImages.Large.AbsoluteUri : null;
                                    string ThumbnailImage = (AlbumThumbnail != null) ? AlbumThumbnail.ToString() : null;

                                    if (!string.IsNullOrWhiteSpace(ThumbnailImage))
                                    {
                                        builder.WithThumbnailUrl(ThumbnailImage);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                                ExceptionReporter.ReportException(disclient, e);
                            }

                            int correctnum = (i + 1);
                            builder.AddField("Track #" + correctnum.ToString() + ":", TrackName + " - " + ArtistName + " | " + AlbumName);
                        }

                        EmbedFooterBuilder efb = new EmbedFooterBuilder();

                        LastResponse<LastUser> userinfo = await client.User.GetInfoAsync(LastFMName);
                        int playcount = userinfo.Content.Playcount;

                        efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString("0");

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
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                await ReplyAsync("Unable to show your recent tracks on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("fmartists"), Summary("Displays artists that a user listened to.")]
        public async Task fmartistsAsync(string list = "5", string time = "overall", IUser user = null)
        {
            JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

            if (list == "help")
            {
                await ReplyAsync(cfgjson.CommandPrefix + "fmartists [number of items] [weekly/monthly/yearly/overall] [user]");
                return;
            }

            try
            {
                int num = int.Parse(list);
                IUser DiscordUser = GlobalVars.CheckIfDM(user, Context);
                ISelfUser SelfUser = Context.Client.CurrentUser;
                string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
                if (LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("Unable to show your artists on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
                }
                else
                {
                    LastfmClient client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                    try
                    {
                        LastStatsTimeSpan timespan = LastStatsTimeSpan.Overall;

                        if (time.Equals("weekly") || time.Equals("week") || time.Equals("w"))
                        {
                            timespan = LastStatsTimeSpan.Week;
                        }
                        else if (time.Equals("monthly") || time.Equals("month") || time.Equals("m"))
                        {
                            timespan = LastStatsTimeSpan.Month;
                        }
                        else if (time.Equals("yearly") || time.Equals("year") || time.Equals("y"))
                        {
                            timespan = LastStatsTimeSpan.Year;
                        }
                        else if (time.Equals("overall") || time.Equals("alltime") || time.Equals("o") || time.Equals("at"))
                        {
                            timespan = LastStatsTimeSpan.Overall;
                        }

                        PageResponse<LastArtist> artists = await client.User.GetTopArtists(LastFMName, timespan, 1, num);

                        EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                        eab.IconUrl = DiscordUser.GetAvatarUrl();
                        eab.Name = GlobalVars.GetNameString(DiscordUser, Context);

                        EmbedBuilder builder = new EmbedBuilder();
                        builder.WithAuthor(eab);
                        string URI = "https://www.last.fm/user/" + LastFMName;
                        builder.WithUrl(URI);
                        builder.Title = await userService.GetUserTitleAsync(Context);

                        if (time.Equals("weekly") || time.Equals("week") || time.Equals("w"))
                        {
                            builder.WithDescription("Top " + num + " Weekly Artist List");
                        }
                        else if (time.Equals("monthly") || time.Equals("month") || time.Equals("m"))
                        {
                            builder.WithDescription("Top " + num + " Monthly Artist List");
                        }
                        else if (time.Equals("yearly") || time.Equals("year") || time.Equals("y"))
                        {
                            builder.WithDescription("Top " + num + " Yearly Artist List");
                        }
                        else if (time.Equals("overall") || time.Equals("alltime") || time.Equals("o") || time.Equals("at"))
                        {
                            builder.WithDescription("Top " + num + " Overall Artist List");
                        }

                        string nulltext = "[undefined]";
                        int indexval = (num - 1);
                        for (int i = 0; i <= indexval; i++)
                        {
                            LastArtist artist = artists.Content.ElementAt(i);

                            string ArtistName = string.IsNullOrWhiteSpace(artist.Name) ? nulltext : artist.Name;

                            try
                            {
                                if (i == 0)
                                {
                                    LastResponse<LastArtist> ArtistInfo = await client.Artist.GetInfoAsync(ArtistName);
                                    LastImageSet ArtistImages = (ArtistInfo.Content.MainImage != null) ? ArtistInfo.Content.MainImage : null;
                                    string ArtistThumbnail = (ArtistImages != null) ? ArtistImages.Large.AbsoluteUri : null;
                                    string ThumbnailImage = (ArtistThumbnail != null) ? ArtistThumbnail.ToString() : null;

                                    if (!string.IsNullOrWhiteSpace(ThumbnailImage))
                                    {
                                        builder.WithThumbnailUrl(ThumbnailImage);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                                ExceptionReporter.ReportException(disclient, e);
                            }

                            int correctnum = (i + 1);
                            builder.AddField("Artist #" + correctnum.ToString() + ":", ArtistName);
                        }

                        EmbedFooterBuilder efb = new EmbedFooterBuilder();

                        LastResponse<LastUser> userinfo = await client.User.GetInfoAsync(LastFMName);
                        int playcount = userinfo.Content.Playcount;

                        efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString("0");

                        builder.WithFooter(efb);

                        await Context.Channel.SendMessageAsync("", false, builder.Build());
                    }
                    catch (Exception e)
                    {
                        DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                        ExceptionReporter.ReportException(disclient, e);

                        await ReplyAsync("Unable to show your artists on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
                    }
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                await ReplyAsync("Unable to show your artists on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("fmalbums"), Summary("Displays albums that a user listened to.")]
        public async Task fmalbumsAsync(string list = "5", string time = "overall", IUser user = null)
        {
            JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

            if (list == "help")
            {
                await ReplyAsync(cfgjson.CommandPrefix + "fmalbums [number of items] [weekly/monthly/yearly/overall] [user]");
                return;
            }

            try
            {
                int num = int.Parse(list);
                IUser DiscordUser = GlobalVars.CheckIfDM(user, Context);
                ISelfUser SelfUser = Context.Client.CurrentUser;
                string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
                if (LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("Unable to show your albums on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
                }
                else
                {
                    LastfmClient client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                    try
                    {
                        LastStatsTimeSpan timespan = LastStatsTimeSpan.Overall;

                        if (time.Equals("weekly") || time.Equals("week") || time.Equals("w"))
                        {
                            timespan = LastStatsTimeSpan.Week;
                        }
                        else if (time.Equals("monthly") || time.Equals("month") || time.Equals("m"))
                        {
                            timespan = LastStatsTimeSpan.Month;
                        }
                        else if (time.Equals("yearly") || time.Equals("year") || time.Equals("y"))
                        {
                            timespan = LastStatsTimeSpan.Year;
                        }
                        else if (time.Equals("overall") || time.Equals("alltime") || time.Equals("o") || time.Equals("at"))
                        {
                            timespan = LastStatsTimeSpan.Overall;
                        }

                        PageResponse<LastAlbum> albums = await client.User.GetTopAlbums(LastFMName, timespan, 1, num);

                        EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                        eab.IconUrl = DiscordUser.GetAvatarUrl();
                        eab.Name = GlobalVars.GetNameString(DiscordUser, Context);

                        EmbedBuilder builder = new EmbedBuilder();
                        builder.WithAuthor(eab);
                        string URI = "https://www.last.fm/user/" + LastFMName;
                        builder.WithUrl(URI);
                        builder.Title = await userService.GetUserTitleAsync(Context);

                        if (time.Equals("weekly") || time.Equals("week") || time.Equals("w"))
                        {
                            builder.WithDescription("Top " + num + " Weekly Album List");
                        }
                        else if (time.Equals("monthly") || time.Equals("month") || time.Equals("m"))
                        {
                            builder.WithDescription("Top " + num + " Monthly Album List");
                        }
                        else if (time.Equals("yearly") || time.Equals("year") || time.Equals("y"))
                        {
                            builder.WithDescription("Top " + num + " Yearly Album List");
                        }
                        else if (time.Equals("overall") || time.Equals("alltime") || time.Equals("o") || time.Equals("at"))
                        {
                            builder.WithDescription("Top " + num + " Overall Album List");
                        }

                        string nulltext = "[undefined]";
                        int indexval = (num - 1);
                        for (int i = 0; i <= indexval; i++)
                        {
                            LastAlbum album = albums.Content.ElementAt(i);

                            string AlbumName = string.IsNullOrWhiteSpace(album.Name) ? nulltext : album.Name;
                            string ArtistName = string.IsNullOrWhiteSpace(album.ArtistName) ? nulltext : album.ArtistName;

                            try
                            {
                                if (i == 0)
                                {
                                    LastResponse<LastAlbum> AlbumInfo = await client.Album.GetInfoAsync(ArtistName, AlbumName);
                                    LastImageSet AlbumImages = (AlbumInfo.Content.Images != null) ? AlbumInfo.Content.Images : null;
                                    string AlbumThumbnail = (AlbumImages != null) ? AlbumImages.Large.AbsoluteUri : null;
                                    string ThumbnailImage = (AlbumThumbnail != null) ? AlbumThumbnail.ToString() : null;

                                    if (!string.IsNullOrWhiteSpace(ThumbnailImage))
                                    {
                                        builder.WithThumbnailUrl(ThumbnailImage);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                                ExceptionReporter.ReportException(disclient, e);
                            }

                            int correctnum = (i + 1);
                            builder.AddField("Album #" + correctnum.ToString() + ":", AlbumName + " | " + ArtistName);
                        }

                        EmbedFooterBuilder efb = new EmbedFooterBuilder();

                        LastResponse<LastUser> userinfo = await client.User.GetInfoAsync(LastFMName);
                        int playcount = userinfo.Content.Playcount;

                        efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString("0");

                        builder.WithFooter(efb);

                        await Context.Channel.SendMessageAsync("", false, builder.Build());
                    }
                    catch (Exception e)
                    {
                        DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                        ExceptionReporter.ReportException(disclient, e);

                        await ReplyAsync("Unable to show your albums on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
                    }
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                await ReplyAsync("Unable to show your albums on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("fmstats"), Summary("Displays user stats related to Last.FM and FMBot")]
        [Alias("fminfo")]
        public async Task fmstatsAsync(IUser user = null)
        {
            try
            {
                JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

                IUser DiscordUser = GlobalVars.CheckIfDM(user, Context);
                ISelfUser SelfUser = Context.Client.CurrentUser;
                string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
                if (LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("Unable to show your stats on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
                }
                else
                {
                    LastfmClient client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

                    EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                    eab.IconUrl = DiscordUser.GetAvatarUrl();
                    eab.Name = GlobalVars.GetNameString(DiscordUser, Context);

                    EmbedBuilder builder = new EmbedBuilder();
                    builder.WithAuthor(eab);
                    string URI = "https://www.last.fm/user/" + LastFMName;
                    builder.WithUrl(URI);
                    builder.Title = await userService.GetUserTitleAsync(Context);
                    builder.WithDescription("Last.FM Statistics for " + LastFMName);

                    LastResponse<LastUser> userinfo = await client.User.GetInfoAsync(LastFMName);

                    try
                    {
                        LastImageSet userinfoImages = (userinfo.Content.Avatar != null) ? userinfo.Content.Avatar : null;
                        string userinfoThumbnail = (userinfoImages != null) ? userinfoImages.Large.AbsoluteUri : null;
                        string ThumbnailImage = (userinfoThumbnail != null) ? userinfoThumbnail.ToString() : null;

                        if (!string.IsNullOrWhiteSpace(ThumbnailImage))
                        {
                            builder.WithThumbnailUrl(ThumbnailImage);
                        }
                    }
                    catch (Exception e)
                    {
                        DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                        ExceptionReporter.ReportException(disclient, e);
                    }

                    int playcount = userinfo.Content.Playcount;
                    string usertype = userinfo.Content.Type;
                    int playlists = userinfo.Content.Playlists;
                    bool premium = userinfo.Content.IsSubscriber;

                    string LastFMMode = DBase.GetNameForModeInt(DBase.GetModeIntForID(DiscordUser.Id.ToString()));

                    builder.AddInlineField("Last.FM Name: ", LastFMName);
                    builder.AddInlineField(SelfUser.Username + " Mode: ", LastFMMode);
                    builder.AddInlineField("User Type: ", usertype.ToString());
                    builder.AddInlineField("Total Tracks: ", playcount.ToString("0"));
                    builder.AddInlineField("Has Last.FM Premium? ", premium.ToString());
                    builder.AddInlineField("Is " + SelfUser.Username + " Admin? ", FMBotAdminUtil.IsAdmin(DiscordUser).ToString());
                    builder.AddInlineField("Is " + SelfUser.Username + " Super Admin? ", FMBotAdminUtil.IsSuperAdmin(DiscordUser).ToString());
                    builder.AddInlineField("Is " + SelfUser.Username + " Owner? ", FMBotAdminUtil.IsOwner(DiscordUser).ToString());

                    await Context.Channel.SendMessageAsync("", false, builder.Build());
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                await ReplyAsync("Unable to show your stats on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        #endregion

        #region FMBot Commands

        [Command("fmfeatured"), Summary("Displays the featured avatar.")]
        [Alias("fmfeaturedavatar", "fmfeatureduser", "fmfeaturedalbum")]
        public async Task fmfeaturedAsync()
        {
            try
            {
                EmbedBuilder builder = new EmbedBuilder();
                ISelfUser SelfUser = Context.Client.CurrentUser;
                builder.WithThumbnailUrl(SelfUser.GetAvatarUrl());
                builder.AddInlineField("Featured:", _timer.GetTrackString());

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
                await ReplyAsync(cfgjson.CommandPrefix + "fmset <Last.FM Username> [embedmini/embedfull/textfull/textmini]");
                return;
            }

            if (!await lastFMService.LastFMUserExistsAsync(lastFMUserName))
            {
                await ReplyAsync("LastFM user could not be found. Please check if the name you entered is correct.");
                return;
            }

            if (!Enum.TryParse(chartType, out ChartType chartTypeEnum))
            {
                await ReplyAsync("Invalid mode. Please use 'embedmini', 'embedfull', 'textfull', or 'textmini'.");
                return;
            }

            userService.SetLastFM(Context.User, lastFMUserName, chartTypeEnum);

            await ReplyAsync("Your Last.FM name has been set to '" + lastFMUserName + "' and your mode has been set to '" + chartType + "'.");
        }

        [Command("fmaddfriends"), Summary("Adds your friends' Last.FM names.")]
        [Alias("fmfriendsset", "fmsetfriends", "fmfriendsadd")]
        public async Task fmfriendssetAsync([Summary("Friend names")] params string[] friends)
        {
            try
            {
                string SelfID = Context.Message.Author.Id.ToString();

                JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();
                LastfmClient client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

                List<string> friendList = new List<string>();
                List<string> friendNotFoundList = new List<string>();


                foreach (string friend in friends)
                {
                    if (!guildService.CheckIfDM(Context))
                    {
                        var user = guildService.FindUserFromGuildAsync(Context, friend);

                        if (user != null)
                        {
                            Settings userSettings = await userService.GetUserSettingsAsync((IUser)user);

                            if (userSettings == null || userSettings.UserNameLastFM == null)
                            {

                            }
                        }
                    }
                    

                    if (await lastFMService.LastFMUserExistsAsync(friend))
                    {
                        //friendList.Add(user.Content.Name);
                    }
                    else
                    {
                        friendNotFoundList.Add(friend);
                    }
                }

                if (friendList.Any())
                {
                    int friendcount = DBase.AddFriendsEntry(SelfID, friendList.ToArray());

                    if (friendcount > 1)
                    {
                        await ReplyAsync("Succesfully added " + friendcount + " friends.");
                    }
                    else if (friendcount < 1)
                    {
                        await ReplyAsync("Didn't add  " + friendcount + " friends. Maybe they are already on your friendlist.");
                    }
                    else
                    {
                        await ReplyAsync("Succesfully added a friend.");
                    }
                }

                if (friendNotFoundList.Any())
                {
                    if (friendNotFoundList.Count > 1)
                    {
                        await ReplyAsync("Could not find " + friendNotFoundList.Count + " friends. Please ensure that you spelled their names correctly.");
                    }
                    else
                    {
                        await ReplyAsync("Could not find 1 friend. Please ensure that you spelled the name correctly.");
                    }
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                int friendcount = friends.Count();

                if (friendcount > 1)
                {
                    await ReplyAsync("Unable to add " + friendcount + " friends due to an internal error.");
                }
                else
                {
                    await ReplyAsync("Unable to add a friend due to an internal error.");
                }
            }
        }

        [Command("fmremovefriends"), Summary("Remove your friends' Last.FM names.")]
        [Alias("fmfriendsremove")]
        public async Task fmfriendsremoveAsync([Summary("Friend names")] params string[] friends)
        {
            if (!friends.Any())
            {
                await ReplyAsync("Please enter at least one friend to remove.");
                return;
            }

            try
            {
                string SelfID = Context.Message.Author.Id.ToString();

                int friendcount = DBase.RemoveFriendsEntry(SelfID, friends);

                if (friendcount > 1)
                {
                    await ReplyAsync("Succesfully removed " + friendcount + " friends.");
                }
                else if (friendcount < 1)
                {
                    await ReplyAsync("Couldn't remove " + friendcount + " friends. Please check if the user is on your friendslist.");
                }
                else
                {
                    await ReplyAsync("Succesfully removed a friend.");
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                int friendcount = friends.Count();

                if (friendcount > 1)
                {
                    await ReplyAsync("Unable to remove " + friendcount + " friends due to an internal error. Did you add anyone?");
                }
                else
                {
                    await ReplyAsync("Unable to add a friend due to an internal error. Did you add anyone?");
                }
            }
        }

        [Command("fmremove"), Summary("Deletes your FMBot data.")]
        [Alias("fmdelete", "fmremovedata", "fmdeletedata")]
        public async Task fmremoveAsync()
        {
            string SelfID = Context.Message.Author.Id.ToString();
            ISelfUser SelfUser = Context.Client.CurrentUser;
            DBase.RemoveEntry(SelfID);
            await ReplyAsync("Your " + SelfUser.Username + " settings and data have been successfully deleted.");
        }

        [Command("fmhelp"), Summary("Displays this list.")]
        [Alias("fmbot")]
        public async Task fmhelpAsync()
        {
            JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

            string prefix = cfgjson.CommandPrefix;

            ISelfUser SelfUser = Context.Client.CurrentUser;

            foreach (ModuleInfo module in _service.Modules)
            {
                string description = null;
                foreach (CommandInfo cmd in module.Commands)
                {
                    PreconditionResult result = await cmd.CheckPreconditionsAsync(Context);
                    if (result.IsSuccess)
                    {
                        if (!string.IsNullOrWhiteSpace(cmd.Summary))
                        {
                            description += $"{prefix}{cmd.Aliases.First()} - {cmd.Summary}\n";
                        }
                        else
                        {
                            description += $"{prefix}{cmd.Aliases.First()}\n";
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(description))
                {
                    await Context.User.SendMessageAsync(module.Name + "\n" + description);
                }
            }

            string helpstring = SelfUser.Username + " Info\n\nBe sure to use 'help' after a command name to see the parameters.\n\nChart sizes range from 3x3 to 10x10.\n\nModes for the fmset command:\nembedmini\nembedfull\ntextfull\ntextmini\nuserdefined (fmserverset only)\n\nFMBot Time Periods for the fmchart, fmartistchart, fmartists, and fmalbums commands:\nweekly\nweek\nw\nmonthly\nmonth\nm\nyearly\nyear\ny\noverall\nalltime\no\nat\n\nFMBot Title options for FMChart:\ntitles\nnotitles";

            if (!GlobalVars.GetDMBool())
            {
                await Context.Channel.SendMessageAsync("Check your DMs!");
                await Context.User.SendMessageAsync(helpstring);
            }
            else
            {
                await Context.Channel.SendMessageAsync(helpstring);
            }

        }

        [Command("fmstatus"), Summary("Displays bot stats.")]
        public async Task statusAsync()
        {
            ISelfUser SelfUser = Context.Client.CurrentUser;

            EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
            eab.IconUrl = SelfUser.GetAvatarUrl();
            eab.Name = SelfUser.Username;

            EmbedBuilder builder = new EmbedBuilder();
            builder.WithAuthor(eab);

            builder.WithDescription(SelfUser.Username + " Statistics");

            TimeSpan startTime = (DateTime.Now - Process.GetCurrentProcess().StartTime);
            IEnumerable<FileData> files = FastDirectoryEnumerator.EnumerateFiles(GlobalVars.UsersFolder, "*.txt");

            string pattern = "[0-9]{18}\\.txt";

            int filecount = 0;

            foreach (FileData file in files)
            {
                if (Regex.IsMatch(file.Name, pattern))
                {
                    filecount += 1;
                }
            }

            DiscordSocketClient SocketClient = Context.Client as DiscordSocketClient;
            int SelfGuilds = SocketClient.Guilds.Count();

            SocketSelfUser SocketSelf = Context.Client.CurrentUser as SocketSelfUser;

            string status = "Online";

            switch (SocketSelf.Status)
            {
                case UserStatus.Offline: status = "Offline"; break;
                case UserStatus.Online: status = "Online"; break;
                case UserStatus.Idle: status = "Idle"; break;
                case UserStatus.AFK: status = "AFK"; break;
                case UserStatus.DoNotDisturb: status = "Do Not Disturb"; break;
                case UserStatus.Invisible: status = "Invisible/Offline"; break;
            }

            string assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            int fixedCmdGlobalCount = GlobalVars.CommandExecutions + 1;
            int fixedCmdGlobalCount_Servers = GlobalVars.CommandExecutions_Servers + 1;
            int fixedCmdGlobalCount_DMs = GlobalVars.CommandExecutions_DMs + 1;

            builder.AddInlineField("Bot Uptime: ", startTime.ToReadableString());
            builder.AddInlineField("Server Uptime: ", GlobalVars.SystemUpTime().ToReadableString());
            builder.AddInlineField("Number of users in the database: ", filecount);
            builder.AddInlineField("Total number of command executions since bot start: ", fixedCmdGlobalCount);
            builder.AddInlineField("Command executions in servers since bot start: ", fixedCmdGlobalCount_Servers);
            builder.AddInlineField("Command executions in DMs since bot start: ", fixedCmdGlobalCount_DMs);
            builder.AddField("Number of servers the bot is on: ", SelfGuilds);
            builder.AddField("Bot status: ", status);
            builder.AddField("Bot version: ", assemblyVersion);

            await Context.Channel.SendMessageAsync("", false, builder.Build());
        }

        [Command("fminvite"), Summary("Invites the bot to a server")]
        public async Task inviteAsync()
        {
            string SelfID = Context.Client.CurrentUser.Id.ToString();
            await ReplyAsync("https://discordapp.com/oauth2/authorize?client_id=" + SelfID + "&scope=bot&permissions=0");
        }

        [Command("fmdonate"), Summary("Please donate if you like this bot!")]
        public async Task donateAsync()
        {
            await ReplyAsync("If you like the bot and you would like to support its development, feel free to support the developer at: https://www.paypal.me/Bitl");
        }

        [Command("fmgithub"), Summary("GitHub Page")]
        public async Task githubAsync()
        {
            await ReplyAsync("https://github.com/Bitl/FMBot.Bot");
        }

        [Command("fmgitlab"), Summary("GitLab Page")]
        public async Task gitlabAsync()
        {
            await ReplyAsync("https://gitlab.com/Bitl/FMBot.Bot");
        }

        [Command("fmbugs"), Summary("Report bugs here!")]
        public async Task bugsAsync()
        {
            await ReplyAsync("Report bugs here:\nGithub: https://github.com/Bitl/FMBot.Bot/issues \nGitLab: https://gitlab.com/Bitl/FMBot.Bot/issues");
        }

        [Command("fmserver"), Summary("Join the Discord server!")]
        public async Task serverAsync()
        {
            await ReplyAsync("Join the Discord server! https://discord.gg/srmpCaa");
        }

        #endregion
    }
}
