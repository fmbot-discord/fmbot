using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using NetCord;
using NetCord.Rest;
using NetCord.Services.Commands;
using FMBot.Domain.Enums;
using User = FMBot.Persistence.Domain.Models.User;

namespace FMBot.Bot.Builders;

public class StaticBuilders
{
    private readonly SupporterService _supporterService;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly FaqService _faqService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly ILastfmRepository _lastfmRepository;


    public StaticBuilders(SupporterService supporterService,
        IDbContextFactory<FMBotDbContext> contextFactory, FaqService faqService, IDataSourceFactory dataSourceFactory,
        ILastfmRepository lastfmRepository)
    {
        this._supporterService = supporterService;
        this._contextFactory = contextFactory;
        this._faqService = faqService;
        this._dataSourceFactory = dataSourceFactory;
        this._lastfmRepository = lastfmRepository;
    }

    public async Task<ResponseModel> OutOfSync(
        ContextModel context,
        UserSettingsModel userSettings = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var differentUser = userSettings != null && userSettings.DifferentUser;
        var userNameLastFm = differentUser ? userSettings.UserNameLastFm : context.ContextUser?.UserNameLastFM;

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);
        container.WithTextDisplay(context.Localize("outofsync.title"));

        var profileUrl = LastfmUrlExtensions.GetUserUrl(userNameLastFm) ?? "https://last.fm/user/_";
        container.WithTextDisplay(differentUser
            ? context.Localize("outofsync.introOther",
                ("user", StringExtensions.Sanitize(userSettings.DisplayName)),
                ("url", profileUrl))
            : context.Localize("outofsync.intro", ("url", profileUrl)));

        if (userNameLastFm != null)
        {
            var status = await GetOutOfSyncStatus(context, userNameLastFm,
                differentUser ? null : context.ContextUser.SessionKeyLastFm);

            if (status != null)
            {
                container.WithSeparator();
                container.WithTextDisplay(status);
            }
        }

        container.WithSeparator();

        var reconnect = new StringBuilder();
        reconnect.AppendLine(context.Localize("outofsync.reconnectTitle"));
        reconnect.AppendLine(userNameLastFm != null
            ? context.Localize("outofsync.reconnectStepSettings", ("username", userNameLastFm))
            : context.Localize("outofsync.reconnectStepSettingsNoUser"));
        reconnect.AppendLine(context.Localize("outofsync.reconnectSteps"));
        reconnect.Append(context.Localize("outofsync.reconnectInterval"));

        container.WithTextDisplay(reconnect.ToString());
        container.WithActionRow(new ActionRowProperties()
            .AddComponents(new LinkButtonProperties("https://www.last.fm/settings/applications",
                context.Localize("outofsync.settingsButton"))));
        container.WithSeparator();

        var thingsToTry = new StringBuilder();
        thingsToTry.AppendLine(context.Localize("outofsync.stillNotWorkingTitle"));
        thingsToTry.Append(context.Localize("outofsync.stillNotWorkingTips"));

        container.WithTextDisplay(thingsToTry.ToString());

        if (PublicProperties.IssuesAtLastFm)
        {
            container.WithSeparator();
            container.WithTextDisplay(context.Localize("outofsync.lastfmIssuesNote"));
        }

