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

namespace FMBot.Bot.SlashCommands;

public class TrackSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly TrackBuilders _trackBuilders;
    private readonly TrackService _trackService;

    private InteractiveService Interactivity { get; }


    public TrackSlashCommands(UserService userService,
        SettingService settingService,
        TrackBuilders trackBuilders,
        InteractiveService interactivity,
        TrackService trackService)
    {
        this._userService = userService;
        this._settingService = settingService;
        this._trackBuilders = trackBuilders;
        this.Interactivity = interactivity;
        this._trackService = trackService;
    }

    [SlashCommand("track", "Shows track info for the track you're currently listening to or searching for")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task TrackAsync(
        [Summary("Track", "The track your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(TrackAutoComplete))] string name = null)
    {
        _ = DeferAsync();

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
        [Autocomplete(typeof(TrackAutoComplete))] string name = null,
        [Summary("Mode", "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [Summary("Role-picker", "Display a rolepicker to filter with roles")] bool displayRoleFilter = false)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        try
        {
            var response =
                await this._trackBuilders.WhoKnowsTrackAsync(new ContextModel(this.Context, contextUser), mode.Value, name, displayRoleFilter);

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
            var response = await this._trackBuilders.WhoKnowsTrackAsync(new ContextModel(this.Context, contextUser), ResponseMode.Embed, $"{track.ArtistName} | {track.Name}", true, roleIds);

            await this.Context.UpdateInteractionEmbed(response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("fwktrack", "Shows who of your friends listen to a track")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task FriendsWhoKnowAlbumAsync(
        [Summary("Track", "The track your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(TrackAutoComplete))] string name = null,
        [Summary("Mode", "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [Summary("Private", "Only show response to you")] bool privateResponse = false)
    {
        _ = DeferAsync(privateResponse);

        var contextUser = await this._userService.GetUserWithFriendsAsync(this.Context.User);

        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        try
        {
            var response = await this._trackBuilders.FriendsWhoKnowTrackAsync(new ContextModel(this.Context, contextUser), mode.Value, name);

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
    public async Task GlobalWhoKnowsTrackAsync(
        [Summary("Track", "The track your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(TrackAutoComplete))] string name = null,
        [Summary("Mode", "The type of response you want - change default with /responsemode")] ResponseMode? mode = null,
        [Summary("Hide-private", "Hide or show private users")] bool hidePrivate = false)
    {
        _ = DeferAsync();

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
                await this._trackBuilders.GlobalWhoKnowsTrackAsync(new ContextModel(this.Context, contextUser), currentSettings, name);

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
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task TrackPlaysAsync(
        [Summary("Track", "The track your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(TrackAutoComplete))] string name = null,
        [Summary("User", "The user to show (defaults to self)")] string user = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var response = await this._trackBuilders.TrackPlays(new ContextModel(this.Context, contextUser), userSettings, name);

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
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task TrackDetailsAsync(
        [Summary("Track", "The track your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(TrackAutoComplete))] string name = null)
    {
        _ = DeferAsync();

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

    [SlashCommand("love", "Loves track you're currently listening to or searching for on Last.fm")]
    [UserSessionRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task LoveTrackAsync(
        [Summary("Track", "The track your want to love (defaults to currently playing)")]
        [Autocomplete(typeof(TrackAutoComplete))] string name = null,
        [Summary("Private", "Only show response to you")] bool privateResponse = false)
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
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task UnloveTrackAsync(
        [Summary("Track", "The track your want to remove (defaults to currently playing)")]
        [Autocomplete(typeof(TrackAutoComplete))] string name = null,
        [Summary("Private", "Only show response to you")] bool privateResponse = false)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await this._trackBuilders.UnLoveTrackAsync(new ContextModel(this.Context, contextUser), name);

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
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task ReceiptAsync(
        [Summary("Time-period", "Time period")][Autocomplete(typeof(DateTimeAutoComplete))] string timePeriod = null,
        [Summary("User", "The user to show (defaults to self)")] string user = null,
        [Summary("Private", "Only show response to you")] bool privateResponse = false)
    {
        _ = DeferAsync(privateResponse);

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var timeSettings = SettingService.GetTimePeriod(timePeriod, timeZone: userSettings.TimeZone);

        if (timeSettings.DefaultPicked)
        {
            var monthName = DateTime.UtcNow.AddDays(-24).ToString("MMM", CultureInfo.InvariantCulture);
            timeSettings = SettingService.GetTimePeriod(monthName, registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone);
        }

        var response = await this._trackBuilders.GetReceipt(new ContextModel(this.Context, contextUser), userSettings, timeSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
        this.Context.LogCommandUsed(response.CommandResponse);
    }
}
