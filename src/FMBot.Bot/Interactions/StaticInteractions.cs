using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.Commands;
using NetCord.Services.ComponentInteractions;
using Shared.Domain.Enums;

namespace FMBot.Bot.Interactions;

public class StaticInteractions(
    UserService userService,
    StaticBuilders staticBuilders,
    SupporterService supporterService,
    InteractiveService interactivity,
    CommandService<CommandContext> commandService,
    IPrefixService prefixService)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.SupporterLinks.GetPurchaseButtons)]
    [UserSessionRequired]
    public async Task SupporterButtonsWithSource(string newResponse, string expandWithPerks, string showExpandButton,
        string source)
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var response = await staticBuilders.SupporterButtons(new ContextModel(this.Context, contextUser),
                expandWithPerks.Equals("true", StringComparison.OrdinalIgnoreCase),
                showExpandButton.Equals("true", StringComparison.OrdinalIgnoreCase),
                userLocale: this.Context.Interaction.UserLocale, source: source);

            if (newResponse.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                await this.Context.SendResponse(interactivity, response, userService, true);
            }
            else
            {
                await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

                await this.Context.Interaction.ModifyResponseAsync(m =>
                {
                    m.Components = response.Components?.Any() == true ? [response.Components] : [];
                    m.Embeds = [response.Embed];
                });
            }

            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.SupporterLinks.GetPurchaseLink)]
    [UserSessionRequired]
    public async Task GetSupporterLink(string type, string source)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var existingStripeSupporter = await supporterService.GetStripeSupporter(contextUser.DiscordUserId);

        var pricing =
            await supporterService.GetPricing(this.Context.Interaction.UserLocale,
                existingStripeSupporter?.Currency);

        var link = await supporterService.GetSupporterCheckoutLink(this.Context.User.Id,
            contextUser.UserNameLastFM, type, pricing, existingStripeSupporter, source);

        var components = new ActionRowProperties().WithButton($"Complete purchase", url: link,
            emote: EmojiProperties.Standard("⭐"));

        var embed = new EmbedProperties();
        embed.WithColor(DiscordConstants.InformationColorBlue);
        var description = new StringBuilder();
        description.AppendLine($"**Click the unique link below to purchase supporter!**");
        if (type == "yearly")
        {
            description.AppendLine($"-# {pricing.YearlySummary}");
        }
        else if (type == "lifetime")
        {
            description.AppendLine($"-# {pricing.LifetimeSummary}");
        }
        else
        {
            description.AppendLine($"-# {pricing.MonthlySummary}");
        }

        if (SupporterService.IsSupporter(contextUser.UserType))
        {
            embed.AddField("⚠️ Note", "You currently already have access to supporter on your .fmbot account.");
        }

        embed.WithDescription(description.ToString());

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithEmbeds([embed])
            .WithComponents([components])
            .WithFlags(MessageFlags.Ephemeral)));
        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }

    [ComponentInteraction($"{InteractionConstants.SupporterLinks.ManageOverview}")]
    [UserSessionRequired]
    public async Task GetManageOverview()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var existingSupporter = await supporterService.GetSupporter(contextUser.DiscordUserId);

        var embed = new EmbedProperties();
        embed.WithColor(DiscordConstants.InformationColorBlue);
        var manageDescription = new StringBuilder();

        if (SupporterService.IsSupporter(contextUser.UserType))
        {
            if (existingSupporter != null)
            {
                if (existingSupporter.SubscriptionType == SubscriptionType.Discord)
                {
                    manageDescription.AppendLine(
                        "To manage your subscription, go to your Discord settings and then 'Subscriptions'. At the moment this is only available on Discord desktop and browser, not on mobile. ");
                }
                else if (existingSupporter.SubscriptionType == SubscriptionType.MonthlyOpenCollective ||
                         existingSupporter.SubscriptionType == SubscriptionType.YearlyOpenCollective)
                {
                    manageDescription.AppendLine(
                        "Go to [OpenCollective](https://opencollective.com/) and sign in with the email you used during purchase. After signing in go to 'Manage Contributions' where you can change your subscription.");
                }
                else if (existingSupporter.SubscriptionType == SubscriptionType.Stripe)
                {
                    manageDescription.AppendLine(
                        "To manage your subscription, go to the [Stripe customer portal](https://billing.stripe.com/p/login/3cs7ww1tR6ay6t28ww) and authenticate with the email you used during purchase.");
                }
                else
                {
                    manageDescription.AppendLine(
                        "You have lifetime supporter, so there is nothing to manage. Enjoy your supporter! <a:msn_dancing_banana:887595947025133569>");
                }
            }
        }
        else
        {
            manageDescription.AppendLine(
                "You currently don't have an active supporter subscription.");
        }

        embed.AddField("Managing your subscription", manageDescription.ToString());

        if (existingSupporter != null)
        {
            var existingSupporterDescription = new StringBuilder();

            var created = DateTime.SpecifyKind(existingSupporter.Created, DateTimeKind.Utc);
            var createdValue = ((DateTimeOffset)created).ToUnixTimeSeconds();
            existingSupporterDescription.AppendLine($"Activation date: <t:{createdValue}:D>");

            if (existingSupporter.LastPayment.HasValue)
            {
                var lastPayment = DateTime.SpecifyKind(existingSupporter.LastPayment.Value, DateTimeKind.Utc);
                var lastPaymentValue = ((DateTimeOffset)lastPayment).ToUnixTimeSeconds();

                if (existingSupporter.SubscriptionType != SubscriptionType.Discord)
                {
                    existingSupporterDescription.AppendLine($"Last payment: <t:{lastPaymentValue}:D>");
                }
                else
                {
                    existingSupporterDescription.AppendLine($"Expiry date: <t:{lastPaymentValue}:D>");
                }
            }
            else if (existingSupporter.SubscriptionType == SubscriptionType.Discord)
            {
                existingSupporterDescription.AppendLine($"Expiry date: Unknown.");
            }

            if (existingSupporter.SubscriptionType.HasValue)
            {
                existingSupporterDescription.AppendLine(
                    $"Subscription type: `{Enum.GetName(existingSupporter.SubscriptionType.Value)}`");
            }

            if (!string.Equals(contextUser.UserNameLastFM, existingSupporter.Name, StringComparison.OrdinalIgnoreCase))
            {
                existingSupporterDescription.AppendLine(
                    $"Name: **{StringExtensions.Sanitize(existingSupporter.Name)}**");
            }

            embed.AddField("Your details", existingSupporterDescription.ToString());
        }

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithEmbeds([embed])
            .WithFlags(MessageFlags.Ephemeral)));
        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }

    [ComponentInteraction(InteractionConstants.SupporterLinks.GetManageLink)]
    [UserSessionRequired]
    public async Task GetManageLink()
    {
        var stripeSupporter = await supporterService.GetStripeSupporter(this.Context.User.Id);
        var stripeManageLink = await supporterService.GetSupporterManageLink(stripeSupporter);

        var embed = new EmbedProperties();
        embed.WithDescription($"**Click the unique link below to manage your supporter.**");
        embed.WithColor(DiscordConstants.InformationColorBlue);

        var components = new ActionRowProperties()
            .WithButton("Manage subscription", url: stripeManageLink, emote: EmojiProperties.Standard("⭐"));

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithEmbeds([embed])
            .WithComponents([components])
            .WithFlags(MessageFlags.Ephemeral)));
        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }

    [ComponentInteraction("gift-supporter-purchase")]
    public async Task HandleGiftPurchase(string duration, string recipientId)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var recipientDiscordId = ulong.Parse(recipientId);

        var recipientUser = await userService.GetUserAsync(recipientDiscordId);
        if (recipientUser == null)
        {
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithContent("❌ Could not find recipient user.")
                .WithFlags(MessageFlags.Ephemeral));
            return;
        }

        var existingStripeSupporter = await supporterService.GetStripeSupporter(this.Context.User.Id);
        var pricing = await supporterService.GetPricing(this.Context.Interaction.UserLocale,
            existingStripeSupporter?.Currency, StripeSupporterType.GiftedSupporter);
        var priceId = duration switch
        {
            "quarterly" => pricing.QuarterlyPriceId,
            "yearly" => pricing.YearlyPriceId,
            "twoyear" => pricing.TwoYearPriceId,
            "lifetime" => pricing.LifetimePriceId,
            _ => null
        };
        var summary = duration switch
        {
            "quarterly" => pricing.QuarterlySummary,
            "yearly" => pricing.YearlySummary,
            "twoyear" => pricing.TwoYearSummary,
            "lifetime" => pricing.LifetimeSummary,
            _ => null
        };

        if (string.IsNullOrEmpty(priceId))
        {
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithContent("❌ Error while attempting to create checkout, please contact support.")
                .WithFlags(MessageFlags.Ephemeral));
            return;
        }

        try
        {
            var checkoutLink = await supporterService.GetSupporterGiftCheckoutLink(
                contextUser.DiscordUserId,
                contextUser.UserNameLastFM,
                priceId,
                $"gift-{duration}",
                recipientDiscordId,
                recipientUser.UserNameLastFM,
                existingStripeSupporter?.StripeCustomerId);

            if (string.IsNullOrEmpty(checkoutLink))
            {
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithContent("❌ Could not create checkout link. Please try again later.")
                    .WithFlags(MessageFlags.Ephemeral));
                return;
            }

            var description = new StringBuilder();
            description.AppendLine(
                $"**Click the unique link below to complete your gift purchase for {recipientUser.UserNameLastFM}**");
            description.AppendLine($"-# {summary}");
            var embed = new EmbedProperties()
                .WithDescription(description.ToString())
                .WithColor(DiscordConstants.InformationColorBlue);

            var components = new ActionRowProperties()
                .WithButton("Complete purchase", url: checkoutLink, emote: EmojiProperties.Standard("🎁"));

            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithEmbeds([embed])
                .WithComponents([components])
                .WithFlags(MessageFlags.Ephemeral));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception ex)
        {
            await this.Context.HandleCommandException(ex, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Help.CategoryMenu)]
    public async Task HelpCategorySelected()
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);

            var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
            var selectedCategory = stringMenuInteraction.Data.SelectedValues.FirstOrDefault();

            if (string.IsNullOrEmpty(selectedCategory))
            {
                return;
            }

            Enum.TryParse<CommandCategory>(selectedCategory, out var category);

            var prefix = prefixService.GetPrefix(this.Context.Interaction.GuildId);
            var allCommands = commandService.GetCommands().SelectMany(kvp => kvp.Value).ToList();
            var userName = this.Context.User.GlobalName ?? this.Context.User.Username;

            var response = await staticBuilders.BuildHelpResponse(
                allCommands,
                prefix,
                category,
                null,
                userName,
                this.Context.User.Id);

            await this.Context.UpdateInteractionEmbed(response, interactivity, defer: false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Help.CommandMenu)]
    public async Task HelpCommandSelected()
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);

            var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
            var selectedCommand = stringMenuInteraction.Data.SelectedValues.FirstOrDefault();

            if (string.IsNullOrEmpty(selectedCommand))
            {
                return;
            }

            var prefix = prefixService.GetPrefix(this.Context.Interaction.GuildId);
            var allCommands = commandService.GetCommands().SelectMany(kvp => kvp.Value).ToList();
            var userName = this.Context.User.GlobalName ?? this.Context.User.Username;

            var response = await staticBuilders.BuildHelpResponse(
                allCommands,
                prefix,
                null,
                selectedCommand,
                userName,
                this.Context.User.Id);

            await this.Context.UpdateInteractionEmbed(response, interactivity, defer: false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
