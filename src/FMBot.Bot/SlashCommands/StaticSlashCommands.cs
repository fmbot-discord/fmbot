using System;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Models;

namespace FMBot.Bot.SlashCommands;

public class StaticSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly StaticBuilders _staticBuilders;
    private readonly SupporterService _supporterService;

    private InteractiveService Interactivity { get; }


    public StaticSlashCommands(UserService userService, StaticBuilders staticBuilders, InteractiveService interactivity,
        SupporterService supporterService)
    {
        this._userService = userService;
        this._staticBuilders = staticBuilders;
        this.Interactivity = interactivity;
        this._supporterService = supporterService;
    }

    [SlashCommand("outofsync", "Shows info if your Last.fm isn't up to date with Spotify")]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task OutOfSyncAsync([Summary("private", "Show info privately?")] bool privateResponse = true)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var response = StaticBuilders.OutOfSync(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this.Interactivity, response, ephemeral: privateResponse);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("getsupporter", "Information about getting supporter or your current subscription")]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task GetSupporterAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var response = await this._staticBuilders.SupporterButtons(new ContextModel(this.Context, contextUser),
            false, true);

        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("supporters", "Shows all current supporters")]
    public async Task SupportersAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var response = await this._staticBuilders.SupportersAsync(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [ComponentInteraction($"{InteractionConstants.SupporterLinks.GetPurchaseButtons}-*-*-*")]
    [UserSessionRequired]
    public async Task SupporterButtons(string newResponse, string expandWithPerks, string showExpandButton)
    {
        try
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var response = await this._staticBuilders.SupporterButtons(new ContextModel(this.Context, contextUser),
                expandWithPerks == "true", showExpandButton == "true");

            if (newResponse == "true")
            {
                await this.Context.SendResponse(this.Interactivity, response, true);
            }
            else
            {
                await Context.Interaction.DeferAsync(ephemeral: true);

                await this.Context.Interaction.ModifyOriginalResponseAsync(m =>
                {
                    m.Components = response.Components?.Build();
                    m.Embed = response.Embed?.Build();
                });
            }

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.SupporterLinks.GetPurchaseLink}-*")]
    [UserSessionRequired]
    public async Task GetSupporterLink(string type)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var existingStripeSupporter = await this._supporterService.GetStripeSupporter(contextUser.DiscordUserId);

        var link = await this._supporterService.GetSupporterCheckoutLink(this.Context.User.Id,
            contextUser.UserNameLastFM, type, existingStripeSupporter);

        var components = new ComponentBuilder().WithButton($"Complete purchase", style: ButtonStyle.Link, url: link,
            emote: Emoji.Parse("⭐"));

        var embed = new EmbedBuilder();
        embed.WithColor(DiscordConstants.InformationColorBlue);
        var description = new StringBuilder();
        description.AppendLine($"**Click the unique link below to purchase supporter!**");
        if (type == "yearly")
        {
            description.AppendLine("-# $23.99 — Yearly");
        }
        else
        {
            description.AppendLine("-# $3.99 — Monthly");
        }

        if (SupporterService.IsSupporter(contextUser.UserType))
        {
            embed.AddField("⚠️ Note", "You currently already have access to supporter on your .fmbot account.");
        }

        embed.WithDescription(description.ToString());

        await RespondAsync(embed: embed.Build(), ephemeral: true, components: components.Build());
        this.Context.LogCommandUsed();
    }

    [ComponentInteraction($"{InteractionConstants.SupporterLinks.ManageOverview}")]
    [UserSessionRequired]
    public async Task GetManageOverview()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var existingSupporter = await this._supporterService.GetSupporter(contextUser.DiscordUserId);

        var embed = new EmbedBuilder();
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
                        "You have lifetime supporter, so there is nothing to manage. Enjoy your supporter!");
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

        await RespondAsync(embed: embed.Build(), ephemeral: true);
        this.Context.LogCommandUsed();
    }

    [ComponentInteraction(InteractionConstants.SupporterLinks.GetManageLink)]
    [UserSessionRequired]
    public async Task GetManageLink()
    {
        var stripeSupporter = await this._supporterService.GetStripeSupporter(this.Context.User.Id);
        var stripeManageLink = await this._supporterService.GetSupporterManageLink(stripeSupporter);

        var embed = new EmbedBuilder();
        embed.WithDescription($"**Click the unique link below to manage your supporter.**");
        embed.WithColor(DiscordConstants.InformationColorBlue);

        var components = new ComponentBuilder()
            .WithButton("Manage subscription", style: ButtonStyle.Link, url: stripeManageLink, emote: Emoji.Parse("⭐"));

        await RespondAsync(embed: embed.Build(), ephemeral: true, components: components.Build());
        this.Context.LogCommandUsed();
    }
}
