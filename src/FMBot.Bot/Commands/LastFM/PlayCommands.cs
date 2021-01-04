using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.API.Rest;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Commands.LastFM
{
    [Name("Plays")]
    public class PlayCommands : ModuleBase
    {
        private readonly GuildService _guildService;
        private readonly IIndexService _indexService;
        private readonly IPrefixService _prefixService;
        private readonly IUpdateService _updateService;
        private readonly LastFmService _lastFmService;
        private readonly PlayService _playService;
        private readonly SettingService _settingService;
        private readonly UserService _userService;
        private readonly WhoKnowsPlayService _whoKnowsPlayService;

        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedBuilder _embed;
        private readonly EmbedFooterBuilder _embedFooter;

        public PlayCommands(
                GuildService guildService,
                IIndexService indexService,
                IPrefixService prefixService,
                IUpdateService updateService,
                LastFmService lastFmService,
                PlayService playService,
                SettingService settingService,
                UserService userService,
                WhoKnowsPlayService whoKnowsPlayService)
        {
            this._guildService = guildService;
            this._indexService = indexService;
            this._lastFmService = lastFmService;
            this._playService = playService;
            this._prefixService = prefixService;
            this._settingService = settingService;
            this._updateService = updateService;
            this._userService = userService;
            this._whoKnowsPlayService = whoKnowsPlayService;

            this._embedAuthor = new EmbedAuthorBuilder();
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("fm", RunMode = RunMode.Async)]
        [Summary("Displays what a user is listening to.")]
        [Alias("np", "qm", "wm", "em", "rm", "tm", "ym", "um", "om", "pm", "gm", "sm", "am", "hm", "jm", "km",
            "lm", "zm", "xm", "cm", "vm", "bm", "nm", "mm", "lastfm", "nowplaying")]
        [UsernameSetRequired]
        public async Task NowPlayingAsync(params string[] parameters)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (parameters.Length > 0 && parameters.First() == "set")
            {
                await ReplyAsync(
                    "Please remove the space between `.fm` and `set` to set your last.fm username.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }
            if (userSettings?.UserNameLastFM == null)
            {
                this._embed.UsernameNotSetErrorResponse(prfx);
                await ReplyAsync("", false, this._embed.Build());

                if (this.Context.InteractionData == null)
                {
                    await ReplyAsync("", false, this._embed.Build());
                }
                else
                {
                    await ReplyInteractionAsync("", embed: this._embed.Build());
                }

                return;
            }
            if (parameters.Length > 0 && parameters.First() == "help")
            {
                var fmString = "fm";
                if (prfx == ".fm")
                {
                    fmString = "";
                }

                var replyString = $"`{prfx}{fmString}` shows you your last scrobble(s). \n " +
                                  $"This command can also be used on others, for example `{prfx}{fmString} lastfmusername` or `{prfx}{fmString} @discorduser`\n \n" +

                                  $"You can change your .fm mode and displayed count with the `{prfx}mode` command.\n";

                var differentMode = userSettings.FmEmbedType == FmEmbedType.embedmini ? "embedfull" : "embedmini";
                replyString += $"`{prfx}mode {differentMode}` \n \n" +
                               $"For more info, use `{prfx}mode help`.";


                this._embed.WithUrl($"{Constants.DocsUrl}/commands/tracks/");
                this._embed.WithTitle($"Using the {prfx}{fmString} command");
                this._embed.WithDescription(replyString);
                this._embed.WithFooter("For more information on the bot in general, use .fmhelp");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            try
            {
                var lastFmUserName = userSettings.UserNameLastFM;
                var self = true;

                if (parameters.Length > 0 && !string.IsNullOrEmpty(parameters.First()) && parameters.Count() == 1)
                {
                    var alternativeLastFmUserName = await FindUser(parameters.First());
                    if (!string.IsNullOrEmpty(alternativeLastFmUserName) && await this._lastFmService.LastFmUserExistsAsync(alternativeLastFmUserName))
                    {
                        lastFmUserName = alternativeLastFmUserName;
                        self = false;
                    }
                }

                _ = this.Context.Channel.TriggerTypingAsync();

                string sessionKey = null;
                if (self && !string.IsNullOrEmpty(userSettings.SessionKeyLastFm))
                {
                    sessionKey = userSettings.SessionKeyLastFm;
                }

                Response<RecentTrackList> recentTracks;

                var totalPlaycount = userSettings.TotalPlaycount;

                if (self)
                {
                    if (userSettings.LastIndexed == null)
                    {
                        _ = this._indexService.IndexUser(userSettings);
                        recentTracks = await this._lastFmService.GetRecentTracksAsync(lastFmUserName, useCache: true, sessionKey: sessionKey);
                    }
                    else 
                    {
                        recentTracks = await this._updateService.UpdateUserAndGetRecentTracks(userSettings);
                    }
                }
                else
                {
                    recentTracks = await this._lastFmService.GetRecentTracksAsync(lastFmUserName, useCache: true);
                }

                var spotifyUsed = false;

                RecentTrack currentTrack;
                RecentTrack previousTrack = null;

                if (ErrorService.RecentScrobbleCallFailed(recentTracks, lastFmUserName))
                {
                    var listeningActivity =
                        this.Context.User.Activities.FirstOrDefault(a => a.Type == ActivityType.Listening);
                    if (listeningActivity != null && PublicProperties.IssuesAtLastFM)
                    {
                        var spotifyActivity = (SpotifyGame)listeningActivity;
                        currentTrack = SpotifyService.SpotifyGameToRecentTrack(spotifyActivity);
                        this._embed.Color = DiscordConstants.SpotifyColorGreen;
                        spotifyUsed = true;
                    }
                    else
                    {
                        await ErrorService.RecentScrobbleCallFailedReply(recentTracks, lastFmUserName, this.Context);
                        return;
                    }
                }
                else
                {
                    currentTrack = recentTracks.Content.RecentTracks[0];
                    previousTrack = recentTracks.Content.RecentTracks[1];
                    if (!self)
                    {
                        totalPlaycount = recentTracks.Content.TotalAmount;
                    }
                }

                if (self)
                {
                    this._whoKnowsPlayService.AddRecentPlayToCache(userSettings.UserId, currentTrack);
                }

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);
                var embedTitle = self ? userTitle : $"{lastFmUserName}, requested by {userTitle}";

                var fmText = "";
                var footerText = "";

                footerText +=
                    $"{lastFmUserName} has ";

                switch (userSettings.FmCountType)
                {
                    case FmCountType.Track:
                        var trackInfo = await this._lastFmService.GetTrackInfoAsync(currentTrack.TrackName,
                            currentTrack.ArtistName, lastFmUserName);
                        if (trackInfo != null)
                        {
                            footerText += $"{trackInfo.Userplaycount} scrobbles on this track | ";
                        }
                        break;
                    case FmCountType.Album:
                        if (!string.IsNullOrEmpty(currentTrack.AlbumName))
                        {
                            var albumInfo = await this._lastFmService.GetAlbumInfoAsync(currentTrack.ArtistName, currentTrack.AlbumName, lastFmUserName);
                            if (albumInfo.Success)
                            {
                                footerText += $"{albumInfo.Content.Album.Userplaycount} scrobbles on this album | ";
                            }
                        }
                        break;
                    case FmCountType.Artist:
                        var artistInfo = await this._lastFmService.GetArtistInfoAsync(currentTrack.ArtistName, lastFmUserName);
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

                footerText += $"{totalPlaycount} total scrobbles";

                switch (userSettings.FmEmbedType)
                {
                    case FmEmbedType.textmini:
                    case FmEmbedType.textfull:
                        if (userSettings.FmEmbedType == FmEmbedType.textmini)
                        {
                            fmText += $"Last track for {embedTitle}: \n";

                            fmText += LastFmService.TrackToString(currentTrack);
                        }
                        else if (previousTrack != null)
                        {
                            fmText += $"Last tracks for {embedTitle}: \n";

                            fmText += LastFmService.TrackToString(currentTrack);
                            fmText += LastFmService.TrackToString(previousTrack);
                        }

                        fmText +=
                            $"<{recentTracks.Content.UserUrl}> has {totalPlaycount} scrobbles";

                        await this.Context.Channel.SendMessageAsync(fmText.FilterOutMentions());
                        break;
                    default:
                        if (userSettings.FmEmbedType == FmEmbedType.embedmini)
                        {
                            fmText += LastFmService.TrackToLinkedString(currentTrack);
                            this._embed.WithDescription(fmText);
                        }
                        else if (previousTrack != null)
                        {
                            this._embed.AddField("Current:", LastFmService.TrackToLinkedString(currentTrack));
                            this._embed.AddField("Previous:", LastFmService.TrackToLinkedString(previousTrack));
                        }

                        string headerText;
                        if (currentTrack.NowPlaying)
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

                        if (!currentTrack.NowPlaying && currentTrack.TimePlayed.HasValue)
                        {
                            footerText += " | Last scrobble:";
                            this._embed.WithTimestamp(currentTrack.TimePlayed.Value);
                        }

                        this._embedAuthor.WithName(headerText);
                        this._embedAuthor.WithUrl(recentTracks.Content.UserUrl);

                        if (this.Context.Guild != null && self)
                        {
                            var guildAlsoPlaying = await this._whoKnowsPlayService.GuildAlsoPlayingTrack(userSettings.UserId,
                                this.Context.Guild.Id, currentTrack.ArtistName, currentTrack.TrackName);

                            if (guildAlsoPlaying != null)
                            {
                                footerText += "\n";
                                footerText += guildAlsoPlaying;
                            }
                        }

                        if (spotifyUsed)
                        {
                            footerText +=
                                $"\nSpotify status used due to Last.fm error ({recentTracks.Error})";
                        }

                        this._embedFooter.WithText(footerText);

                        this._embed.WithFooter(this._embedFooter);

                        this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                        this._embed.WithAuthor(this._embedAuthor);
                        this._embed.WithUrl(recentTracks.Content.UserUrl);

                        if (currentTrack.AlbumCoverUrl != null)
                        {
                            this._embed.WithThumbnailUrl(currentTrack.AlbumCoverUrl);
                        }

                        if (this.Context.InteractionData != null)
                        {
                            await this.Context.Channel.SendInteractionMessageAsync(this.Context.InteractionData, embed: this._embed.Build(), type: InteractionMessageType.ChannelMessageWithSource);
                        }
                        else
                        {
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
                                this.Context.LogCommandException(e, "Could not add emote reactions");
                                await ReplyAsync(
                                    "Couldn't add emote reactions to `.fm`. If you have recently changed changed any of the configured emotes please use `.fmserverreactions` to reset the automatic emote reactions.");
                            }
                        }

                        break;
                }

                this.Context.LogCommandUsed();

                if (!this._guildService.CheckIfDM(this.Context))
                {
                    await this._indexService.UpdateUserNameWithoutGuildUser(await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId), userSettings);
                }
            }
            catch (Exception e)
            {
                if (!string.IsNullOrEmpty(e.Message) && e.Message.Contains("The server responded with error 50013: Missing Permissions"))
                {
                    this.Context.LogCommandException(e);
                    await ReplyAsync("Error while replying: The bot is missing permissions.\n" +
                                     "Make sure it has permission to 'Embed links' and 'Attach Images'");
                }
                else
                {
                    this.Context.LogCommandException(e);
                    await ReplyAsync("Something went wrong while showing info from Last.fm. Please try again later or contact staff on our support server.");
                }
            }
        }

        [Command("recent", RunMode = RunMode.Async)]
        [Summary("Displays a user's recent tracks.")]
        [Alias("recenttracks", "recents", "r")]
        [UsernameSetRequired]
        public async Task RecentAsync([Remainder] string extraOptions = null)
        {
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (!string.IsNullOrWhiteSpace(extraOptions) && extraOptions.ToLower() == "help")
            {
                await ReplyAsync($"{prfx}recent 'number of items (max 10)' 'lastfm username/@discord user'");
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var userSettings = await this._settingService.GetUser(extraOptions, user, this.Context);
            var amount = SettingService.GetAmount(extraOptions, 5, 10);

            try
            {
                string sessionKey = null;
                if (!userSettings.DifferentUser && !string.IsNullOrEmpty(user.SessionKeyLastFm))
                {
                    sessionKey = user.SessionKeyLastFm;
                }

                var recentTracks = await this._lastFmService.GetRecentTracksAsync(userSettings.UserNameLastFm, amount, useCache: true, sessionKey: sessionKey);

                if (await ErrorService.RecentScrobbleCallFailedReply(recentTracks, userSettings.UserNameLastFm, this.Context))
                {
                    return;
                }

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);
                var title = !userSettings.DifferentUser ? userTitle : $"{userSettings.UserNameLastFm}, requested by {userTitle}";
                this._embedAuthor.WithName($"Latest tracks for {title}");

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                this._embedAuthor.WithUrl(recentTracks.Content.UserRecentTracksUrl);
                this._embed.WithAuthor(this._embedAuthor);

                var fmRecentText = "";
                for (var i = 0; i < recentTracks.Content.RecentTracks.Count(); i++)
                {
                    var track = recentTracks.Content.RecentTracks[i];

                    if (i == 0)
                    {
                        if (track.AlbumCoverUrl != null)
                        {
                            this._embed.WithThumbnailUrl(track.AlbumCoverUrl);
                        }
                    }

                    if (track.NowPlaying)
                    {
                        fmRecentText += $"🎶 - {LastFmService.TrackToLinkedString(track)}\n";
                    }
                    else
                    {
                        fmRecentText += $"`{i + 1}` - {LastFmService.TrackToLinkedString(track)}\n";
                    }
                }

                this._embed.WithDescription(fmRecentText);

                string footerText;
                var firstTrack = recentTracks.Content.RecentTracks[0];
                if (firstTrack.NowPlaying)
                {
                    footerText =
                        $"{userSettings.UserNameLastFm} has {recentTracks.Content.TotalAmount} scrobbles | Now Playing";
                }
                else
                {
                    footerText =
                        $"{userSettings.UserNameLastFm} has {recentTracks.Content.TotalAmount} scrobbles";

                    if (!firstTrack.NowPlaying && firstTrack.TimePlayed.HasValue)
                    {
                        footerText += " | Last scrobble:";
                        this._embed.WithTimestamp(firstTrack.TimePlayed.Value);
                    }
                }

                this._embedFooter.WithText(footerText);

                this._embed.WithFooter(this._embedFooter);

                if (this.Context.InteractionData != null)
                {
                    await this.Context.Channel.SendInteractionMessageAsync(this.Context.InteractionData, embed: this._embed.Build(), type: InteractionMessageType.ChannelMessageWithSource);
                }
                else
                {
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                }

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Unable to show your recent tracks on Last.fm due to an internal error. Please try again later or contact .fmbot support.");
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

            if (userSettings.LastIndexed == null)
            {
                _ = this.Context.Channel.TriggerTypingAsync();
                await this._indexService.IndexUser(userSettings);
            }
            else if (userSettings.LastUpdated < DateTime.UtcNow.AddMinutes(-20))
            {
                _ = this.Context.Channel.TriggerTypingAsync();
                await this._updateService.UpdateUser(userSettings);
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
                    "Unable to show your overview on Last.fm due to an internal error. Please try again later or contact .fmbot support.");
            }
        }

        [Command("pace", RunMode = RunMode.Async)]
        [Summary("Displays the date a goal amount of scrobbles is reached")]
        [UsernameSetRequired]
        [Alias("p", "pc")]
        public async Task PaceAsync([Remainder] string extraOptions = null)
        {
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (!string.IsNullOrWhiteSpace(extraOptions) && extraOptions.ToLower() == "help")
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

            var userSettings = await this._settingService.GetUser(extraOptions, user, this.Context);
            var userInfo = await this._lastFmService.GetFullUserInfoAsync(userSettings.UserNameLastFm);

            var goalAmount = SettingService.GetGoalAmount(extraOptions, userInfo.Playcount);

            var timePeriodString = extraOptions;
            if (this.Context.InteractionData != null)
            {
                var time = this.Context.InteractionData.Choices.FirstOrDefault(w => w.Name == "time");
                timePeriodString = time?.Value?.ToLower();
            }
            var timeType = SettingService.GetTimePeriod(timePeriodString, ChartTimePeriod.AllTime);

            long timeFrom;
            if (timeType.ChartTimePeriod != ChartTimePeriod.AllTime && timeType.PlayDays != null)
            {
                var dateAgo = DateTime.UtcNow.AddDays(-timeType.PlayDays.Value);
                timeFrom = ((DateTimeOffset)dateAgo).ToUnixTimeSeconds();
            }
            else
            {
                timeFrom = userInfo.Registered.Unixtime;
            }

            var count = await this._lastFmService.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm, timeFrom);

            if (count == null || count == 0)
            {
                var errorReply = $"<@{this.Context.User.Id}> No plays found in the {timeType.Description} time period.";
                if (this.Context.InteractionData != null)
                {
                    await ReplyInteractionAsync(errorReply.FilterOutMentions(), ghostMessage: true, type: InteractionMessageType.ChannelMessage);
                }
                else
                {
                    await this.Context.Channel.SendMessageAsync(errorReply);
                }
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

            if (this.Context.InteractionData != null)
            {
                await this.Context.Channel.SendInteractionMessageAsync(this.Context.InteractionData, reply.ToString(), type: InteractionMessageType.ChannelMessageWithSource);
            }
            else
            {
                await this.Context.Channel.SendMessageAsync(reply.ToString());
            }

            this.Context.LogCommandUsed();
        }

        [Command("streak", RunMode = RunMode.Async)]
        [Summary("Shows you your streak")]
        [UsernameSetRequired]
        [Alias("str", "combo", "cb")]
        public async Task StreakAsync([Remainder] string extraOptions = null)
        {
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);

            if (user.LastIndexed == null)
            {
                _ = this.Context.Channel.TriggerTypingAsync();
                await this._indexService.IndexUser(user);
            }
            else if (user.LastUpdated < DateTime.UtcNow.AddMinutes(-1))
            {
                _ = this.Context.Channel.TriggerTypingAsync();
                await this._updateService.UpdateUser(user);
            }

            var userSettings = await this._settingService.GetUser(extraOptions, user, this.Context);

            var recentScrobbles = await this._lastFmService.GetRecentScrobblesAsync(userSettings.UserNameLastFm, 1);
            var nowPlaying = recentScrobbles.FirstOrDefault(f => f.IsNowPlaying == true);

            var streak = await this._playService.GetStreak(userSettings.UserId, nowPlaying);
            this._embed.WithDescription(streak);

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            this._embedAuthor.WithName($"{userTitle} streak overview");
            this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            this._embedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library");
            this._embed.WithAuthor(this._embedAuthor);

            if (this.Context.InteractionData != null)
            {
                await this.Context.Channel.SendInteractionMessageAsync(this.Context.InteractionData, embed: this._embed.Build(), type: InteractionMessageType.ChannelMessageWithSource);
            }
            else
            {
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            }

            this.Context.LogCommandUsed();
        }

        private async Task<string> FindUser(string user)
        {
            if (await this._lastFmService.LastFmUserExistsAsync(user))
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
