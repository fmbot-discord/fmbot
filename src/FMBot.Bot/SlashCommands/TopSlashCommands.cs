using System;
using System.Threading.Tasks;

using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using NetCord.Services.ApplicationCommands;
using NetCord;
using NetCord.Rest;
using Fergun.Interactive;
using NetCord.Services.Commands;

namespace FMBot.Bot.SlashCommands;

[SlashCommand("top", "Top lists - Artist/Albums/Tracks/Genres/Countries", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
public class TopSlashCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserService _userService;
    private readonly ArtistBuilders _artistBuilders;
    private readonly SettingService _settingService;
    private readonly AlbumBuilders _albumBuilders;
    private readonly TrackBuilders _trackBuilders;
    private readonly GenreBuilders _genreBuilders;
    private readonly CountryBuilders _countryBuilders;
    private readonly DiscogsBuilder _discogsBuilders;
    private readonly IndexService _indexService;

    private InteractiveService Interactivity { get; }

    public TopSlashCommands(UserService userService,
        ArtistBuilders artistBuilders,
        SettingService settingService,
        InteractiveService interactivity,
        AlbumBuilders albumBuilders,
        TrackBuilders trackBuilders,
        GenreBuilders genreBuilders,
        CountryBuilders countryBuilders,
        DiscogsBuilder discogsBuilders,
        IndexService indexService)
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
        this._indexService = indexService;
    }

    [SlashCommand("artists", "Your top artists")]
    [UsernameSetRequired]
    public async Task TopArtistsAsync(
        [SlashCommandParameter(Name = "Time-period", Description = "Time period", AutocompleteProviderType = typeof(DateTimeAutoComplete))] string timePeriod = null,
        [SlashCommandParameter(Name = "Billboard", Description = "Show top artists billboard-style")] bool billboard = false,
        [SlashCommandParameter(Name = "User", Description = "The user to show (defaults to self)")] string user = null,
        [SlashCommandParameter(Name = "Mode", Description = "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [SlashCommandParameter(Name = "Size", Description = "Amount of artists to show")] EmbedSize? embedSize = null,
        [SlashCommandParameter(Name = "Private", Description = "Only show response to you")] bool privateResponse = false,
        [SlashCommandParameter(Name = "Discogs", Description = "Show top artists in Discogs collection")] bool discogs = false)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        userSettings.RegisteredLastFm ??= await this._indexService.AddUserRegisteredLfmDate(userSettings.UserId);

        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        var timeSettings = SettingService.GetTimePeriod(timePeriod, discogs ? TimePeriod.AllTime : TimePeriod.Weekly, userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone);
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
        [SlashCommandParameter(Name = "Time-period", Description = "Time period", AutocompleteProviderType = typeof(DateTimeAutoComplete))] string timePeriod = null,
        [SlashCommandParameter(Name = "Released", Description = "Filter to albums released in year", AutocompleteProviderType = typeof(YearAutoComplete))] string year = null,
        [SlashCommandParameter(Name = "Decade", Description = "Filter to albums released in decade", AutocompleteProviderType = typeof(DecadeAutoComplete))] string decade = null,
        [SlashCommandParameter(Name = "Billboard", Description = "Show top albums billboard-style")] bool billboard = false,
        [SlashCommandParameter(Name = "User", Description = "The user to show (defaults to self)")] string user = null,
        [SlashCommandParameter(Name = "Mode", Description = "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [SlashCommandParameter(Name = "Size", Description = "Amount of albums to show")] EmbedSize? embedSize = null,
        [SlashCommandParameter(Name = "Private", Description = "Only show response to you")] bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

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
        [SlashCommandParameter(Name = "Time-period", Description = "Time period", AutocompleteProviderType = typeof(DateTimeAutoComplete))] string timePeriod = null,
        [SlashCommandParameter(Name = "Billboard", Description = "Show top tracks billboard-style")] bool billboard = false,
        [SlashCommandParameter(Name = "User", Description = "The user to show (defaults to self)")] string user = null,
        [SlashCommandParameter(Name = "Mode", Description = "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [SlashCommandParameter(Name = "Size", Description = "Amount of tracks to show")] EmbedSize? embedSize = null,
        [SlashCommandParameter(Name = "Private", Description = "Only show response to you")] bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

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
        [SlashCommandParameter(Name = "Time-period", Description = "Time period", AutocompleteProviderType = typeof(DateTimeAutoComplete))] string timePeriod = null,
        [SlashCommandParameter(Name = "Billboard", Description = "Show top genres billboard-style")] bool billboard = false,
        [SlashCommandParameter(Name = "User", Description = "The user to show (defaults to self)")] string user = null,
        [SlashCommandParameter(Name = "Mode", Description = "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [SlashCommandParameter(Name = "Size", Description = "Amount of genres to show")] EmbedSize? embedSize = null,
        [SlashCommandParameter(Name = "Private", Description = "Only show response to you")] bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        var timeSettings = SettingService.GetTimePeriod(timePeriod, timeZone: userSettings.TimeZone);

        var topListSettings = new TopListSettings(embedSize ?? EmbedSize.Default, billboard);

        var response = await this._genreBuilders.TopGenresAsync(new ContextModel(this.Context, contextUser),
            userSettings, timeSettings, topListSettings, mode.Value);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("countries", "Shows your top countries")]
    [UsernameSetRequired]
    public async Task TopCountriesAsync(
        [SlashCommandParameter(Name = "Time-period", Description = "Time period", AutocompleteProviderType = typeof(DateTimeAutoComplete))] string timePeriod = null,
        [SlashCommandParameter(Name = "Billboard", Description = "Show top countries billboard-style")] bool billboard = false,
        [SlashCommandParameter(Name = "User", Description = "The user to show (defaults to self)")] string user = null,
        [SlashCommandParameter(Name = "Mode", Description = "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [SlashCommandParameter(Name = "Size", Description = "Amount of countries to show")] EmbedSize? embedSize = null,
        [SlashCommandParameter(Name = "Private", Description = "Only show response to you")] bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        var timeSettings = SettingService.GetTimePeriod(timePeriod, timeZone: userSettings.TimeZone);

        var topListSettings = new TopListSettings(embedSize ?? EmbedSize.Default, billboard);

        var response = await this._countryBuilders.TopCountriesAsync(new ContextModel(this.Context, contextUser),
            userSettings, timeSettings, topListSettings, mode.Value);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
        this.Context.LogCommandUsed(response.CommandResponse);
    }
}
