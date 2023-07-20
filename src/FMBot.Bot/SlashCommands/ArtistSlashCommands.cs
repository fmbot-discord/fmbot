using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;

namespace FMBot.Bot.SlashCommands;

public class ArtistSlashCommands : InteractionModuleBase
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

    [SlashCommand("artist", "Shows artist info for the artist you're currently listening to or searching for")]
    [UsernameSetRequired]
    public async Task ArtistAsync(
        [Summary("Artist", "The artist your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(ArtistAutoComplete))] string name = null,
        [Summary("Redirects", "Toggle Last.fm artist name redirects (defaults to enabled)")] bool redirectsEnabled = true)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserWithDiscogs(this.Context.User.Id);

        try
        {
            var response = await this._artistBuilders.ArtistAsync(new ContextModel(this.Context, contextUser), name, redirectsEnabled);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("artisttracks", "Shows your top tracks for an artist")]
    [UsernameSetRequired]
    public async Task ArtistTracksAsync(
        [Summary("Artist", "The artist your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(ArtistAutoComplete))]string name = null,
        [Summary("Time-period", "Time period to base show tracks for")] PlayTimePeriod timePeriod = PlayTimePeriod.AllTime,
        [Summary("User", "The user to show (defaults to self)")] string user = null,
        [Summary("Redirects", "Toggle Last.fm artist name redirects (defaults to enabled)")] bool redirectsEnabled = true)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var timeSettings = SettingService.GetTimePeriod(Enum.GetName(typeof(PlayTimePeriod), timePeriod), TimePeriod.AllTime);

        var response = await this._artistBuilders.ArtistTracksAsync(new ContextModel(this.Context, contextUser), timeSettings,
            userSettings, name, redirectsEnabled);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("artistpace", "Shows estimated date you reach a certain amount of plays on an artist")]
    [UsernameSetRequired]
    public async Task ArtistPaceAsync(
        [Summary("Artist", "The artist your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(ArtistAutoComplete))]string name = null,
        [Summary("Amount", "Goal play amount")] int amount = 1,
        [Summary("Time-period", "Time period to base average playcount on")] ArtistPaceTimePeriod timePeriod = ArtistPaceTimePeriod.Monthly,
        [Summary("User", "The user to show (defaults to self)")] string user = null,
        [Summary("Redirects", "Toggle Last.fm artist name redirects (defaults to enabled)")] bool redirectsEnabled = true)
    {
        _ = DeferAsync();

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
        [Summary("Artist", "The artist your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(ArtistAutoComplete))] string name = null,
        [Summary("Mode", "The type of response you want")] WhoKnowsMode? mode = null,
        [Summary("Role-picker", "Display a rolepicker to filter with roles")] bool displayRoleFilter = false,
        [Summary("Redirects", "Toggle Last.fm artist name redirects (defaults to enabled)")] bool redirectsEnabled = true)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        mode ??= contextUser.Mode ?? WhoKnowsMode.Embed;

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

    [ComponentInteraction($"{InteractionConstants.WhoKnowsRolePicker}-*")]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task WhoKnowsFilteringAsync(string artistId, string[] inputs)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var artist = await this._artistsService.GetArtistForId(int.Parse(artistId));

        var roleIds = new List<ulong>();
        if (inputs != null)
        {
            foreach (var input in inputs)
            {
                var roleId = ulong.Parse(input);
                roleIds.Add(roleId);
            }
        }

        try
        {
            // TODO add redirects
            var response = await this._artistBuilders.WhoKnowsArtistAsync(new ContextModel(this.Context, contextUser), WhoKnowsMode.Embed, artist.Name, true, roleIds);

            await this.Context.UpdateInteractionEmbed(response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("friendswhoknow", "Shows who of your friends listen to an artist")]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task FriendsWhoKnowAsync(
        [Summary("Artist", "The artist your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(ArtistAutoComplete))] string name = null,
        [Summary("Mode", "The type of response you want")] WhoKnowsMode? mode = null,
        [Summary("Private", "Only show response to you")] bool privateResponse = false,
        [Summary("Redirects", "Toggle Last.fm artist name redirects (defaults to enabled)")] bool redirectsEnabled = true)
    {
        _ = DeferAsync(privateResponse);

        var contextUser = await this._userService.GetUserWithFriendsAsync(this.Context.User);

        mode ??= contextUser.Mode ?? WhoKnowsMode.Embed;


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

    [SlashCommand("globalwhoknows", "Shows what other users listen to an artist in .fmbot")]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task GlobalWhoKnowsAsync(
        [Summary("Artist", "The artist your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(ArtistAutoComplete))] string name = null,
        [Summary("Mode", "The type of response you want")] WhoKnowsMode? mode = null,
        [Summary("Hide-private", "Hide or show private users")] bool hidePrivate = false,
        [Summary("Redirects", "Toggle Last.fm artist name redirects (defaults to enabled)")] bool redirectsEnabled = true)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var currentSettings = new WhoKnowsSettings
        {
            HidePrivateUsers = hidePrivate,
            ShowBotters = false,
            AdminView = false,
            NewSearchValue = name,
            WhoKnowsMode = mode ?? contextUser.Mode ?? WhoKnowsMode.Embed,
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

    [UserCommand("Compare taste")]
    [UsernameSetRequired]
    public async Task UserTasteAsync(IUser user)
    {
        await TasteAsync(user.Id.ToString(), privateResponse: true);
    }

    [SlashCommand("taste", "Compares your top artist, genres and countries to those from another user.")]
    [UsernameSetRequired]
    public async Task TasteAsync(
        [Summary("User", "The user to compare your taste with")] string user,
        [Summary("Time-period", "Time period")][Autocomplete(typeof(DateTimeAutoComplete))] string timePeriod = null,
        [Summary("Type", "Taste view type")] TasteType tasteType = TasteType.Table,
        [Summary("Private", "Only show response to you")] bool privateResponse = false,
        [Summary("XXL", "Show extra large comparison")] bool extraLarge = false)
    {
        _ = DeferAsync(privateResponse);

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var timeSettings = SettingService.GetTimePeriod(timePeriod, TimePeriod.AllTime);

            var response = await this._artistBuilders.TasteAsync(new ContextModel(this.Context, contextUser),
                new TasteSettings { TasteType = tasteType, ExtraLarge = extraLarge }, timeSettings, userSettings);

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
    public async Task AffinityAsync([Summary("User", "The user to get the affinity for")] string user = null)
    {
        _ = DeferAsync();

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
}
