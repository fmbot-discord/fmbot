using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Enums;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

public class PlaySlashCommands(
    UserService userService,
    SettingService settingService,
    PlayBuilder playBuilder,
    GuildService guildService,
    IDataSourceFactory dataSourceFactory,
    InteractiveService interactivity,
    RecapBuilders recapBuilders)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;

    private static readonly List<DateTimeOffset> StackCooldownTimer = [];
    private static readonly List<ulong> StackCooldownTarget = [];

    [SlashCommand("discoverydate", "⭐ Shows the date you discovered the artist, album, and track",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task DateDiscoveredAsync(
        [SlashCommandParameter(Name = "track",
            Description = "The track you want to search for (defaults to currently playing)",
            AutocompleteProviderType = typeof(TrackAutoComplete))]
        string name = null,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var context = new ContextModel(this.Context, contextUser);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var supporterRequiredResponse = ArtistBuilders.DiscoverySupporterRequired(context, userSettings);

            if (supporterRequiredResponse != null)
            {
                await this.Context.SendFollowUpResponse(this.Interactivity, supporterRequiredResponse, userService);
                await this.Context.LogCommandUsedAsync(supporterRequiredResponse, userService);
                return;
            }

            var response =
                await playBuilder.DiscoveryDate(context, name, userSettings);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("fm", "Now Playing - Shows you or someone else's current track",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task NowPlayingAsync(
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "embed-type",
            Description = "The embed type to use, can also be configured as a setting")]
        FmEmbedType? embedType = null)
    {
        var existingFmCooldown = await guildService.GetChannelCooldown(this.Context.Channel?.Id);
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
                            .WithContent(
                                $"This channel has a `{existingFmCooldown.Value}` second cooldown on `/fm`. Please wait for this to expire before using this command again.")
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
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var userSettings =
                await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

            var response =
                await playBuilder.NowPlayingAsync(new ContextModel(this.Context, contextUser), userSettings,
                    embedType ?? contextUser.FmSetting?.EmbedType ?? FmEmbedType.EmbedMini);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);

            var message = await this.Context.Interaction.GetResponseAsync();

            try
            {
                if (message != null &&
                    this.Context.Channel != null &&
                    response.CommandResponse == CommandResponse.Ok &&
                    this.Context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType
                        .GuildInstall))
                {
                }
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, userService, "Could not add emote reactions", sendReply: false);
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Channel.Id, new MessageProperties
                {
                    Content =
                        "Could not add automatic emoji reactions to `/fm`. Make sure the emojis still exist, the bot is the same server as where the emojis come from and the bot has permission to `Add Reactions`."
                });
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("recent", "Shows you or someone else's recent tracks",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task RecentAsync(
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "artist", Description = "Artist you want to filter on",
            AutocompleteProviderType = typeof(ArtistAutoComplete))]
        string artistName = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var response = await playBuilder.RecentAsync(new ContextModel(this.Context, contextUser),
                userSettings, artistName);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("streak", "Your or someone else's streak for an artist, album, and track",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task StreakAsync(
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        var userWithStreak = await userService.GetUserAsync(userSettings.DiscordUserId);

        try
        {
            var response = await playBuilder.StreakAsync(new ContextModel(this.Context, contextUser),
                userSettings, userWithStreak);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
        }
    }

    [SlashCommand("streakhistory", "Shows you or someone else's streak history",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task StreakHistory(
        [SlashCommandParameter(Name = "editmode", Description = "Enable or disable editor mode")]
        bool editMode = false,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "artist", Description = "The artist you want to filter your results to",
            AutocompleteProviderType = typeof(ArtistAutoComplete))]
        string name = null)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var response = await playBuilder.StreakHistoryAsync(new ContextModel(this.Context, contextUser),
                userSettings, editMode, name);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
        }
    }

    [SlashCommand("overview", "Shows a daily overview",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task OverviewAsync(
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "amount", Description = "Amount of days to show")]
        int amount = 4)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        if (amount > 8)
        {
            amount = 8;
        }

        try
        {
            var response = await playBuilder.OverviewAsync(new ContextModel(this.Context, contextUser),
                userSettings, amount);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("pace", "Shows the estimated date you'll reach a scrobble goal based on average scrobbles per day",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task PaceAsync(
        [SlashCommandParameter(Name = "amount", Description = "Goal scrobble amount")]
        int amount = 1,
        [SlashCommandParameter(Name = "time-period", Description = "Time period to base average playcount on",
            AutocompleteProviderType = typeof(DateTimeAutoComplete))]
        string timePeriod = null,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var userInfo = await dataSourceFactory.GetLfmUserInfoAsync(userSettings.UserNameLastFm);
            var goalAmount = SettingService.GetGoalAmount(amount.ToString(), userInfo.Playcount);
            var timeSettings =
                SettingService.GetTimePeriod(timePeriod, TimePeriod.AllTime, timeZone: userSettings.TimeZone);

            var response = await playBuilder.PaceAsync(new ContextModel(this.Context, contextUser),
                userSettings, timeSettings, goalAmount, userInfo.Playcount, userInfo.RegisteredUnix);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("plays", "Shows your total scrobble count for a specific time period",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task PlaysAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period to base average playcount on",
            AutocompleteProviderType = typeof(DateTimeAutoComplete))]
        string timePeriod = null,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var timeSettings =
                SettingService.GetTimePeriod(timePeriod, TimePeriod.AllTime, timeZone: userSettings.TimeZone);

            var response = await playBuilder.PlaysAsync(new ContextModel(this.Context, contextUser), userSettings,
                timeSettings);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("milestone", "Shows a milestone scrobble",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task MileStoneAsync(
        [SlashCommandParameter(Name = "amount", Description = "Milestone scrobble amount")]
        int amount = 99999999,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var userInfo = await dataSourceFactory.GetLfmUserInfoAsync(userSettings.UserNameLastFm);
            var mileStoneAmount = SettingService.GetMilestoneAmount(amount.ToString(), userInfo.Playcount);

            var response = await playBuilder.MileStoneAsync(new ContextModel(this.Context, contextUser),
                userSettings, mileStoneAmount.amount, userInfo.Playcount);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("year", "Shows an overview of your year")]
    [UsernameSetRequired]
    public async Task YearAsync(
        [SlashCommandParameter(Name = "year", Description = "Year to view")]
        int? year = null,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        var parsedYear = SettingService.GetYear(year?.ToString()).GetValueOrDefault(DateTime.UtcNow.AddDays(-30).Year);

        try
        {
            var response = await playBuilder.YearAsync(new ContextModel(this.Context, contextUser),
                userSettings, parsedYear);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("recap", "Shows a recap for your selected time period",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task RecapAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period to show (defaults to year)",
            AutocompleteProviderType = typeof(DateTimeAutoComplete))]
        string timePeriod = null,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var year = SettingService.GetYear(timePeriod).GetValueOrDefault(DateTime.UtcNow.AddDays(-90).Year);
            var selectedTimePeriod = !string.IsNullOrWhiteSpace(timePeriod) ? timePeriod : year.ToString();

            var timeSettings = SettingService.GetTimePeriod(selectedTimePeriod, TimePeriod.AllTime,
                timeZone: userSettings.TimeZone);

            var response = await recapBuilders.RecapAsync(new ContextModel(this.Context, contextUser),
                userSettings, timeSettings, RecapPage.Overview);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("gaps", "⭐ Music you've returned to after a gap in listening",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task ListeningGapsAsync(
        [SlashCommandParameter(Name = "type", Description = "Music gap type")]
        GapEntityType gapType = GapEntityType.Artist,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "mode",
            Description = "The type of response you want - change default with /responsemode")]
        ResponseMode? mode = null,
        [SlashCommandParameter(Name = "size", Description = "Amount of listening gaps to show per page")]
        EmbedSize? embedSize = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        mode ??= contextUser.Mode ?? ResponseMode.Embed;

        var context = new ContextModel(this.Context, contextUser);

        var supporterRequiredResponse = ArtistBuilders.DiscoverySupporterRequired(context, userSettings);

        if (supporterRequiredResponse != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse, userService);
            await this.Context.LogCommandUsedAsync(supporterRequiredResponse, userService);
            return;
        }

        await Context.Interaction.SendResponseAsync(
            InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var topListSettings = new TopListSettings(embedSize ?? EmbedSize.Default);

        var response =
            await playBuilder.ListeningGapsAsync(context, topListSettings, userSettings, mode.Value, gapType);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, privateResponse);
        await this.Context.LogCommandUsedAsync(response, userService);
    }
}
