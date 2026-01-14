using System;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using NetCord.Services.Commands;
using TimePeriod = FMBot.Domain.Models.TimePeriod;

namespace FMBot.Bot.TextCommands;

// Note: These space command modules handle space-separated command aliases
// (e.g., ".top artists", ".wk track") since NetCord doesn't support them natively.
// Each command includes the original alias and usage count for reference.

[Command("top")]
public class TopSpaceCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly IndexService _indexService;
    private readonly ArtistBuilders _artistBuilders;
    private readonly AlbumBuilders _albumBuilders;
    private readonly TrackBuilders _trackBuilders;
    private readonly GenreBuilders _genreBuilders;
    private readonly CountryBuilders _countryBuilders;
    private readonly DiscogsBuilder _discogsBuilders;
    private InteractiveService Interactivity { get; }

    public TopSpaceCommands(
        IPrefixService prefixService,
        UserService userService,
        SettingService settingService,
        IndexService indexService,
        ArtistBuilders artistBuilders,
        AlbumBuilders albumBuilders,
        TrackBuilders trackBuilders,
        GenreBuilders genreBuilders,
        CountryBuilders countryBuilders,
        DiscogsBuilder discogsBuilders,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        _prefixService = prefixService;
        _userService = userService;
        _settingService = settingService;
        _indexService = indexService;
        _artistBuilders = artistBuilders;
        _albumBuilders = albumBuilders;
        _trackBuilders = trackBuilders;
        _genreBuilders = genreBuilders;
        _countryBuilders = countryBuilders;
        _discogsBuilders = discogsBuilders;
        Interactivity = interactivity;
    }

    // Alias: ".top artists" (12680) -> Original: topartists
    [Command("artists")]
    public async Task ArtistsAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = Context.Channel?.TriggerTypingStateAsync()!;
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);

        try
        {
            var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
            var userSettings = await _settingService.GetUser(extraOptions, contextUser, Context);
            var topListSettings = SettingService.SetTopListSettings(userSettings.NewSearchValue);
            userSettings.RegisteredLastFm ??= await _indexService.AddUserRegisteredLfmDate(userSettings.UserId);

            var timeSettings = SettingService.GetTimePeriod(topListSettings.NewSearchValue,
                topListSettings.Discogs ? TimePeriod.AllTime : TimePeriod.Weekly,
                registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone);
            var mode = SettingService.SetMode(extraOptions, contextUser.Mode);

            var response = topListSettings.Discogs
                ? await _discogsBuilders.DiscogsTopArtistsAsync(new ContextModel(Context, prfx, contextUser),
                    topListSettings, timeSettings, userSettings)
                : await _artistBuilders.TopArtistsAsync(new ContextModel(Context, prfx, contextUser),
                    topListSettings, timeSettings, userSettings, mode.mode);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }

    // Alias: ".top albums" (8506) -> Original: topalbums
    [Command("albums")]
    public async Task AlbumsAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = Context.Channel?.TriggerTypingStateAsync()!;
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);

        try
        {
            var userSettings = await _settingService.GetUser(extraOptions, contextUser, Context);
            var topListSettings = SettingService.SetTopListSettings(extraOptions);
            userSettings.RegisteredLastFm ??= await _indexService.AddUserRegisteredLfmDate(userSettings.UserId);

            var timeSettings = SettingService.GetTimePeriod(topListSettings.NewSearchValue,
                topListSettings.ReleaseYearFilter.HasValue || topListSettings.ReleaseDecadeFilter.HasValue
                    ? TimePeriod.AllTime
                    : TimePeriod.Weekly,
                registeredLastFm: userSettings.RegisteredLastFm,
                timeZone: userSettings.TimeZone);
            var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
            var mode = SettingService.SetMode(timeSettings.NewSearchValue, contextUser.Mode);

            var response = await _albumBuilders.TopAlbumsAsync(new ContextModel(Context, prfx, contextUser),
                topListSettings, timeSettings, userSettings, mode.mode);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }

    // Alias: ".top tracks" (10024) -> Original: toptracks
    [Command("tracks")]
    public async Task TracksAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        _ = Context.Channel?.TriggerTypingStateAsync()!;
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);

        try
        {
            var userSettings = await _settingService.GetUser(extraOptions, contextUser, Context);
            var topListSettings = SettingService.SetTopListSettings(extraOptions);
            userSettings.RegisteredLastFm ??= await _indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var timeSettings = SettingService.GetTimePeriod(extraOptions,
                registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone);
            var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
            var mode = SettingService.SetMode(extraOptions, contextUser.Mode);

            var response = await _trackBuilders.TopTracksAsync(new ContextModel(Context, prfx, contextUser),
                topListSettings, timeSettings, userSettings, mode.mode);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }

    // Alias: ".top track" (751) -> Original: toptracks
    [Command("track")]
    public Task TrackAsync([CommandParameter(Remainder = true)] string extraOptions = null)
        => TracksAsync(extraOptions);

    // Alias: ".top genres" (4132) -> Original: topgenres
    [Command("genres")]
    public async Task GenresAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);
        _ = Context.Channel?.TriggerTypingStateAsync()!;

        try
        {
            var userSettings = await _settingService.GetUser(extraOptions, contextUser, Context);
            var topListSettings = SettingService.SetTopListSettings(extraOptions);
            userSettings.RegisteredLastFm ??= await _indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var timeSettings = SettingService.GetTimePeriod(extraOptions, registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone);
            var mode = SettingService.SetMode(extraOptions, contextUser.Mode);

            var response = await _genreBuilders.TopGenresAsync(new ContextModel(Context, prfx, contextUser),
                userSettings, timeSettings, topListSettings, mode.mode);
            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }

    // Alias: ".top countries" (662) -> Original: topcountries
    [Command("countries")]
    public async Task CountriesAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);
        _ = Context.Channel?.TriggerTypingStateAsync()!;

        try
        {
            var userSettings = await _settingService.GetUser(extraOptions, contextUser, Context);
            var topListSettings = SettingService.SetTopListSettings(extraOptions);
            userSettings.RegisteredLastFm ??= await _indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var timeSettings = SettingService.GetTimePeriod(extraOptions, registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone);
            var mode = SettingService.SetMode(extraOptions, contextUser.Mode);

            var response = await _countryBuilders.TopCountriesAsync(new ContextModel(Context, prfx, contextUser),
                userSettings, timeSettings, topListSettings, mode.mode);
            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }
}

