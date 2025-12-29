using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;
using GuildUser = FMBot.Persistence.Domain.Models.GuildUser;
using User = FMBot.Persistence.Domain.Models.User;

namespace FMBot.Bot.Interactions;

public class UserInteractions : ComponentInteractionModule<ComponentInteractionContext>
{
    private readonly UserService _userService;
    private readonly FriendsService _friendsService;
    private readonly UserBuilder _userBuilder;
    private readonly InteractiveService _interactivity;
    private readonly SettingService _settingService;
    private readonly GuildService _guildService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly IndexService _indexService;
    private readonly AdminService _adminService;
    private readonly ArtistsService _artistsService;
    private readonly TrackService _trackService;
    private readonly IPrefixService _prefixService;
    private readonly ImportService _importService;
    private readonly PlayService _playService;
    private readonly BotSettings _botSettings;

    public UserInteractions(
        UserService userService,
        FriendsService friendsService,
        UserBuilder userBuilder,
        InteractiveService interactivity,
        SettingService settingService,
        GuildService guildService,
        IDataSourceFactory dataSourceFactory,
        IndexService indexService,
        AdminService adminService,
        ArtistsService artistsService,
        TrackService trackService,
        IPrefixService prefixService,
        ImportService importService,
        PlayService playService,
        IOptions<BotSettings> botSettings)
    {
        this._userService = userService;
        this._friendsService = friendsService;
        this._userBuilder = userBuilder;
        this._interactivity = interactivity;
        this._settingService = settingService;
        this._guildService = guildService;
        this._dataSourceFactory = dataSourceFactory;
        this._indexService = indexService;
        this._adminService = adminService;
        this._artistsService = artistsService;
        this._trackService = trackService;
        this._prefixService = prefixService;
        this._importService = importService;
        this._playService = playService;
        this._botSettings = botSettings.Value;
    }

    [ComponentInteraction(InteractionConstants.RemoveFmbotAccount)]
    [UsernameSetRequired]
    public async Task RemoveAccountModalButton(string discordUserId)
    {
        var parsedId = ulong.Parse(discordUserId);
        if (parsedId != this.Context.User.Id)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Hey, this button is not for you. At least you tried.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message == null)
        {
            return;
        }

