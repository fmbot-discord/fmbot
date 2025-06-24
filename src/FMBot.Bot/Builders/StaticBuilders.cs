using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Shared.Domain.Enums;

namespace FMBot.Bot.Builders;

public class StaticBuilders
{
    private readonly SupporterService _supporterService;
    private readonly UserService _userService;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;


    public StaticBuilders(SupporterService supporterService, UserService userService,
        IDbContextFactory<FMBotDbContext> contextFactory)
    {
        this._supporterService = supporterService;
        this._userService = userService;
        this._contextFactory = contextFactory;
    }

    public static ResponseModel OutOfSync(
        ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        response.Embed.WithTitle("Using Spotify and tracking is out of sync?");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        var embedDescription = new StringBuilder();
        embedDescription.AppendLine(".fmbot uses your Last.fm account for knowing what you listen to. ");
        embedDescription.AppendLine(
            $"Unfortunately, Last.fm and Spotify sometimes have issues keeping up to date with your current song, which can cause `{context.Prefix}fm` and other commands to lag behind the song you're currently listening to.");
        embedDescription.AppendLine();
        embedDescription.Append(
            "First, **.fmbot is not affiliated with Last.fm**. Your music is tracked by Last.fm, and not by .fmbot. ");
        embedDescription.AppendLine(
            "This means that this is a Last.fm issue and **not an .fmbot issue**. __We can't fix it for you__, but we can give you some tips that worked for others.");
        embedDescription.AppendLine();
        embedDescription.AppendLine("Some things you can try that usually work:");
        embedDescription.AppendLine("- Restarting your Spotify application");
        embedDescription.AppendLine(
            "- Disconnecting and **reconnecting Spotify in [your Last.fm settings](https://www.last.fm/settings/applications)**");
        embedDescription.AppendLine();
        embedDescription.AppendLine(
            "If the two options above don't work, check out **[the complete guide for this issue on the Last.fm support forums](https://support.last.fm/t/spotify-has-stopped-scrobbling-what-can-i-do/3184)**.");

        response.Embed.WithDescription(embedDescription.ToString());

        if (PublicProperties.IssuesAtLastFm)
        {
            response.Embed.AddField("Note:",
                "⚠️ [Last.fm](https://twitter.com/lastfmstatus) is currently experiencing issues, so the steps listed above might not work. " +
                ".fmbot is not affiliated with Last.fm.");
        }

        response.Components = new ComponentBuilder()
            .WithButton("Last.fm settings", style: ButtonStyle.Link, url: "https://www.last.fm/settings/applications")
            .WithButton("Full guide", style: ButtonStyle.Link,
                url: "https://support.last.fm/t/spotify-has-stopped-scrobbling-what-can-i-do/3184")
            .WithButton("Your profile", style: ButtonStyle.Link,
                url: $"{LastfmUrlExtensions.GetUserUrl(context.ContextUser.UserNameLastFM)}");

        return response;
    }