[Command("wk")]
public class WkSpaceCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly UserService _userService;
    private readonly TrackBuilders _trackBuilders;
    private readonly AlbumBuilders _albumBuilders;
    private InteractiveService Interactivity { get; }

    public WkSpaceCommands(
        IPrefixService prefixService,
        UserService userService,
        TrackBuilders trackBuilders,
        AlbumBuilders albumBuilders,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        _prefixService = prefixService;
        _userService = userService;
        _trackBuilders = trackBuilders;
        _albumBuilders = albumBuilders;
        Interactivity = interactivity;
    }

    // Alias: ".wk track" (4855) -> Original: whoknowstrack
    [Command("track")]
    public async Task TrackAsync([CommandParameter(Remainder = true)] string trackValues = null)
    {
        _ = Context.Channel?.TriggerTypingStateAsync()!;
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);

        try
        {
            var currentSettings = new WhoKnowsSettings
            {
                ResponseMode = contextUser.Mode ?? ResponseMode.Embed,
                NewSearchValue = trackValues,
                DisplayRoleFilter = false
            };
            var settings = SettingService.SetWhoKnowsSettings(currentSettings, trackValues, contextUser.UserType);

            var response = await _trackBuilders.WhoKnowsTrackAsync(
                new ContextModel(Context, prfx, contextUser), settings.ResponseMode, settings.NewSearchValue,
                settings.DisplayRoleFilter);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }

    // Alias: ".wk album" (2840) -> Original: whoknowsalbum
    [Command("album")]
    public async Task AlbumAsync([CommandParameter(Remainder = true)] string albumValues = null)
    {
        _ = Context.Channel?.TriggerTypingStateAsync()!;
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);

        try
        {
            var currentSettings = new WhoKnowsSettings
            {
                ResponseMode = contextUser.Mode ?? ResponseMode.Embed,
                NewSearchValue = albumValues,
                DisplayRoleFilter = false
            };
            var settings = SettingService.SetWhoKnowsSettings(currentSettings, albumValues, contextUser.UserType);

            var response = await _albumBuilders.WhoKnowsAlbumAsync(
                new ContextModel(Context, prfx, contextUser), settings.ResponseMode, settings.NewSearchValue,
                settings.DisplayRoleFilter);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }
}

[Command("c")]
public class CSpaceCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly ChartService _chartService;
    private readonly ChartBuilders _chartBuilders;
    private InteractiveService Interactivity { get; }

    public CSpaceCommands(
        IPrefixService prefixService,
        UserService userService,
        SettingService settingService,
        ChartService chartService,
        ChartBuilders chartBuilders,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        _prefixService = prefixService;
        _userService = userService;
        _settingService = settingService;
        _chartService = chartService;
        _chartBuilders = chartBuilders;
        Interactivity = interactivity;
    }

    // Alias: ".c artist" (2083) -> Original: artistchart
    [Command("artist")]
    public Task ArtistAsync([CommandParameter(Remainder = true)] string otherSettings = null)
        => ArtistsAsync(otherSettings);

    // Alias: ".c artists" (3492) -> Original: artistchart
    [Command("artists")]
    public async Task ArtistsAsync([CommandParameter(Remainder = true)] string otherSettings = null)
    {
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
        var user = await _userService.GetUserSettingsAsync(Context.User);
        var chartCount = await _userService.GetCommandExecutedAmount(user.UserId, "artistchart", DateTime.UtcNow.AddSeconds(-45));
        if (chartCount >= 3)
        {
            await Context.Channel.SendMessageAsync(new MessageProperties { Content = "Please wait a minute before generating charts again." });
            Context.LogCommandUsed(CommandResponse.Cooldown);
            return;
        }

        var userSettings = await _settingService.GetUser(otherSettings, user, Context);

        if (Context.Guild != null)
        {
            var perms = await GuildService.GetGuildPermissionsAsync(Context);
            if (!perms.HasFlag(Permissions.AttachFiles))
            {
                await Context.Channel.SendMessageAsync(new MessageProperties { Content = "I'm missing the 'Attach files' permission in this server, so I can't post a chart." });
                Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }
        }

        try
        {
            _ = Context.Channel?.TriggerTypingStateAsync()!;
            var chartSettings = new ChartSettings(Context.User) { ArtistChart = true };
            chartSettings = await _chartService.SetSettings(chartSettings, userSettings);

            var response = await _chartBuilders.ArtistChartAsync(new ContextModel(Context, prfx, user), userSettings,
                chartSettings);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }
}

