using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Enums;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

public class ArtistSlashCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserService _userService;
    private readonly ArtistBuilders _artistBuilders;
    private readonly SettingService _settingService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly GuildService _guildService;
    private readonly ArtistsService _artistsService;

    private InteractiveService Interactivity { get; }

    public ArtistSlashCommands(UserService userService,
        ArtistBuilders artistBuilders,
        SettingService settingService,
        InteractiveService interactivity,
        IDataSourceFactory dataSourceFactory,
        GuildService guildService,
        ArtistsService artistsService)
    {
        this._userService = userService;
        this._artistBuilders = artistBuilders;
        this._settingService = settingService;
        this.Interactivity = interactivity;
        this._dataSourceFactory = dataSourceFactory;
        this._guildService = guildService;
        this._artistsService = artistsService;
    }

    [SlashCommand("artist", "General info for current artist or one you're searching for", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task ArtistAsync(
        [SlashCommandParameter(Name = "Artist", Description = "The artist your want to search for (defaults to currently playing)", AutocompleteProviderType = typeof(ArtistAutoComplete))] string name = null,
        [SlashCommandParameter(Name = "Redirects", Description = "Toggle Last.fm artist name redirects (defaults to enabled)")] bool redirectsEnabled = true)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserWithDiscogs(this.Context.User.Id);
        var userSettings = await this._settingService.GetUser(null, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var response = await this._artistBuilders.ArtistInfoAsync(new ContextModel(this.Context, contextUser), userSettings, name, redirectsEnabled);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("artistoverview", "Shows overview for current artist or the one you're searching for", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task ArtistOverviewAsync(
        [SlashCommandParameter(Name = "Artist", Description = "The artist your want to search for (defaults to currently playing)", AutocompleteProviderType = typeof(ArtistAutoComplete))] string name = null,
        [SlashCommandParameter(Name = "Redirects", Description = "Toggle Last.fm artist name redirects (defaults to enabled)")] bool redirectsEnabled = true,
        [SlashCommandParameter(Name = "User", Description = "The user to show (defaults to self)")] string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserWithDiscogs(this.Context.User.Id);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var response = await this._artistBuilders.ArtistOverviewAsync(new ContextModel(this.Context, contextUser), userSettings, name, redirectsEnabled);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("artistplays", "Shows playcount for current artist or the one you're searching for", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task ArtistPlaysAsync(
        [SlashCommandParameter(Name = "Artist", Description = "The artist your want to search for (defaults to currently playing)", AutocompleteProviderType = typeof(ArtistAutoComplete))] string name = null,
        [SlashCommandParameter(Name = "Redirects", Description = "Toggle Last.fm artist name redirects (defaults to enabled)")] bool redirectsEnabled = true,
        [SlashCommandParameter(Name = "User", Description = "The user to show (defaults to self)")] string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var response = await this._artistBuilders.ArtistPlaysAsync(new ContextModel(this.Context, contextUser), userSettings, name, redirectsEnabled);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("artisttracks", "Shows your top tracks for an artist", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task ArtistTracksAsync(
        [SlashCommandParameter(Name = "Artist", Description = "The artist your want to search for (defaults to currently playing)", AutocompleteProviderType = typeof(ArtistAutoComplete))]string name = null,
        [SlashCommandParameter(Name = "Time-period", Description = "Time period to base show tracks for")] PlayTimePeriod timePeriod = PlayTimePeriod.AllTime,
        [SlashCommandParameter(Name = "User", Description = "The user to show (defaults to self)")] string user = null,
        [SlashCommandParameter(Name = "Redirects", Description = "Toggle Last.fm artist name redirects (defaults to enabled)")] bool redirectsEnabled = true)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var timeSettings = SettingService.GetTimePeriod(Enum.GetName(typeof(PlayTimePeriod), timePeriod), TimePeriod.AllTime);

        var response = await this._artistBuilders.ArtistTracksAsync(new ContextModel(this.Context, contextUser), timeSettings,
            userSettings, name, redirectsEnabled);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("artistalbums", "Shows your top albums for an artist", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task ArtistAlbumsAsync(
        [SlashCommandParameter(Name = "Artist", Description = "The artist your want to search for (defaults to currently playing)", AutocompleteProviderType = typeof(ArtistAutoComplete))]string name = null,
        [SlashCommandParameter(Name = "User", Description = "The user to show (defaults to self)")] string user = null,
        [SlashCommandParameter(Name = "Redirects", Description = "Toggle Last.fm artist name redirects (defaults to enabled)")] bool redirectsEnabled = true)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var response = await this._artistBuilders.ArtistAlbumsAsync(new ContextModel(this.Context, contextUser),
            userSettings, name, redirectsEnabled);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("artistpace", "Shows estimated date you reach a certain amount of plays on an artist", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task ArtistPaceAsync(
        [SlashCommandParameter(Name = "Artist", Description = "The artist your want to search for (defaults to currently playing)", AutocompleteProviderType = typeof(ArtistAutoComplete))]string name = null,
        [SlashCommandParameter(Name = "Amount", Description = "Goal play amount")] int amount = 1,
        [SlashCommandParameter(Name = "Time-period", Description = "Time period to base average playcount on")] ArtistPaceTimePeriod timePeriod = ArtistPaceTimePeriod.Monthly,
        [SlashCommandParameter(Name = "User", Description = "The user to show (defaults to self)")] string user = null,
        [SlashCommandParameter(Name = "Redirects", Description = "Toggle Last.fm artist name redirects (defaults to enabled)")] bool redirectsEnabled = true)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(userSettings.UserNameLastFm);
            var timeSettings = SettingService.GetTimePeriod(Enum.GetName(typeof(ArtistPaceTimePeriod), timePeriod), TimePeriod.Monthly);

            var response = await this._artistBuilders.ArtistPaceAsync(new ContextModel(this.Context, contextUser),
                userSettings, timeSettings, amount.ToString(), name, redirectsEnabled);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    public enum ArtistPaceTimePeriod
    {
        Weekly = 1,
        Monthly = 2
    }

    [SlashCommand("whoknows", "Shows what other users listen to an artist in your server")]
    [UsernameSetRequired]
    [RequiresIndex]
    [GuildOnly]
    public async Task WhoKnowsAsync(
        [SlashCommandParameter(Name = "Artist", Description = "The artist your want to search for (defaults to currently playing)", AutocompleteProviderType = typeof(ArtistAutoComplete))] string name = null,
        [SlashCommandParameter(Name = "Mode", Description = "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [SlashCommandParameter(Name = "Role-picker", Description = "Display a rolepicker to filter with roles")] bool displayRoleFilter = false,
        [SlashCommandParameter(Name = "Redirects", Description = "Toggle Last.fm artist name redirects (defaults to enabled)")] bool redirectsEnabled = true)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        try
        {
            var response = await this._artistBuilders.WhoKnowsArtistAsync(new ContextModel(this.Context, contextUser),
                mode.Value, name, displayRoleFilter, redirectsEnabled: redirectsEnabled);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("friendswhoknow", "Shows who of your friends listen to an artist", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task FriendsWhoKnowAsync(
        [SlashCommandParameter(Name = "Artist", Description = "The artist your want to search for (defaults to currently playing)", AutocompleteProviderType = typeof(ArtistAutoComplete))] string name = null,
        [SlashCommandParameter(Name = "Mode", Description = "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [SlashCommandParameter(Name = "Private", Description = "Only show response to you")] bool privateResponse = false,
        [SlashCommandParameter(Name = "Redirects", Description = "Toggle Last.fm artist name redirects (defaults to enabled)")] bool redirectsEnabled = true)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await this._userService.GetUserWithFriendsAsync(this.Context.User);

        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        try
        {
            var response = await this._artistBuilders.FriendsWhoKnowArtistAsync(new ContextModel(this.Context, contextUser),
                mode.Value, name, redirectsEnabled);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("globalwhoknows", "Shows what other users listen to an artist in .fmbot", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task GlobalWhoKnowsAsync(
        [SlashCommandParameter(Name = "Artist", Description = "The artist your want to search for (defaults to currently playing)", AutocompleteProviderType = typeof(ArtistAutoComplete))] string name = null,
        [SlashCommandParameter(Name = "Mode", Description = "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [SlashCommandParameter(Name = "Hide-private", Description = "Hide or show private users")] bool hidePrivate = false,
        [SlashCommandParameter(Name = "Redirects", Description = "Toggle Last.fm artist name redirects (defaults to enabled)")] bool redirectsEnabled = true)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var currentSettings = new WhoKnowsSettings
        {
            HidePrivateUsers = hidePrivate,
            ShowBotters = false,
            AdminView = false,
            NewSearchValue = name,
            ResponseMode = mode ?? contextUser.Mode ?? ResponseMode.Embed,
            RedirectsEnabled = redirectsEnabled
        };

        try
        {
            var response = await this._artistBuilders.GlobalWhoKnowsArtistAsync(new ContextModel(this.Context, contextUser), currentSettings);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("discoveries", "⭐ Shows artists you've recently discovered", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task ArtistDiscoveriesAsync(
        [SlashCommandParameter(Name = "Time-period", Description = "Time period", AutocompleteProviderType = typeof(DateTimeAutoComplete))] string timePeriod = null,
        [SlashCommandParameter(Name = "User", Description = "The user to show (defaults to self)")] string user = null,
        [SlashCommandParameter(Name = "Mode", Description = "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [SlashCommandParameter(Name = "Size", Description = "Amount of artists discoveries to show per page")] EmbedSize? embedSize = null,
        [SlashCommandParameter(Name = "Private", Description = "Only show response to you")] bool privateResponse = false)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        var context = new ContextModel(this.Context, contextUser);

        var supporterRequiredResponse = ArtistBuilders.DiscoverySupporterRequired(context, userSettings);

        if (supporterRequiredResponse != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse);
            this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
            return;
        }

        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var timeSettings = SettingService.GetTimePeriod(timePeriod, TimePeriod.Quarterly, timeZone: userSettings.TimeZone);

        var topListSettings = new TopListSettings(embedSize ?? EmbedSize.Default);

        var response = await this._artistBuilders.ArtistDiscoveriesAsync(context, topListSettings, timeSettings, userSettings, mode.Value);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [UserCommand("Compare taste", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task UserTasteAsync(NetCord.User user)
    {
        await TasteAsync(user.Id.ToString());
    }

    [SlashCommand("taste", "Compares your top artist, genres and countries to those from another user.", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task TasteAsync(
        [SlashCommandParameter(Name = "User", Description = "The user to compare your taste with")] string user,
        [SlashCommandParameter(Name = "Time-period", Description = "Time period", AutocompleteProviderType = typeof(DateTimeAutoComplete))] string timePeriod = null,
        [SlashCommandParameter(Name = "Type", Description = "Taste view type")] TasteType tasteType = TasteType.Table,
        [SlashCommandParameter(Name = "Private", Description = "Only show response to you")] bool privateResponse = false,
        [SlashCommandParameter(Name = "Size", Description = "Amount of comparisons to show")] EmbedSize? embedSize = null)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var timeSettings = SettingService.GetTimePeriod(timePeriod, TimePeriod.AllTime, timeZone: userSettings.TimeZone);

            var response = await this._artistBuilders.TasteAsync(new ContextModel(this.Context, contextUser),
                new TasteSettings { TasteType = tasteType, EmbedSize = embedSize ?? EmbedSize.Default }, timeSettings, userSettings);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("affinity", "Shows users from this server with similar top artists.")]
    [UsernameSetRequired]
    [RequiresIndex]
    [GuildOnly]
    public async Task AffinityAsync([SlashCommandParameter(Name = "User", Description = "The user to get the affinity for")] string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        var guildUsers = await this._guildService.GetGuildUsers(this.Context.Guild.Id);

        try
        {
            var response = await this._artistBuilders.AffinityAsync(new ContextModel(this.Context, contextUser), userSettings, guild, guildUsers, guildUsers.Count > 2000);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("iceberg", "Shows your iceberg, based on artists popularity.", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task IcebergAsync(
        [SlashCommandParameter(Name = "Time-period", Description = "Time period", AutocompleteProviderType = typeof(DateTimeAutoComplete))] string timePeriod = null,
        [SlashCommandParameter(Name = "User", Description = "The user to show (defaults to self)")] string user = null,
        [SlashCommandParameter(Name = "Private", Description = "Only show response to you")] bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var timeSettings = SettingService.GetTimePeriod(timePeriod,
            registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone,
            defaultTimePeriod: TimePeriod.AllTime);

        var response = await this._artistBuilders.GetIceberg(new ContextModel(this.Context, contextUser), userSettings, timeSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
        this.Context.LogCommandUsed(response.CommandResponse);
    }
}
