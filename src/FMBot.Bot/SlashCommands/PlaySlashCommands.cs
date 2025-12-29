using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Models;
using FMBot.Bot.Models.Modals;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

public class PlaySlashCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly PlayBuilder _playBuilder;
    private readonly GuildService _guildService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly RecapBuilders _recapBuilders;

    private InteractiveService Interactivity { get; }

    private static readonly List<DateTimeOffset> StackCooldownTimer = new();
    private static readonly List<ulong> StackCooldownTarget = new();

    public PlaySlashCommands(UserService userService,
        SettingService settingService,
        PlayBuilder playBuilder,
        GuildService guildService,
        IDataSourceFactory dataSourceFactory,
        InteractiveService interactivity, RecapBuilders recapBuilders)
    {
        this._userService = userService;
        this._settingService = settingService;
        this._playBuilder = playBuilder;
        this._guildService = guildService;
        this._dataSourceFactory = dataSourceFactory;
        this.Interactivity = interactivity;
        this._recapBuilders = recapBuilders;
    }

    [SlashCommand("discoverydate", "⭐ Shows the date you discovered the artist, album, and track", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,     InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task DateDiscoveredAsync(
        [SlashCommandParameter(Name = "track", Description = "The track your want to search for (defaults to currently playing)", AutocompleteProviderType = typeof(TrackAutoComplete))]
        string name = null,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var context = new ContextModel(this.Context, contextUser);
        var userSettings =
            await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var supporterRequiredResponse = ArtistBuilders.DiscoverySupporterRequired(context, userSettings);

            if (supporterRequiredResponse != null)
            {
                await this.Context.SendFollowUpResponse(this.Interactivity, supporterRequiredResponse);
                this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
                return;
            }

            var response =
                await this._playBuilder.DiscoveryDate(context, name, userSettings);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("fm", "Now Playing - Shows you or someone else's current track", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,     InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task NowPlayingAsync([SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")] string user = null,
        [SlashCommandParameter(Name = "embed-type", Description = "The embed type to use, can also be configured as a setting")]FmEmbedType? embedType = null )
    {
        var existingFmCooldown = await this._guildService.GetChannelCooldown(this.Context.Channel?.Id);
        if (existingFmCooldown.HasValue)
        {
            if (StackCooldownTarget.Contains(this.Context.User.Id))
            {
                if (StackCooldownTimer[StackCooldownTarget.IndexOf(this.Context.User.Id)]
                        .AddSeconds(existingFmCooldown.Value) >= DateTimeOffset.Now)
                {
                    var secondsLeft = (int)(StackCooldownTimer[
                            StackCooldownTarget.IndexOf(this.Context.User.Id)]
                        .AddSeconds(existingFmCooldown.Value) - DateTimeOffset.Now).TotalSeconds;
                    if (secondsLeft <= existingFmCooldown.Value - 2)
                    {
                        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                            .WithContent($"This channel has a `{existingFmCooldown.Value}` second cooldown on `/fm`. Please wait for this to expire before using this command again.")
                            .WithFlags(MessageFlags.Ephemeral)));
                    }

                    return;
                }

                StackCooldownTimer[StackCooldownTarget.IndexOf(this.Context.User.Id)] = DateTimeOffset.Now;
            }
            else
            {
                StackCooldownTarget.Add(this.Context.User.Id);
                StackCooldownTimer.Add(DateTimeOffset.Now);
            }
        }

        await RespondAsync(InteractionCallback.DeferredMessage());

        try
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var userSettings =
                await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

            var response =
                await this._playBuilder.NowPlayingAsync(new ContextModel(this.Context, contextUser), userSettings, embedType ?? contextUser.FmEmbedType);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);

            var message = await this.Context.Interaction.GetResponseAsync();

            try
            {
                if (message != null &&
                    this.Context.Channel != null &&
                    response.CommandResponse == CommandResponse.Ok &&
                    this.Context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall))
                {
                }
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, "Could not add emote reactions", sendReply: false);
                await this.Context.Channel.SendMessageAsync(new MessageProperties { Content = "Could not add automatic emoji reactions to `/fm`. Make sure the emojis still exist, the bot is the same server as where the emojis come from and the bot has permission to `Add Reactions`." });
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("recent", "Shows you or someone else's recent tracks", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,     InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task RecentAsync(
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "artist", Description = "Artist you want to filter on", AutocompleteProviderType = typeof(ArtistAutoComplete))]
        string artistName = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var response = await this._playBuilder.RecentAsync(new ContextModel(this.Context, contextUser),
                userSettings, artistName);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("streak", "You or someone else's streak for an artist, album and track", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,     InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task StreakAsync(
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        var userWithStreak = await this._userService.GetUserAsync(userSettings.DiscordUserId);

        try
        {
            var response = await this._playBuilder.StreakAsync(new ContextModel(this.Context, contextUser),
                userSettings, userWithStreak);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
        }
    }

    [SlashCommand("streakhistory", "Shows you or someone else's streak history", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,     InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task StreakHistory(
        [SlashCommandParameter(Name = "editmode", Description = "Enable or disable editor mode")]
        bool editMode = false,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "artist", Description = "The artist you want to filter your results to", AutocompleteProviderType = typeof(ArtistAutoComplete))]
        string name = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var response = await this._playBuilder.StreakHistoryAsync(new ContextModel(this.Context, contextUser),
                userSettings, editMode, name);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
        }
    }

    [SlashCommand("overview", "Shows a daily overview", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,     InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task OverviewAsync(
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "amount", Description = "Amount of days to show")]
        int amount = 4)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        if (amount > 8)
        {
            amount = 8;
        }

        try
        {
            var response = await this._playBuilder.OverviewAsync(new ContextModel(this.Context, contextUser),
                userSettings, amount);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("pace", "Shows estimated date you reach a scrobble goal based on average scrobbles per day", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,     InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task PaceAsync(
        [SlashCommandParameter(Name = "amount", Description = "Goal scrobble amount")]
        int amount = 1,
        [SlashCommandParameter(Name = "time-period", Description = "Time period to base average playcount on", AutocompleteProviderType = typeof(DateTimeAutoComplete))]
        string timePeriod = null,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(userSettings.UserNameLastFm);
            var goalAmount = SettingService.GetGoalAmount(amount.ToString(), userInfo.Playcount);
            var timeSettings =
                SettingService.GetTimePeriod(timePeriod, TimePeriod.AllTime, timeZone: userSettings.TimeZone);

            var response = await this._playBuilder.PaceAsync(new ContextModel(this.Context, contextUser),
                userSettings, timeSettings, goalAmount, userInfo.Playcount, userInfo.RegisteredUnix);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("plays", "Shows your total scrobble count for a specific time period", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,     InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task PlaysAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period to base average playcount on", AutocompleteProviderType = typeof(DateTimeAutoComplete))]
        string timePeriod = null,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var timeSettings =
                SettingService.GetTimePeriod(timePeriod, TimePeriod.AllTime, timeZone: userSettings.TimeZone);

            var response = await this._playBuilder.PlaysAsync(new ContextModel(this.Context, contextUser), userSettings,
                timeSettings);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("milestone", "Shows a milestone scrobble", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,     InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task MileStoneAsync(
        [SlashCommandParameter(Name = "amount", Description = "Milestone scrobble amount")]
        int amount = 99999999,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(userSettings.UserNameLastFm);
            var mileStoneAmount = SettingService.GetMilestoneAmount(amount.ToString(), userInfo.Playcount);

            var response = await this._playBuilder.MileStoneAsync(new ContextModel(this.Context, contextUser),
                userSettings, mileStoneAmount.amount, userInfo.Playcount);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("year", "Shows an overview of your year")]
    [UsernameSetRequired]
    public async Task YearAsync(
        [SlashCommandParameter(Name = "year", Description = "Year to view")] int? year = null,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        var parsedYear = SettingService.GetYear(year?.ToString()).GetValueOrDefault(DateTime.UtcNow.AddDays(-30).Year);

        try
        {
            var response = await this._playBuilder.YearAsync(new ContextModel(this.Context, contextUser),
                userSettings, parsedYear);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("recap", "Shows a recap for your selected time period", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,     InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task RecapAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period to show (defaults to year)", AutocompleteProviderType = typeof(DateTimeAutoComplete))]
        string timePeriod = null,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var year = SettingService.GetYear(timePeriod).GetValueOrDefault(DateTime.UtcNow.AddDays(-90).Year);
            var selectedTimePeriod = !string.IsNullOrWhiteSpace(timePeriod) ? timePeriod : year.ToString();

            var timeSettings = SettingService.GetTimePeriod(selectedTimePeriod, TimePeriod.AllTime,
                timeZone: userSettings.TimeZone);

            var response = await this._recapBuilders.RecapAsync(new ContextModel(this.Context, contextUser),
                userSettings, timeSettings, RecapPage.Overview);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("gaps", "⭐ Music you've returned to after a gap in listening", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,     InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task ListeningGapsAsync(
        [SlashCommandParameter(Name = "type", Description = "Music gap type")] GapEntityType gapType = GapEntityType.Artist,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "mode", Description = "The type of response you want - change default with /responsemode")]
        ResponseMode? mode = null,
        [SlashCommandParameter(Name = "size", Description = "Amount of listening gaps to show per page")]
        EmbedSize? embedSize = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
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

        var topListSettings = new TopListSettings(embedSize ?? EmbedSize.Default);

        var response =
            await this._playBuilder.ListeningGapsAsync(context, topListSettings, userSettings, mode.Value, gapType);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
        this.Context.LogCommandUsed(response.CommandResponse);
    }
}
