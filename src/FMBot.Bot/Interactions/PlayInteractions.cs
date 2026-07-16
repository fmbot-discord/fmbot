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
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class PlayInteractions(
    UserService userService,
    SettingService settingService,
    PlayBuilder playBuilder,
    PlayService playService,
    RecapBuilders recapBuilders,
    InteractiveService interactivity,
    IMemoryCache cache)
    : ComponentInteractionModule<ComponentInteractionContext>
{
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
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        if (!long.TryParse(streakIdStr, out var streakId))
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Invalid input. Please enter the ID of the streak you want to delete.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        var response = await playBuilder.DeleteStreakAsync(new ContextModel(this.Context, contextUser), streakId);

        await this.Context.SendResponse(interactivity, response, userService, ephemeral: true);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [ComponentInteraction(InteractionConstants.RestoreStreakHistory)]
    [UsernameSetRequired]
    public async Task RestoreStreakHistoryAsync(string discordUserId)
    {
        try
        {
            var ownerDiscordUserId = ulong.Parse(discordUserId);
            if (this.Context.User.Id != ownerDiscordUserId)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent("Only the user this streak history belongs to can restore their past streaks.")
                    .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            if (!SupporterService.IsSupporter(contextUser.UserType))
            {
                var supporterRow = new ActionRowProperties();
                supporterRow.AddComponents(new ButtonProperties(
                    InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "streak-restore"),
                    Constants.GetSupporterButton, ButtonStyle.Primary));

                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithEmbeds([
                        new EmbedProperties()
                            .WithDescription(
                                "To restore your past streaks, we need to scan your lifetime Last.fm history. Your lifetime history and more are only available for supporters.")
                            .WithColor(DiscordConstants.WarningColorOrange)
                    ])
                    .WithComponents([supporterRow])
                    .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            var existingStreaks = await playService.GetStreaks(contextUser.UserId);
            if (existingStreaks.Count > 0)
            {
                var confirmRow = new ActionRowProperties();
                confirmRow.AddComponents(new ButtonProperties(
                    $"{InteractionConstants.RestoreStreakHistoryConfirmed}:{discordUserId}",
                    "Confirm restore", ButtonStyle.Primary));

                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithEmbeds([
                        new EmbedProperties()
                            .WithDescription(
                                $"You currently have {existingStreaks.Count} saved {(existingStreaks.Count == 1 ? "streak" : "streaks")}. " +
                                "Restoring will scan your lifetime listening history and add streaks that were never saved. Your existing streaks won't be changed.")
                            .WithColor(DiscordConstants.InformationColorBlue)
                    ])
                    .WithComponents([confirmRow])
                    .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            _ = this.Context.DisableInteractionButtons(
                specificButtonOnly: $"{InteractionConstants.RestoreStreakHistory}:{discordUserId}",
                addLoaderToSpecificButton: true);

            var restoredStreaks = await playService.RestoreStreakHistory(contextUser.UserId);

            var userSettings =
                await settingService.GetUser(null, contextUser, this.Context.Guild, this.Context.User, true);

            var response =
                await playBuilder.StreakHistoryAsync(new ContextModel(this.Context, contextUser), userSettings);

            await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            await this.Context.LogCommandUsedAsync(response, userService);

            var followUp = new ResponseModel
            {
                ResponseType = ResponseType.Text,
                Text = GetRestoreResultText(restoredStreaks)
            };

            await this.Context.SendFollowUpResponse(interactivity, followUp, userService, ephemeral: true);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.RestoreStreakHistoryConfirmed)]
    [UsernameSetRequired]
    public async Task RestoreStreakHistoryConfirmedAsync(string discordUserId)
    {
        try
        {
            var ownerDiscordUserId = ulong.Parse(discordUserId);
            if (this.Context.User.Id != ownerDiscordUserId)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent("Only the user this streak history belongs to can restore their past streaks.")
                    .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            if (!SupporterService.IsSupporter(contextUser.UserType))
            {
                return;
            }

            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            _ = this.Context.DisableInteractionButtons(
                specificButtonOnly: $"{InteractionConstants.RestoreStreakHistoryConfirmed}:{discordUserId}",
                addLoaderToSpecificButton: true);

            var restoredStreaks = await playService.RestoreStreakHistory(contextUser.UserId);

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds =
                [
                    new EmbedProperties()
                        .WithDescription(GetRestoreResultText(restoredStreaks))
                        .WithColor(restoredStreaks == null
                            ? DiscordConstants.WarningColorOrange
                            : DiscordConstants.SuccessColorGreen)
                ];
                e.Components = [];
            });

            var response = new ResponseModel
            {
                ResponseType = ResponseType.Text,
                Text = GetRestoreResultText(restoredStreaks)
            };

            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    private static string GetRestoreResultText(int? restoredStreaks)
    {
        return restoredStreaks switch
        {
            null => "A streak restore is already running for your account. Please wait for it to complete.",
            0 => "No missing streaks found in your listening history.\n" +
                 "-# Enable editmode on your streak history to delete streaks you don't want. For example: `.streaks edit`",
            _ =>
                $"Restored {restoredStreaks} {(restoredStreaks == 1 ? "streak" : "streaks")} from your listening history.\n" +
                "-# Enable editmode on your streak history to delete streaks you don't want. For example: `.streaks edit`"
        };
    }

    [ComponentInteraction(InteractionConstants.DeleteAllStreaks)]
    [UsernameSetRequired]
    public async Task DeleteAllStreaksAsync(string discordUserId)
    {
        try
        {
            var ownerDiscordUserId = ulong.Parse(discordUserId);
            if (this.Context.User.Id != ownerDiscordUserId)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent("Only the user this streak history belongs to can delete their streaks.")
                    .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var existingStreaks = await playService.GetStreaks(contextUser.UserId);

            if (existingStreaks.Count == 0)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent("You have no saved streaks to delete.")
                    .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            var confirmRow = new ActionRowProperties();
            confirmRow.AddComponents(new ButtonProperties(
                $"{InteractionConstants.DeleteAllStreaksConfirmed}:{discordUserId}",
                "Confirm deletion", ButtonStyle.Danger));

            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithEmbeds([
                    new EmbedProperties()
                        .WithDescription(
                            $"This will permanently delete all {existingStreaks.Count} of your saved {(existingStreaks.Count == 1 ? "streak" : "streaks")}. This cannot be undone.")
                        .WithColor(DiscordConstants.WarningColorOrange)
                ])
                .WithComponents([confirmRow])
                .WithFlags(MessageFlags.Ephemeral)));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.DeleteAllStreaksConfirmed)]
    [UsernameSetRequired]
    public async Task DeleteAllStreaksConfirmedAsync(string discordUserId)
    {
        try
        {
            var ownerDiscordUserId = ulong.Parse(discordUserId);
            if (this.Context.User.Id != ownerDiscordUserId)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent("Only the user this streak history belongs to can delete their streaks.")
                    .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            _ = this.Context.DisableInteractionButtons(
                specificButtonOnly: $"{InteractionConstants.DeleteAllStreaksConfirmed}:{discordUserId}",
                addLoaderToSpecificButton: true);

            var deletedStreaks = await playService.DeleteAllStreaks(contextUser.UserId);

            var resultText =
                $"🗑 Deleted {deletedStreaks} saved {(deletedStreaks == 1 ? "streak" : "streaks")}.";

            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds =
                [
                    new EmbedProperties()
                        .WithDescription(resultText)
                        .WithColor(DiscordConstants.SuccessColorGreen)
                ];
                e.Components = [];
            });

            var response = new ResponseModel
            {
                ResponseType = ResponseType.Text,
                Text = resultText
            };

            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
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

        await RespondAsync(InteractionCallback.DeferredModifyMessage);
        await this.Context.DisableInteractionButtons();

        var contextUser = await userService.GetUserWithDiscogs(requesterDiscordUserId);
        var userSettings = await settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId,
            this.Context.Guild, this.Context.User);
        var targetUser = await userService.GetUserWithDiscogs(discordUserId);

        try
        {
            var mileStoneAmount =
                SettingService.GetMilestoneAmount("random", targetUser.TotalPlaycount.GetValueOrDefault());

            var response = await playBuilder.MileStoneAsync(new ContextModel(this.Context, contextUser),
                userSettings, mileStoneAmount.amount, targetUser.TotalPlaycount.GetValueOrDefault(),
                mileStoneAmount.isRandom);

            await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            await this.Context.LogCommandUsedAsync(response, userService);

            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
            if (message != null && response.ReferencedMusic != null &&
                PublicProperties.UsedCommandsResponseContextId.TryGetValue(message.Id, out var contextId))
            {
                await userService.UpdateInteractionContext(contextId, response.ReferencedMusic);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.RecapAlltime)]
    [UsernameSetRequired]
    public async Task RecapAllTime(string userId)
    {
        await RespondAsync(InteractionCallback.DeferredModifyMessage);
        _ = this.Context.DisableInteractionButtons(specificButtonOnly: $"{InteractionConstants.RecapAlltime}:{userId}",
            addLoaderToSpecificButton: true);

        var contextUser = await userService.GetUserForIdAsync(int.Parse(userId));
        var userSettings =
            await settingService.GetUser(null, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var timeSettings =
                SettingService.GetTimePeriod("alltime", TimePeriod.AllTime, timeZone: userSettings.TimeZone, language: LocalizationService.GetLanguage(this.Context.Interaction.GuildId, this.Context.Interaction.GuildLocale));

            var response = await recapBuilders.RecapAsync(new ContextModel(this.Context, contextUser),
                userSettings, timeSettings, RecapPage.Overview);

            await this.Context.SendFollowUpResponse(interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);

            _ = this.Context.DisableInteractionButtons(
                specificButtonOnly: $"{InteractionConstants.RecapAlltime}:{userId}");
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.RecapPicker)]
    [RequiresIndex]
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

            var contextUser = await userService.GetUserWithDiscogs(requesterDiscordUserId);
            var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
            var userSettings = await settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId,
                this.Context.Guild, this.Context.User);

            var timeSettings = SettingService.GetTimePeriod(splitInput[3],
                registeredLastFm: userSettings.RegisteredLastFm,
                timeZone: userSettings.TimeZone, defaultTimePeriod: TimePeriod.Yearly, language: LocalizationService.GetLanguage(this.Context.Interaction.GuildId, this.Context.Interaction.GuildLocale));

            var context = new ContextModel(this.Context, contextUser, discordContextUser);

            if (userSettings.DiscordUserId != this.Context.User.Id &&
                (viewType == RecapPage.BotStats ||
                 viewType == RecapPage.BotStatsArtists ||
                 viewType == RecapPage.BotStatsCommands))
            {
                var noPermResponse = new ResponseModel();
                noPermResponse.Embed.WithDescription(context.Localize("recap.privacyBotStats"));
                noPermResponse.CommandResponse = CommandResponse.NoPermission;
                noPermResponse.ResponseType = ResponseType.Embed;
                noPermResponse.Embed.WithColor(DiscordConstants.WarningColorOrange);
                await this.Context.SendResponse(interactivity, noPermResponse, userService, true);
                await this.Context.LogCommandUsedAsync(noPermResponse, userService);
                return;
            }

            await RespondAsync(InteractionCallback.DeferredModifyMessage);

            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
            if (message == null)
            {
                return;
            }

            var loadingLabel = context.Localize("recap.loadingPage",
                ("page", context.LocalizeOption(viewType)),
                ("period", context.Localizer.PeriodLabel(timeSettings)));
            var components =
                new ActionRowProperties().WithButton(loadingLabel, customId: "1",
                    emote: EmojiProperties.Custom(DiscordConstants.Loading), disabled: true, style: ButtonStyle.Secondary);
            await Context.ModifyComponents(message, components);

            var response =
                await recapBuilders.RecapAsync(context, userSettings, timeSettings,
                    viewType);

            await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Search.Tab)]
    [UsernameSetRequired]
    public async Task SearchTabAsync(string cacheKey, string tabIndexStr, string discordUserIdStr)
    {
        await RenderSearchAsync(cacheKey, tabIndexStr, "0", discordUserIdStr);
    }

    [ComponentInteraction(InteractionConstants.Search.Page)]
    [UsernameSetRequired]
    public async Task SearchPageAsync(string cacheKey, string tabIndexStr, string pageStr, string discordUserIdStr)
    {
        await RenderSearchAsync(cacheKey, tabIndexStr, pageStr, discordUserIdStr);
    }

    private async Task RenderSearchAsync(string cacheKey, string tabIndexStr, string pageStr, string discordUserIdStr)
    {
        try
        {
            if (!ulong.TryParse(discordUserIdStr, out var ownerDiscordId) ||
                !Enum.TryParse<SearchTab>(tabIndexStr, out var tab) ||
                !Enum.IsDefined(tab) ||
                !int.TryParse(pageStr, out var page))
            {
                return;
            }

            if (page < 0)
            {
                page = 0;
            }

            if (this.Context.User.Id != ownerDiscordId)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent("Only the user who ran the search can interact with it.")
                    .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            if (!cache.TryGetValue<SearchQueryModel>($"search-{cacheKey}", out var search) || search == null)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent("Search expired — please run `/search` again.")
                    .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            await RespondAsync(InteractionCallback.DeferredModifyMessage);

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var response = await playBuilder.SearchAsync(new ContextModel(this.Context, contextUser), search, tab,
                page, cacheKey);

            await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Search.EditButton)]
    [UsernameSetRequired]
    public async Task SearchEditButtonAsync(string cacheKey, string tabIndexStr, string discordUserIdStr)
    {
        try
        {
            if (!ulong.TryParse(discordUserIdStr, out var ownerDiscordId) ||
                !Enum.TryParse<SearchTab>(tabIndexStr, out var tab) ||
                !Enum.IsDefined(tab))
            {
                return;
            }

            if (this.Context.User.Id != ownerDiscordId)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent("Only the user who ran the search can edit it.")
                    .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            if (!cache.TryGetValue<SearchQueryModel>($"search-{cacheKey}", out var search) || search == null)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent("Search expired — please run `/search` again.")
                    .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            var modalCustomId = $"{InteractionConstants.Search.EditModal}:{cacheKey}:{(int)tab}:{ownerDiscordId}";
            await RespondAsync(InteractionCallback.Modal(ModalFactory.CreateSearchModal(modalCustomId, search)));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Search.EditModal)]
    [UsernameSetRequired]
    public async Task SearchEditModalAsync(string cacheKey, string tabIndexStr, string discordUserIdStr)
    {
        try
        {
            if (!ulong.TryParse(discordUserIdStr, out var ownerDiscordId) ||
                !Enum.TryParse<SearchTab>(tabIndexStr, out var tab) ||
                !Enum.IsDefined(tab))
            {
                return;
            }

            if (this.Context.User.Id != ownerDiscordId)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent("Only the user who ran the search can edit it.")
                    .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            var newQuery = this.Context.GetModalValue("query");
            if (string.IsNullOrWhiteSpace(newQuery))
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent("Please enter a search query.")
                    .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            await RespondAsync(InteractionCallback.DeferredModifyMessage);

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var search = new SearchQueryModel { Query = newQuery };
            var response = await playBuilder.SearchAsync(new ContextModel(this.Context, contextUser), search,
                tab, 0, cacheKey);

            await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.GapView)]
    [RequiresIndex]
    [GuildOnly]
    public async Task ListeningGapsPickerAsync(params string[] inputs)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
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

            var contextUser = await userService.GetUserWithDiscogs(requesterDiscordUserId);
            var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
            var userSettings = await settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId,
                this.Context.Guild, this.Context.User);

            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
            if (message == null)
            {
                return;
            }

            var response =
                await playBuilder.ListeningGapsAsync(
                    new ContextModel(this.Context, contextUser, discordContextUser), new TopListSettings(),
                    userSettings, responseMode, viewType);

            await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
