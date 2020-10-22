using System;
using System.Globalization;
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
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using IF.Lastfm.Core.Api.Enums;

namespace FMBot.Bot.Commands.LastFM
{
    public class PlayCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;
        private readonly GuildService _guildService;
        private readonly LastFMService _lastFmService;
        private readonly PlayService _playService;
        private readonly IUpdateService _updateService;
        private readonly IIndexService _indexService;


        private readonly UserService _userService;

        private readonly IPrefixService _prefixService;

        public PlayCommands(
            IPrefixService prefixService,
            GuildService guildService,
            UserService userService,
            LastFMService lastFmService,
            PlayService playService,
            IUpdateService updateService,
            IIndexService indexService)
        {
            this._prefixService = prefixService;
            this._guildService = guildService;
            this._userService = userService;
            this._lastFmService = lastFmService;
            this._playService = playService;
            this._updateService = updateService;
            this._indexService = indexService;
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("fm", RunMode = RunMode.Async)]
        [Summary("Displays what a user is listening to.")]
        [Alias("np", "qm", "wm", "em", "rm", "tm", "ym", "um", "om", "pm", "dm", "gm", "sm", "am", "hm", "jm", "km",
            "lm", "zm", "xm", "cm", "vm", "bm", "nm", "mm", "lastfm")]
        [UsernameSetRequired]
        public async Task FMAsync(params string[] user)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (userSettings?.UserNameLastFM == null)
            {
                this._embed.UsernameNotSetErrorResponse(prfx);
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

                                  $"You can set your username and you can change the mode with the `{prfx}set` command.\n";

                var differentMode = userSettings.FmEmbedType == FmEmbedType.embedmini ? "embedfull" : "embedmini";
                replyString += $"`{prfx}set {userSettings.UserNameLastFM} {differentMode}` \n \n" +
                               $"For more info, use `{prfx}set help`.";


                this._embed.WithUrl($"{Constants.DocsUrl}/commands/tracks/");
                this._embed.WithTitle($"Using the {prfx}{fmString} command");
                this._embed.WithDescription(replyString);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            if (user.Length > 0 && user.First() == "set")
            {
                await ReplyAsync(
                    "Please remove the space between `.fm` and `set` to set your last.fm username.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
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
                    this._embed.NoScrobblesFoundErrorResponse(recentScrobbles.Status, prfx , lastFMUserName);
                    this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
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
                    case null:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
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

                        try
                        {
                            if (!this._guildService.CheckIfDM(this.Context))
                            {
                                await this._guildService.AddReactionsAsync(message, this.Context.Guild);
                            }
                        }
                        catch (Exception e)
                        {
                            this.Context.LogCommandException(e);
                            await ReplyAsync(
                                "Couldn't add emote reactions to `.fm`. If you have recently changed changed any of the configured emotes please use `.fmserverreactions` to reset the automatic emote reactions.");
                        }


                        break;
                }

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Unable to show Last.FM info due to an internal error. Try scrobbling something then use the command again.");
            }
        }

