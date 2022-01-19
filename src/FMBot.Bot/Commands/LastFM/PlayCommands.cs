using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using Microsoft.Extensions.Options;
using Swan;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;
using TimePeriod = FMBot.Domain.Models.TimePeriod;

namespace FMBot.Bot.Commands.LastFM;

[Name("Plays")]
public class PlayCommands : BaseCommandModule
{
    private readonly CensorService _censorService;
    private readonly GuildService _guildService;
    private readonly IIndexService _indexService;
    private readonly IPrefixService _prefixService;
    private readonly IUpdateService _updateService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly PlayService _playService;
    private readonly GenreService _genreService;
    private readonly SettingService _settingService;
    private readonly TimeService _timeService;
    private readonly TrackService _trackService;
    private readonly UserService _userService;
    private readonly WhoKnowsPlayService _whoKnowsPlayService;
    private readonly WhoKnowsArtistService _whoKnowsArtistService;
    private readonly WhoKnowsAlbumService _whoKnowsAlbumService;
    private readonly WhoKnowsTrackService _whoKnowsTrackService;
    private readonly PlayBuilder _playBuilder;
    private InteractiveService Interactivity { get; }

    private static readonly List<DateTimeOffset> StackCooldownTimer = new();
    private static readonly List<SocketUser> StackCooldownTarget = new();


    public PlayCommands(
        GuildService guildService,
        IIndexService indexService,
        IPrefixService prefixService,
        IUpdateService updateService,
        LastFmRepository lastFmRepository,
        PlayService playService,
        SettingService settingService,
        UserService userService,
        WhoKnowsPlayService whoKnowsPlayService,
        CensorService censorService,
        WhoKnowsArtistService whoKnowsArtistService,
        WhoKnowsAlbumService whoKnowsAlbumService,
        WhoKnowsTrackService whoKnowsTrackService,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings,
        TimeService timeService,
        GenreService genreService,
        TrackService trackService,
        PlayBuilder playBuilder) : base(botSettings)
    {
        this._guildService = guildService;
        this._indexService = indexService;
        this._lastFmRepository = lastFmRepository;
        this._playService = playService;
        this._prefixService = prefixService;
        this._settingService = settingService;
        this._updateService = updateService;
        this._userService = userService;
        this._whoKnowsPlayService = whoKnowsPlayService;
        this._censorService = censorService;
        this._whoKnowsArtistService = whoKnowsArtistService;
        this._whoKnowsAlbumService = whoKnowsAlbumService;
        this._whoKnowsTrackService = whoKnowsTrackService;
        this.Interactivity = interactivity;
        this._timeService = timeService;
        this._genreService = genreService;
        this._trackService = trackService;
        this._playBuilder = playBuilder;
    }

    [Command("fm", RunMode = RunMode.Async)]
    [Summary("Shows you or someone else their current track")]
    [Alias("np", "qm", "wm", "em", "rm", "tm", "ym", "om", "pm", "gm", "sm", "am", "hm", "jm", "km",
        "lm", "zm", "xm", "cm", "vm", "bm", "nm", "mm", "lastfm", "nowplaying")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task NowPlayingAsync([Remainder] string options = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (contextUser?.UserNameLastFM == null)
        {
            var userNickname = (this.Context.User as SocketGuildUser)?.Nickname;
            this._embed.UsernameNotSetErrorResponse(prfx, userNickname ?? this.Context.User.Username);

            await ReplyAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.UsernameNotSet);
            return;
        }
        if (options == "help")
        {
            var fmString = "fm";
            if (prfx == ".fm")
            {
                fmString = "";
            }

            var commandName = prfx + fmString;

            this._embed.WithTitle($"Information about the '{commandName}' command");
            this._embed.WithColor(DiscordConstants.InformationColorBlue);
            this._embed.WithDescription("Displays what you or someone else is listening to.");

            this._embed.AddField("Examples",
                $"`{prfx}fm`\n" +
                $"`{prfx}nowplaying`\n" +
                $"`{prfx}fm lfm:fm-bot`\n" +
                $"`{prfx}fm @user`");

            this._embed.AddField("Options", $"To customize how this command looks, check out `{prfx}mode`.");

            this._embed.WithFooter($"For more information on the bot in general, use '{prfx}help'");

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.Help);
            return;
        }

