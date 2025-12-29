using System;
using System.Linq;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class PlayInteractions : ComponentInteractionModule<ComponentInteractionContext>
{
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly PlayBuilder _playBuilder;
    private readonly RecapBuilders _recapBuilders;
    private readonly InteractiveService _interactivity;

    public PlayInteractions(
        UserService userService,
        SettingService settingService,
        PlayBuilder playBuilder,
        RecapBuilders recapBuilders,
        InteractiveService interactivity)
    {
        this._userService = userService;
        this._settingService = settingService;
        this._playBuilder = playBuilder;
        this._recapBuilders = recapBuilders;
        this._interactivity = interactivity;
    }

    [ComponentInteraction(InteractionConstants.DeleteStreak)]
    public async Task StreakDeleteOpenModal()
    {
        await RespondAsync(InteractionCallback.Modal(
            ModalFactory.CreateDeleteStreakModal(InteractionConstants.DeleteStreakModal)));
    }

    [ComponentInteraction(InteractionConstants.DeleteStreakModal)]
    public async Task StreakDeleteButton()
    {
        var streakIdStr = this.Context.GetModalValue("streak_id");
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (!long.TryParse(streakIdStr, out var streakId))
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Invalid input. Please enter the ID of the streak you want to delete.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        var response = await this._playBuilder.DeleteStreakAsync(new ContextModel(this.Context, contextUser), streakId);

        await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [ComponentInteraction(InteractionConstants.RandomMilestone)]
    [UsernameSetRequired]
    public async Task RandomMilestoneAsync(string discordUser, string requesterDiscordUser)
    {
        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);

        if (this.Context.User.Id != requesterDiscordUserId)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("🎲 Sorry, only the user that requested the random milestone can reroll.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage());
        await this.Context.DisableInteractionButtons();

        var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId,
            this.Context.Guild, this.Context.User);
        var targetUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);

        try
        {
            var mileStoneAmount =
                SettingService.GetMilestoneAmount("random", targetUser.TotalPlaycount.GetValueOrDefault());

            var response = await this._playBuilder.MileStoneAsync(new ContextModel(this.Context, contextUser),
                userSettings, mileStoneAmount.amount, targetUser.TotalPlaycount.GetValueOrDefault(),
                mileStoneAmount.isRandom);

            await this.Context.UpdateInteractionEmbed(response, this._interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);

            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
            if (message != null && response.ReferencedMusic != null &&
                PublicProperties.UsedCommandsResponseContextId.TryGetValue(message.Id, out var contextId))
            {
                await this._userService.UpdateInteractionContext(contextId, response.ReferencedMusic);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.RecapAlltime)]
    [UsernameSetRequired]
    public async Task RecapAllTime(string userId)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());
        _ = this.Context.DisableInteractionButtons(specificButtonOnly: $"{InteractionConstants.RecapAlltime}:{userId}",
            addLoaderToSpecificButton: true);

        var contextUser = await this._userService.GetUserForIdAsync(int.Parse(userId));
        var userSettings =
            await this._settingService.GetUser(null, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var timeSettings =
                SettingService.GetTimePeriod("alltime", TimePeriod.AllTime, timeZone: userSettings.TimeZone);

            var response = await this._recapBuilders.RecapAsync(new ContextModel(this.Context, contextUser),
                userSettings, timeSettings, RecapPage.Overview);

            await this.Context.SendFollowUpResponse(this._interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);

            _ = this.Context.DisableInteractionButtons(
                specificButtonOnly: $"{InteractionConstants.RecapAlltime}:{userId}");
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.RecapPicker)]
    [RequiresIndex]
    [GuildOnly]
    public async Task RecapAsync(params string[] inputs)
    {
        try
        {
            var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
            var splitInput = stringMenuInteraction.Data.SelectedValues.First().Split("-");
            if (!Enum.TryParse(splitInput[0], out RecapPage viewType))
            {
                return;
            }

            var discordUserId = ulong.Parse(splitInput[1]);
            var requesterDiscordUserId = ulong.Parse(splitInput[2]);

            var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
            var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
            var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId,
                this.Context.Guild, this.Context.User);

            var timeSettings = SettingService.GetTimePeriod(splitInput[3],
                registeredLastFm: userSettings.RegisteredLastFm,
                timeZone: userSettings.TimeZone, defaultTimePeriod: TimePeriod.Yearly);

            if (userSettings.DiscordUserId != this.Context.User.Id &&
                (viewType == RecapPage.BotStats ||
                 viewType == RecapPage.BotStatsArtists ||
                 viewType == RecapPage.BotStatsCommands))
            {
                var noPermResponse = new ResponseModel();
                noPermResponse.Embed.WithDescription(
                    "Sorry, due to privacy reasons only the user themselves can look up their bot usage stats.");
                noPermResponse.CommandResponse = CommandResponse.NoPermission;
                noPermResponse.ResponseType = ResponseType.Embed;
                noPermResponse.Embed.WithColor(DiscordConstants.WarningColorOrange);
                await this.Context.SendResponse(this._interactivity, noPermResponse, true);
                this.Context.LogCommandUsed(noPermResponse.CommandResponse);
                return;
            }

            await RespondAsync(InteractionCallback.DeferredMessage());

            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
            if (message == null)
            {
                return;
            }

            var name = viewType.GetAttribute<OptionAttribute>().Name;
            var components =
                new ActionRowProperties().WithButton($"{name} for {timeSettings.Description} loading...", customId: "1",
                    emote: EmojiProperties.Custom(DiscordConstants.Loading), disabled: true, style: ButtonStyle.Secondary);
            await Context.ModifyComponents(message, components);

            var response =
                await this._recapBuilders.RecapAsync(
                    new ContextModel(this.Context, contextUser, discordContextUser), userSettings, timeSettings,
                    viewType);

            await this.Context.UpdateInteractionEmbed(response, this._interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.GapView)]
    [RequiresIndex]
    [GuildOnly]
    public async Task ListeningGapsPickerAsync(params string[] inputs)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredMessage());
            var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
            var splitInput = stringMenuInteraction.Data.SelectedValues.First().Split("-");
            if (!Enum.TryParse(splitInput[0], out GapEntityType viewType))
            {
                return;
            }

            if (!Enum.TryParse(splitInput[1], out ResponseMode responseMode))
            {
                return;
            }

            var components =
                new ActionRowProperties().WithButton($"Loading {viewType.ToString().ToLower()} gaps...", customId: "1",
                    emote: EmojiProperties.Custom(DiscordConstants.Loading), disabled: true, style: ButtonStyle.Secondary);
            await this.Context.Interaction.ModifyResponseAsync(m => m.Components = [components]);

            var discordUserId = ulong.Parse(splitInput[2]);
            var requesterDiscordUserId = ulong.Parse(splitInput[3]);

            var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
            var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
            var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId,
                this.Context.Guild, this.Context.User);

            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
            if (message == null)
            {
                return;
            }

            var response =
                await this._playBuilder.ListeningGapsAsync(
                    new ContextModel(this.Context, contextUser, discordContextUser), new TopListSettings(),
                    userSettings, responseMode, viewType);

            await this.Context.UpdateInteractionEmbed(response, this._interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
