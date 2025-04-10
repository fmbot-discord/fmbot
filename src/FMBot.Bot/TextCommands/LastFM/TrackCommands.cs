using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.Commands;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.LastFM.Repositories;
using Microsoft.Extensions.Options;
using TimePeriod = FMBot.Domain.Models.TimePeriod;

namespace FMBot.Bot.TextCommands.LastFM;

[Name("Tracks")]
public class TrackCommands : BaseCommandModule
{
    private readonly GuildService _guildService;
    private readonly IndexService _indexService;
    private readonly IPrefixService _prefixService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly SettingService _settingService;
    private readonly UserService _userService;
    private readonly TrackService _trackService;
    private readonly TrackBuilders _trackBuilders;
    private readonly EurovisionBuilders _eurovisionBuilders;
    private readonly PlayBuilder _playBuilders;
    private readonly CountryService _countryService;

    private InteractiveService Interactivity { get; }


    public TrackCommands(
        GuildService guildService,
        IndexService indexService,
        IPrefixService prefixService,
        IDataSourceFactory dataSourceFactory,
        SettingService settingService,
        UserService userService,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings,
        TrackService trackService,
        TrackBuilders trackBuilders,
        EurovisionBuilders eurovisionBuilders,
        PlayBuilder playBuilders,
        CountryService countryService) : base(botSettings)
    {
        this._guildService = guildService;
        this._indexService = indexService;
        this._dataSourceFactory = dataSourceFactory;
        this._prefixService = prefixService;
        this._settingService = settingService;
        this._userService = userService;
        this.Interactivity = interactivity;
        this._trackService = trackService;
        this._trackBuilders = trackBuilders;
        this._eurovisionBuilders = eurovisionBuilders;
        this._playBuilders = playBuilders;
        this._countryService = countryService;
    }

