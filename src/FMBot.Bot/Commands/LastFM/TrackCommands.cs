using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Enums;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;
using RunMode = Discord.Commands.RunMode;
using TimePeriod = FMBot.Domain.Models.TimePeriod;

namespace FMBot.Bot.Commands.LastFM;

[Name("Tracks")]
public class TrackCommands : BaseCommandModule
{
    private readonly GuildService _guildService;
    private readonly IIndexService _indexService;
    private readonly IPrefixService _prefixService;
    private readonly IUpdateService _updateService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly PlayService _playService;
    private readonly SettingService _settingService;
    private readonly SpotifyService _spotifyService;
    private readonly TimeService _timeService;
    private readonly UserService _userService;
    private readonly FriendsService _friendsService;
    private readonly WhoKnowsTrackService _whoKnowsTrackService;
    private readonly WhoKnowsPlayService _whoKnowsPlayService;
    private readonly WhoKnowsService _whoKnowsService;
    private readonly TrackService _trackService;
    private readonly AlbumService _albumService;

    private InteractiveService Interactivity { get; }


    private static readonly List<DateTimeOffset> StackCooldownTimer = new();
    private static readonly List<SocketUser> StackCooldownTarget = new();

    public TrackCommands(
        GuildService guildService,
        IIndexService indexService,
        IPrefixService prefixService,
        IUpdateService updateService,
        LastFmRepository lastFmRepository,
        PlayService playService,
        SettingService settingService,
        SpotifyService spotifyService,
        UserService userService,
        WhoKnowsTrackService whoKnowsTrackService,
        WhoKnowsPlayService whoKnowsPlayService,
        InteractiveService interactivity,
        WhoKnowsService whoKnowsService,
        IOptions<BotSettings> botSettings,
        FriendsService friendsService,
        TimeService timeService,
        TrackService trackService,
        AlbumService albumService) : base(botSettings)
    {
        this._guildService = guildService;
        this._indexService = indexService;
        this._lastFmRepository = lastFmRepository;
        this._playService = playService;
        this._prefixService = prefixService;
        this._settingService = settingService;
        this._spotifyService = spotifyService;
        this._updateService = updateService;
        this._userService = userService;
        this._whoKnowsTrackService = whoKnowsTrackService;
        this._whoKnowsPlayService = whoKnowsPlayService;
        this.Interactivity = interactivity;
        this._whoKnowsService = whoKnowsService;
        this._friendsService = friendsService;
        this._timeService = timeService;
        this._trackService = trackService;
        this._albumService = albumService;
    }