        [Command("recent", RunMode = RunMode.Async)]
        [Summary("Displays a user's recent tracks.")]
        [Alias("recenttracks", "r")]
        [UsernameSetRequired]
        public async Task RecentAsync(string amount = "5", string user = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (amount == "help")
            {
                await ReplyAsync($"{prfx}recent 'number of items (max 10)' 'lastfm username/discord user'");
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            if (!int.TryParse(amount, out var amountOfTracks))
            {
                await ReplyAsync("Please enter a valid amount. \n" +
                                 $"`{prfx}recent 'number of items (max 10)' 'lastfm username/discord user'` \n" +
                                 $"Example: `{prfx}recent 8`");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
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
                    this._embed.NoScrobblesFoundErrorResponse(tracks.Status, prfx, lastFMUserName);
                    this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
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
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Unable to show your recent tracks on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }


        [Command("overview", RunMode = RunMode.Async)]
        [Summary("Displays a week overview.")]
        [Alias("o", "ov")]
        [UsernameSetRequired]
        public async Task OverviewAsync(string amount = "4")
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (amount == "help")
            {
                await ReplyAsync($"{prfx}overview 'number of days (max 8)'");
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            if (!int.TryParse(amount, out var amountOfDays))
            {
                await ReplyAsync("Please enter a valid amount. \n" +
                                 $"`{prfx}overview 'number of days (max 8)'` \n" +
                                 $"Example: `{prfx}overview 8`");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            if (amountOfDays > 8)
            {
                amountOfDays = 8;
            }

            if (amountOfDays < 1)
            {
                amountOfDays = 1;
            }

            if (userSettings.LastUpdated < DateTime.UtcNow.AddMinutes(-15))
            {
                _ = this.Context.Channel.TriggerTypingAsync();
                if (userSettings.LastIndexed == null)
                {
                    await this._indexService.IndexUser(userSettings);
                }
                else
                {
                    await this._updateService.UpdateUser(userSettings);
                }
            }

            try
            {
                var week = await this._playService.GetDailyOverview(userSettings, amountOfDays);

                foreach (var day in week.Days)
                {
                    this._embed.AddField(
                        $"{day.Playcount} plays - {day.Date.ToString("dddd MMMM d", CultureInfo.InvariantCulture)}",
                        $"{day.TopArtist}\n" +
                        $"{day.TopAlbum}\n" +
                        $"{day.TopTrack}"
                    );
                }

                var description = $"Top artist, album and track for last {amountOfDays} days";

                if (week.Days.Count < amountOfDays)
                {
                    description += $"\n{amountOfDays - week.Days.Count} days not shown because of no plays.";
                }

                this._embed.WithDescription(description);

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);
                this._embedAuthor.WithName($"Daily overview for {userTitle}");
                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                this._embedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFM}/library?date_preset=LAST_7_DAYS");
                this._embed.WithAuthor(this._embedAuthor);

                this._embedFooter.WithText($"{week.Uniques} unique tracks - {week.Playcount} total plays - avg {Math.Round(week.AvgPerDay, 1)} per day");
                this._embed.WithFooter(this._embedFooter);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Unable to show your overview on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("pace", RunMode = RunMode.Async)]
        [Summary("Displays the date a goal amount of scrobbles is reached")]
        [UsernameSetRequired]
        [Alias("p", "pc")]
        public async Task PaceAsync(params string[] extraOptions)
        {
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (extraOptions.Any() && extraOptions.First() == "help")
            {
                this._embed.WithTitle($"{prfx}pace");

                var helpDescription = new StringBuilder();
                helpDescription.AppendLine("Displays the date you reach a scrobble goal based on average scrobbles per day.");
                helpDescription.AppendLine();
                helpDescription.AppendLine($"Time periods: {Constants.CompactTimePeriodList}");
                helpDescription.AppendLine("Optional goal amount: For example `10000`");
                helpDescription.AppendLine("User to check pace for: Mention or user id");

                this._embed.WithDescription(helpDescription.ToString());

                this._embed.AddField("Examples",
                    $"`{prfx}pc` \n" +
                    $"`{prfx}pc 100000 q` \n" +
                    $"`{prfx}pc 40000 h @user` \n" +
                    $"`{prfx}pace` \n" +
                    $"`{prfx}pace yearly @user 250000`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var userSettings = await SettingService.GetUser(extraOptions, user.UserNameLastFM, this.Context);
            var userInfo = await this._lastFmService.GetFullUserInfoAsync(userSettings.UserNameLastFm);

            var goalAmount = SettingService.GetGoalAmount(extraOptions, userInfo.Playcount);

            var timeType = SettingService.GetTimePeriod(extraOptions, ChartTimePeriod.AllTime);

            long timeFrom;
            if (timeType.ChartTimePeriod != ChartTimePeriod.AllTime && timeType.PlayDays != null)
            {
                var dateAgo = DateTime.UtcNow.AddDays(-timeType.PlayDays.Value);
                timeFrom = ((DateTimeOffset) dateAgo).ToUnixTimeSeconds();
            }
            else
            {
                timeFrom = userInfo.Registered.Unixtime;
            }

            var count = await this._lastFmService.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm, timeFrom);

            if (count == null || count == 0)
            {
                await ReplyAsync(
                    $"<@{this.Context.User.Id}> No plays found in the {timeType.Description} time period.");
            }

            var age = DateTimeOffset.FromUnixTimeSeconds(timeFrom);
            var totalDays = (DateTime.UtcNow - age).TotalDays;

            var playsLeft = goalAmount - userInfo.Playcount;

            var avgPerDay = count / totalDays;

            var goalDate = (DateTime.Now.AddDays(playsLeft / avgPerDay.Value)).ToString("dd MMM yyyy");

            var reply = new StringBuilder();

            var determiner = "your";
            if (userSettings.DifferentUser)
            {
                reply.Append($"<@{this.Context.User.Id}> My estimate is that the user '{userSettings.UserNameLastFm.FilterOutMentions()}'");
                determiner = "their";
            }
            else
            {
                reply.Append($"<@{this.Context.User.Id}> My estimate is that you");
            }

            reply.AppendLine($" will reach **{goalAmount}** scrobbles on **{goalDate}**.");

            if (timeType.ChartTimePeriod == ChartTimePeriod.AllTime)
            {
                reply.AppendLine(
                    $"This is based on {determiner} alltime avg of {Math.Round(avgPerDay.GetValueOrDefault(0), 1)} per day. ({count} in {Math.Round(totalDays, 0)} days)");
            }
            else
            {
                reply.AppendLine(
                    $"This is based on {determiner} avg of {Math.Round(avgPerDay.GetValueOrDefault(0), 1)} per day in the last {Math.Round(totalDays, 0)} days ({count} total)");
            }

            await ReplyAsync(reply.ToString());
            this.Context.LogCommandUsed();
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