        await RespondAsync(InteractionCallback.Modal(
            ModalFactory.CreateRemoveAccountConfirmModal(
                $"{InteractionConstants.RemoveFmbotAccountModal}:{discordUserId}:{message.Id}")));
    }

    [ComponentInteraction(InteractionConstants.RemoveFmbotAccountModal)]
    [UsernameSetRequired]
    public async Task RemoveConfirmAsync(string discordUserId, string messageId)
    {
        var confirmation = this.Context.GetModalValue("confirmation");
        var parsedId = ulong.Parse(discordUserId);

        if (parsedId != this.Context.User.Id)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Hey, this button is not for you. At least you tried.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        if (confirmation?.ToLower() != "confirm")
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Account deletion cancelled, wrong modal input")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (userSettings == null)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("We don't have any data from you in our database")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        var parsedMessageId = ulong.Parse(messageId);
        var msg = await this.Context.Channel.GetMessageAsync(parsedMessageId);

        if (msg is not RestMessage message)
        {
            return;
        }

        try
        {
            await message.ModifyAsync(m => m.Components = []);

            await this.Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            await this._friendsService.RemoveAllFriendsAsync(userSettings.UserId);
            await this._friendsService.RemoveUserFromOtherFriendsAsync(userSettings.UserId);

            await this._userService.DeleteUser(userSettings.UserId);

            var followUpEmbed = new EmbedProperties();
            followUpEmbed.WithTitle("Removal successful");
            followUpEmbed.WithDescription(
                "Your settings, friends and any other data have been successfully deleted from .fmbot.");
            await this.Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithEmbeds([followUpEmbed])
                .WithFlags(MessageFlags.Ephemeral));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Shortcuts.Create)]
    public async Task CreateShortcutButton(string discordUserId)
    {
        try
        {
            var contextUser = await this._userService.GetUserAsync(this.Context.User.Id);
            var supporterRequiredResponse =
                UserBuilder.ShortcutsSupporterRequired(new ContextModel(this.Context, contextUser));
            if (supporterRequiredResponse != null)
            {
                await this.Context.SendResponse(this._interactivity, supporterRequiredResponse, true);
                this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
                return;
            }

            var parsedDiscordUserId = ulong.Parse(discordUserId);
            if (parsedDiscordUserId != this.Context.User.Id)
            {
                var embed = new EmbedProperties();
                embed.WithColor(DiscordConstants.WarningColorOrange);
                embed.WithDescription("Please run the command yourself if you want to create shortcuts.");
                await this.Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithEmbeds([embed])
                    .WithFlags(MessageFlags.Ephemeral)));
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
            await RespondAsync(InteractionCallback.Modal(
                ModalFactory.CreateCreateShortcutModal(
                    $"{InteractionConstants.Shortcuts.CreateModal}:{message?.Id ?? 0}")));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Shortcuts.Modify)]
    public async Task ModifyShortcutButton(string shortcutId, string overviewMessageId)
    {
        try
        {
            var shortcut = await this._userBuilder.GetShortcut(int.Parse(shortcutId));
            await RespondAsync(InteractionCallback.Modal(
                ModalFactory.CreateModifyShortcutModal(
                    $"{InteractionConstants.Shortcuts.ModifyModal}:{shortcutId}:{overviewMessageId}",
                    shortcut.Input,
                    shortcut.Output)));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Shortcuts.CreateModal)]
    public async Task CreateShortcutModal(string messageId)
    {
        try
        {
            var input = this.Context.GetModalValue("input");
            var output = this.Context.GetModalValue("output");
            var contextUser = await this._userService.GetUserAsync(this.Context.User.Id);

            var response = await this._userBuilder.CreateShortcutAsync(
                new ContextModel(this.Context, contextUser),
                input,
                output);

            if (response == null)
            {
                var parsedMessageId = ulong.Parse(messageId);
                if (parsedMessageId != 0)
                {
                    var list = await this._userBuilder.ListShortcutsAsync(new ContextModel(this.Context, contextUser));
                    await this.Context.UpdateMessageEmbed(list, messageId);
                }
            }
            else
            {
                await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
                this.Context.LogCommandUsed(response.CommandResponse);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Shortcuts.ModifyModal)]
    public async Task ModifyShortcutModal(string shortcutId, string overviewMessageId)
    {
        try
        {
            var input = this.Context.GetModalValue("input");
            var output = this.Context.GetModalValue("output");

            await this.Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            var contextUser = await this._userService.GetUserAsync(this.Context.User.Id);
            var id = int.Parse(shortcutId);

            var response = await this._userBuilder.ModifyShortcutAsync(
                new ContextModel(this.Context, contextUser),
                id,
                input,
                output);

            if (response == null)
            {
                var parsedOverviewMessageId = ulong.Parse(overviewMessageId);
                if (parsedOverviewMessageId != 0)
                {
                    var list = await this._userBuilder.ListShortcutsAsync(new ContextModel(this.Context, contextUser));
                    await this.Context.UpdateMessageEmbed(list, overviewMessageId, defer: false);
                }

                var manage = await this._userBuilder.ManageShortcutAsync(new ContextModel(this.Context, contextUser),
                    id,
                    parsedOverviewMessageId);
                await this.Context.Interaction.ModifyResponseAsync(m =>
                    m.Components = manage.ComponentsV2);
            }
            else
            {
                await this.Context.SendFollowUpResponse(this._interactivity, response, ephemeral: true);
                this.Context.LogCommandUsed(response.CommandResponse);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.User.Settings)]
    [UsernameSetRequired]
    public async Task UserSettingsButtonAsync()
    {
        try
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response = UserBuilder.GetUserSettings(new ContextModel(this.Context, contextUser));

            await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.UserSetting)]
    [UsernameSetRequired]
    public async Task GetUserSetting(params string[] inputs)
    {
        var setting = inputs.First().Replace("us-", "");

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            if (Enum.TryParse(setting.Replace("view-", "").Replace("set-", ""), out UserSetting userSetting))
            {
                ResponseModel response;
                switch (userSetting)
                {
                    case UserSetting.Privacy:
                    {
                        response = UserBuilder.Privacy(new ContextModel(this.Context, contextUser));

                        await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
                        break;
                    }
                    case UserSetting.FmMode:
                    {
                        response = UserBuilder.FmMode(new ContextModel(this.Context, contextUser));

                        await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
                        break;
                    }
                    case UserSetting.WkMode:
                    {
                        response = UserBuilder.ResponseMode(new ContextModel(this.Context, contextUser));

                        await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
                        break;
                    }
                    case UserSetting.BotScrobbling:
                    {
                        response = UserBuilder.BotScrobblingAsync(new ContextModel(this.Context, contextUser));

                        await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
                        break;
                    }
                    case UserSetting.SpotifyImport:
                    {
                        var supporterRequired =
                            ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

                        if (supporterRequired != null)
                        {
                            await this.Context.SendResponse(this._interactivity, supporterRequired, ephemeral: true);
                            this.Context.LogCommandUsed(supporterRequired.CommandResponse);
                            return;
                        }

                        response = await this._userBuilder.ImportMode(new ContextModel(this.Context, contextUser),
                            contextUser.UserId);

                        await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
                        break;
                    }
                    case UserSetting.CommandShortcuts:
                    {
                        var supporterRequired =
                            UserBuilder.ShortcutsSupporterRequired(new ContextModel(this.Context, contextUser));

                        if (supporterRequired != null)
                        {
                            await this.Context.SendResponse(this._interactivity, supporterRequired, ephemeral: true);
                            this.Context.LogCommandUsed(supporterRequired.CommandResponse);
                            return;
                        }

                        var serverEmbed = new EmbedProperties()
                            .WithColor(DiscordConstants.InformationColorBlue)
                            .WithDescription("Check your DMs to continue with configuring your command shortcuts.");

                        await this.Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                            .WithEmbeds([serverEmbed])
                            .WithFlags(MessageFlags.Ephemeral)));

                        response = await this._userBuilder.ListShortcutsAsync(new ContextModel(this.Context,
                            contextUser));
                        var dmChannel = await this.Context.User.GetDMChannelAsync();
                        await dmChannel.SendMessageAsync(new MessageProperties { Components = response.ComponentsV2 });
                        break;
                    }
                    case UserSetting.UserReactions:
                    {
                        var supporterRequired =
                            UserBuilder.UserReactionsSupporterRequired(new ContextModel(this.Context, contextUser),
                                prfx);

                        if (supporterRequired != null)
                        {
                            await this.Context.SendResponse(this._interactivity, supporterRequired, ephemeral: true);
                            this.Context.LogCommandUsed(supporterRequired.CommandResponse);
                            return;
                        }

                        response = UserBuilder.UserReactions(new ContextModel(this.Context, contextUser), prfx);

                        await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
                        break;
                    }
                    case UserSetting.Localization:
                    {
                        response = UserBuilder.Localization(new ContextModel(this.Context, contextUser));

                        await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
                        break;
                    }
                    case UserSetting.OutOfSync:
                    {
                        response = StaticBuilders.OutOfSync(new ContextModel(this.Context, contextUser));

                        await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
                        break;
                    }
                    case UserSetting.LinkedRoles:
                    {
                        response = this._userBuilder.ManageLinkedRoles(new ContextModel(this.Context, contextUser));

                        await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
                        break;
                    }
                    case UserSetting.ManageAlts:
                    {
                        response = await this._userBuilder.ManageAlts(new ContextModel(this.Context, contextUser));

                        await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
                        break;
                    }
                    case UserSetting.DeleteAccount:
                    {
                        var serverEmbed = new EmbedProperties()
                            .WithColor(DiscordConstants.WarningColorOrange)
                            .WithDescription("Check your DMs to continue with your .fmbot account deletion.");

                        await this.Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                            .WithEmbeds([serverEmbed])
                            .WithFlags(MessageFlags.Ephemeral)));

                        response = UserBuilder.RemoveDataResponse(new ContextModel(this.Context, contextUser));
                        var dmChannel = await this.Context.User.GetDMChannelAsync();
                        await dmChannel.SendMessageAsync(new MessageProperties
                        {
                            Embeds = [response.Embed],
                            Components = response.Components != null ? [response.Components] : null
                        });
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.User.Login)]
    public async Task LoginButtonAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var token = await this._dataSourceFactory.GetAuthToken();

        try
        {
            var loginUrlResponse =
                UserBuilder.StartLogin(contextUser, token.Content.Token, this._botSettings.LastFm.PublicKey);

            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithEmbeds([loginUrlResponse.Embed])
                .WithComponents(loginUrlResponse.Components != null ? [loginUrlResponse.Components] : null)
                .WithFlags(MessageFlags.Ephemeral)));
            this.Context.LogCommandUsed(CommandResponse.UsernameNotSet);

            var loginResult = await this._userService.GetAndStoreAuthSession(this.Context.User, token.Content.Token);

            if (loginResult.Status == UserService.LoginStatus.Success)
            {
                var newUserSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

                var indexUser = contextUser == null ||
                                !string.Equals(contextUser.UserNameLastFM, newUserSettings.UserNameLastFM,
                                    StringComparison.CurrentCultureIgnoreCase);

                var loginSuccessResponse =
                    UserBuilder.LoginSuccess(newUserSettings,
                        indexUser ? UserBuilder.LoginState.SuccessPendingIndex : UserBuilder.LoginState.SuccessNoIndex);

                await this.Context.Interaction.ModifyResponseAsync(m =>
                {
                    m.Components = [loginSuccessResponse.Components];
                    m.Embeds = [loginSuccessResponse.Embed];
                });
                this.Context.LogCommandUsed();

                if (indexUser)
                {
                    await this._indexService.IndexUser(newUserSettings);

                    loginSuccessResponse =
                        UserBuilder.LoginSuccess(newUserSettings, UserBuilder.LoginState.SuccessIndexComplete);

                    await this.Context.Interaction.ModifyResponseAsync(m =>
                    {
                        m.Components = [loginSuccessResponse.Components];
                        m.Embeds = [loginSuccessResponse.Embed];
                    });
                }

                if (this.Context.Guild != null)
                {
                    var guild = await this._guildService.GetGuildForWhoKnows(this.Context.Guild.Id);
                    if (guild != null)
                    {
                        var discordGuildUser = await this.Context.Guild.GetUserAsync(this.Context.User.Id);
                        var newGuildUser = new GuildUser
                        {
                            Bot = false,
                            GuildId = guild.GuildId,
                            UserId = newUserSettings.UserId,
                            UserName = discordGuildUser?.GetDisplayName(),
                        };

                        if (guild.WhoKnowsWhitelistRoleId.HasValue && discordGuildUser != null)
                        {
                            newGuildUser.WhoKnowsWhitelisted =
                                discordGuildUser.RoleIds.Contains(guild.WhoKnowsWhitelistRoleId.Value);
                        }

                        await this._indexService.AddGuildUserToDatabase(newGuildUser);
                    }
                }
            }
            else if (loginResult.Status == UserService.LoginStatus.TooManyAccounts)
            {
                var loginFailure = UserBuilder.LoginTooManyAccounts(loginResult.AltCount);
                await this.Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithEmbeds([loginFailure.Embed])
                    .WithComponents(loginFailure.Components != null ? [loginFailure.Components] : null)
                    .WithFlags(MessageFlags.Ephemeral));

                this.Context.LogCommandUsed(CommandResponse.RateLimited);
            }
            else
            {
                var loginFailure = UserBuilder.LoginFailure();
                await this.Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithEmbeds([loginFailure.Embed])
                    .WithFlags(MessageFlags.Ephemeral));

                this.Context.LogCommandUsed(CommandResponse.WrongInput);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.FmPrivacySetting)]
    [UsernameSetRequired]
    public async Task SetPrivacy(params string[] inputs)
    {
        var embed = new EmbedProperties();
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (Enum.TryParse(inputs.FirstOrDefault(), out PrivacyLevel privacyLevel))
        {
            var newPrivacyLevel = await this._userService.SetPrivacyLevel(userSettings.UserId, privacyLevel);

            embed.AddField("Your new privacy level", $"Your privacy level has been set to **{newPrivacyLevel}**.");
            if (privacyLevel == PrivacyLevel.Global)
            {
                var bottedUser =
                    await this._adminService.GetBottedUserAsync(userSettings.UserNameLastFM,
                        userSettings.RegisteredLastFm);
                var filteredUser =
                    await this._adminService.GetFilteredUserAsync(userSettings.UserNameLastFM,
                        userSettings.RegisteredLastFm);

                var globalStatus = new StringBuilder();
                var infractionDetails = new StringBuilder();

                if (bottedUser is { BanActive: true })
                {
                    globalStatus.AppendLine(
                        "Sorry, you've been permanently removed from Global WhoKnows leaderboards. " +
                        "This is most likely because we think some of your playcounts have been falsely increased. " +
                        "This might for example be adding fake scrobbles through OpenScrobbler or listening to a short song a lot of times.");
                    globalStatus.AppendLine();
                    globalStatus.AppendLine(
                        "You can still use all other functionalities of the bot, you just won't be globally visible when other users use commands. " +
                        "We moderate global leaderboards to keep them fun and fair for everybody. Remember, it's just a few numbers on a list.");
                }
                else if (filteredUser != null &&
                         (filteredUser.OccurrenceEnd ?? filteredUser.Created) >
                         DateTime.UtcNow.AddMonths(-filteredUser.MonthLength ?? -3))
                {
                    var length = filteredUser.MonthLength ?? 3;
                    switch (filteredUser.Reason)
                    {
                        case GlobalFilterReason.PlayTimeInPeriod:
                        {
                            globalStatus.AppendLine(
                                "Sorry, you've been temporarily removed from Global WhoKnows leaderboards. " +
                                $"This is because you've scrobbled over 6 days of listening time within an {WhoKnowsFilterService.PeriodAmountOfDays} day period. " +
                                "For example, this can be caused by scrobbling overnight (sleep scrobbling) or because you've added scrobbles with external tools.");
                            globalStatus.AppendLine();
                            globalStatus.AppendLine(
                                $".fmbot staff is unable to remove this block. The only way to remove it is to make sure you don't go over the listening time threshold again and wait {length} months for the filter to expire. " +
                                "Note that if we think you've intentionally added fake scrobbles this block can become permanent.");

                            infractionDetails.AppendLine(WhoKnowsFilterService.FilteredUserReason(filteredUser));
                        }
                            break;
                        case GlobalFilterReason.AmountPerPeriod:
                        {
                            globalStatus.AppendLine(
                                "Sorry, you've been temporarily removed from Global WhoKnows leaderboards. " +
                                $"This is because you've scrobbled over {WhoKnowsFilterService.MaxAmountOfPlaysPerDay * WhoKnowsFilterService.PeriodAmountOfDays} plays within a {WhoKnowsFilterService.PeriodAmountOfDays} day period. " +
                                "For example, this can be caused by scrobbling very short songs repeatedly or because you've added scrobbles with external tools.");
                            globalStatus.AppendLine();
                            globalStatus.AppendLine(
                                $".fmbot staff is unable to remove this block. The only way to remove it is to make sure you don't go over the scrobble count threshold again and wait {length} months for the filter to expire. " +
                                "Note that if we think you've intentionally added fake scrobbles this block can become permanent.");

                            infractionDetails.AppendLine(WhoKnowsFilterService.FilteredUserReason(filteredUser));
                        }
                            break;
                        case GlobalFilterReason.ShortTrack:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    globalStatus.AppendLine();
                    globalStatus.AppendLine(
                        "You can still use all other functionalities of the bot, you just won't be globally visible when other users use commands. " +
                        "We automatically moderate global leaderboards to keep them fun and fair for everybody. Remember, it's just a few numbers on a list.");

                    globalStatus.AppendLine();
                    globalStatus.AppendLine(
                        ".fmbot is not affiliated with Last.fm. This filter only applies to global charts in .fmbot.");
                }

                if (globalStatus.Length > 0)
                {
                    embed.AddField("Global WhoKnows status", globalStatus.ToString());
                }

                if (infractionDetails.Length > 0)
                {
                    embed.AddField("Infraction details", infractionDetails.ToString());
                }
            }

            embed.WithColor(DiscordConstants.InformationColorBlue);
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithEmbeds([embed])
                .WithFlags(MessageFlags.Ephemeral)));
        }
    }

    [ComponentInteraction(InteractionConstants.FmCommand.FmModeChange)]
    [UsernameSetRequired]
    public async Task FmModePickAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild?.Id);

        try
        {
            var response = UserBuilder.FmMode(new ContextModel(this.Context, contextUser), guild);

            await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.FmCommand.FmSettingType)]
    [UsernameSetRequired]
    public async Task SetEmbedType(params string[] inputs)
    {
        var embed = new EmbedProperties();
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (Enum.TryParse(inputs.FirstOrDefault(), out FmEmbedType embedType))
        {
            await this._userService.SetSettings(userSettings, embedType, FmCountType.None);

            var name = embedType.GetAttribute<OptionAttribute>().Name;
            var description = embedType.GetAttribute<OptionAttribute>().Description;

            embed.WithDescription($"Your `fm` mode has been set to **{name}**.");
            embed.WithFooter(description);
            embed.WithColor(DiscordConstants.InformationColorBlue);
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithEmbeds([embed])
                .WithFlags(MessageFlags.Ephemeral)));
        }
    }

    [ComponentInteraction(InteractionConstants.FmCommand.FmSettingFooter)]
    [UsernameSetRequired]
    public async Task SetFooterOptions(params string[] inputs)
    {
        var embed = new EmbedProperties();
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        var maxOptions = userSettings.UserType == UserType.User
            ? Constants.MaxFooterOptions
            : Constants.MaxFooterOptionsSupporter;
        var amountSelected = 0;

        foreach (var option in Enum.GetNames(typeof(FmFooterOption)))
        {
            if (Enum.TryParse(option, out FmFooterOption flag))
            {
                var supporterOnly = flag.GetAttribute<OptionAttribute>().SupporterOnly;
                if (!supporterOnly)
                {
                    if (inputs.Any(a => a == option) && amountSelected <= maxOptions)
                    {
                        userSettings.FmFooterOptions |= flag;
                        amountSelected++;
                    }
                    else
                    {
                        userSettings.FmFooterOptions &= ~flag;
                    }
                }
            }
        }

        await SaveFooterOptions(userSettings, embed);
    }

    [ComponentInteraction(InteractionConstants.FmCommand.FmSettingFooterSupporter)]
    public async Task SetSupporterFooterOptions(params string[] inputs)
    {
        var embed = new EmbedProperties();
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (userSettings.UserType == UserType.User)
        {
            return;
        }

        var maxOptions = userSettings.UserType == UserType.User ? 0 : 1;
        var amountSelected = 0;

        foreach (var option in Enum.GetNames(typeof(FmFooterOption)))
        {
            if (Enum.TryParse(option, out FmFooterOption flag))
            {
                var supporterOnly = flag.GetAttribute<OptionAttribute>().SupporterOnly;
                if (supporterOnly)
                {
                    if (inputs.Any(a => a == option) && amountSelected <= maxOptions && option != "none")
                    {
                        userSettings.FmFooterOptions |= flag;
                        amountSelected++;
                    }
                    else
                    {
                        userSettings.FmFooterOptions &= ~flag;
                    }
                }
            }
        }

        await SaveFooterOptions(userSettings, embed);
    }

    private async Task SaveFooterOptions(User userSettings, EmbedProperties embed)
    {
        userSettings = await this._userService.SetFooterOptions(userSettings, userSettings.FmFooterOptions);

        var description = new StringBuilder();

        if (userSettings.FmFooterOptions.GetUniqueFlags().Any())
        {
            description.AppendLine("Your `fm` footer options have been set to:");

            foreach (var flag in userSettings.FmFooterOptions.GetUniqueFlags())
            {
                if (userSettings.FmFooterOptions.HasFlag(flag))
                {
                    var name = flag.GetAttribute<OptionAttribute>().Name;
                    description.AppendLine($"- **{name}**");
                }
            }
        }
        else
        {
            description.AppendLine("You have removed all `fm` footer options.");
        }

        embed.WithDescription(description.ToString());
        embed.WithColor(DiscordConstants.InformationColorBlue);
        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithEmbeds([embed])
            .WithFlags(MessageFlags.Ephemeral)));
    }

    [ComponentInteraction(InteractionConstants.ResponseModeChange)]
    [UsernameSetRequired]
    public async Task ResponseModePickAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var response = UserBuilder.ResponseMode(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [ComponentInteraction(InteractionConstants.ResponseModeSetting)]
    [UsernameSetRequired]
    public async Task SetResponseModeAsync(params string[] inputs)
    {
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (Enum.TryParse(inputs.FirstOrDefault(), out ResponseMode mode))
        {
            var newUserSettings = await this._userService.SetResponseMode(userSettings, mode);

            var reply = new StringBuilder();
            reply.Append($"Your default `WhoKnows` and Top list mode has been set to **{newUserSettings.Mode}**.");

            var embed = new EmbedProperties();
            embed.WithColor(DiscordConstants.InformationColorBlue);
            embed.WithDescription(reply.ToString());

            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithEmbeds([embed])
                .WithFlags(MessageFlags.Ephemeral)));
            this.Context.LogCommandUsed();
        }
    }

    [UsernameSetRequired]
    [ComponentInteraction(InteractionConstants.Judge)]
    public async Task JudgeResultAsync(string timeOption, string result, string discordUser,
        string requesterDiscordUser)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredMessage());
            await this.Context.DisableInteractionButtons();

            var discordUserId = ulong.Parse(discordUser);
            var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);

            var contextUser = await this._userService.GetFullUserAsync(requesterDiscordUserId);
            var userSettings = await this._settingService.GetOriginalContextUser(
                discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

            var descriptor = userSettings.DifferentUser ? $"**{userSettings.DisplayName}**'s" : "your";
            EmbedProperties loaderEmbed;
            if (result == "compliment")
            {
                loaderEmbed = new EmbedProperties()
                    .WithDescription($"<a:loading:821676038102056991> Loading {descriptor} compliment...")
                    .WithColor(new Color(186, 237, 169));
            }
            else
            {
                loaderEmbed = new EmbedProperties()
                    .WithDescription(
                        $"<a:loading:821676038102056991> Loading {descriptor} roast (don't take it personally)...")
                    .WithColor(new Color(255, 122, 1));
            }

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [loaderEmbed];
                e.Components = null;
            });

            var timeSettings = SettingService.GetTimePeriod(timeOption, TimePeriod.AllTime);

            List<TopArtist> topArtists;
            if (timeSettings.TimePeriod == TimePeriod.Quarterly && !userSettings.DifferentUser)
            {
                topArtists =
                    await this._artistsService.GetRecentTopArtists(userSettings.DiscordUserId, daysToGoBack: 90);
            }
            else
            {
                topArtists =
                    (await this._dataSourceFactory.GetTopArtistsAsync(userSettings.UserNameLastFm, timeSettings, 20))
                    ?.Content?.TopArtists;
            }

            List<TopTrack> topTracks;
            if (timeSettings.TimePeriod == TimePeriod.Quarterly && !userSettings.DifferentUser)
            {
                topTracks =
                    await this._trackService.GetRecentTopTracks(userSettings.DiscordUserId, daysToGoBack: 90);
            }
            else
            {
                topTracks =
                    (await this._dataSourceFactory.GetTopTracksAsync(userSettings.UserNameLastFm, timeSettings, 20))
                    ?.Content?.TopTracks;
            }

            if (topArtists == null || !topArtists.Any() || topTracks == null || !topTracks.Any())
            {
                var embed = new EmbedProperties();
                embed.WithColor(DiscordConstants.LastFmColorRed);
                embed.WithDescription(
                    $"Sorry, you or the user you're searching for don't have any top artists or top tracks in the selected time period.");
                this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                await this.Context.Interaction.ModifyResponseAsync(e =>
                {
                    e.Embeds = [embed];
                    e.Components = null;
                });
                return;
            }

            var response = await this._userBuilder.JudgeHandleAsync(
                new ContextModel(this.Context, contextUser),
                userSettings, result, topArtists, topTracks);

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [response.Embed];
                e.Components = null;
            });

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.FeaturedLog)]
    [RequiresIndex]
    [GuildOnly]
    public async Task FeaturedLogAsync(params string[] inputs)
    {
        try
        {
            var splitInput = inputs.First().Split("-");
            if (!Enum.TryParse(splitInput[0], out FeaturedView viewType))
            {
                return;
            }

            var discordUserId = ulong.Parse(splitInput[1]);
            var requesterDiscordUserId = ulong.Parse(splitInput[2]);

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
                await this._userBuilder.FeaturedLogAsync(
                    new ContextModel(this.Context, contextUser, discordContextUser), userSettings, viewType);

            await this.Context.UpdateInteractionEmbed(response, this._interactivity);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [UsernameSetRequired]
    [ComponentInteraction(InteractionConstants.User.Profile)]
    public async Task ProfileAsync(string discordUser, string requesterDiscordUser)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());
        await this.Context.DisableActionRows();

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);

        var contextUser = await this._userService.GetFullUserAsync(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId,
            this.Context.Guild, this.Context.User);

        var response =
            await this._userBuilder.ProfileAsync(new ContextModel(this.Context, contextUser, discordContextUser),
                userSettings);

        await this.Context.UpdateInteractionEmbed(response, this._interactivity, false);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [UsernameSetRequired]
    [ComponentInteraction(InteractionConstants.User.History)]
    public async Task ProfileHistoryAsync(string discordUser, string requesterDiscordUser)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredMessage());
            await this.Context.DisableActionRows();

            var discordUserId = ulong.Parse(discordUser);
            var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);

            var contextUser = await this._userService.GetFullUserAsync(requesterDiscordUserId);
            var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
            var userSettings = await this._settingService.GetOriginalContextUser(
                discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

            var response =
                await this._userBuilder.ProfileHistoryAsync(
                    new ContextModel(this.Context, contextUser, discordContextUser),
                    userSettings);

            await this.Context.UpdateInteractionEmbed(response, this._interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ManageAlts.ManageAltsPicker)]
    [UserSessionRequired]
    public async Task ManageAltsPicker(params string[] inputs)
    {
        try
        {
            var contextUser = await this._userService.GetUserOrTempUser(this.Context.User);

            if (contextUser == null)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Session expired. Login again to manage your alts.")
                .WithFlags(MessageFlags.Ephemeral)));
                this.Context.LogCommandUsed(CommandResponse.UsernameNotSet);
                return;
            }

            var targetUser = await this._userService.GetUserForIdAsync(int.Parse(inputs.First()));

            if (targetUser == null)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("The .fmbot account you want to manage doesn't exist (anymore).")
                .WithFlags(MessageFlags.Ephemeral)));
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            if (!targetUser.UserNameLastFM.Equals(contextUser.UserNameLastFM, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var embed = new EmbedProperties();
            embed.WithColor(DiscordConstants.WarningColorOrange);

            var targetUserDiscord = await this._userService.GetUserFromDiscord(targetUser.DiscordUserId);
            embed.WithTitle("Manage .fmbot account");

            var description = new StringBuilder();
            if (targetUserDiscord != null)
            {
                description.AppendLine($"`    Username:` **{targetUserDiscord.Username}**");
                if (!string.IsNullOrWhiteSpace(targetUserDiscord.GlobalName))
                {
                    description.AppendLine(
                        $"`Display name:` **{StringExtensions.Sanitize(targetUserDiscord.GlobalName)}**");
                }
            }

            description.AppendLine($"`  Discord ID:` `{targetUser.DiscordUserId}`");
            if (targetUser.LastUsed.HasValue)
            {
                var lastUsed = ((DateTimeOffset)targetUser.LastUsed).ToUnixTimeSeconds();
                description.AppendLine($"`   Last used:` <t:{lastUsed}:R>");
            }
            else
            {
                description.AppendLine($"`   Last used:` Unknown");
            }

            description.AppendLine();
            description.AppendLine(
                "Transferring data transfers .fmbot streaks, imports and featured history to your current .fmbot account.");
            description.AppendLine();
            description.AppendLine(
                ".fmbot is not affiliated with Last.fm. No Last.fm data can be modified, transferred or deleted with this command.");

            var components = new ActionRowProperties()
                .WithButton("Delete account",
                    $"{InteractionConstants.ManageAlts.ManageAltsDeleteAlt}:false:{targetUser.UserId}",
                    style: ButtonStyle.Danger);

            if (contextUser.SessionKeyLastFm != "tempuser")
            {
                components.WithButton("Transfer data and delete account",
                    $"{InteractionConstants.ManageAlts.ManageAltsDeleteAlt}:true:{targetUser.UserId}",
                    style: ButtonStyle.Danger);
            }

            embed.WithDescription(description.ToString());

            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithEmbeds([embed])
                .WithComponents([components])
                .WithFlags(MessageFlags.Ephemeral)));
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ManageAlts.ManageAltsDeleteAlt)]
    public async Task DeleteAlt(string transferData, string targetUserId)
    {
        var contextUser = await this._userService.GetUserOrTempUser(this.Context.User);

        if (contextUser == null)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Session expired. Login again to manage your alts.")
                .WithFlags(MessageFlags.Ephemeral)));
            this.Context.LogCommandUsed(CommandResponse.UsernameNotSet);
            return;
        }

        var userToDelete = await this._userService.GetUserForIdAsync(int.Parse(targetUserId));

        if (userToDelete == null)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("The .fmbot account you want to manage doesn't exist (anymore).")
                .WithFlags(MessageFlags.Ephemeral)));
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        if (!userToDelete.UserNameLastFM.Equals(contextUser.UserNameLastFM, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (contextUser.SessionKeyLastFm == "tempuser")
        {
            transferData = "false";
        }

        var embed = new EmbedProperties();
        embed.WithColor(DiscordConstants.WarningColorOrange);
        var description = new StringBuilder();
        description.AppendLine("Are you sure you want to delete this .fmbot alt account? This cannot be reversed.");
        description.AppendLine();
        description.AppendLine(transferData == "true"
            ? "Streaks, imports and featured history will be transferred to your current .fmbot account if they exist."
            : "Streaks, imports and featured history will be permanently deleted.");
        embed.WithDescription(description.ToString());
        embed.WithFooter($"Deleting {userToDelete.DiscordUserId.ToString()}");

        var contextUserHasImported = await this._importService.HasImported(contextUser.UserId);
        var targetUserHasImported = await this._importService.HasImported(userToDelete.UserId);

        if (contextUserHasImported && targetUserHasImported && transferData == "true")
        {
            embed.AddField("Imports will not be transferred",
                "Because your current account already has imported plays, imports from the account you're deleting will not be transferred.");
        }

        var components = new ActionRowProperties()
            .WithButton(transferData == "true"
                    ? "Confirm data transfer and deletion"
                    : "Confirm deletion",
                $"{InteractionConstants.ManageAlts.ManageAltsDeleteAltConfirm}:{transferData}:{userToDelete.UserId}",
                style: ButtonStyle.Danger);

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithEmbeds([embed])
            .WithComponents([components])
            .WithFlags(MessageFlags.Ephemeral)));
        this.Context.LogCommandUsed();
    }

    [ComponentInteraction(InteractionConstants.ManageAlts.ManageAltsDeleteAltConfirm)]
    public async Task DeleteAltConfirmed(string transferData, string targetUserId)
    {
        try
        {
            _ = Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
            await this.Context.DisableInteractionButtons(interactionEdit: true);

            var contextUser = await this._userService.GetUserOrTempUser(this.Context.User);

            if (contextUser == null)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Session expired. Login again to manage your alts.")
                .WithFlags(MessageFlags.Ephemeral)));
                this.Context.LogCommandUsed(CommandResponse.UsernameNotSet);
                return;
            }

            var userToDelete = await this._userService.GetUserForIdAsync(int.Parse(targetUserId));

            if (userToDelete == null)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("The .fmbot account you want to delete doesn't exist (anymore).")
                .WithFlags(MessageFlags.Ephemeral)));
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            if (!userToDelete.UserNameLastFM.Equals(contextUser.UserNameLastFM, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (transferData == "true")
            {
                var contextUserHasImported = await this._importService.HasImported(contextUser.UserId);

                await this._playService.MoveData(userToDelete.UserId, contextUser.UserId, !contextUserHasImported);
            }

            await this._userService.DeleteUser(userToDelete.UserId);

            var components =
                new ActionRowProperties().WithButton(
                    transferData == "true"
                        ? "Successfully transferred data and deleted alt"
                        : "Successfully deleted alt",
                    customId: "0", disabled: true, style: ButtonStyle.Success);
            await this.Context.Interaction.ModifyResponseAsync(m => { m.Components = [components]; });

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [UsernameSetRequired]
    [ComponentInteraction($"update-linkedroles")]
    public async Task UpdateLinkedRoles()
    {
        var embed = new EmbedProperties();
        embed.WithColor(DiscordConstants.InformationColorBlue);

        var hasLinked = await this._userService.HasLinkedRole(Context.User.Id);
        if (!hasLinked)
        {
            embed.WithDescription("Click the 'Authorize .fmbot' button first to get started.");
            embed.WithColor(DiscordConstants.WarningColorOrange);
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithEmbeds([embed])
                .WithFlags(MessageFlags.Ephemeral)));
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        await this._userService.UpdateLinkedRole(Context.User.Id);
        embed.WithDescription("Refreshed linked role data");
        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithEmbeds([embed])
            .WithFlags(MessageFlags.Ephemeral)));
        this.Context.LogCommandUsed();
    }

    [ComponentInteraction(InteractionConstants.ManageAlts.ManageAltsButton)]
    public async Task ManageAlts()
    {
        var contextUser = await this._userService.GetUserOrTempUser(this.Context.User);

        if (contextUser == null)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Session expired. Login again to manage your alts.")
                .WithFlags(MessageFlags.Ephemeral)));
            this.Context.LogCommandUsed(CommandResponse.UsernameNotSet);
            return;
        }

        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        try
        {
            var response =
                await this._userBuilder.ManageAlts(new ContextModel(this.Context, contextUser));

            await this.Context.SendFollowUpResponse(this._interactivity, response, ephemeral: true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Shortcuts.Manage)]
    public async Task ManageShortcut(string shortcutId)
    {
        try
        {
            var contextUser = await this._userService.GetUserAsync(this.Context.User.Id);
            var id = int.Parse(shortcutId);
            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
            var context = new ContextModel(this.Context, contextUser);

            var supporterRequiredResponse = UserBuilder.ShortcutsSupporterRequired(context);
            if (supporterRequiredResponse != null)
            {
                await this.Context.SendResponse(this._interactivity, supporterRequiredResponse, true);
                this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
                return;
            }

            var response =
                await this._userBuilder.ManageShortcutAsync(context, id, message?.Id ?? 0);

            await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Shortcuts.Delete)]
    public async Task DeleteShortcut(string shortcutId, string overviewMessageId)
    {
        try
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            var contextUser = await this._userService.GetUserAsync(this.Context.User.Id);
            var id = int.Parse(shortcutId);

            var response = await this._userBuilder.DeleteShortcutAsync(
                new ContextModel(this.Context, contextUser),
                id);

            if (response == null)
            {
                var parsedOverviewMessageId = ulong.Parse(overviewMessageId);
                if (parsedOverviewMessageId != 0)
                {
                    var list = await this._userBuilder.ListShortcutsAsync(new ContextModel(this.Context, contextUser));
                    await this.Context.UpdateMessageEmbed(list, overviewMessageId, defer: false);
                }

                await this.Context.Interaction.DeleteResponseAsync();
            }
            else
            {
                await this.Context.SendFollowUpResponse(this._interactivity, response, ephemeral: true);
                this.Context.LogCommandUsed(response.CommandResponse);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.BotScrobblingManage)]
    [UserSessionRequired]
    public async Task BotScrobblingButtonAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var response = UserBuilder.BotScrobblingAsync(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this._interactivity, response, true);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [ComponentInteraction(InteractionConstants.BotScrobblingEnable)]
    [UserSessionRequired]
    public async Task EnableBotScrobbling()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var reply = new StringBuilder();

        if (contextUser.MusicBotTrackingDisabled != true)
        {
            reply.AppendLine("Music bot scrobbling for your account is already enabled.");
        }
        else
        {
            await this._userService.ToggleBotScrobblingAsync(contextUser.UserId, false);
            reply.AppendLine("Enabled music bot scrobbling for your account.");
        }

        var embed = new EmbedProperties();
        embed.WithDescription(reply.ToString());
        embed.WithColor(DiscordConstants.SuccessColorGreen);

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithEmbeds([embed])
            .WithFlags(MessageFlags.Ephemeral)));
        this.Context.LogCommandUsed();
    }

    [ComponentInteraction(InteractionConstants.BotScrobblingDisable)]
    [UserSessionRequired]
    public async Task DisableBotScrobbling()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var reply = new StringBuilder();

        if (contextUser.MusicBotTrackingDisabled == true)
        {
            reply.AppendLine("Music bot scrobbling for your account is already disabled.");
        }
        else
        {
            await this._userService.ToggleBotScrobblingAsync(contextUser.UserId, true);
            reply.AppendLine("Disabled music bot scrobbling for your account.");
        }

        var embed = new EmbedProperties();
        embed.WithDescription(reply.ToString());
        embed.WithColor(DiscordConstants.LastFmColorRed);

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithEmbeds([embed])
            .WithFlags(MessageFlags.Ephemeral)));
        this.Context.LogCommandUsed();
    }
}