[Command("chart")]
public class ChartSpaceCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly ChartService _chartService;
    private readonly ChartBuilders _chartBuilders;
    private InteractiveService Interactivity { get; }

    public ChartSpaceCommands(
        IPrefixService prefixService,
        UserService userService,
        SettingService settingService,
        ChartService chartService,
        ChartBuilders chartBuilders,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        _prefixService = prefixService;
        _userService = userService;
        _settingService = settingService;
        _chartService = chartService;
        _chartBuilders = chartBuilders;
        Interactivity = interactivity;
    }

    // Alias: ".chart artist" (1042) -> Original: artistchart
    [Command("artist")]
    public Task ArtistAsync([CommandParameter(Remainder = true)] string otherSettings = null)
        => ArtistsAsync(otherSettings);

    // Alias: ".chart artists" (1917) -> Original: artistchart
    [Command("artists")]
    public async Task ArtistsAsync([CommandParameter(Remainder = true)] string otherSettings = null)
    {
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
        var user = await _userService.GetUserSettingsAsync(Context.User);
        var chartCount = await _userService.GetCommandExecutedAmount(user.UserId, "artistchart", DateTime.UtcNow.AddSeconds(-45));
        if (chartCount >= 3)
        {
            await Context.Channel.SendMessageAsync(new MessageProperties { Content = "Please wait a minute before generating charts again." });
            Context.LogCommandUsed(CommandResponse.Cooldown);
            return;
        }

        var userSettings = await _settingService.GetUser(otherSettings, user, Context);

        if (Context.Guild != null)
        {
            var perms = await GuildService.GetGuildPermissionsAsync(Context);
            if (!perms.HasFlag(Permissions.AttachFiles))
            {
                await Context.Channel.SendMessageAsync(new MessageProperties { Content = "I'm missing the 'Attach files' permission in this server, so I can't post a chart." });
                Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }
        }

        try
        {
            _ = Context.Channel?.TriggerTypingStateAsync()!;
            var chartSettings = new ChartSettings(Context.User) { ArtistChart = true };
            chartSettings = await _chartService.SetSettings(chartSettings, userSettings);

            var response = await _chartBuilders.ArtistChartAsync(new ContextModel(Context, prfx, user), userSettings,
                chartSettings);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }
}

[Command("album")]
public class AlbumSpaceCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly AlbumBuilders _albumBuilders;
    private InteractiveService Interactivity { get; }

    public AlbumSpaceCommands(
        IPrefixService prefixService,
        UserService userService,
        SettingService settingService,
        AlbumBuilders albumBuilders,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        _prefixService = prefixService;
        _userService = userService;
        _settingService = settingService;
        _albumBuilders = albumBuilders;
        Interactivity = interactivity;
    }

    // Alias: ".album plays" (1255) -> Original: albumplays
    [Command("plays")]
    public async Task PlaysAsync([CommandParameter(Remainder = true)] string albumValues = null)
    {
        _ = Context.Channel?.TriggerTypingStateAsync()!;
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);
        var userSettings = await _settingService.GetUser(albumValues, contextUser, Context);
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);

        var response = await _albumBuilders.AlbumPlaysAsync(new ContextModel(Context, prfx, contextUser),
            userSettings, userSettings.NewSearchValue);

        await Context.SendResponse(Interactivity, response);
        Context.LogCommandUsed(response.CommandResponse);
    }

    // Alias: ".album cover" (446) -> Original: cover
    [Command("cover")]
    public async Task CoverAsync([CommandParameter(Remainder = true)] string albumValues = null)
    {
        _ = Context.Channel?.TriggerTypingStateAsync()!;
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
        var userSettings = await _settingService.GetUser(albumValues, contextUser, Context);

        try
        {
            var response = await _albumBuilders.CoverAsync(new ContextModel(Context, prfx, contextUser),
                userSettings, albumValues);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }
}

