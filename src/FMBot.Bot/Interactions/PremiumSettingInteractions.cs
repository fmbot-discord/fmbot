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
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;
using Shared.Domain.Enums;

namespace FMBot.Bot.Interactions;

public class PremiumSettingInteractions(
    UserService userService,
    GuildService guildService,
    PremiumSettingBuilder premiumSettingBuilder,
    GuildSettingBuilder guildSettingBuilder,
    SupporterService supporterService,
    InteractiveService interactivity)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.BotBranding.SetFeaturedMode)]
    [ServerStaffOnly]
    public async Task SetBotFeaturedMode()
    {
        try
        {
            if (!PublicProperties.PremiumServers.ContainsKey(this.Context.Guild.Id))
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent(Constants.GetPremiumServer)
                    .WithFlags(MessageFlags.Ephemeral)));
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.PremiumServerRequired }, userService);
                return;
            }

            if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
            {
                await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
                return;
            }

            var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
            var selectedValues = stringMenuInteraction.Data.SelectedValues;

            if (selectedValues.Count == 0 ||
                !Enum.TryParse(selectedValues[0], out GuildFeaturedMode featuredMode))
            {
                featuredMode = GuildFeaturedMode.GlobalFeatured;
            }

            await guildService.SetFeaturedModeAsync(this.Context.Guild, featuredMode);

            if (featuredMode == GuildFeaturedMode.GlobalFeatured)
            {
                await guildService.SetCustomLogoAsync(this.Context.Guild, null);
                await this.Context.Client.Rest.ModifyCurrentGuildUserAsync(this.Context.Guild.Id, o =>
                {
                    o.Avatar = ImageProperties.Empty;
                    o.Bio = "";
                });
            }
            else if (featuredMode == GuildFeaturedMode.CustomBotGlobalFeatured)
            {
                await this.Context.Client.Rest.ModifyCurrentGuildUserAsync(this.Context.Guild.Id,
                    o => o.Bio = "");
            }

            var response = await premiumSettingBuilder.BotBranding(new ContextModel(this.Context), this.Context.User);
            await this.Context.UpdateInteractionEmbed(response);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.BotBranding.RemoveAvatar)]
    [ServerStaffOnly]
    public async Task RemoveBotBrandingAvatar()
    {
        try
        {
            if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
            {
                await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
                return;
            }

            await this.Context.Client.Rest.ModifyCurrentGuildUserAsync(this.Context.Guild.Id,
                o => o.Avatar = ImageProperties.Empty);

            await guildService.SetCustomLogoAsync(this.Context.Guild, null);

            await guildService.SendBotBrandingAuditLog(
                $"🗑️ **Custom bot avatar removed**\n" +
                $"Server: **{StringExtensions.Sanitize(this.Context.Guild.Name)}** — `{this.Context.Guild.Id}`\n" +
                $"By: <@{this.Context.User.Id}> — `{this.Context.User.Username}`");

            var response = await premiumSettingBuilder.BotBranding(new ContextModel(this.Context), this.Context.User);
            await this.Context.UpdateInteractionEmbed(response);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.PremiumServer.GetOverview)]
    public async Task PremiumServerOverview(string source)
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var response = await premiumSettingBuilder.PremiumServerOverview(
                new ContextModel(this.Context, contextUser), this.Context.Interaction.UserLocale, source);

            await this.Context.SendResponse(interactivity, response, userService, true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.PremiumServer.GetPurchaseLink)]
    public async Task GetPremiumServerPurchaseLink(string type, string source)
    {
        try
        {
            await this.Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var stripeSupporter = await supporterService.GetStripeSupporter(this.Context.User.Id);
            var pricing = await supporterService.GetPricing(this.Context.Interaction.UserLocale,
                stripeSupporter?.Currency, StripeSupporterType.PremiumServer);

            if (pricing == null)
            {
                await this.Context.Interaction.ModifyResponseAsync(m =>
                {
                    m.Content = "Premium server is not available for purchase yet. Stay tuned!";
                });
                return;
            }

            var existingCustomerId = await supporterService.GetExistingStripeCustomerId(this.Context.User.Id);

            var checkout = await supporterService.GetPremiumGuildCheckoutLink(this.Context.User.Id,
                contextUser?.UserNameLastFM, type, this.Context.Guild.Id, this.Context.Guild.Name, pricing,
                existingCustomerId, source);

            if (checkout == null || checkout.GuildAlreadySubscribed)
            {
                await this.Context.Interaction.ModifyResponseAsync(m =>
                {
                    m.Content = "✅ This server already has an active Premium server subscription.";
                });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Cooldown }, userService);
                return;
            }

            var components = new ActionRowProperties().WithButton("Complete purchase", url: checkout.CheckoutLink,
                emote: EmojiProperties.Standard("✨"));

            var embed = new EmbedProperties();
            embed.WithColor(DiscordConstants.InformationColorBlue);

            var description = new StringBuilder();
            description.AppendLine("**Click the unique link below to purchase Premium server!**");
            description.AppendLine(type.Equals("yearly", StringComparison.OrdinalIgnoreCase)
                ? $"-# {pricing.YearlySummary}"
                : $"-# {pricing.MonthlySummary}");
            embed.WithDescription(description.ToString());

            await this.Context.Interaction.ModifyResponseAsync(m =>
            {
                m.Embeds = [embed];
                m.Components = [components];
            });

            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: false);
        }
    }

    [ComponentInteraction(InteractionConstants.PremiumServer.ManageSubscription)]
    [UserSessionRequired]
    public async Task PremiumServerManage(string flowType)
    {
        try
        {
            await this.Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            var subscription = await supporterService.GetPremiumGuildSubscription(this.Context.Guild.Id);

            if (subscription == null || string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
            {
                await this.Context.Interaction.ModifyResponseAsync(m =>
                {
                    m.Content = "This server has no active Premium server subscription to manage.";
                });
                return;
            }

            if (subscription.PurchaserDiscordUserId != this.Context.User.Id)
            {
                await this.Context.Interaction.ModifyResponseAsync(m =>
                {
                    m.Content = "Only the purchaser can manage this server's subscription.";
                });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
                return;
            }

            var manageLink = await supporterService.GetPremiumGuildManageLink(subscription,
                flowType == "cancel" ? "cancel" : "update");

            var components = new ActionRowProperties().WithButton(
                flowType == "cancel" ? "Cancel subscription" : "Change plan", url: manageLink);

            await this.Context.Interaction.ModifyResponseAsync(m =>
            {
                m.Content = "Use the button below to manage this server's subscription.";
                m.Components = [components];
            });

            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: false);
        }
    }

    [ComponentInteraction(InteractionConstants.PremiumServer.MyServers)]
    public async Task MyPremiumServers()
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var response = await premiumSettingBuilder.MyPremiumServers(
                new ContextModel(this.Context, contextUser));

            await this.Context.SendResponse(interactivity, response, userService, true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.PremiumServer.MyServersManage)]
    public async Task ManageMyPremiumServer(string subscriptionId)
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var response = await premiumSettingBuilder.ManageMyPremiumServer(
                new ContextModel(this.Context, contextUser), int.Parse(subscriptionId));

            await this.Context.SendResponse(interactivity, response, userService, true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.PremiumServer.MyServersManageFlow)]
    public async Task MyPremiumServerManageFlow(string subscriptionId, string flowType)
    {
        try
        {
            await this.Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            var subscription = await supporterService.GetPremiumGuildSubscriptionById(int.Parse(subscriptionId));

            if (subscription == null || string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
            {
                await this.Context.Interaction.ModifyResponseAsync(m =>
                {
                    m.Content = "This subscription no longer exists or has already ended.";
                });
                return;
            }

            if (subscription.PurchaserDiscordUserId != this.Context.User.Id)
            {
                await this.Context.Interaction.ModifyResponseAsync(m =>
                {
                    m.Content = "Only the purchaser can manage this subscription.";
                });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
                return;
            }

            var manageLink = await supporterService.GetPremiumGuildManageLink(subscription,
                flowType == "cancel" ? "cancel" : "update");

            var components = new ActionRowProperties().WithButton(
                flowType == "cancel" ? "Cancel subscription" : "Change plan", url: manageLink);

            await this.Context.Interaction.ModifyResponseAsync(m =>
            {
                m.Content = "Use the button below to manage this subscription.";
                m.Components = [components];
            });

            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: false);
        }
    }

    [ComponentInteraction(InteractionConstants.PremiumServer.MyServersBillingPortal)]
    public async Task MyPremiumServersBillingPortal()
    {
        try
        {
            await this.Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            var subscriptions =
                await supporterService.GetPremiumGuildSubscriptionsForPurchaser(this.Context.User.Id);
            var stripeCustomerId = subscriptions
                .FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.StripeCustomerId))?.StripeCustomerId;

            if (stripeCustomerId == null)
            {
                await this.Context.Interaction.ModifyResponseAsync(m =>
                {
                    m.Content = "You don't have any Premium server billing to manage.";
                });
                return;
            }

            var portalLink = await supporterService.GetStripeCustomerPortalLink(stripeCustomerId);

            var components = new ActionRowProperties().WithButton("Open billing portal", url: portalLink);

            await this.Context.Interaction.ModifyResponseAsync(m =>
            {
                m.Content = "Use the button below to view and manage all your subscriptions and payment details.";
                m.Components = [components];
            });

            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: false);
        }
    }

    [ComponentInteraction(InteractionConstants.GuildShortcuts.ViewAll)]
    [UserSessionRequired]
    public async Task ViewGuildShortcuts()
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var context = new ContextModel(this.Context, contextUser);

            var premiumRequiredResponse = PremiumSettingBuilder.GuildShortcutsPremiumRequired(context);
            if (premiumRequiredResponse != null)
            {
                await this.Context.SendResponse(interactivity, premiumRequiredResponse, userService, true);
                await this.Context.LogCommandUsedAsync(premiumRequiredResponse, userService);
                return;
            }

            var response = await premiumSettingBuilder.ListGuildShortcutsAsync(context);

            await this.Context.SendResponse(interactivity, response, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.GuildShortcuts.Create)]
    [ServerStaffOnly]
    public async Task CreateGuildShortcutButton()
    {
        try
        {
            var contextUser = await userService.GetUserAsync(this.Context.User.Id);
            var context = new ContextModel(this.Context, contextUser);

            var premiumRequiredResponse = PremiumSettingBuilder.GuildShortcutsPremiumRequired(context);
            if (premiumRequiredResponse != null)
            {
                await this.Context.SendResponse(interactivity, premiumRequiredResponse, userService, true);
                await this.Context.LogCommandUsedAsync(premiumRequiredResponse, userService);
                return;
            }

            if (!await guildSettingBuilder.UserIsAllowed(context))
            {
                await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
                return;
            }

            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
            await RespondAsync(InteractionCallback.Modal(
                ModalFactory.CreateCreateShortcutModal(
                    $"{InteractionConstants.GuildShortcuts.CreateModal}:{message?.Id ?? 0}")));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.GuildShortcuts.CreateModal)]
    [ServerStaffOnly]
    public async Task CreateGuildShortcutModal(string messageId)
    {
        try
        {
            var input = this.Context.GetModalValue("input");
            var output = this.Context.GetModalValue("output");
            var contextUser = await userService.GetUserAsync(this.Context.User.Id);
            var context = new ContextModel(this.Context, contextUser);

            if (!await guildSettingBuilder.UserIsAllowed(context))
            {
                await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
                return;
            }

            var response = await premiumSettingBuilder.CreateGuildShortcutAsync(context, input, output);

            if (response == null)
            {
                var parsedMessageId = ulong.Parse(messageId);
                if (parsedMessageId != 0)
                {
                    var list = await premiumSettingBuilder.ListGuildShortcutsAsync(context);
                    await this.Context.UpdateMessageEmbed(list, messageId);
                }
            }
            else
            {
                await this.Context.SendResponse(interactivity, response, userService, ephemeral: true);
                await this.Context.LogCommandUsedAsync(response, userService);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.GuildShortcuts.Manage)]
    [ServerStaffOnly]
    public async Task ManageGuildShortcut(string shortcutId)
    {
        try
        {
            var contextUser = await userService.GetUserAsync(this.Context.User.Id);
            var context = new ContextModel(this.Context, contextUser);
            var id = int.Parse(shortcutId);
            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

            var premiumRequiredResponse = PremiumSettingBuilder.GuildShortcutsPremiumRequired(context);
            if (premiumRequiredResponse != null)
            {
                await this.Context.SendResponse(interactivity, premiumRequiredResponse, userService, true);
                await this.Context.LogCommandUsedAsync(premiumRequiredResponse, userService);
                return;
            }

            if (!await guildSettingBuilder.UserIsAllowed(context))
            {
                await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
                return;
            }

            var response = await premiumSettingBuilder.ManageGuildShortcutAsync(context, id, message?.Id ?? 0);

            await this.Context.SendResponse(interactivity, response, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.GuildShortcuts.Modify)]
    [ServerStaffOnly]
    public async Task ModifyGuildShortcutButton(string shortcutId, string overviewMessageId)
    {
        try
        {
            var shortcut = await premiumSettingBuilder.GetGuildShortcut(int.Parse(shortcutId));
            if (shortcut == null)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent("This shortcut no longer exists.")
                    .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            await RespondAsync(InteractionCallback.Modal(
                ModalFactory.CreateModifyShortcutModal(
                    $"{InteractionConstants.GuildShortcuts.ModifyModal}:{shortcutId}:{overviewMessageId}",
                    shortcut.Input,
                    shortcut.Output)));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.GuildShortcuts.ModifyModal)]
    [ServerStaffOnly]
    public async Task ModifyGuildShortcutModal(string shortcutId, string overviewMessageId)
    {
        try
        {
            var input = this.Context.GetModalValue("input");
            var output = this.Context.GetModalValue("output");

            await this.Context.Interaction.SendResponseAsync(InteractionCallback.DeferredModifyMessage);

            var contextUser = await userService.GetUserAsync(this.Context.User.Id);
            var context = new ContextModel(this.Context, contextUser);
            var id = int.Parse(shortcutId);

            if (!await guildSettingBuilder.UserIsAllowed(context))
            {
                await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
                return;
            }

            var response = await premiumSettingBuilder.ModifyGuildShortcutAsync(context, id, input, output);

            if (response == null)
            {
                var parsedOverviewMessageId = ulong.Parse(overviewMessageId);
                if (parsedOverviewMessageId != 0)
                {
                    var list = await premiumSettingBuilder.ListGuildShortcutsAsync(context);
                    var overviewMsg = await this.Context.Interaction.Channel.GetMessageAsync(parsedOverviewMessageId);
                    await overviewMsg.ModifyAsync(m =>
                    {
                        m.Components = list.GetComponentsV2();
                        m.AllowedMentions = AllowedMentionsProperties.None;
                    });
                }

                var manage = await premiumSettingBuilder.ManageGuildShortcutAsync(context, id, parsedOverviewMessageId);
                await this.Context.Interaction.ModifyResponseAsync(m =>
                {
                    m.Components = manage.GetComponentsV2();
                    m.AllowedMentions = AllowedMentionsProperties.None;
                });
            }
            else
            {
                await this.Context.SendFollowUpResponse(interactivity, response, userService, ephemeral: true);
                await this.Context.LogCommandUsedAsync(response, userService);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.GuildShortcuts.Delete)]
    [ServerStaffOnly]
    public async Task DeleteGuildShortcut(string shortcutId, string overviewMessageId)
    {
        try
        {
            await this.Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            var contextUser = await userService.GetUserAsync(this.Context.User.Id);
            var context = new ContextModel(this.Context, contextUser);

            if (!await guildSettingBuilder.UserIsAllowed(context))
            {
                await this.Context.Interaction.ModifyResponseAsync(m =>
                {
                    m.Content = GuildSettingBuilder.UserNotAllowedResponseText();
                });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
                return;
            }

            var response = await premiumSettingBuilder.DeleteGuildShortcutAsync(context, int.Parse(shortcutId));

            if (response == null)
            {
                var parsedOverviewMessageId = ulong.Parse(overviewMessageId);
                if (parsedOverviewMessageId != 0)
                {
                    var list = await premiumSettingBuilder.ListGuildShortcutsAsync(context);
                    var overviewMsg = await this.Context.Interaction.Channel.GetMessageAsync(parsedOverviewMessageId);
                    await overviewMsg.ModifyAsync(m =>
                    {
                        m.Components = list.GetComponentsV2();
                        m.AllowedMentions = AllowedMentionsProperties.None;
                    });
                }

                await this.Context.Interaction.ModifyResponseAsync(m =>
                {
                    m.Content = "✅ Server shortcut deleted.";
                });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
            }
            else
            {
                await this.Context.SendFollowUpResponse(interactivity, response, userService, ephemeral: true);
                await this.Context.LogCommandUsedAsync(response, userService);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: false);
        }
    }

    [ComponentInteraction(InteractionConstants.SetAllowedRoleMenu)]
    [ServerStaffOnly]
    public async Task SetGuildAllowedRoles()
    {
        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        var entityMenuInteraction = (EntityMenuInteraction)this.Context.Interaction;
        var selectedRoleIds = entityMenuInteraction.Data.SelectedValues;

        await guildService.ChangeGuildAllowedRoles(this.Context.Guild, selectedRoleIds.ToArray());

        var response = await premiumSettingBuilder.AllowedRoles(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.SetBlockedRoleMenu)]
    [ServerStaffOnly]
    public async Task SetGuildBlockedRoles()
    {
        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        var entityMenuInteraction = (EntityMenuInteraction)this.Context.Interaction;
        var selectedRoleIds = entityMenuInteraction.Data.SelectedValues;

        await guildService.ChangeGuildBlockedRoles(this.Context.Guild, selectedRoleIds.ToArray());

        var response = await premiumSettingBuilder.BlockedRoles(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.SetBotManagementRoleMenu)]
    [ServerStaffOnly]
    public async Task SetBotManagementRoles()
    {
        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context), managersAllowed: false))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context, managersAllowed: false);
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        var entityMenuInteraction = (EntityMenuInteraction)this.Context.Interaction;
        var selectedRoleIds = entityMenuInteraction.Data.SelectedValues;

        await guildService.ChangeGuildBotManagementRoles(this.Context.Guild, selectedRoleIds.ToArray());

        var response = await premiumSettingBuilder.BotManagementRoles(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.RemoveGuildActivityThreshold)]
    [ServerStaffOnly]
    public async Task RemoveGuildActivityThreshold()
    {
        await guildService.SetGuildActivityThresholdDaysAsync(this.Context.Guild, null);

        var response = await premiumSettingBuilder.SetGuildActivityThreshold(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.SetGuildActivityThreshold)]
    [ServerStaffOnly]
    public async Task SetGuildActivityThresholdButton()
    {
        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message == null)
        {
            return;
        }

        await RespondAsync(InteractionCallback.Modal(
            ModalFactory.CreateSetGuildActivityThresholdModal($"{InteractionConstants.SetGuildActivityThresholdModal}:{message.Id}")));
    }

    [ComponentInteraction(InteractionConstants.SetGuildActivityThresholdModal)]
    [ServerStaffOnly]
    public async Task SetGuildActivityThreshold(string messageId)
    {
        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        var amount = this.Context.GetModalValue("amount");

        if (!int.TryParse(amount, out var result) || result < 2 || result > 999)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Please enter a valid number between `2` and `999`.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        await guildService.SetGuildActivityThresholdDaysAsync(this.Context.Guild, result);

        var response = await premiumSettingBuilder.SetGuildActivityThreshold(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }
}