    public async Task<ResponseModel> SupporterButtons(
        ContextModel context,
        bool expandWithPerks,
        bool showExpandButton,
        bool publicResponse = false,
        string userLocale = null,
        string source = "unknown")
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);
        response.Components = new ComponentBuilder();

        if (expandWithPerks)
        {
            response.Embed.AddField("📊 More stats, more insights",
                "-# Unlock lifetime Last.fm data for deeper analysis. Track first listens, get expanded profiles, expanded commands, your full Discogs collection and more.");

            response.Embed.AddField("<:history:1131511469096312914> Import your history",
                "-# Import and use your full Spotify and Apple Music history together with your Last.fm data for the most accurate playcounts, listening time, and insights.");

            response.Embed.AddField("<:discoveries:1145740579284713512> Go back in time",
                "-# See exactly when you discovered and re-discovered artists, albums, and tracks in commands and through the exclusive Discoveries and Gaps commands.");

            response.Embed.AddField("🎮 Play unlimited games",
                "-# Remove the daily limit on Jumble and Pixel Jumble and play as much as you want.");

            response.Embed.AddField("⚙️ Customize your commands",
                "-# Expand your .fm footer, add more friends, set automatic emoji reactions, and personalize your experience.");

            response.Embed.AddField("🎤 View lyrics",
                "-# View your favorite `.lyrics` directly inside of .fmbot.");

            response.Embed.AddField("⭐ Exclusive supporter perks",
                $"-# Show your support with a badge, gain access to a private [Discord role and channel](https://discord.gg/fmbot), and a higher chance to be featured on Supporter Sunday (next up in {FeaturedService.GetDaysUntilNextSupporterSunday()} {StringExtensions.GetDaysString(FeaturedService.GetDaysUntilNextSupporterSunday())}).");
        }
        else
        {
            response.Embed.WithDescription(
                "⭐ Take your .fmbot experience to the next level with new features and benefits. " +
                "Import and use your history, access extra statistics, play unlimited games, support development and much more. " +
                "Please note that .fmbot is not affiliated with Last.fm.");
        }

        var existingSupporter = await this._supporterService.GetSupporter(context.ContextUser.DiscordUserId);
        var stripeSupporter = await this._supporterService.GetStripeSupporter(context.ContextUser.DiscordUserId);

        if (publicResponse)
        {
            response.Components.WithButton(
                SupporterService.IsSupporter(context.ContextUser.UserType)
                    ? "Manage your supporter"
                    : "Get .fmbot supporter",
                style: ButtonStyle.Secondary,
                customId: $"{InteractionConstants.SupporterLinks.GetPurchaseButtons}-true-false-false-{source}");
        }
        else
        {
            if (SupporterService.IsSupporter(context.ContextUser.UserType) &&
                existingSupporter != null && existingSupporter.Expired != true)
            {
                if (stripeSupporter == null)
                {
                    response.Embed.AddField("Thank you for being a supporter",
                        "Manage your subscription with the button below.");

                    response.Components.WithButton("View current supporter status", style: ButtonStyle.Secondary,
                        customId: InteractionConstants.SupporterLinks.ManageOverview);
                }
                else if (stripeSupporter.Type == StripeSupporterType.GiftedSupporter)
                {
                    response.Embed.AddField("Thank you for being a supporter",
                        "You have been gifted supporter status! Since this was a gift, you cannot manage this subscription directly.");
                }
                else
                {
                    response.Embed.AddField("Thank you for being a supporter",
                        "Manage your subscription with the link below.");

                    var stripeManageLink = await this._supporterService.GetSupporterManageLink(stripeSupporter);

                    response.Components.WithButton("Manage subscription", style: ButtonStyle.Link,
                        url: stripeManageLink);
                }
            }
            else
            {
                var pricing = await this._supporterService.GetPricing(userLocale, stripeSupporter?.Currency);
                response.Embed.AddField($"Monthly - {pricing.MonthlyPriceString}",
                    $"-# {pricing.MonthlySubText}", true);
                response.Embed.AddField($"Yearly - {pricing.YearlyPriceString}",
                    $"-# {pricing.YearlySubText}", true);

                response.Components = new ComponentBuilder()
                    .WithButton("Get monthly",
                        customId: $"{InteractionConstants.SupporterLinks.GetPurchaseLink}-monthly-{source}")
                    .WithButton("Get yearly",
                        customId: $"{InteractionConstants.SupporterLinks.GetPurchaseLink}-yearly-{source}");

                if (pricing.LifetimePriceId != null &&
                    pricing.LifetimePriceString != null &&
                    pricing.LifetimeSubText != null)
                {
                    response.Embed.AddField($"Lifetime - {pricing.LifetimePriceString}",
                        $"-# {pricing.LifetimeSubText}", true);

                    response.Components
                        .WithButton("Get lifetime",
                            customId: $"{InteractionConstants.SupporterLinks.GetPurchaseLink}-lifetime-{source}");
                }
            }
        }

        if (showExpandButton)
        {
            if (expandWithPerks)
            {
                response.Components.WithButton("Hide all perks", style: ButtonStyle.Secondary,
                    customId: $"{InteractionConstants.SupporterLinks.GetPurchaseButtons}-false-false-true-{source}");
            }
            else
            {
                response.Components.WithButton("View all perks", style: ButtonStyle.Secondary,
                    customId: $"{InteractionConstants.SupporterLinks.GetPurchaseButtons}-false-true-true-{source}");
            }
        }

        return response;
    }

    public async Task<ResponseModel> BuildGiftSupporterResponse(ulong purchaserDiscordId, User recipient,
        string userLocale)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed
            .WithTitle("🎁 Gift .fmbot supporter")
            .WithDescription(
                $"You are gifting supporter to **{recipient.UserNameLastFM}** (<@{recipient.DiscordUserId}>)")
            .WithColor(Color.Gold);

        var existingStripeSupporter = await this._supporterService.GetStripeSupporter(purchaserDiscordId);
        var pricing = await this._supporterService.GetPricing(userLocale, existingStripeSupporter?.Currency, StripeSupporterType.GiftedSupporter);

        response.Embed.AddField("Note",
            "- This is a gift purchase - no subscription will be created\n" +
            "- The recipient will receive all supporter benefits\n" +
            "- Your identity will not be revealed",
            false);

        var components = new ComponentBuilder();
        var actionRow = new ActionRowBuilder();

        if (!string.IsNullOrEmpty(pricing.QuarterlyPriceId))
        {
            response.Embed.AddField($"Quarterly - {pricing.QuarterlyPriceString}",
                $"-# {pricing.QuarterlySubText}", true);
            actionRow.WithButton("Gift quarter", $"gift-supporter-purchase-quarterly-{recipient.DiscordUserId}");
        }

        if (!string.IsNullOrEmpty(pricing.YearlyPriceId))
        {
            response.Embed.AddField($"Yearly - {pricing.YearlyPriceString}",
                $"-# {pricing.YearlySubText}", true);
            actionRow.WithButton("Gift year", $"gift-supporter-purchase-yearly-{recipient.DiscordUserId}");
        }

        if (!string.IsNullOrEmpty(pricing.TwoYearPriceId))
        {
            response.Embed.AddField($"Two years - {pricing.TwoYearPriceString}",
                $"-# {pricing.TwoYearSubText}", true);
            actionRow.WithButton("Gift two years", $"gift-supporter-purchase-twoyear-{recipient.DiscordUserId}");
        }

        // if (!string.IsNullOrEmpty(pricing.LifetimePriceId))
        // {
        //     actionRow.WithButton("Lifetime", $"gift-supporter-purchase-lifetime-{recipient.DiscordUserId}", ButtonStyle.Success, new Emoji("⭐"));
        // }

        components.AddRow(actionRow);

        response.Components = components;

        return response;
    }

    public async Task<ResponseModel> SupportersAsync(
        ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        var supporters = await this._supporterService.GetAllVisibleSupporters();

        var supporterLists = supporters.ChunkBy(10);

        var description = new StringBuilder();
        description.AppendLine(
            $"Thank you to all our supporters that help keep .fmbot running. To view all supporter perks and join this list, run `{context.Prefix}getsupporter`.");
        description.AppendLine();

        var pages = new List<PageBuilder>();
        foreach (var supporterList in supporterLists)
        {
            var supporterString = new StringBuilder();
            supporterString.Append(description.ToString());

            foreach (var supporter in supporterList)
            {
                var type = supporter.SupporterType switch
                {
                    SupporterType.Guild => " (server)",
                    SupporterType.User => "",
                    SupporterType.Company => " (business)",
                    _ => ""
                };

                supporterString.AppendLine($"- **{supporter.Name}** {type}");
            }

            pages.Add(new PageBuilder()
                .WithDescription(supporterString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithTitle(".fmbot supporters overview"));
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);

        return response;
    }

    public async Task<ResponseModel> OpenCollectiveSupportersAsync(ContextModel context, bool expiredOnly)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        var existingSupporters = await this._supporterService.GetAllSupporters();

        var supporters = await this._supporterService.GetOpenCollectiveSupporters();

        if (expiredOnly)
        {
            var ocIds = existingSupporters
                .Where(w => w.SubscriptionType == SubscriptionType.MonthlyOpenCollective && w.OpenCollectiveId != null)
                .OrderByDescending(o => o.LastPayment)
                .GroupBy(g => g.OpenCollectiveId)
                .ToDictionary(d => d.Key, d => d.First());
            supporters.Users = supporters.Users.Where(w => ocIds.ContainsKey(w.Id) &&
                                                           ocIds[w.Id].Expired != true &&
                                                           ocIds[w.Id].SubscriptionType ==
                                                           SubscriptionType.MonthlyOpenCollective &&
                                                           ocIds[w.Id].LastPayment <= DateTime.UtcNow.AddDays(-61))
                .ToList();
        }

        var supporterLists = supporters.Users.OrderByDescending(o => o.FirstPayment).Chunk(10);

        var description = new StringBuilder();

        var pages = new List<PageBuilder>();
        foreach (var supporterList in supporterLists)
        {
            var supporterString = new StringBuilder();
            supporterString.Append(description.ToString());

            foreach (var supporter in supporterList)
            {
                supporterString.AppendLine($"**{supporter.Name}** - `{supporter.Id}` - `{supporter.SubscriptionType}`");

                var lastPayment = DateTime.SpecifyKind(supporter.LastPayment, DateTimeKind.Utc);
                var lastPaymentValue = ((DateTimeOffset)lastPayment).ToUnixTimeSeconds();

                var firstPayment = DateTime.SpecifyKind(supporter.FirstPayment, DateTimeKind.Utc);
                var firstPaymentValue = ((DateTimeOffset)firstPayment).ToUnixTimeSeconds();

                if (firstPaymentValue == lastPaymentValue &&
                    supporter.SubscriptionType == SubscriptionType.LifetimeOpenCollective)
                {
                    supporterString.AppendLine($"Purchase date: <t:{firstPaymentValue}:D>");
                }
                else
                {
                    supporterString.AppendLine(
                        $"First payment: <t:{firstPaymentValue}:D> - Last payment: <t:{lastPaymentValue}:D>");
                }

                var existingSupporter = existingSupporters.FirstOrDefault(f => f.OpenCollectiveId == supporter.Id);
                if (existingSupporter != null)
                {
                    supporterString.Append($"✅ Connected");

                    if (existingSupporter.Expired == true)
                    {
                        supporterString.Append($" *(Expired)*");
                    }

                    supporterString.Append(
                        $" - {existingSupporter.DiscordUserId} / <@{existingSupporter.DiscordUserId}>");
                    supporterString.AppendLine();
                }

                supporterString.AppendLine();
            }

            pages.Add(new PageBuilder()
                .WithDescription(supporterString.ToString())
                .WithUrl("https://opencollective.com/fmbot/transactions")
                .WithColor(DiscordConstants.InformationColorBlue)
                .WithAuthor(response.EmbedAuthor)
                .WithFooter($"OC: {supporters.Users.Count} - db: {existingSupporters.Count}\n" +
                            $"{supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.MonthlyOpenCollective && c.LastPayment >= DateTime.Now.AddDays(-35))} active monthly ({supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.MonthlyOpenCollective)} total)\n" +
                            $"{supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.YearlyOpenCollective && c.LastPayment >= DateTime.Now.AddDays(-370))} active yearly ({supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.YearlyOpenCollective)} total)\n" +
                            $"{supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.LifetimeOpenCollective)} lifetime")
                .WithTitle(".fmbot opencollective supporters overview"));
        }

        if (!pages.Any())
        {
            pages.Add(new PageBuilder()
                .WithDescription("No pages, most likely an error while fetching supporters"));
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);

        return response;
    }

    public async Task<ResponseModel> DiscordSupportersAsync(
        ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        await using var db = await this._contextFactory.CreateDbContextAsync();

        var existingSupporters = await db.Supporters
            .Where(w => w.SubscriptionType == SubscriptionType.Discord &&
                        w.DiscordUserId.HasValue)
            .ToListAsync();

        var userIds = existingSupporters.Select(s => s.DiscordUserId.Value).ToList();
        var users = await db.Users
            .AsQueryable()
            .Where(w => userIds.Contains(w.DiscordUserId))
            .ToListAsync();

        var supporterLists = existingSupporters.OrderByDescending(o => o.Created).Chunk(10);

        var footer = new StringBuilder();

        footer.Append(
            $"Total: {existingSupporters.Count()}");
        footer.Append(
            $" - Active {existingSupporters.Count(c => c.Expired != true)}");
        footer.AppendLine();
        footer.Append(
            $"Average new per day: {Math.Round(existingSupporters.Where(w => w.Created >= DateTime.UtcNow.AddDays(-60)).GroupBy(g => g.Created.Date).Average(c => c.Count()), 1)}");
        footer.AppendLine();
        footer.Append(
            $"New yesterday: {existingSupporters.Count(c => c.Created.Date == DateTime.UtcNow.AddDays(-1).Date)}");
        footer.Append(
            $" - New today: {existingSupporters.Count(c => c.Created.Date == DateTime.UtcNow.Date)}");
        footer.AppendLine();
        footer.Append(
            $"New last month: {existingSupporters.Count(c => c.Created.Month == DateTime.UtcNow.AddMonths(-1).Month &&
                                                             c.Created.Year == DateTime.UtcNow.AddMonths(-1).Year)}");
        footer.Append(
            $" - New this month: {existingSupporters.Count(c => c.Created.Month == DateTime.UtcNow.Month &&
                                                                c.Created.Year == DateTime.UtcNow.Year)}");

        var pages = new List<PageBuilder>();
        foreach (var supporterList in supporterLists)
        {
            var supporterString = new StringBuilder();

            foreach (var supporter in supporterList)
            {
                supporterString.Append($"**{supporter.DiscordUserId}** - <@{supporter.DiscordUserId}>");

                var user = users.FirstOrDefault(f => f.DiscordUserId == supporter.DiscordUserId.Value);
                if (user != null)
                {
                    supporterString.Append(
                        $" - [{user.UserNameLastFM}]({Constants.LastFMUserUrl}{user.UserNameLastFM})");
                }
                else
                {
                    supporterString.Append($" - No .fmbot user :(");
                }

                supporterString.AppendLine();

                var startsAtValue = ((DateTimeOffset)supporter.Created).ToUnixTimeSeconds();

                if (supporter.LastPayment.HasValue)
                {
                    var endsAt = DateTime.SpecifyKind(supporter.LastPayment.Value, DateTimeKind.Utc);
                    var endsAtValue = ((DateTimeOffset)endsAt).ToUnixTimeSeconds();

                    supporterString.AppendLine($"Started <t:{startsAtValue}:f> - Ends on <t:{endsAtValue}:D>");
                }
                else
                {
                    supporterString.AppendLine($"Started <t:{startsAtValue}:f> - Ends on unknown>");
                }


                supporterString.AppendLine();
            }

            pages.Add(new PageBuilder()
                .WithDescription(supporterString.ToString())
                .WithColor(DiscordConstants.InformationColorBlue)
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString())
                .WithTitle(".fmbot Discord supporters overview"));
        }

        if (!pages.Any())
        {
            pages.Add(new PageBuilder()
                .WithDescription("No pages, most likely an error while fetching supporters"));
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);

        return response;
    }
}