[Command("artist")]
public class ArtistSpaceCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly ArtistBuilders _artistBuilders;
    private InteractiveService Interactivity { get; }

    public ArtistSpaceCommands(
        IPrefixService prefixService,
        UserService userService,
        SettingService settingService,
        ArtistBuilders artistBuilders,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        _prefixService = prefixService;
        _userService = userService;
        _settingService = settingService;
        _artistBuilders = artistBuilders;
        Interactivity = interactivity;
    }

    // Alias: ".artist plays" (734) -> Original: artistplays
    [Command("plays")]
    public async Task PlaysAsync([CommandParameter(Remainder = true)] string artistValues = null)
    {
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);
        _ = Context.Channel?.TriggerTypingStateAsync()!;

        var userSettings = await _settingService.GetUser(artistValues, contextUser, Context);
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
        var redirectsEnabled = SettingService.RedirectsEnabled(userSettings.NewSearchValue);

        var response = await _artistBuilders.ArtistPlaysAsync(new ContextModel(Context, prfx, contextUser),
            userSettings, redirectsEnabled.NewSearchValue, redirectsEnabled.Enabled);

        await Context.SendResponse(Interactivity, response);
        Context.LogCommandUsed(response.CommandResponse);
    }

    // Alias: ".artist tracks" (679) -> Original: artisttracks
    [Command("tracks")]
    public async Task TracksAsync([CommandParameter(Remainder = true)] string artistValues = null)
    {
        _ = Context.Channel?.TriggerTypingStateAsync()!;
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);
        var userSettings = await _settingService.GetUser(artistValues, contextUser, Context);
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);

        var redirectsEnabled = SettingService.RedirectsEnabled(userSettings.NewSearchValue);
        var timeSettings = SettingService.GetTimePeriod(redirectsEnabled.NewSearchValue, TimePeriod.AllTime,
            cachedOrAllTimeOnly: true, dailyTimePeriods: false);

        var response = await _artistBuilders.ArtistTracksAsync(new ContextModel(Context, prfx, contextUser),
            timeSettings, userSettings, redirectsEnabled.NewSearchValue, redirectsEnabled.Enabled);

        await Context.SendResponse(Interactivity, response);
        Context.LogCommandUsed(response.CommandResponse);
    }

    // Alias: ".artist track" (47) -> Original: artisttracks
    [Command("track")]
    public Task TrackAsync([CommandParameter(Remainder = true)] string artistValues = null)
        => TracksAsync(artistValues);

    // Alias: ".artist albums" (166) -> Original: artistalbums
    [Command("albums")]
    public async Task AlbumsAsync([CommandParameter(Remainder = true)] string artistValues = null)
    {
        _ = Context.Channel?.TriggerTypingStateAsync()!;
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);
        var userSettings = await _settingService.GetUser(artistValues, contextUser, Context);
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);

        var redirectsEnabled = SettingService.RedirectsEnabled(userSettings.NewSearchValue);

        var response = await _artistBuilders.ArtistAlbumsAsync(new ContextModel(Context, prfx, contextUser),
            userSettings, redirectsEnabled.NewSearchValue, redirectsEnabled.Enabled);

        await Context.SendResponse(Interactivity, response);
        Context.LogCommandUsed(response.CommandResponse);
    }

    // Alias: ".artist album" (17) -> Original: artistalbums
    [Command("album")]
    public Task AlbumAsync([CommandParameter(Remainder = true)] string artistValues = null)
        => AlbumsAsync(artistValues);

    // Alias: ".artist overview" (244) -> Original: artistoverview
    [Command("overview")]
    public async Task OverviewAsync([CommandParameter(Remainder = true)] string artistValues = null)
    {
        _ = Context.Channel?.TriggerTypingStateAsync()!;
        var contextUser = await _userService.GetUserWithDiscogs(Context.User.Id);
        var userSettings = await _settingService.GetUser(artistValues, contextUser, Context);
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
        var redirectsEnabled = SettingService.RedirectsEnabled(userSettings.NewSearchValue);

        try
        {
            var response = await _artistBuilders.ArtistOverviewAsync(
                new ContextModel(Context, prfx, contextUser),
                userSettings, redirectsEnabled.NewSearchValue, redirectsEnabled.Enabled);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }
}

[Command("track")]
public class TrackSpaceCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly TrackBuilders _trackBuilders;
    private InteractiveService Interactivity { get; }

    public TrackSpaceCommands(
        IPrefixService prefixService,
        UserService userService,
        SettingService settingService,
        TrackBuilders trackBuilders,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        _prefixService = prefixService;
        _userService = userService;
        _settingService = settingService;
        _trackBuilders = trackBuilders;
        Interactivity = interactivity;
    }

    // Alias: ".track plays" (467) -> Original: trackplays
    [Command("plays")]
    public async Task PlaysAsync([CommandParameter(Remainder = true)] string trackValues = null)
    {
        _ = Context.Channel?.TriggerTypingStateAsync()!;
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);
        var userSettings = await _settingService.GetUser(trackValues, contextUser, Context);
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);

        var response = await _trackBuilders.TrackPlays(new ContextModel(Context, prfx, contextUser),
            userSettings, userSettings.NewSearchValue);

        await Context.SendResponse(Interactivity, response);
        Context.LogCommandUsed(response.CommandResponse);
    }
}