    [Command("track", RunMode = RunMode.Async)]
    [Discord.Commands.Summary("Track you're currently listening to or searching for.")]
    [Examples(
        "tr",
        "track",
        "track Depeche Mode Enjoy The Silence",
        "track Crystal Waters | Gypsy Woman (She's Homeless) - Radio Edit")]
    [Alias("tr", "ti", "ts", "trackinfo")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task TrackAsync([Remainder] string trackValues = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel.TriggerTypingAsync();

        var track = await this.SearchTrack(trackValues, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm);
        if (track == null)
        {
            return;
        }

        var userTitle = await this._userService.GetUserTitleAsync(this.Context);

        this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
        this._embedAuthor.WithName($"Info about {track.ArtistName} - {track.TrackName} for {userTitle}");

        if (track.TrackUrl != null)
        {
            this._embedAuthor.WithUrl(track.TrackUrl);
        }

        this._embed.WithAuthor(this._embedAuthor);

        var spotifyTrack = await this._spotifyService.GetOrStoreTrackAsync(track);
        try
        {
            var leftStats = new StringBuilder();
            var rightStats = new StringBuilder();
            var footer = new StringBuilder();

            leftStats.AppendLine($"`{track.TotalListeners}` listeners");
            leftStats.AppendLine($"`{track.TotalPlaycount}` global {StringExtensions.GetPlaysString(track.TotalPlaycount)}");
            leftStats.AppendLine($"`{track.UserPlaycount}` {StringExtensions.GetPlaysString(track.UserPlaycount)} by you");

            if (track.UserPlaycount.HasValue)
            {
                await this._updateService.CorrectUserTrackPlaycount(contextUser.UserId, track.ArtistName,
                    track.TrackName, track.UserPlaycount.Value);
            }

            var duration = spotifyTrack?.DurationMs ?? track.Duration;
            if (duration is > 0)
            {
                var trackLength = TimeSpan.FromMilliseconds(duration.GetValueOrDefault());
                var formattedTrackLength =
                    $"{(trackLength.Hours == 0 ? "" : $"{trackLength.Hours}:")}{trackLength.Minutes}:{trackLength.Seconds:D2}";

                rightStats.AppendLine($"`{formattedTrackLength}` duration");

                if (track.UserPlaycount > 1)
                {
                    var listeningTime =
                        await this._timeService.GetPlayTimeForTrackWithPlaycount(track.ArtistName, track.TrackName,
                            track.UserPlaycount.GetValueOrDefault());

                    leftStats.AppendLine($"`{StringExtensions.GetListeningTimeString(listeningTime)}` spent listening");
                }
            }

            if (spotifyTrack != null && !string.IsNullOrEmpty(spotifyTrack.SpotifyId))
            {
                var pitch = StringExtensions.KeyIntToPitchString(spotifyTrack.Key.GetValueOrDefault());

                rightStats.AppendLine($"`{pitch}` key");

                if (spotifyTrack.Tempo.HasValue)
                {
                    var bpm = $"{spotifyTrack.Tempo.Value:0.0}";
                    rightStats.AppendLine($"`{bpm}` bpm");
                }

                if (spotifyTrack.Danceability.HasValue && spotifyTrack.Energy.HasValue && spotifyTrack.Instrumentalness.HasValue &&
                    spotifyTrack.Acousticness.HasValue && spotifyTrack.Speechiness.HasValue && spotifyTrack.Liveness.HasValue && spotifyTrack.Valence.HasValue)
                {
                    var danceability = ((decimal)(spotifyTrack.Danceability / 1)).ToString("0%");
                    var energetic = ((decimal)(spotifyTrack.Energy / 1)).ToString("0%");
                    var instrumental = ((decimal)(spotifyTrack.Instrumentalness / 1)).ToString("0%");
                    var acoustic = ((decimal)(spotifyTrack.Acousticness / 1)).ToString("0%");
                    var speechful = ((decimal)(spotifyTrack.Speechiness / 1)).ToString("0%");
                    var lively = ((decimal)(spotifyTrack.Liveness / 1)).ToString("0%");
                    var valence = ((decimal)(spotifyTrack.Valence / 1)).ToString("0%");
                    footer.AppendLine($"{danceability} danceable - {energetic} energetic - {acoustic} acoustic\n" +
                                      $"{instrumental} instrumental - {speechful} speechful - {lively} lively\n" +
                                      $"{valence} valence (musical positiveness)");
                }
            }

            if (contextUser.TotalPlaycount.HasValue && track.UserPlaycount is >= 10)
            {
                footer.AppendLine($"{(decimal)track.UserPlaycount.Value / contextUser.TotalPlaycount.Value:P} of all your scrobbles are on this track");
            }

            if (footer.Length > 0)
            {
                this._embed.WithFooter(footer.ToString());
            }

            this._embed.AddField("Statistics", leftStats.ToString(), true);

            if (rightStats.Length > 0)
            {
                this._embed.AddField("Info", rightStats.ToString(), true);
            }

            if (!string.IsNullOrWhiteSpace(track.Description))
            {
                this._embed.AddField("Summary", track.Description);
            }

            //if (track.Tags != null && track.Tags.Any())
            //{
            //    var tags = LastFmRepository.TagsToLinkedString(track.Tags);

            //    this._embed.AddField("Tags", tags);
            //}

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            this.Context.LogCommandUsed();

        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await ReplyAsync(
                "Unable to show your track info due to an internal error. Please try again later or contact .fmbot support.");
        }
    }

    [Command("trackplays", RunMode = RunMode.Async)]
    [Discord.Commands.Summary("Shows playcount for current track or the one you're searching for.\n\n" +
                              "You can also mention another user to see their playcount.")]
    [Examples(
        "tp",
        "trackplays",
        "trackplays Mac DeMarco Here Comes The Cowboy",
        "tp lfm:fm-bot",
        "trackplays Cocteau Twins | Heaven or Las Vegas @user")]
    [Alias("tp", "trackplay", "tplays", "trackp", "track plays")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task TrackPlaysAsync([Remainder] string trackValues = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel.TriggerTypingAsync();

        var userSettings = await this._settingService.GetUser(trackValues, contextUser, this.Context);

        var track = await this.SearchTrack(userSettings.NewSearchValue, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm, userSettings.UserNameLastFm);
        if (track == null)
        {
            return;
        }

        var reply =
            $"**{userSettings.DiscordUserName.FilterOutMentions()}{userSettings.UserType.UserTypeToIcon()}** has `{track.UserPlaycount}` {StringExtensions.GetPlaysString(track.UserPlaycount)} for **{track.TrackName.FilterOutMentions()}** " +
            $"by **{track.ArtistName.FilterOutMentions()}**";

        if (track.UserPlaycount.HasValue && !userSettings.DifferentUser)
        {
            await this._updateService.CorrectUserTrackPlaycount(contextUser.UserId, track.ArtistName,
                track.TrackName, track.UserPlaycount.Value);
        }

        if (!userSettings.DifferentUser && contextUser.LastUpdated != null)
        {
            var playsLastWeek =
                await this._playService.GetWeekTrackPlaycountAsync(userSettings.UserId, track.TrackName, track.ArtistName);
            if (playsLastWeek != 0)
            {
                reply += $" (`{playsLastWeek}` last week)";
            }
        }

        await this.Context.Channel.SendMessageAsync(reply, allowedMentions: AllowedMentions.None);
        this.Context.LogCommandUsed();
    }

    [Command("love", RunMode = RunMode.Async)]
    [Discord.Commands.Summary("Loves a track on Last.fm")]
    [Examples("love", "l", "love Tame Impala Borderline")]
    [Alias("l", "heart", "favorite", "affection", "appreciation", "lust", "fuckyeah", "fukk", "unfuck")]
    [UserSessionRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task LoveAsync([Remainder] string trackValues = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (!string.IsNullOrWhiteSpace(trackValues) && trackValues.ToLower() == "help")
        {
            this._embed.WithTitle($"{prfx}love");
            this._embed.WithDescription("Loves the track you're currently listening to or searching for on last.fm.");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.Help);
            return;
        }

        _ = this.Context.Channel.TriggerTypingAsync();

        var track = await this.SearchTrack(trackValues, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm);
        if (track == null)
        {
            return;
        }

        var userTitle = await this._userService.GetUserTitleAsync(this.Context);


        if (track.Loved)
        {
            this._embed.WithTitle($"❤️ Track already loved");
            this._embed.WithDescription(LastFmRepository.ResponseTrackToLinkedString(track));
        }
        else
        {
            var trackLoved = await this._lastFmRepository.LoveTrackAsync(contextUser, track.ArtistName, track.TrackName);

            if (trackLoved)
            {
                this._embed.WithTitle($"❤️ Loved track for {userTitle}");
                this._embed.WithDescription(LastFmRepository.ResponseTrackToLinkedString(track));
            }
            else
            {
                await this.Context.Message.Channel.SendMessageAsync(
                    "Something went wrong while adding loved track.");
                this.Context.LogCommandUsed(CommandResponse.Error);
                return;
            }
        }

        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        this.Context.LogCommandUsed();
    }

    [Command("unlove", RunMode = RunMode.Async)]
    [Discord.Commands.Summary("Removes the track you're currently listening to or searching for from your last.fm loved tracks.")]
    [Examples("unlove", "ul", "unlove Lou Reed Brandenburg Gate")]
    [Alias("ul", "unheart", "hate", "fuck")]
    [UserSessionRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task UnLoveAsync([Remainder] string trackValues = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (!string.IsNullOrWhiteSpace(trackValues) && trackValues.ToLower() == "help")
        {
            this._embed.WithTitle($"{prfx}unlove");
            this._embed.WithDescription("Unloves the track you're currently listening to or searching for on last.fm.");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.Help);
            return;
        }

        _ = this.Context.Channel.TriggerTypingAsync();

        var track = await this.SearchTrack(trackValues, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm);
        if (track == null)
        {
            return;
        }

        var userTitle = await this._userService.GetUserTitleAsync(this.Context);

        if (!track.Loved)
        {
            this._embed.WithTitle($"💔 Track wasn't loved");
            this._embed.WithDescription(LastFmRepository.ResponseTrackToLinkedString(track));
        }
        else
        {
            var trackLoved = await this._lastFmRepository.UnLoveTrackAsync(contextUser, track.ArtistName, track.TrackName);

            if (trackLoved)
            {
                this._embed.WithTitle($"💔 Unloved track for {userTitle}");
                this._embed.WithDescription(LastFmRepository.ResponseTrackToLinkedString(track));
            }
            else
            {
                await this.Context.Message.Channel.SendMessageAsync(
                    "Something went wrong while unloving track.");
                this.Context.LogCommandUsed(CommandResponse.Error);
                return;
            }
        }

        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        this.Context.LogCommandUsed();
    }

    [Command("loved", RunMode = RunMode.Async)]
    [Discord.Commands.Summary("Shows your Last.fm loved tracks.")]
    [Examples("loved", "lt", "lovedtracks lfm:fm-bot", "lovedtracks @user")]
    [Alias("lovedtracks", "lt")]
    [UserSessionRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task LovedAsync([Remainder] string extraOptions = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        _ = this.Context.Channel.TriggerTypingAsync();

        var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);

        var pages = new List<PageBuilder>();

        try
        {
            string sessionKey = null;
            if (!userSettings.DifferentUser && !string.IsNullOrEmpty(contextUser.SessionKeyLastFm))
            {
                sessionKey = contextUser.SessionKeyLastFm;
            }

            const int amount = 200;

            var lovedTracks = await this._lastFmRepository.GetLovedTracksAsync(userSettings.UserNameLastFm, amount, sessionKey: sessionKey);

            if (!lovedTracks.Content.RecentTracks.Any())
            {
                this._embed.WithDescription(
                    $"The Last.fm user `{userSettings.UserNameLastFm}` has no loved tracks yet! \n" +
                    $"Use `{prfx}love` to add tracks to your list.");
                this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                return;
            }

            if (await GenericEmbedService.RecentScrobbleCallFailedReply(lovedTracks, userSettings.UserNameLastFm, this.Context))
            {
                return;
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);
            var title = !userSettings.DifferentUser ? userTitle : $"{userSettings.UserNameLastFm}, requested by {userTitle}";

            this._embedAuthor.WithName($"Last loved tracks for {title}");
            this._embedAuthor.WithUrl(lovedTracks.Content.UserRecentTracksUrl);

            if (!userSettings.DifferentUser)
            {
                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            }

            var firstTrack = lovedTracks.Content.RecentTracks[0];

            var footer = $"{userSettings.UserNameLastFm} has {lovedTracks.Content.TotalAmount} loved tracks";
            DateTime? timePlaying = null;

            if (!firstTrack.NowPlaying && firstTrack.TimePlayed.HasValue)
            {
                timePlaying = firstTrack.TimePlayed.Value;
            }

            if (timePlaying.HasValue)
            {
                footer += " | Last loved track:";
            }

            var lovedTrackPages = lovedTracks.Content.RecentTracks.ChunkBy(10);

            var counter = lovedTracks.Content.RecentTracks.Count;
            foreach (var lovedTrackPage in lovedTrackPages)
            {
                var albumPageString = new StringBuilder();
                foreach (var lovedTrack in lovedTrackPage)
                {
                    var trackString = LastFmRepository.TrackToOneLinedLinkedString(lovedTrack);

                    albumPageString.AppendLine($"`{counter}` - {trackString}");
                    counter--;
                }

                var page = new PageBuilder()
                    .WithDescription(albumPageString.ToString())
                    .WithAuthor(this._embedAuthor)
                    .WithFooter(footer);

                if (timePlaying.HasValue)
                {
                    page.WithTimestamp(timePlaying);
                }

                pages.Add(page);
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
            await ReplyAsync(
                "Unable to show your recent tracks on Last.fm due to an internal error. Please try again later or contact .fmbot support.");
        }
    }

    [Command("scrobble", RunMode = RunMode.Async)]
    [Discord.Commands.Summary("Scrobbles a track on Last.fm.")]
    [Examples("scrobble", "sb stronger Kanye West", "scrobble Loona Heart Attack", "scrobble Mac DeMarco | Chamber of Reflection")]
    [UserSessionRequired]
    [Alias("sb")]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task ScrobbleAsync([Remainder] string trackValues = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (string.IsNullOrWhiteSpace(trackValues))
        {
            this._embed.WithColor(DiscordConstants.InformationColorBlue);
            this._embed.WithTitle($"{prfx}scrobble");
            this._embed.WithDescription("Scrobbles a track. You can enter a search value or enter the exact name with separators. " +
                                        "You can only scrobble tracks that already exist on Last.fm.");

            this._embed.AddField("Search for a track to scrobble",
                $"Format: `{prfx}scrobble SearchValue`\n" +
                $"`{prfx}sb Stronger Kanye` *(scrobbles Stronger by Kanye West)*\n" +
                $"`{prfx}scrobble Loona Heart Attack` *(scrobbles Heart Attack (츄) by LOONA)*");

            this._embed.AddField("Or enter the exact name with separators",
                $"Format: `{prfx}scrobble Artist | Track`\n" +
                $"`{prfx}scrobble Mac DeMarco | Chamber of Reflection`\n" +
                $"`{prfx}scrobble Home | Climbing Out`");

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.Help);
            return;
        }

        var track = await this.SearchTrack(trackValues, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm);
        if (track == null)
        {
            return;
        }

        var msg = this.Context.Message as SocketUserMessage;
        if (StackCooldownTarget.Contains(this.Context.Message.Author))
        {
            if (StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)].AddSeconds(30) >= DateTimeOffset.Now)
            {
                var secondsLeft = (int)(StackCooldownTimer[
                        StackCooldownTarget.IndexOf(this.Context.Message.Author as SocketGuildUser)]
                    .AddSeconds(30) - DateTimeOffset.Now).TotalSeconds;
                if (secondsLeft <= 28)
                {
                    await ReplyAsync("Please wait before scrobbling to Last.fm again.");
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

        var userTitle = await this._userService.GetUserTitleAsync(this.Context);

        var trackScrobbled = await this._lastFmRepository.ScrobbleAsync(contextUser, track.ArtistName, track.TrackName, track.AlbumName);

        if (trackScrobbled.Success && trackScrobbled.Content.Scrobbles.Attr.Accepted > 0)
        {
            Statistics.LastfmScrobbles.Inc();
            this._embed.WithTitle($"Scrobbled track for {userTitle}");
            this._embed.WithDescription(LastFmRepository.ResponseTrackToLinkedString(track));
        }
        else if (trackScrobbled.Success && trackScrobbled.Content.Scrobbles.Attr.Ignored > 0)
        {
            this._embed.WithTitle($"Last.fm ignored scrobble for {userTitle}");
            var description = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(trackScrobbled.Content.Scrobbles.Scrobble.IgnoredMessage?.Text))
            {
                description.AppendLine($"Reason: {trackScrobbled.Content.Scrobbles.Scrobble.IgnoredMessage?.Text}");
            }

            description.AppendLine(LastFmRepository.ResponseTrackToLinkedString(track));
            this._embed.WithDescription(description.ToString());
        }
        else
        {
            await this.Context.Message.Channel.SendMessageAsync("Something went wrong while scrobbling track :(.");
            this.Context.LogCommandWithLastFmError(trackScrobbled.Error);
            return;
        }

        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        this.Context.LogCommandUsed();
    }

    [Command("toptracks", RunMode = RunMode.Async)]
    [Discord.Commands.Summary("Shows your or someone else their top tracks over a certain time period.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample,
        Constants.BillboardExample, Constants.ExtraLargeExample)]
    [Examples("tt", "toptracks", "tt y 3", "toptracks weekly @user", "tt bb xl")]
    [Alias("tt", "tl", "tracklist", "tracks", "trackslist", "top tracks", "top track")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task TopTracksAsync([Remainder] string extraOptions = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel.TriggerTypingAsync();

        try
        {
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
            var topListSettings = SettingService.SetTopListSettings(extraOptions);
            userSettings.RegisteredLastFm ??= await this._indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var timeSettings = SettingService.GetTimePeriod(extraOptions, registeredLastFm: userSettings.RegisteredLastFm);

            var pages = new List<PageBuilder>();

            string userTitle;
            if (!userSettings.DifferentUser)
            {
                userTitle = await this._userService.GetUserTitleAsync(this.Context);
                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            }
            else
            {
                userTitle =
                    $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(this.Context)}";
            }

            var userUrl = $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/tracks?{timeSettings.UrlParameter}";
            this._embedAuthor.WithName($"Top {timeSettings.Description.ToLower()} tracks for {userTitle}");
            this._embedAuthor.WithUrl(userUrl);

            var topTracks = await this._lastFmRepository.GetTopTracksAsync(userSettings.UserNameLastFm, timeSettings, 200);

            if (!topTracks.Success)
            {
                this._embed.ErrorResponse(topTracks.Error, topTracks.Message, this.Context.Message.Content, this.Context.User);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }
            if (topTracks.Content?.TopTracks == null || !topTracks.Content.TopTracks.Any())
            {
                this._embed.WithDescription($"Sorry, you or the user you're searching for don't have any top tracks in the [selected time period]({userUrl}).");
                this._embed.WithColor(DiscordConstants.WarningColorOrange);
                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                return;
            }

            var previousTopTracks = new List<TopTrack>();
            if (topListSettings.Billboard && timeSettings.BillboardStartDateTime.HasValue && timeSettings.BillboardEndDateTime.HasValue)
            {
                var previousTopTracksCall = await this._lastFmRepository
                    .GetTopTracksForCustomTimePeriodAsyncAsync(userSettings.UserNameLastFm, timeSettings.BillboardStartDateTime.Value, timeSettings.BillboardEndDateTime.Value, 200);

                if (previousTopTracksCall.Success)
                {
                    previousTopTracks.AddRange(previousTopTracksCall.Content.TopTracks);
                }
            }

            this._embed.WithAuthor(this._embedAuthor);

            var trackPages = topTracks.Content.TopTracks.ChunkBy(topListSettings.ExtraLarge ? Constants.DefaultExtraLargePageSize : Constants.DefaultPageSize);

            var counter = 1;
            var pageCounter = 1;
            var rnd = new Random().Next(0, 4);

            foreach (var trackPage in trackPages)
            {
                var trackPageString = new StringBuilder();
                foreach (var track in trackPage)
                {
                    var name = $"**{track.ArtistName}** - **[{track.TrackName}]({track.TrackUrl})** ({track.UserPlaycount} {StringExtensions.GetPlaysString(track.UserPlaycount)})";

                    if (topListSettings.Billboard && previousTopTracks.Any())
                    {
                        var previousTopTrack = previousTopTracks.FirstOrDefault(f => f.ArtistName == track.ArtistName && f.AlbumName == track.AlbumName);
                        int? previousPosition = previousTopTrack == null ? null : previousTopTracks.IndexOf(previousTopTrack);

                        trackPageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition).Text);
                    }
                    else
                    {
                        trackPageString.Append($"{counter}. ");
                        trackPageString.AppendLine(name);
                    }

                    counter++;
                }

                var footer = new StringBuilder();
                footer.Append($"Page {pageCounter}/{trackPages.Count}");
                if (topTracks.Content.TotalAmount.HasValue)
                {
                    footer.Append($" - {topTracks.Content.TotalAmount.Value} total tracks in this time period");
                }
                if (topListSettings.Billboard)
                {
                    footer.AppendLine();
                    footer.Append(StringService.GetBillBoardSettingString(timeSettings, userSettings.RegisteredLastFm));
                }

                if (rnd == 1 && !topListSettings.Billboard)
                {
                    footer.AppendLine();
                    footer.Append("View this list as a billboard by adding 'billboard' or 'bb'");
                }

                pages.Add(new PageBuilder()
                    .WithDescription(trackPageString.ToString())
                    .WithAuthor(this._embedAuthor)
                    .WithFooter(footer.ToString()));
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
            await ReplyAsync("Unable to show Last.fm info due to an internal error.");
        }
    }

    [Command("whoknowstrack", RunMode = RunMode.Async)]
    [Discord.Commands.Summary("Shows what other users listen to a track in your server")]
    [Examples("wt", "whoknowstrack", "whoknowstrack Hothouse Flowers Don't Go", "whoknowstrack Natasha Bedingfield | Unwritten")]
    [Alias("wt", "wkt", "wktr", "wtr", "wktrack", "wk track", "whoknows track")]
    [UsernameSetRequired]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Tracks, CommandCategory.WhoKnows)]
    public async Task WhoKnowsTrackAsync([Remainder] string trackValues = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var guildTask = this._guildService.GetGuildForWhoKnows(this.Context.Guild.Id);

        _ = this.Context.Channel.TriggerTypingAsync();

        var track = await this.SearchTrack(trackValues, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm, useCachedTracks: true, userId: contextUser.UserId);
        if (track == null)
        {
            return;
        }

        await this._spotifyService.GetOrStoreTrackAsync(track);

        var trackName = $"{track.TrackName} by {track.ArtistName}";

        try
        {
            var guild = await guildTask;

            var currentUser = await this._indexService.GetOrAddUserToGuild(guild, await this.Context.Guild.GetUserAsync(contextUser.DiscordUserId), contextUser);

            if (!guild.GuildUsers.Select(s => s.UserId).Contains(contextUser.UserId))
            {
                guild.GuildUsers.Add(currentUser);
            }

            await this._indexService.UpdateGuildUser(await this.Context.Guild.GetUserAsync(contextUser.DiscordUserId), currentUser.UserId, guild);

            var usersWithTrack = await this._whoKnowsTrackService.GetIndexedUsersForTrack(this.Context, guild.GuildId, track.ArtistName, track.TrackName);

            if (track.UserPlaycount.HasValue && track.UserPlaycount != 0)
            {
                usersWithTrack = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithTrack, currentUser, trackName, track.UserPlaycount.Value);
            }

            var filteredUsersWithTrack = WhoKnowsService.FilterGuildUsersAsync(usersWithTrack, guild);

            var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithTrack, contextUser.UserId, PrivacyLevel.Server);
            if (filteredUsersWithTrack.Count == 0)
            {
                serverUsers = "Nobody in this server (not even you) has listened to this track.";
            }

            this._embed.WithDescription(serverUsers);

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);
            var footer = $"WhoKnows track requested by {userTitle}";

            var rnd = new Random();
            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);
            if (rnd.Next(0, 10) == 1 && lastIndex < DateTime.UtcNow.AddDays(-30))
            {
                footer += $"\nMissing members? Update with {prfx}index";
            }

            if (filteredUsersWithTrack.Any() && filteredUsersWithTrack.Count > 1)
            {
                var serverListeners = filteredUsersWithTrack.Count;
                var serverPlaycount = filteredUsersWithTrack.Sum(a => a.Playcount);
                var avgServerPlaycount = filteredUsersWithTrack.Average(a => a.Playcount);

                footer += $"\n{serverListeners} {StringExtensions.GetListenersString(serverListeners)} - ";
                footer += $"{serverPlaycount} total {StringExtensions.GetPlaysString(serverPlaycount)} - ";
                footer += $"{(int)avgServerPlaycount} avg {StringExtensions.GetPlaysString((int)avgServerPlaycount)}";
            }

            if (usersWithTrack.Count > filteredUsersWithTrack.Count && !guild.WhoKnowsWhitelistRoleId.HasValue)
            {
                var filteredAmount = usersWithTrack.Count - filteredUsersWithTrack.Count;
                footer += $"\n{filteredAmount} inactive/blocked users filtered";
            }
            if (guild.WhoKnowsWhitelistRoleId.HasValue)
            {
                footer += $"\nUsers with WhoKnows whitelisted role only";
            }

            this._embed.WithTitle($"{trackName} in {this.Context.Guild.Name}");

            if (track.TrackUrl != null)
            {
                this._embed.WithUrl(track.TrackUrl);
            }

            this._embedFooter.WithText(footer);
            this._embed.WithFooter(this._embedFooter);

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await ReplyAsync("Something went wrong while using whoknows track. Please let us know as this feature is in beta.");
        }
    }

    [Command("globalwhoknowstrack", RunMode = RunMode.Async)]
    [Discord.Commands.Summary("Shows what other users listen to a track in .fmbot")]
    [Examples("gwt", "globalwhoknowstrack", "globalwhoknowstrack Hothouse Flowers Don't Go", "globalwhoknowstrack Natasha Bedingfield | Unwritten")]
    [Alias("gwt", "gwkt", "gwtr", "gwktr", "globalwkt", "globalwktrack", "globalwhoknows track")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks, CommandCategory.WhoKnows)]
    public async Task GlobalWhoKnowsTrackAsync([Remainder] string trackValues = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var guildTask = this._guildService.GetGuildForWhoKnows(this.Context.Guild?.Id);
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var currentSettings = new WhoKnowsSettings
        {
            HidePrivateUsers = false,
            ShowBotters = false,
            AdminView = false,
            NewSearchValue = trackValues
        };
        var settings = this._settingService.SetWhoKnowsSettings(currentSettings, trackValues, contextUser.UserType);

        var track = await this.SearchTrack(settings.NewSearchValue, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm, useCachedTracks: true, userId: contextUser.UserId);
        if (track == null)
        {
            return;
        }

        await this._spotifyService.GetOrStoreTrackAsync(track);

        var trackName = $"{track.TrackName} by {track.ArtistName}";

        try
        {
            var usersWithArtist = await this._whoKnowsTrackService.GetGlobalUsersForTrack(this.Context, track.ArtistName, track.TrackName);

            if (track.UserPlaycount != 0 && this.Context.Guild != null)
            {
                var discordGuildUser = await this.Context.Guild.GetUserAsync(contextUser.DiscordUserId);
                var guildUser = new GuildUser
                {
                    UserName = discordGuildUser != null ? discordGuildUser.Nickname ?? discordGuildUser.Username : contextUser.UserNameLastFM,
                    User = contextUser
                };
                usersWithArtist = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithArtist, guildUser, trackName, track.UserPlaycount);
            }

            var guild = await guildTask;
            var privacyLevel = PrivacyLevel.Global;

            var filteredUsersWithAlbum = await this._whoKnowsService.FilterGlobalUsersAsync(usersWithArtist);

            if (guild != null)
            {
                filteredUsersWithAlbum =
                    WhoKnowsService.ShowGuildMembersInGlobalWhoKnowsAsync(filteredUsersWithAlbum, guild.GuildUsers.ToList());

                if (settings.AdminView && guild.SpecialGuild == true)
                {
                    privacyLevel = PrivacyLevel.Server;
                }
            }

            var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithAlbum, contextUser.UserId, privacyLevel, hidePrivateUsers: settings.HidePrivateUsers);
            if (!filteredUsersWithAlbum.Any())
            {
                serverUsers = "Nobody that uses .fmbot has listened to this track.";
            }

            this._embed.WithDescription(serverUsers);

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);
            var footer = $"Global WhoKnows track requested by {userTitle}";

            if (filteredUsersWithAlbum.Any() && filteredUsersWithAlbum.Count() > 1)
            {
                var serverListeners = filteredUsersWithAlbum.Count();
                var serverPlaycount = filteredUsersWithAlbum.Sum(a => a.Playcount);
                var avgServerPlaycount = filteredUsersWithAlbum.Average(a => a.Playcount);

                footer += $"\n{serverListeners} {StringExtensions.GetListenersString(serverListeners)} - ";
                footer += $"{serverPlaycount} total {StringExtensions.GetPlaysString(serverPlaycount)} - ";
                footer += $"{(int)avgServerPlaycount} avg {StringExtensions.GetPlaysString((int)avgServerPlaycount)}";
            }

            if (settings.AdminView)
            {
                footer += "\nAdmin view enabled - not for public channels";
            }
            if (contextUser.PrivacyLevel != PrivacyLevel.Global)
            {
                footer += $"\nYou are currently not globally visible - use '{prfx}privacy global' to enable.";
            }

            if (settings.HidePrivateUsers)
            {
                footer += "\nAll private users are hidden from results";
            }

            this._embed.WithTitle($"{trackName} globally");

            if (!string.IsNullOrWhiteSpace(track.TrackUrl))
            {
                this._embed.WithUrl(track.TrackUrl);
            }

            this._embedFooter.WithText(footer);
            this._embed.WithFooter(this._embedFooter);

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

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
                await ReplyAsync("Something went wrong while using global whoknows track.");
            }
        }
    }

    [Command("friendwhoknowstrack", RunMode = RunMode.Async)]
    [Discord.Commands.Summary("Shows who of your friends listen to an track in .fmbot")]
    [Examples("fwt", "fwkt The Beatles Yesterday", "friendwhoknowstrack", "friendwhoknowstrack Hothouse Flowers Don't Go", "friendwhoknowstrack Mall Grab | Sunflower")]
    [Alias("fwt", "fwkt", "fwktr", "fwtrack", "friendwhoknows track", "friends whoknows track", "friend whoknows track")]
    [UsernameSetRequired]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Tracks, CommandCategory.WhoKnows, CommandCategory.Friends)]
    public async Task FriendWhoKnowsTrackAsync([Remainder] string albumValues = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var contextUser = await this._userService.GetUserWithFriendsAsync(this.Context.User);

            if (contextUser.Friends?.Any() != true)
            {
                await ReplyAsync("We couldn't find any friends. To add friends:\n" +
                                 $"`{prfx}addfriends {Constants.UserMentionOrLfmUserNameExample.Replace("`", "")}`");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            var track = await this.SearchTrack(albumValues, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm, useCachedTracks: true, userId: contextUser.UserId);
            if (track == null)
            {
                return;
            }

            var trackName = $"{track.TrackName} by {track.ArtistName}";

            var usersWithTrack = await this._whoKnowsTrackService.GetFriendUsersForTrack(this.Context, guild.GuildId, contextUser.UserId, track.ArtistName, track.TrackName);

            if (track.UserPlaycount.HasValue && this.Context.Guild != null)
            {
                var discordGuildUser = await this.Context.Guild.GetUserAsync(contextUser.DiscordUserId);
                var guildUser = new GuildUser
                {
                    UserName = discordGuildUser != null ? discordGuildUser.Nickname ?? discordGuildUser.Username : contextUser.UserNameLastFM,
                    User = contextUser
                };
                usersWithTrack = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithTrack, guildUser, trackName, track.UserPlaycount);
            }

            var serverUsers = WhoKnowsService.WhoKnowsListToString(usersWithTrack, contextUser.UserId, PrivacyLevel.Server);
            if (usersWithTrack.Count == 0)
            {
                serverUsers = "None of your friends have listened to this track.";
            }

            this._embed.WithDescription(serverUsers);

            var footer = "";

            var amountOfHiddenFriends = contextUser.Friends.Count(c => !c.FriendUserId.HasValue);
            if (amountOfHiddenFriends > 0)
            {
                footer += $"\n{amountOfHiddenFriends} non-fmbot {StringExtensions.GetFriendsString(amountOfHiddenFriends)} not visible";
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);
            footer += $"\nFriends WhoKnow track requested by {userTitle}";

            if (usersWithTrack.Any() && usersWithTrack.Count > 1)
            {
                var globalListeners = usersWithTrack.Count;
                var globalPlaycount = usersWithTrack.Sum(a => a.Playcount);
                var avgPlaycount = usersWithTrack.Average(a => a.Playcount);

                footer += $"\n{globalListeners} {StringExtensions.GetListenersString(globalListeners)} - ";
                footer += $"{globalPlaycount} total {StringExtensions.GetPlaysString(globalPlaycount)} - ";
                footer += $"{(int)avgPlaycount} avg {StringExtensions.GetPlaysString((int)avgPlaycount)}";
            }

            this._embed.WithTitle($"{trackName} with friends");

            if (Uri.IsWellFormedUriString(track.TrackUrl, UriKind.Absolute))
            {
                this._embed.WithUrl(track.TrackUrl);
            }

            this._embedFooter.WithText(footer);
            this._embed.WithFooter(this._embedFooter);

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

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
                await ReplyAsync("Something went wrong while using friend whoknows track.");
            }
        }
    }

    [Command("servertracks", RunMode = RunMode.Async)]
    [Discord.Commands.Summary("Top tracks for your server, optionally for an artist")]
    [Options("Time periods: `weekly`, `monthly` and `alltime`", "Order options: `plays` and `listeners`", "Artist name")]
    [Examples("st", "st a p", "servertracks", "servertracks alltime", "servertracks listeners weekly", "servertracks kanye west listeners")]
    [Alias("st", "stt", "servertoptracks", "servertrack", "server tracks", "billboard", "bb")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task GuildTracksAsync([Remainder] string guildTracksOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        var guildListSettings = new GuildRankingSettings
        {
            ChartTimePeriod = TimePeriod.Weekly,
            TimeDescription = "weekly",
            OrderType = OrderType.Listeners,
            AmountOfDays = 7,
            NewSearchValue = guildTracksOptions
        };

        guildListSettings = SettingService.SetGuildRankingSettings(guildListSettings, guildTracksOptions);
        var timeSettings = SettingService.GetTimePeriod(guildListSettings.NewSearchValue, guildListSettings.ChartTimePeriod, cachedOrAllTimeOnly: true);
        var artistName = timeSettings.NewSearchValue;

        if (timeSettings.UsePlays || timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
        {
            guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
        }

        var footer = new StringBuilder();

        try
        {
            ICollection<GuildTrack> topGuildTracks;
            IList<GuildTrack> previousTopGuildTracks = null;
            if (guildListSettings.ChartTimePeriod == TimePeriod.AllTime)
            {
                topGuildTracks = await this._whoKnowsTrackService.GetTopAllTimeTracksForGuild(guild.GuildId, guildListSettings.OrderType, artistName);
            }
            else
            {
                var plays = await this._playService.GetGuildUsersPlays(guild.GuildId, guildListSettings.AmountOfDaysWithBillboard);

                topGuildTracks = PlayService.GetGuildTopTracks(plays, guildListSettings.StartDateTime, guildListSettings.OrderType, artistName);
                previousTopGuildTracks = PlayService.GetGuildTopTracks(plays, guildListSettings.BillboardStartDateTime, guildListSettings.OrderType, artistName);
            }

            if (!topGuildTracks.Any())
            {
                this._embed.WithDescription(artistName != null
                    ? $"Sorry, there are no registered top tracks for artist `{artistName}` on this server in the time period you selected."
                    : $"Sorry, there are no registered top tracks on this server in the time period you selected.");
                this._embed.WithColor(DiscordConstants.WarningColorOrange);
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var title = string.IsNullOrWhiteSpace(artistName) ?
                $"Top {guildListSettings.TimeDescription.ToLower()} tracks in {this.Context.Guild.Name}" :
                $"Top {guildListSettings.TimeDescription.ToLower()} '{artistName}' tracks in {this.Context.Guild.Name}";

            footer.AppendLine(guildListSettings.OrderType == OrderType.Listeners
                ? " - Ordered by listeners"
                : " - Ordered by plays");

            var rnd = new Random();
            var randomHintNumber = rnd.Next(0, 5);
            switch (randomHintNumber)
            {
                case 1:
                    footer.AppendLine($"View specific track listeners with '{prfx}whoknowstrack'");
                    break;
                case 2:
                    footer.AppendLine($"Available time periods: alltime, monthly, weekly and daily");
                    break;
                case 3:
                    footer.AppendLine($"Available sorting options: plays and listeners");
                    break;
            }

            var trackPages = topGuildTracks.Chunk(12).ToList();

            var counter = 1;
            var pageCounter = 1;
            var pages = new List<PageBuilder>();
            foreach (var page in trackPages)
            {
                var pageString = new StringBuilder();
                foreach (var track in page)
                {
                    var name = guildListSettings.OrderType == OrderType.Listeners
                        ? $"`{track.ListenerCount}` · **{track.ArtistName}** - **{track.TrackName}** ({track.TotalPlaycount} {StringExtensions.GetPlaysString(track.TotalPlaycount)})"
                        : $"`{track.TotalPlaycount}` · **{track.ArtistName}** - **{track.TrackName}** ({track.ListenerCount} {StringExtensions.GetListenersString(track.ListenerCount)})";

                    if (previousTopGuildTracks != null && previousTopGuildTracks.Any())
                    {
                        var previousTopTrack = previousTopGuildTracks.FirstOrDefault(f => f.ArtistName == track.ArtistName && f.TrackName == track.TrackName);
                        int? previousPosition = previousTopTrack == null ? null : previousTopGuildTracks.IndexOf(previousTopTrack);

                        pageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition, false).Text);
                    }
                    else
                    {
                        pageString.AppendLine(name);
                    }

                    counter++;
                }

                var pageFooter = new StringBuilder();
                pageFooter.Append($"Page {pageCounter}/{trackPages.Count}");
                pageFooter.Append(footer);

                pages.Add(new PageBuilder()
                    .WithTitle(title)
                    .WithDescription(pageString.ToString())
                    .WithAuthor(this._embedAuthor)
                    .WithFooter(pageFooter.ToString()));
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
            await ReplyAsync(
                "Something went wrong while using servertracks. Please report this issue.");
        }
    }

    private async Task<TrackInfo> SearchTrack(string trackValues, string lastFmUserName, string sessionKey = null,
        string otherUserUsername = null, bool useCachedTracks = false, int? userId = null)
    {
        string searchValue;
        if (!string.IsNullOrWhiteSpace(trackValues) && trackValues.Length != 0)
        {
            searchValue = trackValues;

            if (trackValues.Contains(" | "))
            {
                if (otherUserUsername != null)
                {
                    lastFmUserName = otherUserUsername;
                }

                var trackName = searchValue.Split(" | ")[1];
                var trackArtist = searchValue.Split(" | ")[0];

                Response<TrackInfo> trackInfo;
                if (useCachedTracks)
                {
                    trackInfo = await GetCachedTrack(trackArtist, trackName, lastFmUserName, userId);
                }
                else
                {
                    trackInfo = await this._lastFmRepository.GetTrackInfoAsync(trackName, trackArtist,
                        lastFmUserName);
                }

                if (!trackInfo.Success && trackInfo.Error == ResponseStatus.MissingParameters)
                {
                    this._embed.WithDescription($"Track `{trackName}` by `{trackArtist}` could not be found, please check your search values and try again.");
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return null;
                }
                if (!trackInfo.Success || trackInfo.Content == null)
                {
                    this._embed.ErrorResponse(trackInfo.Error, trackInfo.Message, this.Context.Message.Content, this.Context.User, "track");
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.LastFmError);
                    return null;
                }
                return trackInfo.Content;
            }
        }
        else
        {
            var recentScrobbles = await this._lastFmRepository.GetRecentTracksAsync(lastFmUserName, 1, useCache: true, sessionKey: sessionKey);

            if (await GenericEmbedService.RecentScrobbleCallFailedReply(recentScrobbles, lastFmUserName, this.Context))
            {
                return null;
            }

            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            var lastPlayedTrack = recentScrobbles.Content.RecentTracks[0];

            Response<TrackInfo> trackInfo;
            if (useCachedTracks)
            {
                trackInfo = await GetCachedTrack(lastPlayedTrack.ArtistName, lastPlayedTrack.TrackName, lastFmUserName, userId);
            }
            else
            {
                trackInfo = await this._lastFmRepository.GetTrackInfoAsync(lastPlayedTrack.TrackName, lastPlayedTrack.ArtistName,
                    lastFmUserName);
            }

            if (trackInfo?.Content == null)
            {
                this._embed.WithDescription($"Last.fm did not return a result for **{lastPlayedTrack.TrackName}** by **{lastPlayedTrack.ArtistName}**.\n\n" +
                                            $"This usually happens on recently released tracks. Please try again later.");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return null;
            }

            return trackInfo.Content;
        }

        var result = await this._lastFmRepository.SearchTrackAsync(searchValue);
        if (result.Success && result.Content != null)
        {
            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            Response<TrackInfo> trackInfo;
            if (useCachedTracks)
            {
                trackInfo = await GetCachedTrack(result.Content.ArtistName, result.Content.TrackName, lastFmUserName, userId);
            }
            else
            {
                trackInfo = await this._lastFmRepository.GetTrackInfoAsync(result.Content.TrackName, result.Content.ArtistName,
                    lastFmUserName);
            }

            if (trackInfo.Content == null || !trackInfo.Success)
            {
                this._embed.WithDescription($"Last.fm did not return a result for **{result.Content.TrackName}** by **{result.Content.ArtistName}**.\n" +
                                            $"This usually happens on recently released tracks. Please try again later.");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return null;
            }

            return trackInfo.Content;
        }

        if (result.Success)
        {
            this._embed.WithDescription($"Track could not be found, please check your search values and try again.");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return null;
        }

        this._embed.WithDescription($"Last.fm returned an error: {result.Error}");
        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        this.Context.LogCommandUsed(CommandResponse.LastFmError);
        return null;
    }

    private async Task<Response<TrackInfo>> GetCachedTrack(string artistName, string trackName, string lastFmUserName, int? userId = null)
    {
        Response<TrackInfo> trackInfo;
        var cachedTrack = await this._trackService.GetTrackFromDatabase(artistName, trackName);
        if (cachedTrack != null)
        {
            trackInfo = new Response<TrackInfo>
            {
                Content = this._trackService.CachedTrackToTrackInfo(cachedTrack),
                Success = true
            };

            if (userId.HasValue)
            {
                var userPlaycount = await this._whoKnowsTrackService.GetTrackPlayCountForUser(cachedTrack.ArtistName,
                    cachedTrack.Name, userId.Value);
                trackInfo.Content.UserPlaycount = userPlaycount;
            }

            var cachedAlbum = await this._albumService.GetAlbumFromDatabase(cachedTrack.ArtistName, cachedTrack.AlbumName);
            if (cachedAlbum != null)
            {
                trackInfo.Content.AlbumCoverUrl = cachedAlbum.SpotifyImageUrl ?? cachedAlbum.SpotifyImageUrl;
                trackInfo.Content.AlbumUrl = cachedAlbum.LastFmUrl;
                trackInfo.Content.TrackUrl = cachedAlbum.LastFmUrl;
            }
        }
        else
        {
            trackInfo = await this._lastFmRepository.GetTrackInfoAsync(trackName, artistName,
                lastFmUserName);
        }

        return trackInfo;
    }
}
