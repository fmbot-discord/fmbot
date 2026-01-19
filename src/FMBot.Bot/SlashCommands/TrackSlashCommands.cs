using System;
using System.Globalization;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using NetCord.Services.ApplicationCommands;
using NetCord;
using NetCord.Rest;
using Fergun.Interactive;

namespace FMBot.Bot.SlashCommands;

public class TrackSlashCommands(
    UserService userService,
    SettingService settingService,
    TrackBuilders trackBuilders,
    InteractiveService interactivity,
    EurovisionBuilders eurovisionBuilders,
    CountryService countryService)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;

    [SlashCommand("track", "Shows track info for the track you're currently listening to or searching for",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task TrackAsync(
        [SlashCommandParameter(Name = "track",
            Description = "The track your want to search for (defaults to currently playing)",
            AutocompleteProviderType = typeof(TrackAutoComplete))]
        string name = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await trackBuilders.TrackAsync(new ContextModel(this.Context, contextUser), name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("wktrack", "Shows what other users listen to a track in your server")]
    [UsernameSetRequired]
    public async Task WhoKnowsTrackAsync(
        [SlashCommandParameter(Name = "track",
            Description = "The track your want to search for (defaults to currently playing)",
            AutocompleteProviderType = typeof(TrackAutoComplete))]
        string name = null,
        [SlashCommandParameter(Name = "mode",
            Description = "The type of response you want - change default with /responsemode")]
        ResponseMode? mode = null,
        [SlashCommandParameter(Name = "role-picker", Description = "Display a rolepicker to filter with roles")]
        bool displayRoleFilter = false)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        try
        {
            var response =
                await trackBuilders.WhoKnowsTrackAsync(new ContextModel(this.Context, contextUser), mode.Value,
                    name, displayRoleFilter);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("fwktrack", "Who of your friends know a track",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task FriendsWhoKnowAlbumAsync(
        [SlashCommandParameter(Name = "track",
            Description = "The track your want to search for (defaults to currently playing)",
            AutocompleteProviderType = typeof(TrackAutoComplete))]
        string name = null,
        [SlashCommandParameter(Name = "mode",
            Description = "The type of response you want - change default with /responsemode")]
        ResponseMode? mode = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(
            InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await userService.GetUserWithFriendsAsync(this.Context.User);

        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        try
        {
            var response =
                await trackBuilders.FriendsWhoKnowTrackAsync(new ContextModel(this.Context, contextUser),
                    mode.Value, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, privateResponse);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("gwktrack", "Shows what other users listen to a track globally in .fmbot",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task GlobalWhoKnowsTrackAsync(
        [SlashCommandParameter(Name = "track",
            Description = "The track your want to search for (defaults to currently playing)",
            AutocompleteProviderType = typeof(TrackAutoComplete))]
        string name = null,
        [SlashCommandParameter(Name = "mode",
            Description = "The type of response you want - change default with /responsemode")]
        ResponseMode? mode = null,
        [SlashCommandParameter(Name = "hide-private", Description = "Hide or show private users")]
        bool hidePrivate = false)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

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
                await trackBuilders.GlobalWhoKnowsTrackAsync(new ContextModel(this.Context, contextUser),
                    currentSettings, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("trackplays", "Shows playcount for current track or the one you're searching for",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task TrackPlaysAsync(
        [SlashCommandParameter(Name = "track",
            Description = "The track your want to search for (defaults to currently playing)",
            AutocompleteProviderType = typeof(TrackAutoComplete))]
        string name = null,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var response =
                await trackBuilders.TrackPlays(new ContextModel(this.Context, contextUser), userSettings, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("trackdetails", "Shows metadata details for current track or the one you're searching for",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task TrackDetailsAsync(
        [SlashCommandParameter(Name = "track",
            Description = "The track your want to search for (defaults to currently playing)",
            AutocompleteProviderType = typeof(TrackAutoComplete))]
        string name = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await trackBuilders.TrackDetails(new ContextModel(this.Context, contextUser), name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("love", "Loves track you're currently listening to or searching for on Last.fm",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UserSessionRequired]
    public async Task LoveTrackAsync(
        [SlashCommandParameter(Name = "track",
            Description = "The track your want to love (defaults to currently playing)",
            AutocompleteProviderType = typeof(TrackAutoComplete))]
        string name = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await trackBuilders.LoveTrackAsync(new ContextModel(this.Context, contextUser), name);

            await this.Context.SendResponse(this.Interactivity, response, userService, privateResponse);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("unlove", "Removes track you're currently listening to or searching from your loved tracks",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UserSessionRequired]
    public async Task UnloveTrackAsync(
        [SlashCommandParameter(Name = "track",
            Description = "The track your want to remove (defaults to currently playing)",
            AutocompleteProviderType = typeof(TrackAutoComplete))]
        string name = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response =
                await trackBuilders.UnLoveTrackAsync(new ContextModel(this.Context, contextUser), name);

            await this.Context.SendResponse(this.Interactivity, response, userService, privateResponse);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("receipt", "Shows your track receipt. Based on Receiptify.",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task ReceiptAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period",
            AutocompleteProviderType = typeof(DateTimeAutoComplete))]
        string timePeriod = null,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(
            InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var timeSettings = SettingService.GetTimePeriod(timePeriod, timeZone: userSettings.TimeZone);

        if (timeSettings.DefaultPicked)
        {
            var monthName = DateTime.UtcNow.AddDays(-24).ToString("MMM", CultureInfo.InvariantCulture);
            timeSettings = SettingService.GetTimePeriod(monthName, registeredLastFm: userSettings.RegisteredLastFm,
                timeZone: userSettings.TimeZone);
        }

        var response =
            await trackBuilders.GetReceipt(new ContextModel(this.Context, contextUser), userSettings,
                timeSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, privateResponse);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [SlashCommand("loved", "Tracks you've loved on Last.fm",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task LovedTracksAsync(
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(
            InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var response =
            await trackBuilders.LovedTracksAsync(new ContextModel(this.Context, contextUser), userSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, privateResponse);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [SlashCommand("lyrics", "⭐ Shows lyrics for the track you're currently listening to or searching for",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    [SupportsPagination]
    public async Task LyricsAsync(
        [SlashCommandParameter(Name = "track",
            Description = "The track you want to search for (defaults to currently playing)",
            AutocompleteProviderType = typeof(TrackAutoComplete))]
        string name = null)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        var context = new ContextModel(this.Context, contextUser);

        var supporterRequiredResponse = TrackBuilders.LyricsSupporterRequired(context);

        if (supporterRequiredResponse != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse, userService);
            await this.Context.LogCommandUsedAsync(supporterRequiredResponse, userService);
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage());

        try
        {
            var response = await trackBuilders.TrackLyricsAsync(context, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("scrobble", "Scrobbles a track on Last.fm",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UserSessionRequired]
    public async Task ScrobbleAsync(
        [SlashCommandParameter(Name = "track", Description = "The track your want to scrobble",
            AutocompleteProviderType = typeof(TrackAutoComplete))]
        string name = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await trackBuilders.ScrobbleAsync(new ContextModel(this.Context, contextUser), name);

            await this.Context.SendResponse(this.Interactivity, response, userService, privateResponse);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("eurovision", "View Eurovision overview for a year and/or a country",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UserSessionRequired]
    public async Task EurovisionAsync(
        [SlashCommandParameter(Name = "year", Description = "Year (1956 to now)",
            AutocompleteProviderType = typeof(YearAutoComplete))]
        string year = null,
        [SlashCommandParameter(Name = "country", Description = "Eurovision country",
            AutocompleteProviderType = typeof(EurovisionAutoComplete))]
        string country = null)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var context = new ContextModel(this.Context, contextUser);

        try
        {
            CountryInfo pickedCountry = null;
            if (country != null)
            {
                pickedCountry = countryService.GetValidCountry(country);
            }

            var pickedYear = SettingService.GetYear(year) ?? DateTime.UtcNow.Year;

            ResponseModel response;
            if (pickedCountry != null)
            {
                response = await eurovisionBuilders.GetEurovisionCountryYear(context,
                    pickedCountry, pickedYear);
            }
            else
            {
                response =
                    await eurovisionBuilders.GetEurovisionYear(context, pickedYear);
            }

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