[Command("server")]
public class ServerSpaceCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly GuildService _guildService;
    private readonly ArtistBuilders _artistBuilders;
    private readonly AlbumBuilders _albumBuilders;
    private readonly TrackBuilders _trackBuilders;
    private readonly GenreBuilders _genreBuilders;
    private InteractiveService Interactivity { get; }

    public ServerSpaceCommands(
        IPrefixService prefixService,
        GuildService guildService,
        ArtistBuilders artistBuilders,
        AlbumBuilders albumBuilders,
        TrackBuilders trackBuilders,
        GenreBuilders genreBuilders,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        _prefixService = prefixService;
        _guildService = guildService;
        _artistBuilders = artistBuilders;
        _albumBuilders = albumBuilders;
        _trackBuilders = trackBuilders;
        _genreBuilders = genreBuilders;
        Interactivity = interactivity;
    }

    // Alias: ".server artists" (647) -> Original: serverartists
    [Command("artists")]
    public async Task ArtistsAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
        var guild = await _guildService.GetGuildAsync(Context.Guild.Id);
        _ = Context.Channel?.TriggerTypingStateAsync()!;

        var guildListSettings = new GuildRankingSettings
        {
            ChartTimePeriod = TimePeriod.Weekly,
            TimeDescription = "weekly",
            OrderType = OrderType.Listeners,
            AmountOfDays = 7,
            NewSearchValue = extraOptions
        };

        guildListSettings = SettingService.SetGuildRankingSettings(guildListSettings, extraOptions);
        var timeSettings = SettingService.GetTimePeriod(extraOptions, guildListSettings.ChartTimePeriod, cachedOrAllTimeOnly: true);

        if (timeSettings.UsePlays || timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
        {
            guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
        }

        try
        {
            var response = await _artistBuilders.GuildArtistsAsync(new ContextModel(Context, prfx), guild, guildListSettings);
            _ = Interactivity.SendPaginatorAsync(
                response.StaticPaginator.Build(),
                Context.Channel,
                TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }

    // Alias: ".server albums" (291) -> Original: serveralbums
    [Command("albums")]
    public async Task AlbumsAsync([CommandParameter(Remainder = true)] string guildAlbumsOptions = null)
    {
        _ = Context.Channel?.TriggerTypingStateAsync()!;
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
        var guild = await _guildService.GetGuildAsync(Context.Guild.Id);

        var guildListSettings = new GuildRankingSettings
        {
            ChartTimePeriod = TimePeriod.Weekly,
            TimeDescription = "weekly",
            OrderType = OrderType.Listeners,
            AmountOfDays = 7,
            NewSearchValue = guildAlbumsOptions
        };

        try
        {
            guildListSettings = SettingService.SetGuildRankingSettings(guildListSettings, guildAlbumsOptions);
            var timeSettings = SettingService.GetTimePeriod(guildListSettings.NewSearchValue,
                guildListSettings.ChartTimePeriod, cachedOrAllTimeOnly: true);

            if (timeSettings.UsePlays || timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
            {
                guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
            }

            var response = await _albumBuilders.GuildAlbumsAsync(new ContextModel(Context, prfx), guild, guildListSettings);
            _ = Interactivity.SendPaginatorAsync(
                response.StaticPaginator.Build(),
                Context.Channel,
                TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }

    // Alias: ".server tracks" (350) -> Original: servertracks
    [Command("tracks")]
    public async Task TracksAsync([CommandParameter(Remainder = true)] string guildTracksOptions = null)
    {
        _ = Context.Channel?.TriggerTypingStateAsync()!;
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
        var guild = await _guildService.GetGuildAsync(Context.Guild.Id);

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

        if (timeSettings.UsePlays || timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
        {
            guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
        }

        try
        {
            var response = await _trackBuilders.GuildTracksAsync(new ContextModel(Context, prfx), guild, guildListSettings);
            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }

    // Alias: ".server genres" (180) -> Original: servergenres
    [Command("genres")]
    public async Task GenresAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
        var guild = await _guildService.GetGuildAsync(Context.Guild.Id);
        _ = Context.Channel?.TriggerTypingStateAsync()!;

        var guildListSettings = new GuildRankingSettings
        {
            ChartTimePeriod = TimePeriod.Weekly,
            TimeDescription = "weekly",
            OrderType = OrderType.Listeners,
            AmountOfDays = 7,
            NewSearchValue = extraOptions
        };

        guildListSettings = SettingService.SetGuildRankingSettings(guildListSettings, extraOptions);
        var timeSettings = SettingService.GetTimePeriod(extraOptions, guildListSettings.ChartTimePeriod, cachedOrAllTimeOnly: true);

        if (timeSettings.UsePlays || timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
        {
            guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
        }

        try
        {
            var response = await _genreBuilders.GetGuildGenres(new ContextModel(Context, prfx), guild, guildListSettings);
            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }

    // Alias: ".server genre" (51) -> Original: servergenres
    [Command("genre")]
    public Task GenreAsync([CommandParameter(Remainder = true)] string extraOptions = null)
        => GenresAsync(extraOptions);
}

[Command("fm")]
public class FmSpaceCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly UserService _userService;
    private readonly UserBuilder _userBuilder;
    private InteractiveService Interactivity { get; }

    public FmSpaceCommands(
        IPrefixService prefixService,
        UserService userService,
        UserBuilder userBuilder,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        _prefixService = prefixService;
        _userService = userService;
        _userBuilder = userBuilder;
        Interactivity = interactivity;
    }

    // Alias: ".fm set" (525) -> Original: fmset
    [Command("set")]
    public async Task SetAsync([CommandParameter(Remainder = true)] string _ = null)
    {
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
        var contextUser = await _userService.GetUserAsync(Context.User.Id);

        var response = UserBuilder.LoginRequired(prfx, contextUser != null);

        await Context.SendResponse(Interactivity, response);
        Context.LogCommandUsed(response.CommandResponse);
    }
}

[Command("crown")]
public class CrownSpaceCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly GuildBuilders _guildBuilders;
    private InteractiveService Interactivity { get; }

    public CrownSpaceCommands(
        IPrefixService prefixService,
        UserService userService,
        GuildService guildService,
        GuildBuilders guildBuilders,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        _prefixService = prefixService;
        _userService = userService;
        _guildService = guildService;
        _guildBuilders = guildBuilders;
        Interactivity = interactivity;
    }

    // Alias: ".crown leaderboard" (212) -> Original: crownleaderboard
    [Command("leaderboard")]
    public async Task LeaderboardAsync()
    {
        try
        {
            _ = Context.Channel?.TriggerTypingStateAsync()!;
            var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
            var guild = await _guildService.GetGuildForWhoKnows(Context.Guild?.Id);
            var contextUser = await _userService.GetUserSettingsAsync(Context.User);

            var response = await _guildBuilders.MemberOverviewAsync(new ContextModel(Context, prfx, contextUser),
                guild, GuildViewType.Crowns);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }

    // Alias: ".crown kill" (5) -> Original: crowns (redirect)
    [Command("kill")]
    public async Task KillAsync([CommandParameter(Remainder = true)] string _ = null)
    {
        await Context.Channel.SendMessageAsync(new MessageProperties { Content = "Please use the `/crowns` slash command or `.crowns` text command to manage crowns." });
        Context.LogCommandUsed(CommandResponse.Help);
    }
}

[Command("friends")]
public class FriendsSpaceCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly UserService _userService;
    private readonly FriendBuilders _friendBuilders;
    private InteractiveService Interactivity { get; }

    public FriendsSpaceCommands(
        IPrefixService prefixService,
        UserService userService,
        FriendBuilders friendBuilders,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        _prefixService = prefixService;
        _userService = userService;
        _friendBuilders = friendBuilders;
        Interactivity = interactivity;
    }

    // Alias: ".friends add" (95) -> Original: addfriends
    [Command("add")]
    public async Task AddAsync([CommandParameter(Remainder = true)] string enteredFriends = null)
    {
        if (string.IsNullOrWhiteSpace(enteredFriends))
        {
            await Context.Channel.SendMessageAsync(new MessageProperties { Content = "Please enter at least one friend to add. You can use their Last.fm usernames, Discord mention or Discord id." });
            Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        _ = Context.Channel?.TriggerTypingStateAsync()!;
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id) ?? _botSettings.Bot.Prefix;

        try
        {
            var friendsArray = enteredFriends.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var response = await _friendBuilders.AddFriendsAsync(new ContextModel(Context, prfx, contextUser), friendsArray);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }

    // Alias: ".friends remove" (24) -> Original: removefriends
    [Command("remove")]
    public async Task RemoveAsync([CommandParameter(Remainder = true)] string enteredFriends = null)
    {
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id) ?? _botSettings.Bot.Prefix;

        if (string.IsNullOrWhiteSpace(enteredFriends))
        {
            await Context.Channel.SendMessageAsync(new MessageProperties { Content = "Please enter at least one friend to remove. You can use their Last.fm usernames, Discord mention or discord id." });
            Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        try
        {
            var friendsArray = enteredFriends.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var response = await _friendBuilders.RemoveFriendsAsync(new ContextModel(Context, prfx, contextUser), friendsArray);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }
}

[Command("friend")]
public class FriendSpaceCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly UserService _userService;
    private readonly FriendBuilders _friendBuilders;
    private InteractiveService Interactivity { get; }

    public FriendSpaceCommands(
        IPrefixService prefixService,
        UserService userService,
        FriendBuilders friendBuilders,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        _prefixService = prefixService;
        _userService = userService;
        _friendBuilders = friendBuilders;
        Interactivity = interactivity;
    }

    // Alias: ".friend remove" (68) -> Original: removefriends
    [Command("remove")]
    public async Task RemoveAsync([CommandParameter(Remainder = true)] string enteredFriends = null)
    {
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id) ?? _botSettings.Bot.Prefix;

        if (string.IsNullOrWhiteSpace(enteredFriends))
        {
            await Context.Channel.SendMessageAsync(new MessageProperties { Content = "Please enter at least one friend to remove. You can use their Last.fm usernames, Discord mention or discord id." });
            Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        try
        {
            var friendsArray = enteredFriends.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var response = await _friendBuilders.RemoveFriendsAsync(new ContextModel(Context, prfx, contextUser), friendsArray);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }
}

[Command("help")]
public class HelpSpaceCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly StaticBuilders _staticBuilders;

    public HelpSpaceCommands(
        IPrefixService prefixService,
        StaticBuilders staticBuilders,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        _prefixService = prefixService;
        _staticBuilders = staticBuilders;
    }

    // Alias: ".help server" (25) -> Original: serverhelp
    [Command("server")]
    public async Task ServerAsync()
    {
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
        _embed.WithColor(DiscordConstants.InformationColorBlue);
        _embed.WithDescription("**See all server settings below.**\n" +
                               "These commands require either the `Admin` or the `Ban Members` permission.");

        _embed.AddField("Server Settings",
            $"`{prfx}configuration`, `{prfx}prefix`, `{prfx}servermode`, `{prfx}serverreactions`, " +
            $"`{prfx}togglecommand`, `{prfx}toggleservercommand`, `{prfx}cooldown`, `{prfx}members`");

        _embed.WithFooter($"Add 'help' after a command to get more info. For example: '{prfx}prefix help'");
        await Context.Channel.SendMessageAsync(new MessageProperties().AddEmbeds(_embed));
        Context.LogCommandUsed();
    }
}

[Command("login")]
public class LoginSpaceCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly UserService _userService;
    private readonly DiscogsBuilder _discogsBuilder;
    private InteractiveService Interactivity { get; }

    public LoginSpaceCommands(
        IPrefixService prefixService,
        UserService userService,
        DiscogsBuilder discogsBuilder,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        _prefixService = prefixService;
        _userService = userService;
        _discogsBuilder = discogsBuilder;
        Interactivity = interactivity;
    }

    // Alias: ".login discogs" (14) -> Original: discogs
    [Command("discogs")]
    public async Task DiscogsAsync([CommandParameter(Remainder = true)] string unusedValues = null)
    {
        var contextUser = await _userService.GetUserWithDiscogs(Context.User.Id);
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);

        if (contextUser.UserDiscogs == null)
        {
            if (Context.Guild != null)
            {
                var serverEmbed = new EmbedProperties()
                    .WithColor(DiscordConstants.InformationColorBlue)
                    .WithDescription("Check your DMs for a link to connect your Discogs account to .fmbot!");
                await Context.Channel.SendMessageAsync(new MessageProperties().AddEmbeds(serverEmbed));
            }

            var response = _discogsBuilder.DiscogsLoginGetLinkAsync(new ContextModel(Context, prfx, contextUser));
            var dmChannel = await Context.User.GetDMChannelAsync();
            await dmChannel.SendMessageAsync(new MessageProperties
            {
                Embeds = [response.Embed],
                Components = [response.Components]
            });
            Context.LogCommandUsed(response.CommandResponse);
        }
        else
        {
            if (Context.Guild != null)
            {
                var serverEmbed = new EmbedProperties()
                    .WithColor(DiscordConstants.InformationColorBlue)
                    .WithDescription("Check your DMs for a message to manage your connected Discogs account!");
                await Context.Channel.SendMessageAsync(new MessageProperties { Embeds = [serverEmbed] });
            }

            var response = DiscogsBuilder.DiscogsManage(new ContextModel(Context, prfx, contextUser));
            var manageDmChannel = await Context.User.GetDMChannelAsync();
            await manageDmChannel.SendMessageAsync(new MessageProperties
            {
                Embeds = [response.Embed],
                Components = [response.Components]
            });
            Context.LogCommandUsed(response.CommandResponse);
        }
    }
}

[Command("whoknows")]
public class WhoknowsSpaceCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly UserService _userService;
    private readonly TrackBuilders _trackBuilders;
    private readonly AlbumBuilders _albumBuilders;
    private readonly ArtistBuilders _artistBuilders;
    private readonly GenreBuilders _genreBuilders;
    private InteractiveService Interactivity { get; }

    public WhoknowsSpaceCommands(
        IPrefixService prefixService,
        UserService userService,
        TrackBuilders trackBuilders,
        AlbumBuilders albumBuilders,
        ArtistBuilders artistBuilders,
        GenreBuilders genreBuilders,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        _prefixService = prefixService;
        _userService = userService;
        _trackBuilders = trackBuilders;
        _albumBuilders = albumBuilders;
        _artistBuilders = artistBuilders;
        _genreBuilders = genreBuilders;
        Interactivity = interactivity;
    }

    // Alias: ".whoknows track" (26) -> Original: whoknowstrack
    [Command("track")]
    public async Task TrackAsync([CommandParameter(Remainder = true)] string trackValues = null)
    {
        _ = Context.Channel?.TriggerTypingStateAsync()!;
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);

        try
        {
            var currentSettings = new WhoKnowsSettings
            {
                ResponseMode = contextUser.Mode ?? ResponseMode.Embed,
                NewSearchValue = trackValues,
                DisplayRoleFilter = false
            };
            var settings = SettingService.SetWhoKnowsSettings(currentSettings, trackValues, contextUser.UserType);

            var response = await _trackBuilders.WhoKnowsTrackAsync(
                new ContextModel(Context, prfx, contextUser), settings.ResponseMode, settings.NewSearchValue,
                settings.DisplayRoleFilter);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }

    // Alias: ".whoknows album" (37) -> Original: whoknowsalbum
    [Command("album")]
    public async Task AlbumAsync([CommandParameter(Remainder = true)] string albumValues = null)
    {
        _ = Context.Channel?.TriggerTypingStateAsync()!;
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);

        try
        {
            var currentSettings = new WhoKnowsSettings
            {
                ResponseMode = contextUser.Mode ?? ResponseMode.Embed,
                NewSearchValue = albumValues,
                DisplayRoleFilter = false
            };
            var settings = SettingService.SetWhoKnowsSettings(currentSettings, albumValues, contextUser.UserType);

            var response = await _albumBuilders.WhoKnowsAlbumAsync(
                new ContextModel(Context, prfx, contextUser), settings.ResponseMode, settings.NewSearchValue,
                settings.DisplayRoleFilter);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }

    // Alias: ".whoknows artist" (24) -> Original: whoknows
    [Command("artist")]
    public async Task ArtistAsync([CommandParameter(Remainder = true)] string artistValues = null)
    {
        _ = Context.Channel?.TriggerTypingStateAsync()!;
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);

        try
        {
            var contextUser = await _userService.GetUserSettingsAsync(Context.User);
            var currentSettings = new WhoKnowsSettings
            {
                ResponseMode = contextUser.Mode ?? ResponseMode.Embed,
                NewSearchValue = artistValues,
                DisplayRoleFilter = false
            };
            var settings = SettingService.SetWhoKnowsSettings(currentSettings, artistValues, contextUser.UserType);

            var response = await _artistBuilders.WhoKnowsArtistAsync(new ContextModel(Context, prfx, contextUser),
                settings.ResponseMode, settings.NewSearchValue, settings.DisplayRoleFilter,
                redirectsEnabled: settings.RedirectsEnabled);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }

    // Alias: ".whoknows genre" (23) -> Original: whoknowsgenre
    [Command("genre")]
    public async Task GenreAsync([CommandParameter(Remainder = true)] string genreValues = null)
    {
        _ = Context.Channel?.TriggerTypingStateAsync()!;
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);

        try
        {
            var response = await _genreBuilders.WhoKnowsGenreAsync(new ContextModel(Context, prfx, contextUser), genreValues);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }
}

[Command("scrobble")]
public class ScrobbleSpaceCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly GuildBuilders _guildBuilders;
    private InteractiveService Interactivity { get; }

    public ScrobbleSpaceCommands(
        IPrefixService prefixService,
        UserService userService,
        GuildService guildService,
        GuildBuilders guildBuilders,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        _prefixService = prefixService;
        _userService = userService;
        _guildService = guildService;
        _guildBuilders = guildBuilders;
        Interactivity = interactivity;
    }

    // Alias: ".scrobble leaderboard" (27) -> Original: scrobbleleaderboard
    [Command("leaderboard")]
    public async Task LeaderboardAsync([CommandParameter(Remainder = true)] string options = null)
    {
        try
        {
            _ = Context.Channel?.TriggerTypingStateAsync()!;
            var prfx = _prefixService.GetPrefix(Context.Guild?.Id);
            var guild = await _guildService.GetGuildForWhoKnows(Context.Guild?.Id);
            var contextUser = await _userService.GetUserSettingsAsync(Context.User);

            var response = await _guildBuilders.MemberOverviewAsync(
                new ContextModel(Context, prfx, contextUser),
                guild, GuildViewType.Plays);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }
}

// Voice assistant commands (ok google, hey google, hey siri, alexa play)
[Command("ok")]
public class OkSpaceCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly UserService _userService;
    private readonly PlayBuilder _playBuilder;
    private readonly SettingService _settingService;
    private InteractiveService Interactivity { get; }

    public OkSpaceCommands(
        IPrefixService prefixService,
        UserService userService,
        PlayBuilder playBuilder,
        SettingService settingService,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        _prefixService = prefixService;
        _userService = userService;
        _playBuilder = playBuilder;
        _settingService = settingService;
        Interactivity = interactivity;
    }

    // Alias: ".ok google" (107) -> Original: fm
    [Command("google")]
    public async Task GoogleAsync([CommandParameter(Remainder = true)] string options = null)
    {
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);

        try
        {
            _ = Context.Channel?.TriggerTypingStateAsync()!;
            var userSettings = await _settingService.GetUser(options, contextUser, Context);
            var configuredFmType = SettingService.GetEmbedType(userSettings.NewSearchValue, contextUser.FmEmbedType);

            var response = await _playBuilder.NowPlayingAsync(new ContextModel(Context, prfx, contextUser),
                userSettings, configuredFmType.embedType);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }
}

[Command("hey")]
public class HeySpaceCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly UserService _userService;
    private readonly PlayBuilder _playBuilder;
    private readonly SettingService _settingService;
    private InteractiveService Interactivity { get; }

    public HeySpaceCommands(
        IPrefixService prefixService,
        UserService userService,
        PlayBuilder playBuilder,
        SettingService settingService,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        _prefixService = prefixService;
        _userService = userService;
        _playBuilder = playBuilder;
        _settingService = settingService;
        Interactivity = interactivity;
    }

    // Alias: ".hey google" (15) -> Original: fm
    [Command("google")]
    public Task GoogleAsync([CommandParameter(Remainder = true)] string options = null)
        => PlayNowPlayingAsync(options);

    // Alias: ".hey siri" (14) -> Original: fm
    [Command("siri")]
    public Task SiriAsync([CommandParameter(Remainder = true)] string options = null)
        => PlayNowPlayingAsync(options);

    private async Task PlayNowPlayingAsync(string options = null)
    {
        var contextUser = await _userService.GetUserSettingsAsync(Context.User);
        var prfx = _prefixService.GetPrefix(Context.Guild?.Id);

        try
        {
            _ = Context.Channel?.TriggerTypingStateAsync()!;
            var userSettings = await _settingService.GetUser(options, contextUser, Context);
            var configuredFmType = SettingService.GetEmbedType(userSettings.NewSearchValue, contextUser.FmEmbedType);

            var response = await _playBuilder.NowPlayingAsync(new ContextModel(Context, prfx, contextUser),
                userSettings, configuredFmType.embedType);

            await Context.SendResponse(Interactivity, response);
            Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await Context.HandleCommandException(e);
        }
    }
}

