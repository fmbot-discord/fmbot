using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
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

namespace FMBot.Bot.TextCommands.LastFM;

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
    private readonly TrackBuilders _trackBuilders;
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
        AlbumService albumService,
        TrackBuilders trackBuilders) : base(botSettings)
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
        this._trackBuilders = trackBuilders;
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
        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await this._trackBuilders.TrackAsync(new ContextModel(this.Context, prfx, contextUser), trackValues);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
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
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(trackValues, contextUser, this.Context);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var response = await this._trackBuilders.TrackPlays(new ContextModel(this.Context, prfx, contextUser), userSettings, userSettings.NewSearchValue);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("love", RunMode = RunMode.Async)]
    [Discord.Commands.Summary("Loves a track on Last.fm")]
    [Examples("love", "l", "love Tame Impala Borderline")]
    [Alias("l", "heart", "favorite", "affection", "appreciation", "lust", "fuckyeah", "fukk", "unfuck")]
    [UserSessionRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task LoveAsync([Remainder] string trackValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var response = await this._trackBuilders.LoveTrackAsync(new ContextModel(this.Context, prfx, contextUser), trackValues);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("unlove", RunMode = RunMode.Async)]
    [Discord.Commands.Summary("Removes the track you're currently listening to or searching for from your last.fm loved tracks.")]
    [Examples("unlove", "ul", "unlove Lou Reed Brandenburg Gate")]
    [Alias("ul", "unheart", "hate", "fuck")]
    [UserSessionRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task UnLoveAsync([Remainder] string trackValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var response = await this._trackBuilders.UnLoveTrackAsync(new ContextModel(this.Context, prfx, contextUser), trackValues);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
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
            await this.Context.HandleCommandException(e);
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
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
            var topListSettings = SettingService.SetTopListSettings(extraOptions);
            userSettings.RegisteredLastFm ??= await this._indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var timeSettings = SettingService.GetTimePeriod(extraOptions, registeredLastFm: userSettings.RegisteredLastFm);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await this._trackBuilders.TopTracksAsync(new ContextModel(this.Context, prfx, contextUser),
                topListSettings, timeSettings, userSettings);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("receipt", RunMode = RunMode.Async)]
    [Discord.Commands.Summary("Shows your track receipt. Based on Receiptify.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample)]
    [Examples("receipt", "receipt 2022", "rcpt week")]
    [Alias("rcpt", "receiptify", "reciept")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task ReceiptAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
            userSettings.RegisteredLastFm ??= await this._indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var timeSettings = SettingService.GetTimePeriod(extraOptions, registeredLastFm: userSettings.RegisteredLastFm);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            if (timeSettings.DefaultPicked)
            {
                var monthName = DateTime.UtcNow.AddDays(-24).ToString("MMM", CultureInfo.InvariantCulture);
                timeSettings = SettingService.GetTimePeriod(monthName, registeredLastFm: userSettings.RegisteredLastFm);
            }

            var response = await this._trackBuilders.GetReceipt(new ContextModel(this.Context, prfx, contextUser), userSettings, timeSettings);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
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

            var usersWithTrack = await this._whoKnowsTrackService.GetIndexedUsersForTrack(this.Context, guild.GuildId, track.ArtistName, track.TrackName);

            var discordGuildUser = await this.Context.Guild.GetUserAsync(this.Context.User.Id);
            var currentUser = await this._indexService.GetOrAddUserToGuild(usersWithTrack, guild, discordGuildUser, contextUser);
            await this._indexService.UpdateGuildUser(await this.Context.Guild.GetUserAsync(contextUser.DiscordUserId), currentUser.UserId, guild);
            
            usersWithTrack = await WhoKnowsService.AddOrReplaceUserToIndexList(usersWithTrack, contextUser, trackName, this.Context.Guild, track.UserPlaycount);

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
            await this.Context.HandleCommandException(e);
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

        var guildTask = this._guildService.GetGuildWithGuildUsers(this.Context.Guild?.Id);
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

        var spotifyTrack = await this._spotifyService.GetOrStoreTrackAsync(track);

        var trackName = $"{track.TrackName} by {track.ArtistName}";

        try
        {
            var usersWithTrack = await this._whoKnowsTrackService.GetGlobalUsersForTrack(this.Context, track.ArtistName, track.TrackName);

            usersWithTrack = await WhoKnowsService.AddOrReplaceUserToIndexList(usersWithTrack, contextUser, trackName, this.Context.Guild, track.UserPlaycount);

            var privacyLevel = PrivacyLevel.Global;

            var filteredUsersWithTrack = await this._whoKnowsService.FilterGlobalUsersAsync(usersWithTrack);

            if (this.Context.Guild != null)
            {
                var guild = await guildTask;

                filteredUsersWithTrack =
                    WhoKnowsService.ShowGuildMembersInGlobalWhoKnowsAsync(filteredUsersWithTrack, guild.GuildUsers.ToList());

                if (settings.AdminView && guild.SpecialGuild == true)
                {
                    privacyLevel = PrivacyLevel.Server;
                }
            }

            var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithTrack, contextUser.UserId, privacyLevel, hidePrivateUsers: settings.HidePrivateUsers);
            if (!filteredUsersWithTrack.Any())
            {
                serverUsers = "Nobody that uses .fmbot has listened to this track.";
            }

            this._embed.WithDescription(serverUsers);

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);
            var footer = $"Global WhoKnows track requested by {userTitle}";

            var duration = spotifyTrack?.DurationMs ?? track.Duration;

            if (duration is > 0)
            {
                var trackLength = TimeSpan.FromMilliseconds(duration.GetValueOrDefault());
                if (trackLength < TimeSpan.FromSeconds(60) &&
                    track.UserPlaycount > 2500)
                {
                    this._embed.AddField("Heads up",
                        "We regularly remove people who spam short songs to raise their playcounts from Global WhoKnows. " +
                        "Consider not spamming scrobbles and/or removing your scrobbles on this track if you don't want to be removed.");
                }
            }

            if (filteredUsersWithTrack.Any() && filteredUsersWithTrack.Count() > 1)
            {
                var serverListeners = filteredUsersWithTrack.Count();
                var serverPlaycount = filteredUsersWithTrack.Sum(a => a.Playcount);
                var avgServerPlaycount = filteredUsersWithTrack.Average(a => a.Playcount);

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
                await this.Context.HandleCommandException(e, sendReply: false);
                await ReplyAsync("Error while replying: The bot is missing permissions.\n" +
                                 "Make sure it has permission to 'Embed links' and 'Attach Images'");
            }
            else
            {
                await this.Context.HandleCommandException(e);
            }
        }
    }

    [Command("friendwhoknowstrack", RunMode = RunMode.Async)]
    [Discord.Commands.Summary("Shows who of your friends listen to an track in .fmbot")]
    [Examples("fwt", "fwkt The Beatles Yesterday", "friendwhoknowstrack", "friendwhoknowstrack Hothouse Flowers Don't Go", "friendwhoknowstrack Mall Grab | Sunflower")]
    [Alias("fwt", "fwkt", "fwktr", "fwtrack", "friendwhoknows track", "friends whoknows track", "friend whoknows track")]
    [UsernameSetRequired]
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

            var guild = await this._guildService.GetGuildAsync(this.Context.Guild?.Id);

            var track = await this.SearchTrack(albumValues, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm, useCachedTracks: true, userId: contextUser.UserId);
            if (track == null)
            {
                return;
            }

            var trackName = $"{track.TrackName} by {track.ArtistName}";

            var usersWithTrack = await this._whoKnowsTrackService.GetFriendUsersForTrack(this.Context, guild?.GuildId ?? 0, contextUser.UserId, track.ArtistName, track.TrackName);

            usersWithTrack = await WhoKnowsService.AddOrReplaceUserToIndexList(usersWithTrack, contextUser, trackName, this.Context.Guild, track.UserPlaycount);

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
                await this.Context.HandleCommandException(e, sendReply: false);
                await ReplyAsync("Error while replying: The bot is missing permissions.\n" +
                                 "Make sure it has permission to 'Embed links' and 'Attach Images'");
            }
            else
            {
                await this.Context.HandleCommandException(e);
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

        if (timeSettings.UsePlays || timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
        {
            guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
        }

        try
        {
            var response =
                await this._trackBuilders.GuildTracksAsync(new ContextModel(this.Context, prfx), guild, guildListSettings);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
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
                    trackInfo = await this._trackService.GetCachedTrack(trackArtist, trackName, lastFmUserName, userId);
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
                trackInfo = await this._trackService.GetCachedTrack(lastPlayedTrack.ArtistName, lastPlayedTrack.TrackName, lastFmUserName, userId);
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
                trackInfo = await this._trackService.GetCachedTrack(result.Content.ArtistName, result.Content.TrackName, lastFmUserName, userId);
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
}