        try
        {
            var existingFmCooldown = await this._guildService.GetChannelCooldown(this.Context.Channel.Id);
            if (existingFmCooldown.HasValue)
            {
                var msg = this.Context.Message as SocketUserMessage;
                if (StackCooldownTarget.Contains(this.Context.Message.Author))
                {
                    if (StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)].AddSeconds(existingFmCooldown.Value) >= DateTimeOffset.Now)
                    {
                        var secondsLeft = (int)(StackCooldownTimer[
                                StackCooldownTarget.IndexOf(this.Context.Message.Author as SocketGuildUser)]
                            .AddSeconds(existingFmCooldown.Value) - DateTimeOffset.Now).TotalSeconds;
                        if (secondsLeft <= existingFmCooldown.Value - 2)
                        {
                            _ = this.Interactivity.DelayedDeleteMessageAsync(
                                await this.Context.Channel.SendMessageAsync($"This channel has a `{existingFmCooldown.Value}` second cooldown on `{prfx}fm`. Please wait for this to expire before using this command again."),
                                TimeSpan.FromSeconds(6));
                            this.Context.LogCommandUsed(CommandResponse.Cooldown);
                        }

                        return;
                    }

                    StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)] = DateTimeOffset.Now;
                }
                else
                {
                    StackCooldownTarget.Add(msg.Author);
                    StackCooldownTimer.Add(DateTimeOffset.Now);
                }
            }

            _ = this.Context.Channel.TriggerTypingAsync();
            var userSettings = await this._settingService.GetUser(options, contextUser, this.Context);

            var response = await this._playBuilder.NowPlayingAsync(prfx, this.Context.Guild, this.Context.Channel,
                this.Context.User, contextUser, userSettings);

            IUserMessage message;
            if (response.ResponseType == ResponseType.Embed)
            {
                message = await ReplyAsync("", false, response.Embed.Build());
            }
            else
            {
                message = await ReplyAsync(response.Text);
            }

            try
            {
                if (message != null && response.CommandResponse == CommandResponse.Ok && this.Context.Guild != null)
                {
                    await this._guildService.AddReactionsAsync(message, this.Context.Guild);
                }
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e, "Could not add emote reactions");
                await ReplyAsync(
                    $"Couldn't add emote reactions to `{prfx}fm`. If you have recently changed changed any of the configured emotes please use `{prfx}serverreactions` to reset the automatic emote reactions.");
            }

            this.Context.LogCommandUsed();
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
    [Summary("Shows you or someone else their recent tracks")]
    [Options("Amount of recent tracks to show (max 10)", Constants.UserMentionExample)]
    [Examples("recent", "r", "recent 8", "recent 5 @user", "recent lfm:fm-bot")]
    [Alias("recenttracks", "recents", "r")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task RecentAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
        var amount = SettingService.GetAmount(extraOptions, 5, 10);

        try
        {
            var response = await this._playBuilder.RecentAsync(this.Context.Guild, this.Context.Channel, this.Context.User, contextUser,
                userSettings, amount);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await ReplyAsync(
                "Unable to show your recent tracks on Last.fm due to an internal error. Please try again later or contact .fmbot support.");
        }
    }

    [Command("overview", RunMode = RunMode.Async)]
    [Summary("Shows a daily overview")]
    [Options("Amount of days to show (max 8)")]
    [Examples("o", "overview", "overview 7")]
    [Alias("o", "ov")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks, CommandCategory.Albums, CommandCategory.Artists)]
    public async Task OverviewAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
        var amountOfDays = SettingService.GetAmount(extraOptions, 4, 8);

        try
        {
            var response = await this._playBuilder.OverviewAsync(this.Context.Guild, this.Context.User, contextUser,
                userSettings, amountOfDays);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await ReplyAsync(
                "Unable to show your overview on Last.fm due to an internal error. Please try again later or contact .fmbot support.");
        }
    }

    [Command("year", RunMode = RunMode.Async)]
    [Summary("Shows an overview of your year")]
    [Alias("yr", "lastyear", "yearoverview", "yearov", "yov", "last.year", "wrapped")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks, CommandCategory.Albums, CommandCategory.Artists)]
    public async Task YearAsync([Remainder] string extraOptions = null)
    {
        await ReplyAsync(
            "This command has temporarily been disabled due to issues communicating with Last.fm. It will most likely be disabled for a while.\n" +
            "Sorry for the inconvenience.");
        return;

        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
        var year = SettingService.GetYear(extraOptions).GetValueOrDefault(DateTime.UtcNow.AddDays(-90).Year);

        try
        {
            var yearOverview = await this._playService.GetYear(userSettings.UserId, year);

            if (yearOverview.LastfmErrors)
            {
                await ReplyAsync("Sorry, Last.fm returned an error. Please try again");
                this.Context.LogCommandUsed(CommandResponse.LastFmError);
                return;
            }
            if (yearOverview.TopArtists?.TopArtists == null || !yearOverview.TopArtists.TopArtists.Any())
            {
                await ReplyAsync("Sorry, you haven't listened to music in this year. If you think this message is wrong, please try again.");
                this.Context.LogCommandUsed(CommandResponse.LastFmError);
                return;
            }

            var userTitle = $"{userSettings.DiscordUserName}{userSettings.UserType.UserTypeToIcon()}'s";
            var pages = new List<PageBuilder>();

            var description = new StringBuilder();
            var fields = new List<EmbedFieldBuilder>();

            if (yearOverview.PreviousTopArtists?.TopArtists is { Count: > 0 })
            {
                description.AppendLine($"Your top genres, artists, albums and tracks for {year} compared to {year - 1}.");
            }
            else
            {
                description.AppendLine($"Welcome to Last.fm and .fmbot. Here's your overview for {year}.");
            }

            this._embed.WithDescription(description.ToString());

            var genres = await this._genreService.GetTopGenresForTopArtists(yearOverview.TopArtists.TopArtists);

            var previousTopGenres = new List<TopGenre>();
            if (yearOverview.PreviousTopArtists?.TopArtists != null)
            {
                previousTopGenres = await this._genreService.GetTopGenresForTopArtists(yearOverview.PreviousTopArtists?.TopArtists);
            }

            var genreDescription = new StringBuilder();
            var lines = new List<StringService.BillboardLine>();
            for (var i = 0; i < genres.Count; i++)
            {
                var topGenre = genres[i];

                var previousTopGenre = previousTopGenres.FirstOrDefault(f => f.GenreName == topGenre.GenreName);

                int? previousPosition = previousTopGenre == null ? null : previousTopGenres.IndexOf(previousTopGenre);

                var line = StringService.GetBillboardLine($"**{topGenre.GenreName}**", i, previousPosition);
                lines.Add(line);

                if (i < 10)
                {
                    genreDescription.AppendLine(line.Text);
                }
            }

            fields.Add(new EmbedFieldBuilder().WithName("Genres").WithValue(genreDescription.ToString()).WithIsInline(true));

            var artistDescription = new StringBuilder();
            for (var i = 0; i < yearOverview.TopArtists.TopArtists.Count; i++)
            {
                var topArtist = yearOverview.TopArtists.TopArtists[i];

                var previousTopArtist =
                    yearOverview.PreviousTopArtists?.TopArtists?.FirstOrDefault(f =>
                        f.ArtistName == topArtist.ArtistName);

                var previousPosition = previousTopArtist == null ? null : yearOverview.PreviousTopArtists?.TopArtists?.IndexOf(previousTopArtist);

                var line = StringService.GetBillboardLine($"**{topArtist.ArtistName}**", i, previousPosition);
                lines.Add(line);

                if (i < 10)
                {
                    artistDescription.AppendLine(line.Text);
                }
            }

            fields.Add(new EmbedFieldBuilder().WithName("Artists").WithValue(artistDescription.ToString()).WithIsInline(true));

            var rises = lines
                .Where(w => w.OldPosition is >= 20 && w.NewPosition <= 15 && w.PositionsMoved >= 15)
                .OrderBy(o => o.PositionsMoved)
                .ThenBy(o => o.NewPosition)
                .ToList();

            var risesDescription = new StringBuilder();
            if (rises.Any())
            {
                foreach (var rise in rises.Take(6))
                {
                    risesDescription.Append($"<:5_or_more_up:912380324841918504>");
                    risesDescription.AppendLine($"{rise.Name} (From #{rise.OldPosition} to #{rise.NewPosition})");
                }
            }

            if (risesDescription.Length > 0)
            {
                fields.Add(new EmbedFieldBuilder().WithName("Rises").WithValue(risesDescription.ToString()));
            }

            var drops = lines
                .Where(w => w.OldPosition is <= 15 && w.NewPosition >= 20 && w.PositionsMoved <= -15)
                .OrderBy(o => o.PositionsMoved)
                .ThenBy(o => o.OldPosition)
                .ToList();

            var dropsDescription = new StringBuilder();
            if (drops.Any())
            {
                foreach (var drop in drops.Take(6))
                {
                    dropsDescription.Append($"<:5_or_more_down:912380324753838140> ");
                    dropsDescription.AppendLine($"{drop.Name} (From #{drop.OldPosition} to #{drop.NewPosition})");
                }
            }

            if (dropsDescription.Length > 0)
            {
                fields.Add(new EmbedFieldBuilder().WithName("Drops").WithValue(dropsDescription.ToString()));
            }

            pages.Add(new PageBuilder()
                .WithFields(fields)
                .WithDescription(description.ToString())
                .WithTitle($"{userTitle} {year} in Review - 1/2"));

            fields = new List<EmbedFieldBuilder>();

            var albumDescription = new StringBuilder();
            if (yearOverview.TopAlbums.TopAlbums.Any())
            {
                for (var i = 0; i < yearOverview.TopAlbums.TopAlbums.Take(8).Count(); i++)
                {
                    var topAlbum = yearOverview.TopAlbums.TopAlbums[i];

                    var previousTopAlbum =
                        yearOverview.PreviousTopAlbums?.TopAlbums?.FirstOrDefault(f =>
                            f.ArtistName == topAlbum.ArtistName && f.AlbumName == topAlbum.AlbumName);

                    var previousPosition = previousTopAlbum == null ? null : yearOverview.PreviousTopAlbums?.TopAlbums?.IndexOf(previousTopAlbum);

                    albumDescription.AppendLine(StringService.GetBillboardLine($"**{topAlbum.ArtistName}** - **{topAlbum.AlbumName}**", i, previousPosition).Text);
                }
                fields.Add(new EmbedFieldBuilder().WithName("Albums").WithValue(albumDescription.ToString()));
            }

            var trackDescription = new StringBuilder();
            for (var i = 0; i < yearOverview.TopTracks.TopTracks.Take(8).Count(); i++)
            {
                var topTrack = yearOverview.TopTracks.TopTracks[i];

                var previousTopTrack =
                    yearOverview.PreviousTopTracks?.TopTracks?.FirstOrDefault(f =>
                        f.ArtistName == topTrack.ArtistName && f.TrackName == topTrack.TrackName);

                var previousPosition = previousTopTrack == null ? null : yearOverview.PreviousTopTracks?.TopTracks?.IndexOf(previousTopTrack);

                trackDescription.AppendLine(StringService.GetBillboardLine($"**{topTrack.ArtistName}** - **{topTrack.TrackName}**", i, previousPosition).Text);
            }

            fields.Add(new EmbedFieldBuilder().WithName("Tracks").WithValue(trackDescription.ToString()));

            var tracksAudioOverview = await this._trackService.GetAverageTrackAudioFeaturesForTopTracks(yearOverview.TopTracks.TopTracks);
            var previousTracksAudioOverview = await this._trackService.GetAverageTrackAudioFeaturesForTopTracks(yearOverview.PreviousTopTracks?.TopTracks);

            if (tracksAudioOverview.Total > 0)
            {
                fields.Add(new EmbedFieldBuilder().WithName("Top track analysis")
                    .WithValue(TrackService.AudioFeatureAnalysisComparisonString(tracksAudioOverview, previousTracksAudioOverview)));
            }

            if (DateTime.UtcNow.Month == 12 && DateTime.UtcNow.Year == year)
            {
                fields.Add(new EmbedFieldBuilder().WithName("Note")
                    .WithValue("Happy holidays from the .fmbot team!"));
                //"Make sure you also check out your [Last.Year](https://www.last.fm/user/_/listening-report/year) report over at Last.fm."
            }

            pages.Add(new PageBuilder()
                .WithFields(fields)
                .WithTitle($"{userTitle} {year} in Review - 2/2"));

            var paginator = StringService.BuildSimpleStaticPaginator(pages);

            _ = this.Interactivity.SendPaginatorAsync(
                paginator,
                this.Context.Channel,
                TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds * 2));

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await ReplyAsync(
                "Unable to show your year overview due to an internal error. Please try again later or contact .fmbot support.");
        }
    }

    [Command("pace", RunMode = RunMode.Async)]
    [Summary("Shows estimated date you reach a scrobble goal based on average scrobbles per day")]
    [Options(Constants.CompactTimePeriodList, "Optional goal amount: For example `10000` or `10k`", Constants.UserMentionExample)]
    [Examples("pc", "pc 100k q", "pc 40000 h @user", "pace", "pace yearly @user 250000")]
    [UsernameSetRequired]
    [Alias("pc")]
    [CommandCategories(CommandCategory.Other)]
    public async Task PaceAsync([Remainder] string extraOptions = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel.TriggerTypingAsync();

        var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
        var userInfo = await this._lastFmRepository.GetLfmUserInfoAsync(userSettings.UserNameLastFm, userSettings.SessionKeyLastFm);

        var goalAmount = SettingService.GetGoalAmount(extraOptions, userInfo.Playcount);
        var timeSettings = SettingService.GetTimePeriod(extraOptions, TimePeriod.AllTime);

        long timeFrom;
        if (timeSettings.TimePeriod != TimePeriod.AllTime && timeSettings.PlayDays != null)
        {
            var dateAgo = DateTime.UtcNow.AddDays(-timeSettings.PlayDays.Value);
            timeFrom = ((DateTimeOffset)dateAgo).ToUnixTimeSeconds();
        }
        else
        {
            timeFrom = userInfo.Registered.Unixtime;
        }

        var response = await this._playBuilder.PaceAsync(this.Context.User,
            userSettings, timeSettings, goalAmount, userInfo.Playcount, timeFrom);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("milestone", RunMode = RunMode.Async)]
    [Summary("Shows a milestone scrobble")]
    [Options("Optional milestone amount: For example `10000` or `10k`", Constants.UserMentionExample)]
    [Examples("ms", "ms 10k", "milestone 500 @user", "milestone", "milestone @user 250k")]
    [UsernameSetRequired]
    [Alias("m", "ms")]
    [CommandCategories(CommandCategory.Other)]
    public async Task MilestoneAsync([Remainder] string extraOptions = null)
    {
        var user = await this._userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel.TriggerTypingAsync();

        var userSettings = await this._settingService.GetUser(extraOptions, user, this.Context);

        var userInfo = await this._lastFmRepository.GetLfmUserInfoAsync(userSettings.UserNameLastFm, userSettings.SessionKeyLastFm);
        var mileStoneAmount = SettingService.GetMilestoneAmount(extraOptions, userInfo.Playcount);

        var response = await this._playBuilder.MileStoneAsync(this.Context.Guild, this.Context.Channel, this.Context.User,
            userSettings, mileStoneAmount, userInfo.Playcount);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("plays", RunMode = RunMode.Async)]
    [Summary("Shows your total scrobblecount for a specific time period.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample)]
    [Examples("p", "plays", "plays @frikandel", "plays monthly")]
    [Alias("p", "scrobbles")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Other)]
    public async Task PlaysAsync([Remainder] string extraOptions = null)
    {
        var user = await this._userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel.TriggerTypingAsync();

        var timeType = SettingService.GetTimePeriod(extraOptions, TimePeriod.AllTime);
        var userSettings = await this._settingService.GetUser(timeType.NewSearchValue, user, this.Context, true);

        long? timeFrom = null;
        if (timeType.TimePeriod != TimePeriod.AllTime && timeType.PlayDays != null)
        {
            var dateAgo = DateTime.UtcNow.AddDays(-timeType.PlayDays.Value);
            timeFrom = ((DateTimeOffset)dateAgo).ToUnixTimeSeconds();
        }

        var count = await this._lastFmRepository.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm, timeFrom);

        var userTitle = $"{userSettings.DiscordUserName.FilterOutMentions()}{userSettings.UserType.UserTypeToIcon()}";

        if (timeType.TimePeriod == TimePeriod.AllTime)
        {
            await this.Context.Channel.SendMessageAsync($"**{userTitle}** has `{count}` total scrobbles", allowedMentions: AllowedMentions.None);
        }
        else
        {
            await this.Context.Channel.SendMessageAsync($"**{userTitle}** has `{count}` scrobbles in the {timeType.AltDescription}", allowedMentions: AllowedMentions.None);
        }
        this.Context.LogCommandUsed();
    }

    [Command("streak", RunMode = RunMode.Async)]
    [Summary("Shows you your streak")]
    [UsernameSetRequired]
    [Alias("str", "combo", "cb")]
    [CommandCategories(CommandCategory.Albums, CommandCategory.Artists, CommandCategory.Tracks)]
    public async Task StreakAsync([Remainder] string extraOptions = null)
    {
        var user = await this._userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel.TriggerTypingAsync();

        if (user.LastIndexed == null)
        {
            await this._indexService.IndexUser(user);
        }

        try
        {
            var userSettings = await this._settingService.GetUser(extraOptions, user, this.Context);
            var userWithStreak = await this._userService.GetUserAsync(userSettings.DiscordUserId);

            var recentTracks = await this._updateService.UpdateUserAndGetRecentTracks(userWithStreak);

            if (await GenericEmbedService.RecentScrobbleCallFailedReply(recentTracks, userSettings.UserNameLastFm,
                    this.Context))
            {
                return;
            }

            var streak = await this._playService.GetStreak(userSettings.UserId, recentTracks);
            this._embed.WithDescription(streak);

            this._embedAuthor.WithName($"{userSettings.DiscordUserName}{userSettings.UserType.UserTypeToIcon()}'s streak overview");
            if (!userSettings.DifferentUser)
            {
                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            }

            this._embedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library");
            this._embed.WithAuthor(this._embedAuthor);

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await ReplyAsync("Something went wrong while showing streak and the error has been logged. Please try again later or contact staff on our support server.");
        }
    }

    [Command("playleaderboard", RunMode = RunMode.Async)]
    [Summary("Shows users with the most crowns in your server")]
    [Alias("sblb", "scrobblelb", "scrobbleleaderboard", "scrobble leaderboard")]
    [UsernameSetRequired]
    [GuildOnly]
    [SupportsPagination]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns)]
    public async Task PlayLeaderboardAsync([Remainder] string options = null)
    {
        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild?.Id);
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);

            var topPlaycountUsers = await this._playService.GetGuildUsersTotalPlaycount(this.Context, guild.GuildId);

            var filteredTopPlaycountUsers = WhoKnowsService.FilterGuildUsersAsync(topPlaycountUsers, guild);

            if (!topPlaycountUsers.Any() && filteredTopPlaycountUsers.Any())
            {
                this._embed.WithDescription($"No top users in this server. Use `.index` to refresh the cached memberlist");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                return;
            }

            var pages = new List<PageBuilder>();

            var title = $"Users with most scrobbles in {this.Context.Guild.Name}";

            var topPlaycountPages = filteredTopPlaycountUsers.ChunkBy(10);
            var requestedUser = filteredTopPlaycountUsers.FirstOrDefault(f => f.UserId == user.UserId);
            var location = filteredTopPlaycountUsers.IndexOf(requestedUser);

            var counter = 1;
            var pageCounter = 1;
            foreach (var playcountPage in topPlaycountPages)
            {
                var playcountString = new StringBuilder();
                foreach (var userPlaycount in playcountPage)
                {
                    playcountString.AppendLine($"{counter}. **{WhoKnowsService.NameWithLink(userPlaycount)}** - **{userPlaycount.Playcount}** {StringExtensions.GetScrobblesString(userPlaycount.Playcount)}");
                    counter++;
                }

                var footer = $"Your ranking: {location + 1}\n" +
                             $"Page {pageCounter}/{topPlaycountPages.Count} - " +
                             $"{filteredTopPlaycountUsers.Count} users - " +
                             $"{filteredTopPlaycountUsers.Sum(s => s.Playcount)} total scrobbles";

                pages.Add(new PageBuilder()
                    .WithDescription(playcountString.ToString())
                    .WithTitle(title)
                    .WithFooter(footer));
                pageCounter++;
            }

            var paginator = StringService.BuildStaticPaginator(pages);

            _ = this.Interactivity.SendPaginatorAsync(
                paginator,
                this.Context.Channel,
                TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await ReplyAsync("Something went wrong while showing playleaderboard and the error has been logged. Please try again later or contact staff on our support server.");
        }
    }

    [Command("timeleaderboard", RunMode = RunMode.Async)]
    [Summary("Shows users with the most playtime in your server")]
    [Alias("playtimeleaderboard", "listeningtimeleaderboard", "ptlb", "ltlb", "tlb", "sleepscrobblers")]
    [UsernameSetRequired]
    [GuildOnly]
    [SupportsPagination]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns)]
    public async Task TimeLeaderboardAsync([Remainder] string options = null)
    {
        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild?.Id);
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);

            var userPlays = await this._playService.GetGuildUsersPlaysForTimeLeaderBoard(guild.GuildId);

            var userListeningTime =
                await this._timeService.UserPlaysToGuildLeaderboard(this.Context, userPlays, guild.GuildUsers);

            var filteredTopListeningTimeUsers = WhoKnowsService.FilterGuildUsersAsync(userListeningTime, guild);

            if (!userListeningTime.Any() && filteredTopListeningTimeUsers.Any())
            {
                this._embed.WithDescription($"No top users in this server. Use `.index` to refresh the cached memberlist");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                return;
            }

            var pages = new List<PageBuilder>();

            var title = $"Users with most listening time in {this.Context.Guild.Name}";

            var topListeningTimePages = filteredTopListeningTimeUsers.ChunkBy(10);
            var requestedUser = filteredTopListeningTimeUsers.FirstOrDefault(f => f.UserId == user.UserId);
            var location = filteredTopListeningTimeUsers.IndexOf(requestedUser);

            var counter = 1;
            var pageCounter = 1;
            foreach (var listeningTimePAge in topListeningTimePages)
            {
                var listeningTimeString = new StringBuilder();
                foreach (var userPlaycount in listeningTimePAge)
                {
                    listeningTimeString.AppendLine($"{counter}. **{WhoKnowsService.NameWithLink(userPlaycount)}** - **{userPlaycount.Name}**");
                    counter++;
                }

                var footer = $"Your ranking: {location + 1} ({requestedUser?.Name})\n" +
                             $"7 days - From {DateTime.UtcNow.AddDays(-9):MMM dd} to {DateTime.UtcNow.AddDays(-2):MMM dd}\n" +
                             $"Page {pageCounter}/{topListeningTimePages.Count} - " +
                             $"{filteredTopListeningTimeUsers.Count} users - " +
                             $"{filteredTopListeningTimeUsers.Sum(s => s.Playcount)} total minutes";

                pages.Add(new PageBuilder()
                    .WithDescription(listeningTimeString.ToString())
                    .WithTitle(title)
                    .WithFooter(footer));
                pageCounter++;
            }

            var paginator = StringService.BuildStaticPaginator(pages);

            _ = this.Interactivity.SendPaginatorAsync(
                paginator,
                this.Context.Channel,
                TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await ReplyAsync("Something went wrong while showing listening time leaderboard and the error has been logged. Please try again later or contact staff on our support server.");
        }
    }

    private async Task<string> FindUser(string user)
    {
        if (await this._lastFmRepository.LastFmUserExistsAsync(user))
        {
            return user;
        }

        if (!this._guildService.CheckIfDM(this.Context))
        {
            var guildUser = await this._settingService.StringWithDiscordIdForUser(user);

            if (guildUser != null)
            {
                return guildUser.UserNameLastFM;
            }
        }

        return null;
    }
}