        return response;
    }

    private async Task<string> GetOutOfSyncStatus(ContextModel context, string userNameLastFm, string sessionKey)
    {
        var recentTracksTask = this._dataSourceFactory.GetRecentTracksAsync(userNameLastFm, useCache: false,
            sessionKey: sessionKey);
        var userInfo = await this._lastfmRepository.GetLfmUserInfoAsync(userNameLastFm);
        var recentTracks = await recentTracksTask;

        var status = new StringBuilder();
        var nowPlaying = false;

        if (recentTracks.Success && recentTracks.Content?.RecentTracks != null)
        {
            var nowPlayingTrack = recentTracks.Content.RecentTracks.FirstOrDefault(f => f.NowPlaying);
            var lastScrobble = recentTracks.Content.RecentTracks
                .Where(w => w.TimePlayed.HasValue)
                .MaxBy(o => o.TimePlayed);

            if (nowPlayingTrack != null)
            {
                nowPlaying = true;
                status.AppendLine(context.Localize("outofsync.nowPlaying",
                    ("track", StringExtensions.Sanitize(nowPlayingTrack.TrackName)),
                    ("artist", StringExtensions.Sanitize(nowPlayingTrack.ArtistName))));
            }
            else if (lastScrobble != null)
            {
                var timePlayed = DateTime.SpecifyKind(lastScrobble.TimePlayed.Value, DateTimeKind.Utc);
                status.AppendLine(context.Localize("outofsync.lastScrobble",
                    ("track", StringExtensions.Sanitize(lastScrobble.TrackName)),
                    ("artist", StringExtensions.Sanitize(lastScrobble.ArtistName)),
                    ("relativeTimestamp", $"<t:{((DateTimeOffset)timePlayed).ToUnixTimeSeconds()}:R>")));
            }
            else
            {
                status.AppendLine(context.Localize("outofsync.noScrobbles"));
            }
        }

        string verdict = null;

        if (userInfo != null)
        {
            var now = DateTime.UtcNow;
            var expiry = userInfo.SpotifyExpiryEstimateUnix.HasValue
                ? DateTime.UnixEpoch.AddSeconds(userInfo.SpotifyExpiryEstimateUnix.Value)
                : (DateTime?)null;

            if (!expiry.HasValue)
            {
                status.AppendLine(context.Localize("outofsync.spotifyNone"));
                verdict = context.Localize("outofsync.verdictNone");
            }
            else
            {
                var connected = expiry.Value.AddMonths(-6);

                if (expiry.Value < now)
                {
                    status.AppendLine(context.Localize("outofsync.spotifyExpired", ("date", DateDisplay(expiry.Value))));
                    verdict = context.Localize("outofsync.verdictExpired");
                }
                else if (expiry.Value < now.AddDays(10))
                {
                    status.AppendLine(context.Localize("outofsync.spotifyExpiring", ("date", DateDisplay(expiry.Value))));
                    verdict = context.Localize("outofsync.verdictExpiring");
                }
                else if (connected > now.AddDays(-14))
                {
                    status.AppendLine(context.Localize("outofsync.spotifyRecent", ("date", DateDisplay(connected))));
                    verdict = nowPlaying
                        ? context.Localize("outofsync.verdictNowPlaying")
                        : context.Localize("outofsync.verdictRecent");
                }
                else
                {
                    status.AppendLine(context.Localize("outofsync.spotifyActive", ("date", DateDisplay(expiry.Value))));
                    verdict = nowPlaying
                        ? context.Localize("outofsync.verdictNowPlaying")
                        : context.Localize("outofsync.verdictActive");
                }
            }
        }
        else if (nowPlaying)
        {
            verdict = context.Localize("outofsync.verdictNowPlaying");
        }

        if (status.Length == 0)
        {
            return null;
        }

        if (verdict != null)
        {
            status.AppendLine();
            status.Append(verdict);
        }

        return status.ToString();
    }

    private static string DateDisplay(DateTime date)
    {
        var midday = new DateTimeOffset(date.Date.AddHours(12), TimeSpan.Zero);
        return $"<t:{midday.ToUnixTimeSeconds()}:D>";
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
            ResponseType = ResponseType.ComponentsV2,
        };

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        container.WithTextDisplay("## ⭐ .fmbot supporter");
        container.WithSeparator();

        if (expandWithPerks)
        {
            container.WithTextDisplay("**📈 Expanded commands & stats**\n" +
                                      "-# Get expanded `.profile`, `.recap`, `.overview` and `.recent` commands, enable visual graphs, see all `.artistalbums` and `.artisttracks` results, and view lyrics right inside .fmbot.");

            container.WithTextDisplay("**<:history:1131511469096312914> Import your history**\n" +
                                      "-# Import and access your full Spotify and Apple Music history together with your Last.fm data for the most accurate playcounts, listening time, and insights.");

            container.WithTextDisplay("**⚙️ More customization**\n" +
                                      "-# Add close friends, configure shortcuts, customize your `fm` with exclusive options, and set your own global emoji reactions.");

            container.WithTextDisplay("**🎮 Higher limits**\n" +
                                      "-# Play unlimited Jumble and Pixel Jumble games, get better quality output on `.judge`, and add more friends.");

            container.WithTextDisplay("**<:discoveries:1145740579284713512> Go back in time**\n" +
                                      "-# See exactly when you discovered and re-discovered artists, albums, and tracks with the exclusive `.discoveries`, `.gaps`, `.discoverydate` and `.last` commands, and restore past streaks in `.streaks`.");

            container.WithTextDisplay("**⭐ Exclusive supporter perks**\n" +
                                      $"-# Show your support with a badge, gain access to a private [Discord role and channel](https://discord.gg/fmbot), and a higher chance to be featured on Supporter Sunday (next up in {FeaturedService.GetDaysUntilNextSupporterSunday()} {StringExtensions.GetDaysString(FeaturedService.GetDaysUntilNextSupporterSunday())}).");
        }
        else
        {
            container.WithTextDisplay(
                "⭐ Take your .fmbot experience to the next level with new features and benefits. " +
                "Import and use your history, access extra statistics, play unlimited games, support development and much more. " +
                "Please note that .fmbot is not affiliated with Last.fm.");
        }

        var existingSupporter = await this._supporterService.GetSupporter(context.ContextUser.DiscordUserId);
        var stripeSupporter = await this._supporterService.GetStripeSupporter(context.ContextUser.DiscordUserId);

        var bottomButtons = new ActionRowProperties();

        if (publicResponse)
        {
            bottomButtons.AddComponents(new ButtonProperties(
                $"{InteractionConstants.SupporterLinks.GetPurchaseButtons}:true:false:false:{source}",
                SupporterService.IsSupporter(context.ContextUser.UserType)
                    ? "Manage your supporter"
                    : "Get .fmbot supporter",
                ButtonStyle.Secondary));
        }
        else
        {
            if (SupporterService.IsSupporter(context.ContextUser.UserType) &&
                existingSupporter != null && existingSupporter.Expired != true)
            {
                container.WithSeparator();

                if (stripeSupporter == null)
                {
                    container.AddComponent(new ComponentSectionProperties(
                        new ButtonProperties(InteractionConstants.SupporterLinks.ManageOverview,
                            "View current supporter status", ButtonStyle.Secondary))
                    {
                        Components =
                        [
                            new TextDisplayProperties("**Thank you for being a supporter**\n" +
                                                      "Use the button to manage your subscription.")
                        ]
                    });
                }
                else if (stripeSupporter.Type == StripeSupporterType.GiftedSupporter)
                {
                    container.WithTextDisplay("**Thank you for being a supporter**\n" +
                                              "You have been gifted supporter status! Since this was a gift, you cannot manage this subscription directly.");
                }
                else if (string.IsNullOrWhiteSpace(stripeSupporter.StripeSubscriptionId))
                {
                    var stripeManageLink = await this._supporterService.GetSupporterManageLink(stripeSupporter);

                    container.AddComponent(new ComponentSectionProperties(
                        new LinkButtonProperties(stripeManageLink, "Manage billing"))
                    {
                        Components =
                        [
                            new TextDisplayProperties("**Thank you for being a lifetime supporter**\n" +
                                                      "You have lifetime supporter! If you still have an active subscription running, you can cancel it with the button.")
                        ]
                    });
                }
                else
                {
                    var stripeManageLink = await this._supporterService.GetSupporterManageLink(stripeSupporter);

                    container.AddComponent(new ComponentSectionProperties(
                        new LinkButtonProperties(stripeManageLink, "Manage subscription"))
                    {
                        Components =
                        [
                            new TextDisplayProperties("**Thank you for being a supporter**\n" +
                                                      "Use the button to manage your subscription.")
                        ]
                    });
                }
            }
            else
            {
                var pricing = await this._supporterService.GetPricing(userLocale, stripeSupporter?.Currency);

                container.WithSeparator();
                container.AddComponent(new ComponentSectionProperties(
                    new ButtonProperties(
                        $"{InteractionConstants.SupporterLinks.GetPurchaseLink}:monthly:{source}", "Get monthly",
                        ButtonStyle.Primary))
                {
                    Components =
                    [
                        new TextDisplayProperties(
                            $"**Monthly - {pricing.MonthlyPriceString}**\n-# {pricing.MonthlySubText}")
                    ]
                });
                container.WithSeparator();
                container.AddComponent(new ComponentSectionProperties(
                    new ButtonProperties(
                        $"{InteractionConstants.SupporterLinks.GetPurchaseLink}:yearly:{source}", "Get yearly",
                        ButtonStyle.Primary))
                {
                    Components =
                    [
                        new TextDisplayProperties(
                            $"**Yearly - {pricing.YearlyPriceString}**\n-# {pricing.YearlySubText}")
                    ]
                });

                if (pricing.LifetimePriceId != null &&
                    pricing.LifetimePriceString != null &&
                    pricing.LifetimeSubText != null)
                {
                    container.WithSeparator();
                    container.AddComponent(new ComponentSectionProperties(
                        new ButtonProperties(
                            $"{InteractionConstants.SupporterLinks.GetPurchaseLink}:lifetime:{source}", "Get lifetime",
                            ButtonStyle.Primary))
                    {
                        Components =
                        [
                            new TextDisplayProperties(
                                $"**Lifetime - {pricing.LifetimePriceString}**\n-# {pricing.LifetimeSubText}")
                        ]
                    });
                }
            }
        }

        if (showExpandButton)
        {
            if (expandWithPerks)
            {
                bottomButtons.AddComponents(new ButtonProperties(
                    $"{InteractionConstants.SupporterLinks.GetPurchaseButtons}:false:false:true:{source}",
                    "Hide all perks", ButtonStyle.Secondary));
            }
            else
            {
                bottomButtons.AddComponents(new ButtonProperties(
                    $"{InteractionConstants.SupporterLinks.GetPurchaseButtons}:false:true:true:{source}",
                    "View all perks", ButtonStyle.Secondary));
            }
        }

        if (bottomButtons.Any())
        {
            container.WithSeparator();
            container.WithActionRow(bottomButtons);
        }

        return response;
    }

    public async Task<ResponseModel> BuildGiftSupporterResponse(ulong purchaserDiscordId, User recipient,
        string userLocale)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        if (recipient == null)
        {
            response.Text = "❌ This user has not used .fmbot before. They need to set up their account first.";
            response.CommandResponse = CommandResponse.UsernameNotSet;
            response.ResponseType = ResponseType.Text;
            return response;
        }

        if (recipient.DiscordUserId == purchaserDiscordId)
        {
            response.Text = "❌ You cannot gift supporter to yourself. Use `/getsupporter` instead.";
            response.CommandResponse = CommandResponse.WrongInput;
            response.ResponseType = ResponseType.Text;
            return response;
        }

        if (SupporterService.IsSupporter(recipient.UserType))
        {
            response.Text = "❌ The user you want to gift supporter already has access to the supporter perks.";
            response.CommandResponse = CommandResponse.Cooldown;
            response.ResponseType = ResponseType.Text;
            return response;
        }

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.Gold);

        container.WithTextDisplay("## 🎁 Gift .fmbot supporter");
        container.WithTextDisplay(
            $"You are gifting supporter to **{recipient.UserNameLastFM}** (<@{recipient.DiscordUserId}>)");
        container.WithTextDisplay(
            "- This is a gift purchase - no subscription will be created\n" +
            "- The recipient will receive all supporter benefits\n" +
            "- Your identity will not be revealed");

        var existingStripeSupporter = await this._supporterService.GetStripeSupporter(purchaserDiscordId);
        var pricing = await this._supporterService.GetPricing(userLocale, existingStripeSupporter?.Currency,
            StripeSupporterType.GiftedSupporter);

        if (!string.IsNullOrEmpty(pricing.QuarterlyPriceId))
        {
            AddGiftOption("quarterly", "Gift quarter",
                $"**Quarter - {pricing.QuarterlyPriceString}**\n-# 3 months - {pricing.QuarterlySubText}");
        }

        if (!string.IsNullOrEmpty(pricing.YearlyPriceId))
        {
            AddGiftOption("yearly", "Gift year",
                $"**Yearly - {pricing.YearlyPriceString}**\n-# 12 months - {pricing.YearlySubText}");
        }

        if (!string.IsNullOrEmpty(pricing.TwoYearPriceId))
        {
            AddGiftOption("twoyear", "Gift two years",
                $"**Two years - {pricing.TwoYearPriceString}**\n-# 24 months - {pricing.TwoYearSubText}");
        }

        return response;

        void AddGiftOption(string duration, string buttonLabel, string text)
        {
            container.WithSeparator();
            container.AddComponent(new ComponentSectionProperties(
                new ButtonProperties($"gift-supporter-purchase:{duration}:{recipient.DiscordUserId}", buttonLabel,
                    ButtonStyle.Primary))
            {
                Components = [new TextDisplayProperties(text)]
            });
        }
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

        response.ComponentPaginator = StringService.BuildComponentPaginator(pages);

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

        response.ComponentPaginator = StringService.BuildComponentPaginator(pages);

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

        response.ComponentPaginator = StringService.BuildComponentPaginator(pages);

        return response;
    }

    public ResponseModel FaqOverview(bool newResponse = false)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);
        container.WithTextDisplay("## Frequently Asked Questions");
        container.WithTextDisplay("Select a category to browse questions");

        foreach (var category in this._faqService.GetCategories())
        {
            container.WithSeparator();
            container.AddComponents(new ComponentSectionProperties(
                new ButtonProperties(
                    $"{InteractionConstants.Faq.Category}:{category.Id}:{newResponse}",
                    category.GetEmojiProperties(),
                    ButtonStyle.Secondary))
            {
                Components = [new TextDisplayProperties($"**{category.Name}**\n-# {category.Description}")]
            });
        }

        return response;
    }

    public ResponseModel FaqCategoryResponse(string categoryId)
    {
        var category = this._faqService.GetCategory(categoryId);
        if (category == null)
        {
            return new ResponseModel
            {
                ResponseType = ResponseType.Text,
                Text = "Category not found.",
                CommandResponse = CommandResponse.NotFound,
            };
        }

        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);
        container.WithTextDisplay($"## {category.GetEmojiText()} {category.Name}");

        foreach (var question in category.Questions)
        {
            container.WithSeparator();
            container.AddComponents(new ComponentSectionProperties(
                new ButtonProperties(
                    $"{InteractionConstants.Faq.Question}:{category.Id}:{question.Id}",
                    "View",
                    ButtonStyle.Secondary))
            {
                Components = [new TextDisplayProperties($"**{question.Title}**")]
            });
        }

        container.WithSeparator();
        container.AddComponents(new ActionRowProperties()
            .AddComponents(new ButtonProperties(
                InteractionConstants.Faq.Overview,
                "Back to categories",
                ButtonStyle.Secondary)));

        return response;
    }

    public ResponseModel FaqQuestionResponse(string categoryId, string questionId)
    {
        var category = this._faqService.GetCategory(categoryId);
        var question = this._faqService.GetQuestion(categoryId, questionId);
        if (category == null || question == null)
        {
            return new ResponseModel
            {
                ResponseType = ResponseType.Text,
                Text = "Question not found.",
                CommandResponse = CommandResponse.NotFound,
            };
        }

        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);
        container.WithTextDisplay($"## {question.Title}");
        container.WithTextDisplay(question.Answer);
        container.WithSeparator();
        container.AddComponents(new ActionRowProperties()
            .AddComponents(new ButtonProperties(
                $"{InteractionConstants.Faq.Category}:{category.Id}:{false}",
                $"Back to {category.Name}",
                ButtonStyle.Secondary)
            {
                Emoji = category.GetEmojiProperties()
            })
            .AddComponents(new ButtonProperties(
                InteractionConstants.Faq.Overview,
                "Back to categories",
                ButtonStyle.Secondary)));

        return response;
    }
}
