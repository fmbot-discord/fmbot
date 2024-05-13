using System;
using System.Collections.Generic;
using System.Linq;
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
using FMBot.Bot.Models.Modals;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using SummaryAttribute = Discord.Interactions.SummaryAttribute;

namespace FMBot.Bot.SlashCommands;

public class PlaySlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly PlayBuilder _playBuilder;
    private readonly GuildService _guildService;
    private readonly IDataSourceFactory _dataSourceFactory;

    private InteractiveService Interactivity { get; }

    private static readonly List<DateTimeOffset> StackCooldownTimer = new();
    private static readonly List<ulong> StackCooldownTarget = new();

    public PlaySlashCommands(UserService userService,
        SettingService settingService,
        PlayBuilder playBuilder,
        GuildService guildService,
        IDataSourceFactory dataSourceFactory,
        InteractiveService interactivity)
    {
        this._userService = userService;
        this._settingService = settingService;
        this._playBuilder = playBuilder;
        this._guildService = guildService;
        this._dataSourceFactory = dataSourceFactory;
        this.Interactivity = interactivity;
    }

    [SlashCommand("fm", "Now Playing - Shows you or someone else their current track")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task NowPlayingAsync([Summary("user", "The user to show (defaults to self)")] string user = null)
    {
        var existingFmCooldown = await this._guildService.GetChannelCooldown(this.Context.Channel?.Id);
        if (existingFmCooldown.HasValue)
        {
            if (StackCooldownTarget.Contains(this.Context.User.Id))
            {
                if (StackCooldownTimer[StackCooldownTarget.IndexOf(this.Context.User.Id)].AddSeconds(existingFmCooldown.Value) >= DateTimeOffset.Now)
                {
                    var secondsLeft = (int)(StackCooldownTimer[
                            StackCooldownTarget.IndexOf(this.Context.User.Id)]
                        .AddSeconds(existingFmCooldown.Value) - DateTimeOffset.Now).TotalSeconds;
                    if (secondsLeft <= existingFmCooldown.Value - 2)
                    {
                        await RespondAsync(
                            $"This channel has a `{existingFmCooldown.Value}` second cooldown on `/fm`. Please wait for this to expire before using this command again.",
                            ephemeral: true);
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

        _ = DeferAsync();

        try
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var userSettings =
                await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

            var response =
                await this._playBuilder.NowPlayingAsync(new ContextModel(this.Context, contextUser), userSettings);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);

            var message = await this.Context.Interaction.GetOriginalResponseAsync();

            try
            {
                if (message != null && this.Context.Channel != null && response.CommandResponse == CommandResponse.Ok)
                {
                    if (contextUser.EmoteReactions != null && contextUser.EmoteReactions.Any() && SupporterService.IsSupporter(contextUser.UserType))
                    {
                        await GuildService.AddReactionsAsync(message, contextUser.EmoteReactions);
                    }
                    else if (this.Context.Guild != null)
                    {
                        await this._guildService.AddGuildReactionsAsync(message, this.Context.Guild);
                    }
                }
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, "Could not add emote reactions", sendReply: false);
                await ReplyAsync(
                    $"Could not add automatic emoji reactions to `/fm`. Make sure the emojis still exist, the bot is the same server as where the emojis come from and the bot has permission to `Add Reactions`.");
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("recent", "Shows you or someone else their recent tracks")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task RecentAsync(
        [Summary("User", "The user to show (defaults to self)")] string user = null,
        [Summary("Artist", "Artist you want to filter on (Supporter only)")]
        [Autocomplete(typeof(ArtistAutoComplete))] string artistName = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

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

    [SlashCommand("streak", "Shows you or someone else their streak")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task StreakAsync(
        [Summary("User", "The user to show (defaults to self)")] string user = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
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

    [SlashCommand("streakhistory", "Shows you or someone else their streak history")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task StreakHistory(
        [Summary("Editmode", "Enable or disable editor mode")] bool editMode = false,
        [Summary("User", "The user to show (defaults to self)")] string user = null,
        [Summary("Artist", "The artist you want to filter your results to")]
        [Autocomplete(typeof(ArtistAutoComplete))] string name = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var response = await this._playBuilder.StreakHistoryAsync(new ContextModel(this.Context, contextUser), userSettings, editMode, name);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.DeleteStreak)]
    public async Task StreakDeleteButton()
    {
        await this.Context.Interaction.RespondWithModalAsync<DeleteStreakModal>(InteractionConstants.DeleteStreakModal);
    }

    [ModalInteraction(InteractionConstants.DeleteStreakModal)]
    public async Task StreakDeleteButton(DeleteStreakModal modal)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (!long.TryParse(modal.StreakId, out var streakId))
        {
            await RespondAsync("Invalid input. Please enter the ID of the streak you want to delete.", ephemeral: true);
            return;
        }

        var response = await this._playBuilder.DeleteStreakAsync(new ContextModel(this.Context, contextUser), streakId);

        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("overview", "Shows a daily overview")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task OverviewAsync(
        [Summary("User", "The user to show (defaults to self)")] string user = null,
        [Summary("Amount", "Amount of days to show")] int amount = 4)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

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

    [SlashCommand("pace", "Shows estimated date you reach a scrobble goal based on average scrobbles per day")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task PaceAsync(
        [Summary("Amount", "Goal scrobble amount")] int amount = 1,
        [Summary("Time-period", "Time period to base average playcount on")][Autocomplete(typeof(DateTimeAutoComplete))] string timePeriod = null,
        [Summary("User", "The user to show (defaults to self)")] string user = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(userSettings.UserNameLastFm);
            var goalAmount = SettingService.GetGoalAmount(amount.ToString(), userInfo.Playcount);
            var timeSettings = SettingService.GetTimePeriod(timePeriod, TimePeriod.AllTime, timeZone: userSettings.TimeZone);

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

    [SlashCommand("milestone", "Shows a milestone scrobble")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task MileStoneAsync(
        [Summary("Amount", "Milestone scrobble amount")] int amount = 99999999,
        [Summary("User", "The user to show (defaults to self)")] string user = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

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

    [ComponentInteraction($"{InteractionConstants.RandomMilestone}-*-*")]
    [UsernameSetRequired]
    public async Task RandomMilestoneAsync(string discordUser, string requesterDiscordUser)
    {
        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);

        if (this.Context.User.Id != requesterDiscordUserId)
        {
            await RespondAsync("🎲 Sorry, only the user that requested the random milestone can reroll.", ephemeral: true);
            return;
        }

        _ = DeferAsync();
        await this.Context.DisableInteractionButtons();

        var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);
        var targetUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);

        try
        {
            var mileStoneAmount = SettingService.GetMilestoneAmount("random", targetUser.TotalPlaycount.GetValueOrDefault());

            var response = await this._playBuilder.MileStoneAsync(new ContextModel(this.Context, contextUser),
                userSettings, mileStoneAmount.amount, targetUser.TotalPlaycount.GetValueOrDefault(), mileStoneAmount.isRandom);

            await this.Context.UpdateInteractionEmbed(response, this.Interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);

            var message = (this.Context.Interaction as SocketMessageComponent)?.Message;
            if (message != null && response.ReferencedMusic != null && PublicProperties.UsedCommandsResponseContextId.TryGetValue(message.Id, out var contextId))
            {
                await this._userService.UpdateInteractionContext(contextId, response.ReferencedMusic);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("year", "Shows an overview of your year")]
    [UsernameSetRequired]
    public async Task YearAsync(
        [Summary("Year", "Year to view")] int? year = null,
        [Summary("User", "The user to show (defaults to self)")] string user = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        var parsedYear = SettingService.GetYear(year?.ToString()).GetValueOrDefault(DateTime.UtcNow.AddDays(-90).Year);

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
}
