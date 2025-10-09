using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.SlashCommands;

public class TrackSlashCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly TrackBuilders _trackBuilders;
    private readonly TrackService _trackService;
    private readonly EurovisionBuilders _eurovisionBuilders;
    private readonly CountryService _countryService;

    private InteractiveService Interactivity { get; }


    public TrackSlashCommands(UserService userService,
        SettingService settingService,
        TrackBuilders trackBuilders,
        InteractiveService interactivity,
        TrackService trackService,
        EurovisionBuilders eurovisionBuilders,
        CountryService countryService)
    {
        this._userService = userService;
        this._settingService = settingService;
        this._trackBuilders = trackBuilders;
        this.Interactivity = interactivity;
        this._trackService = trackService;
        this._eurovisionBuilders = eurovisionBuilders;
        this._countryService = countryService;
    }

    [SlashCommand("track", "Shows track info for the track you're currently listening to or searching for")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task TrackAsync(
        [Summary("Track", "The track your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(TrackAutoComplete))]
        string name = null)
    {
        await DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await this._trackBuilders.TrackAsync(new ContextModel(this.Context, contextUser), name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("wktrack", "Shows what other users listen to a track in your server")]
    [UsernameSetRequired]
    public async Task WhoKnowsTrackAsync(
        [Summary("Track", "The track your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(TrackAutoComplete))]
        string name = null,
        [Summary("Mode", "The type of response you want - change default with /responsemode")]
        ResponseMode? mode = null,
        [Summary("Role-picker", "Display a rolepicker to filter with roles")]
        bool displayRoleFilter = false)
    {
        await DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        try
        {
            var response =
                await this._trackBuilders.WhoKnowsTrackAsync(new ContextModel(this.Context, contextUser), mode.Value,
                    name, displayRoleFilter);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.WhoKnowsTrackRolePicker}-*")]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task WhoKnowsFilteringAsync(string trackId, string[] inputs)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var track = await this._trackService.GetTrackForId(int.Parse(trackId));

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
            var response = await this._trackBuilders.WhoKnowsTrackAsync(new ContextModel(this.Context, contextUser),
                ResponseMode.Embed, $"{track.ArtistName} | {track.Name}", true, roleIds);

            await this.Context.UpdateInteractionEmbed(response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("fwktrack", "Who of your friends know a track")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task FriendsWhoKnowAlbumAsync(
        [Summary("Track", "The track your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(TrackAutoComplete))]
        string name = null,
        [Summary("Mode", "The type of response you want - change default with /responsemode")]
        ResponseMode? mode = null,
        [Summary("Private", "Only show response to you")]
        bool privateResponse = false)
    {
        await DeferAsync(privateResponse);

        var contextUser = await this._userService.GetUserWithFriendsAsync(this.Context.User);

        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        try
        {
            var response =
                await this._trackBuilders.FriendsWhoKnowTrackAsync(new ContextModel(this.Context, contextUser),
                    mode.Value, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("gwktrack", "Shows what other users listen to a track globally in .fmbot")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task GlobalWhoKnowsTrackAsync(
        [Summary("Track", "The track your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(TrackAutoComplete))]
        string name = null,
        [Summary("Mode", "The type of response you want - change default with /responsemode")]
        ResponseMode? mode = null,
        [Summary("Hide-private", "Hide or show private users")]
        bool hidePrivate = false)
    {
        await DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var currentSettings = new WhoKnowsSettings
        {
            HidePrivateUsers = hidePrivate,
            ShowBotters = false,
            AdminView = false,
            NewSearchValue = name,
            ResponseMode = mode ?? contextUser.Mode ?? ResponseMode.Embed
        };

        try
        {
            var response =
                await this._trackBuilders.GlobalWhoKnowsTrackAsync(new ContextModel(this.Context, contextUser),
                    currentSettings, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("trackplays", "Shows playcount for current track or the one you're searching for")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task TrackPlaysAsync(
        [Summary("Track", "The track your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(TrackAutoComplete))]
        string name = null,
        [Summary("User", "The user to show (defaults to self)")]
        string user = null)
    {
        await DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var response =
                await this._trackBuilders.TrackPlays(new ContextModel(this.Context, contextUser), userSettings, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("trackdetails", "Shows metadata details for current track or the one you're searching for")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task TrackDetailsAsync(
        [Summary("Track", "The track your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(TrackAutoComplete))]
        string name = null)
    {
        await DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await this._trackBuilders.TrackDetails(new ContextModel(this.Context, contextUser), name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.TrackPreview}-*")]
    [UsernameSetRequired]
    public async Task TrackPreviewAsync(string trackId)
    {
        await DeferAsync();

        await this.Context.DisableInteractionButtons();

        var parsedTrackId = int.Parse(trackId);
        var dbTrack = await this._trackService.GetTrackForId(parsedTrackId);

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var buttonBuilder = new ButtonBuilder();
        buttonBuilder.WithLabel("Open on Spotify");
        buttonBuilder.WithStyle(ButtonStyle.Link);
        buttonBuilder.WithUrl("https://open.spotify.com/track/" + dbTrack.SpotifyId);
        buttonBuilder.WithEmote(Emote.Parse(DiscordConstants.Spotify));

        await this.Context.AddButton(buttonBuilder);

        try
        {
            var response = await this._trackBuilders.TrackPreviewAsync(new ContextModel(this.Context, contextUser),
                $"{dbTrack.ArtistName} | {dbTrack.Name}", Context.Interaction.Token);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.TrackLyrics}-*")]
    [UsernameSetRequired]
    public async Task TrackLyricsAsync(string trackId)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var context = new ContextModel(this.Context, contextUser);
        var supporterRequiredResponse = TrackBuilders.LyricsSupporterRequired(context);

        if (supporterRequiredResponse != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse, true);
            this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
            return;
        }

        await DeferAsync();

        await this.Context.DisableInteractionButtons(specificButtonOnly: $"{InteractionConstants.TrackLyrics}-{trackId}");

        var parsedTrackId = int.Parse(trackId);
        var dbTrack = await this._trackService.GetTrackForId(parsedTrackId);

        try
        {
            var response =
                await this._trackBuilders.TrackLyricsAsync(context, $"{dbTrack.ArtistName} | {dbTrack.Name}");
            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("love", "Loves track you're currently listening to or searching for on Last.fm")]
    [UserSessionRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task LoveTrackAsync(
        [Summary("Track", "The track your want to love (defaults to currently playing)")]
        [Autocomplete(typeof(TrackAutoComplete))]
        string name = null,
        [Summary("Private", "Only show response to you")]
        bool privateResponse = false)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await this._trackBuilders.LoveTrackAsync(new ContextModel(this.Context, contextUser), name);

            await this.Context.SendResponse(this.Interactivity, response, privateResponse);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("unlove", "Removes track you're currently listening to or searching from your loved tracks")]
    [UserSessionRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task UnloveTrackAsync(
        [Summary("Track", "The track your want to remove (defaults to currently playing)")]
        [Autocomplete(typeof(TrackAutoComplete))]
        string name = null,
        [Summary("Private", "Only show response to you")]
        bool privateResponse = false)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response =
                await this._trackBuilders.UnLoveTrackAsync(new ContextModel(this.Context, contextUser), name);

            await this.Context.SendResponse(this.Interactivity, response, privateResponse);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("receipt", "Shows your track receipt. Based on Receiptify.")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task ReceiptAsync(
        [Summary("Time-period", "Time period")] [Autocomplete(typeof(DateTimeAutoComplete))]
        string timePeriod = null,
        [Summary("User", "The user to show (defaults to self)")]
        string user = null,
        [Summary("Private", "Only show response to you")]
        bool privateResponse = false)
    {
        await DeferAsync(privateResponse);

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var timeSettings = SettingService.GetTimePeriod(timePeriod, timeZone: userSettings.TimeZone);

        if (timeSettings.DefaultPicked)
        {
            var monthName = DateTime.UtcNow.AddDays(-24).ToString("MMM", CultureInfo.InvariantCulture);
            timeSettings = SettingService.GetTimePeriod(monthName, registeredLastFm: userSettings.RegisteredLastFm,
                timeZone: userSettings.TimeZone);
        }

        var response =
            await this._trackBuilders.GetReceipt(new ContextModel(this.Context, contextUser), userSettings,
                timeSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("loved", "Tracks you've loved on Last.fm")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task LovedTracksAsync(
        [Summary("User", "The user to show (defaults to self)")]
        string user = null,
        [Summary("Private", "Only show response to you")]
        bool privateResponse = false)
    {
        await DeferAsync(privateResponse);

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var response =
            await this._trackBuilders.LovedTracksAsync(new ContextModel(this.Context, contextUser), userSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("lyrics", "⭐ Shows lyrics for the track you're currently listening to or searching for")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task LyricsAsync(
        [Summary("Track", "The track you want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(TrackAutoComplete))]
        string name = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var context = new ContextModel(this.Context, contextUser);

        var supporterRequiredResponse = TrackBuilders.LyricsSupporterRequired(context);

        if (supporterRequiredResponse != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse);
            this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
            return;
        }

        await DeferAsync();

        try
        {
            var response = await this._trackBuilders.TrackLyricsAsync(context, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("scrobble", "Scrobbles a track on Last.fm")]
    [UserSessionRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task ScrobbleAsync(
        [Summary("Track", "The track your want to scrobble")] [Autocomplete(typeof(TrackAutoComplete))]
        string name = null,
        [Summary("Private", "Only show response to you")]
        bool privateResponse = false)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await this._trackBuilders.ScrobbleAsync(new ContextModel(this.Context, contextUser), name);

            await this.Context.SendResponse(this.Interactivity, response, privateResponse);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("eurovision", "View Eurovision overview for a year and/or a country")]
    [UserSessionRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task EurovisionAsync(
        [Summary("Year", "Year (1956 to now)")] [Autocomplete(typeof(YearAutoComplete))] string year = null,
        [Summary("Country", "Eurovision country")] [Autocomplete(typeof(EurovisionAutoComplete))] string country = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var context = new ContextModel(this.Context, contextUser);

        try
        {
            CountryInfo pickedCountry = null;
            if (country != null)
            {
                pickedCountry = this._countryService.GetValidCountry(country);
            }
            var pickedYear = SettingService.GetYear(year)?? DateTime.UtcNow.Year;

            ResponseModel response;
            if (pickedCountry != null)
            {
                response = await this._eurovisionBuilders.GetEurovisionCountryYear(context,
                    pickedCountry, pickedYear);
            }
            else
            {
                response =
                    await this._eurovisionBuilders.GetEurovisionYear(context, pickedYear);
            }

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
