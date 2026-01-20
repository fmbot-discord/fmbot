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

namespace FMBot.Bot.SlashCommands;

[SlashCommand("top", "Top lists - Artist/Albums/Tracks/Genres/Countries",
    Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
    IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
public class TopSlashCommands(
    UserService userService,
    ArtistBuilders artistBuilders,
    SettingService settingService,
    InteractiveService interactivity,
    AlbumBuilders albumBuilders,
    TrackBuilders trackBuilders,
    GenreBuilders genreBuilders,
    CountryBuilders countryBuilders,
    DiscogsBuilder discogsBuilders,
    IndexService indexService)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;

    [SubSlashCommand("artists", "Your top artists")]
    [UsernameSetRequired]
    public async Task TopArtistsAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period",
            AutocompleteProviderType = typeof(DateTimeAutoComplete))]
        string timePeriod = null,
        [SlashCommandParameter(Name = "billboard", Description = "Show top artists billboard-style")]
        bool billboard = false,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "mode",
            Description = "The type of response you want - change default with /responsemode")]
        ResponseMode? mode = null,
        [SlashCommandParameter(Name = "size", Description = "Amount of artists to show")]
        EmbedSize? embedSize = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false,
        [SlashCommandParameter(Name = "discogs", Description = "Show top artists in Discogs collection")]
        bool discogs = false)
    {
        await Context.Interaction.SendResponseAsync(
            InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        userSettings.RegisteredLastFm ??= await indexService.AddUserRegisteredLfmDate(userSettings.UserId);

        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        var timeSettings = SettingService.GetTimePeriod(timePeriod, discogs ? TimePeriod.AllTime : TimePeriod.Weekly,
            userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone);
        var topListSettings = new TopListSettings(embedSize ?? EmbedSize.Default, billboard, discogs);

        var response = topListSettings.Discogs
            ? await discogsBuilders.DiscogsTopArtistsAsync(new ContextModel(this.Context, contextUser),
                topListSettings, timeSettings, userSettings)
            : await artistBuilders.TopArtistsAsync(new ContextModel(this.Context, contextUser),
                topListSettings, timeSettings, userSettings, mode.Value);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, privateResponse);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [SubSlashCommand("albums", "Shows your top albums")]
    [UsernameSetRequired]
    public async Task TopAlbumsAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period",
            AutocompleteProviderType = typeof(DateTimeAutoComplete))]
        string timePeriod = null,
        [SlashCommandParameter(Name = "released", Description = "Filter to albums released in year",
            AutocompleteProviderType = typeof(YearAutoComplete))]
        string year = null,
        [SlashCommandParameter(Name = "decade", Description = "Filter to albums released in decade",
            AutocompleteProviderType = typeof(DecadeAutoComplete))]
        string decade = null,
        [SlashCommandParameter(Name = "billboard", Description = "Show top albums billboard-style")]
        bool billboard = false,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "mode",
            Description = "The type of response you want - change default with /responsemode")]
        ResponseMode? mode = null,
        [SlashCommandParameter(Name = "size", Description = "Amount of albums to show")]
        EmbedSize? embedSize = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(
            InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        var timeSettings = SettingService.GetTimePeriod(timePeriod,
            !string.IsNullOrWhiteSpace(year) || !string.IsNullOrWhiteSpace(decade)
                ? TimePeriod.AllTime
                : TimePeriod.Weekly, timeZone: userSettings.TimeZone);

        var topListSettings = new TopListSettings(embedSize ?? EmbedSize.Default, billboard,
            year: year != null ? int.Parse(year) : null, decade: decade != null ? int.Parse(decade) : null);

        var response = await albumBuilders.TopAlbumsAsync(new ContextModel(this.Context, contextUser),
            topListSettings, timeSettings, userSettings, mode.Value);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, privateResponse);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [SubSlashCommand("tracks", "Shows your top tracks")]
    [UsernameSetRequired]
    public async Task TopTracksAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period",
            AutocompleteProviderType = typeof(DateTimeAutoComplete))]
        string timePeriod = null,
        [SlashCommandParameter(Name = "billboard", Description = "Show top tracks billboard-style")]
        bool billboard = false,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "mode",
            Description = "The type of response you want - change default with /responsemode")]
        ResponseMode? mode = null,
        [SlashCommandParameter(Name = "size", Description = "Amount of tracks to show")]
        EmbedSize? embedSize = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(
            InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        var timeSettings = SettingService.GetTimePeriod(timePeriod, timeZone: userSettings.TimeZone);

        var topListSettings = new TopListSettings(embedSize ?? EmbedSize.Default, billboard);

        var response = await trackBuilders.TopTracksAsync(new ContextModel(this.Context, contextUser),
            topListSettings, timeSettings, userSettings, mode.Value);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, privateResponse);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [SubSlashCommand("genres", "Shows your top genres")]
    [UsernameSetRequired]
    public async Task TopGenresAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period",
            AutocompleteProviderType = typeof(DateTimeAutoComplete))]
        string timePeriod = null,
        [SlashCommandParameter(Name = "billboard", Description = "Show top genres billboard-style")]
        bool billboard = false,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "mode",
            Description = "The type of response you want - change default with /responsemode")]
        ResponseMode? mode = null,
        [SlashCommandParameter(Name = "size", Description = "Amount of genres to show")]
        EmbedSize? embedSize = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(
            InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        var timeSettings = SettingService.GetTimePeriod(timePeriod, timeZone: userSettings.TimeZone);

        var topListSettings = new TopListSettings(embedSize ?? EmbedSize.Default, billboard);

        var response = await genreBuilders.TopGenresAsync(new ContextModel(this.Context, contextUser),
            userSettings, timeSettings, topListSettings, mode.Value);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, privateResponse);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [SubSlashCommand("countries", "Shows your top countries")]
    [UsernameSetRequired]
    public async Task TopCountriesAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period",
            AutocompleteProviderType = typeof(DateTimeAutoComplete))]
        string timePeriod = null,
        [SlashCommandParameter(Name = "billboard", Description = "Show top countries billboard-style")]
        bool billboard = false,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "mode",
            Description = "The type of response you want - change default with /responsemode")]
        ResponseMode? mode = null,
        [SlashCommandParameter(Name = "size", Description = "Amount of countries to show")]
        EmbedSize? embedSize = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(
            InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        var timeSettings = SettingService.GetTimePeriod(timePeriod, timeZone: userSettings.TimeZone);

        var topListSettings = new TopListSettings(embedSize ?? EmbedSize.Default, billboard);

        var response = await countryBuilders.TopCountriesAsync(new ContextModel(this.Context, contextUser),
            userSettings, timeSettings, topListSettings, mode.Value);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, privateResponse);
        await this.Context.LogCommandUsedAsync(response, userService);
    }
}
