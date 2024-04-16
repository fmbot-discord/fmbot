using System;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;

namespace FMBot.Bot.SlashCommands;

[Group("top", "Top lists - Artist/Albums/Tracks/Genres/Countries")]
[CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
[IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
public class TopSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly ArtistBuilders _artistBuilders;
    private readonly SettingService _settingService;
    private readonly AlbumBuilders _albumBuilders;
    private readonly TrackBuilders _trackBuilders;
    private readonly GenreBuilders _genreBuilders;
    private readonly CountryBuilders _countryBuilders;
    private readonly DiscogsBuilder _discogsBuilders;

    private InteractiveService Interactivity { get; }

    public TopSlashCommands(UserService userService,
        ArtistBuilders artistBuilders,
        SettingService settingService,
        InteractiveService interactivity,
        AlbumBuilders albumBuilders,
        TrackBuilders trackBuilders,
        GenreBuilders genreBuilders,
        CountryBuilders countryBuilders,
        DiscogsBuilder discogsBuilders)
    {
        this._userService = userService;
        this._artistBuilders = artistBuilders;
        this._settingService = settingService;
        this.Interactivity = interactivity;
        this._albumBuilders = albumBuilders;
        this._trackBuilders = trackBuilders;
        this._genreBuilders = genreBuilders;
        this._countryBuilders = countryBuilders;
        this._discogsBuilders = discogsBuilders;
    }

    [SlashCommand("artists", "Shows your top artists")]
    [UsernameSetRequired]
    public async Task TopArtistsAsync(
        [Summary("Time-period", "Time period")][Autocomplete(typeof(DateTimeAutoComplete))] string timePeriod = null,
        [Summary("Billboard", "Show top artists billboard-style")] bool billboard = false,
        [Summary("User", "The user to show (defaults to self)")] string user = null,
        [Summary("Mode", "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [Summary("Size", "Amount of artists to show")] EmbedSize? embedSize = null,
        [Summary("Private", "Only show response to you")] bool privateResponse = false,
        [Summary("Discogs", "Show top artists in Discogs collection")] bool discogs = false)
    {
        _ = DeferAsync(privateResponse);

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        var timeSettings = SettingService.GetTimePeriod(timePeriod, discogs ? TimePeriod.AllTime : TimePeriod.Weekly, discogs ? DateTime.MinValue : null, timeZone: userSettings.TimeZone);
        var topListSettings = new TopListSettings(embedSize ?? EmbedSize.Default, billboard, discogs);

        var response = topListSettings.Discogs
            ? await this._discogsBuilders.DiscogsTopArtistsAsync(new ContextModel(this.Context, contextUser),
                topListSettings, timeSettings, userSettings)
            : await this._artistBuilders.TopArtistsAsync(new ContextModel(this.Context, contextUser),
                topListSettings, timeSettings, userSettings, mode.Value);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("albums", "Shows your top albums")]
    [UsernameSetRequired]
    public async Task TopAlbumsAsync(
        [Summary("Time-period", "Time period")][Autocomplete(typeof(DateTimeAutoComplete))] string timePeriod = null,
        [Summary("Released", "Filter to albums released in year")][Autocomplete(typeof(YearAutoComplete))] string year = null,
        [Summary("Decade", "Filter to albums released in decade")][Autocomplete(typeof(DecadeAutoComplete))] string decade = null,
        [Summary("Billboard", "Show top albums billboard-style")] bool billboard = false,
        [Summary("User", "The user to show (defaults to self)")] string user = null,
        [Summary("Mode", "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [Summary("Size", "Amount of albums to show")] EmbedSize? embedSize = null,
        [Summary("Private", "Only show response to you")] bool privateResponse = false)
    {
        _ = DeferAsync(privateResponse);
        
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        var timeSettings = SettingService.GetTimePeriod(timePeriod, !string.IsNullOrWhiteSpace(year) || !string.IsNullOrWhiteSpace(decade) ? TimePeriod.AllTime : TimePeriod.Weekly, timeZone: userSettings.TimeZone);

        var topListSettings = new TopListSettings(embedSize ?? EmbedSize.Default, billboard, year: year != null ? int.Parse(year) : null, decade: decade != null ? int.Parse(decade) : null);

        var response = await this._albumBuilders.TopAlbumsAsync(new ContextModel(this.Context, contextUser),
            topListSettings, timeSettings, userSettings, mode.Value);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("tracks", "Shows your top tracks")]
    [UsernameSetRequired]
    public async Task TopTracksAsync(
        [Summary("Time-period", "Time period")][Autocomplete(typeof(DateTimeAutoComplete))] string timePeriod = null,
        [Summary("Billboard", "Show top tracks billboard-style")] bool billboard = false,
        [Summary("User", "The user to show (defaults to self)")] string user = null,
        [Summary("Mode", "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [Summary("Size", "Amount of tracks to show")] EmbedSize? embedSize = null,
        [Summary("Private", "Only show response to you")] bool privateResponse = false)
    {
        _ = DeferAsync(privateResponse);
        
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        var timeSettings = SettingService.GetTimePeriod(timePeriod, timeZone: userSettings.TimeZone);

        var topListSettings = new TopListSettings(embedSize ?? EmbedSize.Default, billboard);

        var response = await this._trackBuilders.TopTracksAsync(new ContextModel(this.Context, contextUser),
            topListSettings, timeSettings, userSettings, mode.Value);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("genres", "Shows your top genres")]
    [UsernameSetRequired]
    public async Task TopGenresAsync(
        [Summary("Time-period", "Time period")][Autocomplete(typeof(DateTimeAutoComplete))] string timePeriod = null,
        [Summary("Billboard", "Show top genres billboard-style")] bool billboard = false,
        [Summary("User", "The user to show (defaults to self)")] string user = null,
        [Summary("Mode", "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [Summary("Size", "Amount of genres to show")] EmbedSize? embedSize = null,
        [Summary("Private", "Only show response to you")] bool privateResponse = false)
    {
        _ = DeferAsync(privateResponse);
        
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        var timeSettings = SettingService.GetTimePeriod(timePeriod, timeZone: userSettings.TimeZone);

        var topListSettings = new TopListSettings(embedSize ?? EmbedSize.Default, billboard);

        var response = await this._genreBuilders.GetTopGenres(new ContextModel(this.Context, contextUser),
            userSettings, timeSettings, topListSettings, mode.Value);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("countries", "Shows your top countries")]
    [UsernameSetRequired]
    public async Task TopCountriesAsync(
        [Summary("Time-period", "Time period")][Autocomplete(typeof(DateTimeAutoComplete))] string timePeriod = null,
        [Summary("Billboard", "Show top countries billboard-style")] bool billboard = false,
        [Summary("User", "The user to show (defaults to self)")] string user = null,
        [Summary("Mode", "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [Summary("Size", "Amount of countries to show")] EmbedSize? embedSize = null,
        [Summary("Private", "Only show response to you")] bool privateResponse = false)
    {
        _ = DeferAsync(privateResponse);
        
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        var timeSettings = SettingService.GetTimePeriod(timePeriod, timeZone: userSettings.TimeZone);

        var topListSettings = new TopListSettings(embedSize ?? EmbedSize.Default, billboard);

        var response = await this._countryBuilders.GetTopCountries(new ContextModel(this.Context, contextUser),
            userSettings, timeSettings, topListSettings, mode.Value);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
        this.Context.LogCommandUsed(response.CommandResponse);
    }
}