    [Command("track", RunMode = RunMode.Async)]
    [Summary("Track you're currently listening to or searching for.")]
    [Examples(
        "tr",
        "track",
        "track Depeche Mode Enjoy The Silence",
        "track Crystal Waters | Gypsy Woman (She's Homeless) - Radio Edit")]
    [Alias("tr", "ti", "ts", "trackinfo")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks)]
    [SupporterEnhanced("Supporters can see the date they first discovered a track")]
    public async Task TrackAsync([Remainder] string trackValues = null)
    {
        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var response =
                await this._trackBuilders.TrackAsync(new ContextModel(this.Context, prfx, contextUser), trackValues);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("trackplays", RunMode = RunMode.Async)]
    [Summary("Shows playcount for current track or the one you're searching for.\n\n" +
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

        var response = await this._trackBuilders.TrackPlays(new ContextModel(this.Context, prfx, contextUser),
            userSettings, userSettings.NewSearchValue);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("trackdetails", RunMode = RunMode.Async)]
    [Summary("Shows metadata for current track or the one you're searching for.")]
    [Examples(
        "tp",
        "trackdetails",
        "td Mac DeMarco Here Comes The Cowboy")]
    [Alias("td", "trackdata", "trackmetadata", "tds")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task TrackDetailsAsync([Remainder] string trackValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var response =
            await this._trackBuilders.TrackDetails(new ContextModel(this.Context, prfx, contextUser), trackValues);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("love", RunMode = RunMode.Async)]
    [Summary("Loves a track on Last.fm")]
    [Examples("love", "l", "love Tame Impala Borderline")]
    [Alias("l", "heart", "favorite", "affection", "appreciation", "lust", "fuckyeah", "fukk", "unfuck")]
    [UserSessionRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task LoveAsync([Remainder] string trackValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var response =
            await this._trackBuilders.LoveTrackAsync(new ContextModel(this.Context, prfx, contextUser), trackValues);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("unlove", RunMode = RunMode.Async)]
    [Summary("Removes the track you're currently listening to or searching for from your last.fm loved tracks.")]
    [Examples("unlove", "ul", "unlove Lou Reed Brandenburg Gate")]
    [Alias("ul", "unheart", "hate", "fuck")]
    [UserSessionRequired]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task UnLoveAsync([Remainder] string trackValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var response =
            await this._trackBuilders.UnLoveTrackAsync(new ContextModel(this.Context, prfx, contextUser), trackValues);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("loved", RunMode = RunMode.Async)]
    [Summary("Shows your Last.fm loved tracks.")]
    [Examples("loved", "lt", "lovedtracks lfm:fm-bot", "lovedtracks @user")]
    [Alias("lovedtracks", "lt", "unfucked")]
    [UserSessionRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task LovedAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);

        try
        {
            var response = await this._trackBuilders.LovedTracksAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("scrobble", RunMode = RunMode.Async)]
    [Summary("Scrobbles a track on Last.fm.")]
    [Examples("scrobble", "sb the less i know the better", "scrobble Loona Heart Attack",
        "scrobble Mac DeMarco | Chamber of Reflection")]
    [UserSessionRequired]
    [Alias("sb")]
    [CommandCategories(CommandCategory.Tracks)]
    public async Task ScrobbleAsync([Remainder] string trackValues = null)
    {
        var contextUser = await this._userService.GetUserWithDiscogs(this.Context.User.Id);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        _ = this.Context.Channel.TriggerTypingAsync();

        var response = await this._trackBuilders.ScrobbleAsync(new ContextModel(this.Context, prfx, contextUser),
            trackValues);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("toptracks", RunMode = RunMode.Async)]
    [Summary("Shows your or someone else's top tracks over a certain time period.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample,
        Constants.BillboardExample, Constants.EmbedSizeExample)]
    [Examples("tt", "toptracks", "tt y", "toptracks weekly @user", "tt bb xl")]
    [Alias("tt", "tl", "tracklist", "tracks", "trackslist", "top tracks", "top track", "ttracks")]
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
            var timeSettings = SettingService.GetTimePeriod(extraOptions,
                registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var mode = SettingService.SetMode(extraOptions, contextUser.Mode);

            var response = await this._trackBuilders.TopTracksAsync(new ContextModel(this.Context, prfx, contextUser),
                topListSettings, timeSettings, userSettings, mode.mode);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("receipt", RunMode = RunMode.Async)]
    [Summary("Shows your track receipt. Based on Receiptify.")]
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
            var timeSettings = SettingService.GetTimePeriod(extraOptions,
                registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            if (timeSettings.DefaultPicked)
            {
                var monthName = DateTime.UtcNow.AddDays(-24).ToString("MMM", CultureInfo.InvariantCulture);
                timeSettings = SettingService.GetTimePeriod(monthName, registeredLastFm: userSettings.RegisteredLastFm,
                    timeZone: userSettings.TimeZone);
            }

            var response = await this._trackBuilders.GetReceipt(new ContextModel(this.Context, prfx, contextUser),
                userSettings, timeSettings);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("whoknowstrack", RunMode = RunMode.Async)]
    [Summary("Shows what other users listen to a track in your server")]
    [Examples("wt", "whoknowstrack", "whoknowstrack Hothouse Flowers Don't Go",
        "whoknowstrack Natasha Bedingfield | Unwritten")]
    [Alias("wt", "wkt", "wktr", "wtr", "wktrack", "wk track", "whoknows track")]
    [UsernameSetRequired]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Tracks, CommandCategory.WhoKnows)]
    public async Task WhoKnowsTrackAsync([Remainder] string trackValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var currentSettings = new WhoKnowsSettings
            {
                ResponseMode = contextUser.Mode ?? ResponseMode.Embed,
                NewSearchValue = trackValues,
                DisplayRoleFilter = false
            };

            var settings = this._settingService.SetWhoKnowsSettings(currentSettings, trackValues, contextUser.UserType);

            var response = await this._trackBuilders.WhoKnowsTrackAsync(
                new ContextModel(this.Context, prfx, contextUser), settings.ResponseMode, settings.NewSearchValue,
                settings.DisplayRoleFilter);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("globalwhoknowstrack", RunMode = RunMode.Async)]
    [Summary("Shows what other users listen to a track in .fmbot")]
    [Examples("gwt", "globalwhoknowstrack", "globalwhoknowstrack Hothouse Flowers Don't Go",
        "globalwhoknowstrack Natasha Bedingfield | Unwritten")]
    [Alias("gwt", "gwkt", "gwtr", "gwktr", "globalwkt", "globalwktrack", "globalwhoknows track")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Tracks, CommandCategory.WhoKnows)]
    public async Task GlobalWhoKnowsTrackAsync([Remainder] string trackValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var currentSettings = new WhoKnowsSettings
        {
            ResponseMode = contextUser.Mode ?? ResponseMode.Embed,
            HidePrivateUsers = false,
            ShowBotters = false,
            AdminView = false,
            NewSearchValue = trackValues
        };
        var settings = this._settingService.SetWhoKnowsSettings(currentSettings, trackValues, contextUser.UserType);

        try
        {
            var response = await this._trackBuilders.GlobalWhoKnowsTrackAsync(
                new ContextModel(this.Context, prfx, contextUser), settings, settings.NewSearchValue);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) &&
                e.Message.Contains("The server responded with error 50013: Missing Permissions"))
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
    [Summary("Shows who of your friends listen to an track in .fmbot")]
    [Examples("fwt", "fwkt The Beatles Yesterday", "friendwhoknowstrack",
        "friendwhoknowstrack Hothouse Flowers Don't Go", "friendwhoknowstrack Mall Grab | Sunflower")]
    [Alias("fwt", "fwkt", "fwktr", "fwtrack", "friendwhoknows track", "friends whoknows track",
        "friend whoknows track")]
    [UsernameSetRequired]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Tracks, CommandCategory.WhoKnows, CommandCategory.Friends)]
    public async Task FriendWhoKnowsTrackAsync([Remainder] string trackValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserWithFriendsAsync(this.Context.User);

        var currentSettings = new WhoKnowsSettings
        {
            ResponseMode = contextUser.Mode ?? ResponseMode.Embed,
            HidePrivateUsers = false,
            NewSearchValue = trackValues
        };
        var settings = this._settingService.SetWhoKnowsSettings(currentSettings, trackValues, contextUser.UserType);

        try
        {
            var response = await this._trackBuilders.FriendsWhoKnowTrackAsync(
                new ContextModel(this.Context, prfx, contextUser), settings.ResponseMode, settings.NewSearchValue);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) &&
                e.Message.Contains("The server responded with error 50013: Missing Permissions"))
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
    [Summary("Top tracks for your server, optionally for an artist")]
    [Options("Time periods: `weekly`, `monthly` and `alltime`", "Order options: `plays` and `listeners`",
        "Artist name")]
    [Examples("st", "st a p", "servertracks", "servertracks alltime", "servertracks listeners weekly",
        "servertracks the beatles listeners", "servertracks the beatles alltime")]
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
        var timeSettings = SettingService.GetTimePeriod(guildListSettings.NewSearchValue,
            guildListSettings.ChartTimePeriod, cachedOrAllTimeOnly: true);

        if (timeSettings.UsePlays ||
            timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
        {
            guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
        }

        try
        {
            var response =
                await this._trackBuilders.GuildTracksAsync(new ContextModel(this.Context, prfx), guild,
                    guildListSettings);

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
                    if (trackInfo.Success && trackInfo.Content.TrackUrl == null)
                    {
                        trackInfo = await this._dataSourceFactory.GetTrackInfoAsync(trackName, trackArtist,
                            lastFmUserName);
                    }
                }
                else
                {
                    trackInfo = await this._dataSourceFactory.GetTrackInfoAsync(trackName, trackArtist,
                        lastFmUserName);
                }

                if (!trackInfo.Success && trackInfo.Error == ResponseStatus.MissingParameters)
                {
                    this._embed.WithDescription(
                        $"Track `{trackName}` by `{trackArtist}` could not be found, please check your search values and try again.");
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return null;
                }

                if (!trackInfo.Success || trackInfo.Content == null)
                {
                    this._embed.ErrorResponse(trackInfo.Error, trackInfo.Message, this.Context.Message.Content,
                        this.Context.User, "track");
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.LastFmError);
                    return null;
                }

                return trackInfo.Content;
            }
        }
        else
        {
            var recentScrobbles =
                await this._dataSourceFactory.GetRecentTracksAsync(lastFmUserName, 1, useCache: true,
                    sessionKey: sessionKey);

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
                trackInfo = await this._trackService.GetCachedTrack(lastPlayedTrack.ArtistName,
                    lastPlayedTrack.TrackName, lastFmUserName, userId);
                if (trackInfo.Success && trackInfo.Content.TrackUrl == null)
                {
                    trackInfo = await this._dataSourceFactory.GetTrackInfoAsync(lastPlayedTrack.TrackName,
                        lastPlayedTrack.ArtistName,
                        lastFmUserName);
                }
            }
            else
            {
                trackInfo = await this._dataSourceFactory.GetTrackInfoAsync(lastPlayedTrack.TrackName,
                    lastPlayedTrack.ArtistName,
                    lastFmUserName);
            }

            if (trackInfo?.Content == null)
            {
                this._embed.WithDescription(
                    $"Last.fm did not return a result for **{lastPlayedTrack.TrackName}** by **{lastPlayedTrack.ArtistName}**.\n\n" +
                    $"This usually happens on recently released tracks. Please try again later.");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return null;
            }

            return trackInfo.Content;
        }

        var result = await this._dataSourceFactory.SearchTrackAsync(searchValue);
        if (result.Success && result.Content != null)
        {
            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            Response<TrackInfo> trackInfo;
            if (useCachedTracks)
            {
                trackInfo = await this._trackService.GetCachedTrack(result.Content.ArtistName, result.Content.TrackName,
                    lastFmUserName, userId);
            }
            else
            {
                trackInfo = await this._dataSourceFactory.GetTrackInfoAsync(result.Content.TrackName,
                    result.Content.ArtistName,
                    lastFmUserName);
            }

            if (trackInfo.Content == null || !trackInfo.Success)
            {
                this._embed.WithDescription(
                    $"Last.fm did not return a result for **{result.Content.TrackName}** by **{result.Content.ArtistName}**.\n" +
                    $"This usually happens on recently released tracks. Please try again later.");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return null;
            }

            return trackInfo.Content;
        }

        if (result.Success)
        {
            this._embed.WithDescription("Track could not be found, please check your search values and try again.");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return null;
        }

        this._embed.WithDescription($"Last.fm returned an error: {result.Error}");
        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        this.Context.LogCommandUsed(CommandResponse.LastFmError);
        return null;
    }

    [Command("eurovision", RunMode = RunMode.Async)]
    [Alias("ev", "esc", "eurovisie", "eurovisionsongcontest", "songcontest")]
    public async Task EurovisionAsync([Remainder] string extraOptions = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var context = new ContextModel(this.Context, prfx, contextUser);

        try
        {
            CountryInfo pickedCountry = null;
            if (extraOptions != null)
            {
                var splitOptions = extraOptions.Split(" ");
                foreach (var option in splitOptions)
                {
                    pickedCountry = this._countryService.GetValidCountry(option);
                    if (pickedCountry != null)
                    {
                        break;
                    }
                }
            }

            var year = SettingService.GetYear(extraOptions);

            ResponseModel response;
            if (pickedCountry != null)
            {
                response = await this._eurovisionBuilders.GetEurovisionCountryYear(context, pickedCountry, year ?? DateTime.UtcNow.Year);
            }
            else
            {
                response =
                    await this._eurovisionBuilders.GetEurovisionYear(context, year ?? DateTime.UtcNow.Year);
            }


            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("trackgaps", RunMode = RunMode.Async)]
    [Summary("Shows the tracks you've returned to after a gap in listening.")]
    [Options(Constants.UserMentionExample, Constants.EmbedSizeExample)]
    [Examples("tgaps", "trackgaps", "trackgaps @user")]
    [Alias("tgaps", "tgap", "trackgap", "songgaps", "songgap")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Tracks)]
    [SupporterExclusive(
        "To see which tracks you've re-discovered we need to store your lifetime Last.fm history. Your lifetime history and more are only available for supporters")]
    public async Task TrackGapsAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var context = new ContextModel(this.Context, prfx, contextUser);
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);

            var supporterRequiredResponse = PlayBuilder.GapsSupporterRequired(context, userSettings);

            if (supporterRequiredResponse != null)
            {
                await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse);
                this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
                return;
            }

            var topListSettings = SettingService.SetTopListSettings(extraOptions);
            userSettings.RegisteredLastFm ??= await this._indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var mode = SettingService.SetMode(userSettings.NewSearchValue, contextUser.Mode);

            var response = await this._playBuilders.ListeningGapsAsync(context, topListSettings, userSettings,
                mode.mode, GapEntityType.Track);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("lyrics", RunMode = RunMode.Async)]
    [Summary("Lyrics for a track you're currently listening to or searching for")]
    [Examples(
        "lyrics",
        "l",
        "lyrics The Beatles Let It Be",
        "lyrics Daft Punk | Get Lucky")]
    [Alias("lyric", "lyr", "lr", "lyricsfind", "lyricsearch", "lyricssearch")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Tracks)]
    [SupporterExclusive("Viewing track lyrics in .fmbot is only available for .fmbot supporters")]
    public async Task LyricsAsync([Remainder] string trackValues = null)
    {
        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var context = new ContextModel(this.Context, prfx, contextUser);

            var supporterRequiredResponse = TrackBuilders.LyricsSupporterRequired(context);
            if (supporterRequiredResponse != null)
            {
                await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse);
                this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
                return;
            }

            var response = await this._trackBuilders.TrackLyricsAsync(context, trackValues);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
