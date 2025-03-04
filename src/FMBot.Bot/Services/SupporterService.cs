using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Webhook;
using Discord.WebSocket;
using FMBot.Bot.Resources;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Subscriptions.Models;
using FMBot.Subscriptions.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog;
using Shared.Domain.Enums;
using Shared.Domain.Models;
using Web.InternalApi;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;
using User = FMBot.Persistence.Domain.Models.User;

namespace FMBot.Bot.Services;

public class SupporterService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly OpenCollectiveService _openCollectiveService;
    private readonly DiscordSkuService _discordSkuService;
    private readonly BotSettings _botSettings;
    private readonly IMemoryCache _cache;
    private readonly IndexService _indexService;
    private readonly DiscordShardedClient _client;
    private readonly SupporterLinkService.SupporterLinkServiceClient _supporterLinkService;

    public SupporterService(IDbContextFactory<FMBotDbContext> contextFactory,
        OpenCollectiveService openCollectiveService,
        IOptions<BotSettings> botSettings,
        IMemoryCache cache,
        IndexService indexService,
        DiscordSkuService discordSkuService,
        DiscordShardedClient client,
        SupporterLinkService.SupporterLinkServiceClient supporterLinkService)
    {
        this._contextFactory = contextFactory;
        this._openCollectiveService = openCollectiveService;
        this._cache = cache;
        this._indexService = indexService;
        this._discordSkuService = discordSkuService;
        this._client = client;
        this._supporterLinkService = supporterLinkService;
        this._botSettings = botSettings.Value;
    }

    public async Task<string> GetRandomSupporter(IGuild guild, UserType userUserType)
    {
        if (guild == null)
        {
            return null;
        }

        if (userUserType != UserType.User)
        {
            return null;
        }

        if (this._cache.TryGetValue(GetGuildPromoCacheKey(guild.Id), out _))
        {
            return null;
        }

        var rnd = new Random();
        var randomHintNumber = rnd.Next(0, Constants.SupporterMessageChance);

        if (randomHintNumber == 1)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var supporters = db.Supporters
                .AsQueryable()
                .Where(w => w.SupporterMessagesEnabled &&
                            w.Name != null &&
                            w.Name != "Incognito" &&
                            w.Name != "Guest")
                .ToList();

            if (!supporters.Any())
            {
                return null;
            }

            var rand = new Random();
            var randomSupporter = supporters[rand.Next(supporters.Count())];

            SetGuildSupporterPromoCache(guild.Id);

            return randomSupporter.Name;
        }

        return null;
    }

    public static bool IsSupporter(UserType userType)
    {
        return userType != UserType.User;
    }

    public bool ShowSupporterPromotionalMessage(UserType userType, ulong? guildId)
    {
        if (IsSupporter(userType))
        {
            return false;
        }

        if (guildId != null)
        {
            if (this._cache.TryGetValue(GetGuildPromoCacheKey(guildId), out _))
            {
                return false;
            }
        }

        return true;
    }

    public static async Task SendSupporterWelcomeMessage(IUser discordUser, bool hasDiscogs, Supporter supporter,
        bool reactivation = false)
    {
        var thankYouEmbed = new EmbedBuilder();
        thankYouEmbed.WithColor(DiscordConstants.InformationColorBlue);

        var thankYouMessage = new StringBuilder();

        thankYouEmbed.WithAuthor("Thank you for getting .fmbot supporter!");

        if (supporter != null && (supporter.SubscriptionType == SubscriptionType.LifetimeOpenCollective ||
                                  supporter.SubscriptionType == SubscriptionType.LifetimeStripeManual))
        {
            thankYouMessage.AppendLine(
                "Your purchase allows us to keep the bot running and continuously add improvements. Here's an overview of the features you can now access:");
        }
        else
        {
            thankYouMessage.AppendLine(
                "Your subscription allows us to keep the bot running and continuously add improvements. Here's an overview of the features you can now access:");
        }

        thankYouMessage.AppendLine();

        thankYouMessage.AppendLine("📈 **Expanded commands with more statistics**");
        thankYouMessage.AppendLine("- `.profile` — Expanded profile with more insights and a yearly overview");
        thankYouMessage.AppendLine("- `.year` — Extra page with discoveries and months");
        thankYouMessage.AppendLine("- `.recent` — See your lifetime listening history and filter to specific artists");
        thankYouMessage.AppendLine("- `.overview` — See your lifetime listening history day to day");
        thankYouMessage.AppendLine("- `.artisttracks` — See all tracks, even those outside of your top 6000");
        thankYouMessage.AppendLine("- `.artistalbums` — See all albums, even those outside of your top 5000");
        thankYouMessage.AppendLine();

        thankYouMessage.AppendLine("🎮 **Enhanced commands**");
        thankYouMessage.AppendLine(
            $"- `.featured` — Chance to get featured on Supporter Sunday (next in {FeaturedService.GetDaysUntilNextSupporterSunday()} {StringExtensions.GetDaysString(FeaturedService.GetDaysUntilNextSupporterSunday())})");
        thankYouMessage.AppendLine("- `.judge` — Better output and ability to use the command on others");
        thankYouMessage.AppendLine("- `.jumble` / `.j` — Play unlimited Jumble games");
        thankYouMessage.AppendLine("- `.pixel` / `.px` — Play unlimited Pixel Jumble games");
        thankYouMessage.AppendLine();

        thankYouMessage.AppendLine("<:discoveries:1145740579284713512> **Go back in time**");
        thankYouMessage.AppendLine("- `.discoveries` — View your recently discovered artists");
        thankYouMessage.AppendLine("- `.gaps` — View music you returned to after a gap in listening");
        thankYouMessage.AppendLine("- `.discoverydate` / `.dd` — View when you discovered an artist, album and track");
        thankYouMessage.AppendLine("- `.artist`, `.album`, `.track` — See discovery dates");
        thankYouMessage.AppendLine();

        thankYouMessage.AppendLine("<:history:1131511469096312914> **Import your Spotify and/or Apple Music**");
        thankYouMessage.AppendLine("- `/import spotify` — Import your full Spotify History");
        thankYouMessage.AppendLine("- `/import applemusic` — Import your full Apple Music History");
        thankYouMessage.AppendLine("- `/import manage` — Configure your imports and how they're used");
        thankYouMessage.AppendLine();

        thankYouMessage.AppendLine("⚙️ **More customization**");
        thankYouMessage.AppendLine($"- `/fmmode` — Expand your `fm` footer with more and exclusive options");
        thankYouMessage.AppendLine($"- `.userreactions` — Set your own emote reactions used globally");
        thankYouMessage.AppendLine(
            $"- `.addfriends` — Add up to {Constants.MaxFriendsSupporter} friends, up from {Constants.MaxFriends}");
        thankYouMessage.AppendLine();

        thankYouMessage.AppendLine("🌐 **Community perks**");
        thankYouMessage.AppendLine("- Support hosting and development");
        thankYouMessage.AppendLine("- ⭐ — Supporter badge in the bot");
        thankYouMessage.AppendLine("- Exclusive role and channel on [our server](https://discord.gg/fmbot)");
        thankYouMessage.AppendLine();

        thankYouEmbed.WithDescription(thankYouMessage.ToString());

        if (reactivation)
        {
            var reactivateEmbed = new EmbedBuilder();
            reactivateEmbed.WithDescription(
                "Welcome back. Please use the `/import manage` command to re-activate the import service if you've used it previously.");
            reactivateEmbed.WithColor(DiscordConstants.InformationColorBlue);
            await discordUser.SendMessageAsync(embeds: [thankYouEmbed.Build(), reactivateEmbed.Build()]);
        }
        else
        {
            await discordUser.SendMessageAsync(embed: thankYouEmbed.Build());
        }
    }

    public static async Task SendSupporterGoodbyeMessage(IUser discordUser, bool openCollective = true,
        bool hadImported = false)
    {
        var goodbyeEmbed = new EmbedBuilder();
        goodbyeEmbed.WithColor(DiscordConstants.InformationColorBlue);

        goodbyeEmbed.AddField("⭐ .fmbot supporter expired",
            "Your .fmbot supporter subscription has expired. Sorry to see you go!\n\n" +
            "Thanks for having supported the bot! Feel free to open a thread in #help on [our server](https://discord.gg/fmbot) if you have any feedback.");

        if (hadImported)
        {
            goodbyeEmbed.AddField($"{DiscordConstants.Imports} Importing service deactivated",
                "The import service is no longer active, so the bot will now only use your Last.fm stats without imported .fmbot data. Your imports are however saved and will be available again if you resubscribe in the future.");
        }

        var buttons = new ComponentBuilder()
            .WithButton("Resubscribe", style: ButtonStyle.Secondary,
                customId: InteractionConstants.SupporterLinks.GetPurchaseButtonsDefault)
            .WithButton("Support server", style: ButtonStyle.Link, url: "https://discord.gg/fmbot");

        goodbyeEmbed.AddField("\ud83c\udff7\ufe0f Annual deal",
            "Resubscribe and save 50% on .fmbot supporter with our new yearly option.");

        await discordUser.SendMessageAsync(embed: goodbyeEmbed.Build(), components: buttons.Build());
    }

    public async Task<(string message, bool showUpgradeButton)> GetPromotionalUpdateMessage(User user, string prfx,
        ulong? guildId = null)
    {
        var randomHintNumber = RandomNumberGenerator.GetInt32(1, 65);
        string message = null;
        var showUpgradeButton = false;

        if (!IsSupporter(user.UserType))
        {
            switch (randomHintNumber)
            {
                case 2:
                {
                    SetGuildSupporterPromoCache(guildId);
                    message =
                        $"*⭐ .fmbot supporters get extra stats and insights into their music history*";
                    showUpgradeButton = true;
                    break;
                }
                case 3:
                {
                    message =
                        $"*<:vinyl:1043644602969763861> Use Discogs for your vinyl collection? Link your account with `{prfx}discogs`*";
                    break;
                }
                case 4:
                {
                    if (user.FmFooterOptions == FmFooterOption.TotalScrobbles)
                    {
                        message =
                            $"*⚙️ Customize your `{prfx}fm` with the custom footer options. Get started by using `/fmmode`*";
                        break;
                    }

                    SetGuildSupporterPromoCache(guildId);
                    message =
                        $"*⚙️ Set up to 10 options in your `{prfx}fm` footer as an .fmbot supporter*";
                    showUpgradeButton = true;
                    break;
                }
                case 5:
                {
                    SetGuildSupporterPromoCache(guildId);
                    message =
                        $"*🔥 Supporters get an improved GPT-4o powered `{prfx}judge` command. They also get higher usage limits and the ability to use the command on others*";
                    showUpgradeButton = true;
                    break;
                }
                case 6:
                case 7:
                {
                    SetGuildSupporterPromoCache(guildId);
                    message =
                        $"*<:spotify:882221219334725662> Supporters can import and use their full Spotify history in the bot*";
                    showUpgradeButton = true;
                    break;
                }
                case 8:
                case 9:
                {
                    SetGuildSupporterPromoCache(guildId);
                    message =
                        $"*<:apple_music:1218182727149420544> Supporters can import and use their full Apple Music history in the bot*";
                    showUpgradeButton = true;
                    break;
                }
                case 10:
                {
                    SetGuildSupporterPromoCache(guildId);
                    message =
                        $"*{DiscordConstants.Discoveries} View which artists you recently discovered with .fmbot supporter*";
                    showUpgradeButton = true;
                    break;
                }
                case 11:
                {
                    SetGuildSupporterPromoCache(guildId);
                    message =
                        $"*<:1_to_5_up:912085138232442920> Set your own `{prfx}fm` emote reactions to be used everywhere with .fmbot supporter*";
                    showUpgradeButton = true;
                    break;
                }
                case 12:
                {
                    SetGuildSupporterPromoCache(guildId);
                    message =
                        $"*{DiscordConstants.Discoveries} View which artists you recently returned to with .fmbot supporter*";
                    showUpgradeButton = true;
                    break;
                }
                case 20:
                {
                    message =
                        $"*🎮 Play the new `.jumble` game and guess the artist together with your friends*";
                    break;
                }
                case 21:
                {
                    message =
                        $"*🎮 Play the new `.pixel` game and guess the album together with your friends*";
                    break;
                }
                case 22:
                {
                    message =
                        $"*🤖 Use .fmbot slash commands everywhere by [adding it to your Discord account](https://discord.com/oauth2/authorize?client_id=356268235697553409&scope=applications.commands&integration_type=1)*";
                    break;
                }
                case 23:
                {
                    message =
                        $"*🗒️ Check out the new `.recap` command that shows all commands in one place. Supports timeframes like `monthly`*";
                    break;
                }
                case 24:
                {
                    message =
                        $"*🧮 Set your preferred number formatting with the `/localization` slash command*";
                    break;
                }
                case 25:
                {
                    message =
                        $"*🕒 Set your timezone with the `/localization` slash command*";
                    break;
                }
            }
        }
        else
        {
            switch (randomHintNumber)
            {
                case 3:
                {
                    message =
                        $"*<:vinyl:1043644602969763861> Use Discogs for your vinyl collection? Link your account with `{prfx}discogs`.*";
                    break;
                }
                case 4:
                {
                    if (user.FmFooterOptions == FmFooterOption.TotalScrobbles)
                    {
                        message =
                            $"*⭐ Customize your `{prfx}fm` with the custom footer options. Get started by using `/fmmode`.*";
                    }

                    break;
                }
                case 5:
                {
                    if (user.DataSource == DataSource.LastFm)
                    {
                        message =
                            $"*<:spotify:882221219334725662> Import your full Spotify history with `/import spotify`*";
                    }

                    break;
                }
                case 6:
                {
                    if (user.DataSource == DataSource.LastFm)
                    {
                        message =
                            $"*<:apple_music:1218182727149420544> Import your full Apple Music history with `/import applemusic`*";
                    }

                    break;
                }
                case 7:
                {
                    if (user.EmoteReactions == null || !user.EmoteReactions.Any())
                    {
                        message =
                            $"*⭐ Set your own emote reactions that will be used globally with `{prfx}userreactions`*";
                    }

                    break;
                }
                case 8:
                {
                    message =
                        $"*{DiscordConstants.Discoveries} View when you discovered an artist, album and track with `{prfx}discoverydate` / `{prfx}dd`*";

                    break;
                }
            }
        }

        switch (randomHintNumber)
        {
            case 20:
            {
                message =
                    $"*🌞 Tip: look up what's `.featured` in other commands. For example, `.wk featured`*";
                break;
            }
            case 21:
            {
                message =
                    $"*🌞 Tip: Reply to a command to use that artist, album or track as context for your next command*";
                break;
            }
            case 22:
            {
                message =
                    $"*🌞 Tip: Album commands support the `random` parameter. For example, `.cover random`*";
                break;
            }
            case 23:
            {
                message =
                    $"*🌞 Tip: Milestone commands support the `random` parameter. For example, `.milestone random`*";
                break;
            }
            case 24:
            {
                message =
                    $"*🌞 Tip: All commands that support time periods also support timeframes like `december` or `2025`*";
                break;
            }
            // case 25:
            // {
            //     message =
            //         $"*🌞 Tip: Delete an unwanted bot response yourself by opening the message options > Apps > 'Delete response'*";
            //     break;
            // }
        }

        return (message, showUpgradeButton);
    }

    private static string GetGuildPromoCacheKey(ulong? guildId = null)
    {
        return $"guild-supporter-promo-{guildId}";
    }

    public void SetGuildSupporterPromoCache(ulong? guildId = null)
    {
        if (guildId != null)
        {
            this._cache.Set(GetGuildPromoCacheKey(guildId), 1, TimeSpan.FromMinutes(5));
        }
    }

    public async Task<Supporter> AddSupporter(ulong id, string name, string notes)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == id);

        user.UserType = UserType.Supporter;
        db.Update(user);

        var supporterToAdd = new Supporter
        {
            Name = name,
            Created = DateTime.UtcNow,
            Notes = notes,
            SupporterMessagesEnabled = true,
            VisibleInOverview = true,
            SupporterType = SupporterType.User
        };

        await db.Supporters.AddAsync(supporterToAdd);

        await db.SaveChangesAsync();

        return supporterToAdd;
    }

    public async Task<Supporter> AddOpenCollectiveSupporter(ulong id, OpenCollectiveUser openCollectiveUser)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == id);

        user.UserType = UserType.Supporter;
        db.Update(user);

        var supporterToAdd = new Supporter
        {
            Name = openCollectiveUser.Name,
            Created = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
            Modified = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
            Notes = "Added through OpenCollective integration",
            SupporterMessagesEnabled = true,
            VisibleInOverview = true,
            SupporterType = SupporterType.User,
            DiscordUserId = id,
            LastPayment = openCollectiveUser.LastPayment,
            SubscriptionType = openCollectiveUser.SubscriptionType,
            OpenCollectiveId = openCollectiveUser.Id
        };

        await db.Supporters.AddAsync(supporterToAdd);

        await db.SaveChangesAsync();

        return supporterToAdd;
    }

    public async Task<Supporter> GetSupporter(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.Supporters
            .OrderByDescending(o => o.Created)
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);
    }

    public async Task<StripeSupporter> GetStripeSupporter(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.StripeSupporters
            .FirstOrDefaultAsync(f => f.PurchaserDiscordUserId == discordUserId);
    }

    public async Task<StripePricing> GetPricing(string userLocale, string existingUserCurrency)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var prices = await db.StripePricing
            .Where(w => w.Type == StripeSupporterType.Supporter)
            .ToListAsync();

        if (existingUserCurrency != null)
        {
            return prices.First(f => f.Currency.Equals(existingUserCurrency, StringComparison.OrdinalIgnoreCase));
        }

        return prices.FirstOrDefault(f => userLocale != null && f.Locales.Any(a => a == userLocale)) ??
               prices.First(p => p.Default);
    }

    public async Task<OpenCollectiveUser> GetOpenCollectiveSupporter(string openCollectiveId)
    {
        var openCollectiveSupporters = await this._openCollectiveService.GetOpenCollectiveOverview();

        return openCollectiveSupporters.Users.FirstOrDefault(f => f.Id == openCollectiveId);
    }

    public async Task<OpenCollectiveOverview> GetOpenCollectiveSupporters()
    {
        return await this._openCollectiveService.GetOpenCollectiveOverview();
    }

    public async Task<Supporter> OpenCollectiveSupporterExpired(Supporter supporter)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        Log.Information("Removing supporter status for {supporterName} - {openCollectiveId}", supporter.Name,
            supporter.Name);
        if (supporter.DiscordUserId.HasValue)
        {
            var user = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordUserId == supporter.DiscordUserId);

            if (user != null)
            {
                user.UserType = UserType.User;

                if (user.DataSource != DataSource.LastFm)
                {
                    user.DataSource = DataSource.LastFm;
                    _ = this._indexService.RecalculateTopLists(user);
                }

                db.Update(user);

                Log.Information("Removed supporter status from Discord account {discordUserId} - {lastFmUsername}",
                    user.DiscordUserId, user.UserNameLastFM);
            }
        }

        supporter.Expired = true;
        supporter.SupporterMessagesEnabled = false;
        supporter.VisibleInOverview = false;

        db.Update(supporter);

        await db.SaveChangesAsync();

        if (this._botSettings.Bot.SupporterAuditLogWebhookUrl != null)
        {
            var supporterAuditLogChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);

            var embed = new EmbedBuilder();

            embed.WithTitle("Supporter expiry processed");
            embed.WithDescription($"Name: `{supporter.Name}`\n" +
                                  $"OpenCollective ID: `{supporter.OpenCollectiveId}`\n" +
                                  $"Subscription type: `{supporter.SubscriptionType}`");

            await supporterAuditLogChannel.SendMessageAsync(null, false, new[] { embed.Build() });
            supporterAuditLogChannel.Dispose();
        }

        return supporter;
    }

    public async Task CheckForNewOcSupporters()
    {
        var openCollectiveSupporters = await this._openCollectiveService.GetOpenCollectiveOverview();

        if (openCollectiveSupporters == null)
        {
            Log.Error("Error while checking newsupporters - response null");
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingSupporters = await db.Supporters
            .Where(w => w.OpenCollectiveId != null)
            .ToListAsync();

        var supporterUpdateChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterUpdatesWebhookUrl);
        var supporterAuditLogChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);

        foreach (var newSupporter in openCollectiveSupporters.Users.Where(w =>
                     w.LastPayment >= DateTime.UtcNow.AddHours(-6)))
        {
            var cacheKey = $"new-supporter-{newSupporter.Id}";
            if (this._cache.TryGetValue(cacheKey, out _))
            {
                continue;
            }

            var embed = new EmbedBuilder();

            var existingSupporter = existingSupporters.FirstOrDefault(f => f.OpenCollectiveId == newSupporter.Id);
            if (existingSupporter is { Expired: true })
            {
                embed.WithTitle("Monthly supporter has re-activated their subscription");
                embed.WithDescription($"Name: `{newSupporter.Name}`\n" +
                                      $"OpenCollective ID: `{newSupporter.Id}`\n" +
                                      $"Subscription type: `{newSupporter.SubscriptionType}`\n\n" +
                                      $"No action should be required. Check <#821661156652875856> if it's reactivated in there.");

                await supporterAuditLogChannel.SendMessageAsync(null, false, new[] { embed.Build() });
                await supporterUpdateChannel.SendMessageAsync(null, false, new[] { embed.Build() });

                this._cache.Set(cacheKey, 1, TimeSpan.FromDays(1));

                return;
            }

            if (existingSupporter != null)
            {
                return;
            }

            if (newSupporter.Transactions.Count > 3)
            {
                return;
            }

            embed.WithTitle("New supporter on OpenCollective!");
            embed.WithDescription($"Name: `{newSupporter.Name}`\n" +
                                  $"OpenCollective ID: `{newSupporter.Id}`\n" +
                                  $"Subscription type: `{newSupporter.SubscriptionType}`\n\n");

            await supporterAuditLogChannel.SendMessageAsync($"`.addsupporter \"discordUserId\" \"{newSupporter.Id}\"`",
                false, new[] { embed.Build() });
            await supporterUpdateChannel.SendMessageAsync($"`.addsupporter \"discordUserId\" \"{newSupporter.Id}\"`",
                false, new[] { embed.Build() });

            this._cache.Set(cacheKey, 1, TimeSpan.FromDays(1));
        }

        supporterAuditLogChannel.Dispose();
        supporterUpdateChannel.Dispose();
    }

    public async Task UpdateExistingOpenCollectiveSupporters()
    {
        var openCollectiveSupporters = await this._openCollectiveService.GetOpenCollectiveOverview();

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingSupporters = await db.Supporters
            .Where(w => w.OpenCollectiveId != null)
            .ToListAsync();

        var supporterUpdateChannel =
            new DiscordWebhookClient(this._botSettings.Bot.SupporterUpdatesWebhookUrl);
        var supporterAuditLogChannel =
            new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);

        foreach (var existingSupporter in existingSupporters)
        {
            var openCollectiveSupporter =
                openCollectiveSupporters.Users.FirstOrDefault(f => f.Id == existingSupporter.OpenCollectiveId);

            if (openCollectiveSupporter != null)
            {
                var currentSubscriptionType = existingSupporter.SubscriptionType.GetValueOrDefault();
                var newSubscriptionType = openCollectiveSupporter.SubscriptionType;

                if (existingSupporter.LastPayment != openCollectiveSupporter.LastPayment ||
                    existingSupporter.Name != openCollectiveSupporter.Name)
                {
                    Log.Information(
                        "Updating last payment date for supporter {supporterName} from {currentDate} to {newDate}",
                        existingSupporter.Name, existingSupporter.LastPayment, openCollectiveSupporter.LastPayment);

                    existingSupporter.LastPayment = openCollectiveSupporter.LastPayment;
                    existingSupporter.Modified = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

                    Log.Information("Updating name for supporter {supporterName} to {newName}", existingSupporter.Name,
                        openCollectiveSupporter.Name);
                    existingSupporter.Name = openCollectiveSupporter.Name;

                    if (existingSupporter.Expired == true &&
                        openCollectiveSupporter.LastPayment >= DateTime.UtcNow.AddHours(-3))
                    {
                        Log.Information("Re-activating supporter status for {supporterName} - {openCollectiveId}",
                            existingSupporter.Name, existingSupporter.Name);
                        var reActivateDescription = new StringBuilder();

                        if (existingSupporter.DiscordUserId.HasValue)
                        {
                            await ModifyGuildRole(existingSupporter.DiscordUserId.Value);
                            await RunFullUpdate(existingSupporter.DiscordUserId.Value);
                            await ReActivateSupporterUser(existingSupporter);
                            reActivateDescription.AppendLine($"DiscordUserId: `{existingSupporter.DiscordUserId}`");
                        }
                        else
                        {
                            reActivateDescription.AppendLine($"DiscordUserId: ⚠️ No Discord user attached");
                        }

                        existingSupporter.Expired = false;
                        existingSupporter.SupporterMessagesEnabled = true;
                        existingSupporter.VisibleInOverview = true;

                        reActivateDescription.AppendLine($"Name: `{existingSupporter.Name}`");
                        reActivateDescription.AppendLine($"LastPayment: `{existingSupporter.LastPayment}`");
                        if (currentSubscriptionType != newSubscriptionType)
                        {
                            reActivateDescription.AppendLine(
                                $"Subscription type: `{Enum.GetName(newSubscriptionType)}` **(updated from `{Enum.GetName(currentSubscriptionType)}`)**");
                        }
                        else
                        {
                            reActivateDescription.AppendLine(
                                $"Subscription type: `{Enum.GetName(newSubscriptionType)}`");
                        }

                        reActivateDescription.AppendLine($"Notes: `{existingSupporter.Notes}`");

                        var reactivateEmbed = new EmbedBuilder();
                        reactivateEmbed.WithTitle("Re-activated supporter");
                        reactivateEmbed.WithDescription(reActivateDescription.ToString());

                        await supporterAuditLogChannel.SendMessageAsync(null, false, new[] { reactivateEmbed.Build() });
                    }

                    var updatedDescription = new StringBuilder();
                    updatedDescription.AppendLine($"Name: `{existingSupporter.Name}`");
                    updatedDescription.AppendLine($"LastPayment: `{existingSupporter.LastPayment}`");
                    if (currentSubscriptionType != newSubscriptionType)
                    {
                        updatedDescription.AppendLine(
                            $"Subscription type: `{Enum.GetName(newSubscriptionType)}` **(updated from `{Enum.GetName(currentSubscriptionType)}`)**");
                        existingSupporter.SubscriptionType = newSubscriptionType;
                    }
                    else
                    {
                        updatedDescription.AppendLine($"Subscription type: `{Enum.GetName(newSubscriptionType)}`");
                    }

                    updatedDescription.AppendLine($"Notes: `{existingSupporter.Notes}`");

                    db.Update(existingSupporter);
                    await db.SaveChangesAsync();

                    var embed = new EmbedBuilder();
                    embed.WithTitle("Updated supporter");
                    embed.WithDescription(updatedDescription.ToString());

                    await supporterAuditLogChannel.SendMessageAsync(null, false, new[] { embed.Build() });
                }
            }

            if (existingSupporter.SubscriptionType == SubscriptionType.MonthlyOpenCollective)
            {
                if (existingSupporter.Expired != true && existingSupporter.LastPayment > DateTime.UtcNow.AddDays(-63) &&
                    existingSupporter.LastPayment < DateTime.UtcNow.AddDays(-60))
                {
                    Log.Information("Monthly supporter expiration detected for {supporterName} - {discordUserId}",
                        existingSupporter.Name, existingSupporter.DiscordUserId);

                    var cacheKey = $"supporter-monthly-expired-{existingSupporter.OpenCollectiveId}";
                    if (this._cache.TryGetValue(cacheKey, out _))
                    {
                        continue;
                    }

                    var embed = new EmbedBuilder();

                    embed.WithTitle("Monthly supporter expired");
                    embed.WithDescription(OpenCollectiveSupporterToEmbedDescription(existingSupporter));

                    await supporterAuditLogChannel.SendMessageAsync(
                        $"`.removesupporter {existingSupporter.DiscordUserId}`", false, new[] { embed.Build() });
                    await supporterUpdateChannel.SendMessageAsync(
                        $"`.removesupporter {existingSupporter.DiscordUserId}`", false, new[] { embed.Build() });

                    this._cache.Set(cacheKey, 1, TimeSpan.FromDays(2));
                }
            }

            if (existingSupporter.SubscriptionType == SubscriptionType.YearlyOpenCollective)
            {
                if (existingSupporter.Expired != true &&
                    existingSupporter.LastPayment > DateTime.UtcNow.AddDays(-388) &&
                    existingSupporter.LastPayment < DateTime.UtcNow.AddDays(-385))
                {
                    Log.Information("Yearly supporter expiration detected for {supporterName} - {discordUserId}",
                        existingSupporter.Name, existingSupporter.DiscordUserId);

                    var cacheKey = $"supporter-yearly-expired-{existingSupporter.OpenCollectiveId}";
                    if (this._cache.TryGetValue(cacheKey, out _))
                    {
                        continue;
                    }

                    var embed = new EmbedBuilder();

                    embed.WithTitle("Yearly supporter expired");
                    embed.WithDescription(OpenCollectiveSupporterToEmbedDescription(existingSupporter));

                    await supporterAuditLogChannel.SendMessageAsync(
                        $"`.removesupporter {existingSupporter.DiscordUserId}`", false, new[] { embed.Build() });
                    await supporterUpdateChannel.SendMessageAsync(null, false, new[] { embed.Build() });

                    this._cache.Set(cacheKey, 1, TimeSpan.FromDays(2));
                }
            }
        }

        supporterAuditLogChannel.Dispose();
        supporterUpdateChannel.Dispose();
    }

    private static string OpenCollectiveSupporterToEmbedDescription(Supporter supporter)
    {
        return $"Name: `{supporter.Name}`\n" +
               $"OC ID: `{supporter.OpenCollectiveId}`\n" +
               $"Discord ID: `{supporter.DiscordUserId}`\n" +
               $"Type: `{supporter.SubscriptionType}`\n" +
               $"Notes: `{supporter.Notes}`";
    }

    public async Task UpdateSingleDiscordSupporter(ulong discordUserId)
    {
        var discordSupporters = await this._discordSkuService.GetGroupedEntitlements(discordUserId);

        await UpdateDiscordSupporters(discordSupporters);
    }

    public async Task AddLatestDiscordSupporters()
    {
        var discordSupporters =
            await this._discordSkuService.GetGroupedEntitlements(
                after: SnowflakeUtils.ToSnowflake(DateTime.UtcNow.AddDays(-1)));

        await UpdateDiscordSupporters(discordSupporters);
    }

    public async Task UpdateDiscordSupporters(List<DiscordEntitlement> groupedEntitlements)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingSupporters = await db.Supporters
            .Where(w =>
                w.DiscordUserId != null &&
                w.SubscriptionType == SubscriptionType.Discord || w.SubscriptionType == SubscriptionType.Stripe)
            .ToListAsync();

        var supporterAuditLogChannel =
            new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);

        foreach (var userEntitlements in groupedEntitlements)
        {
            var type = userEntitlements.StartsAt == null ? SubscriptionType.Stripe : SubscriptionType.Discord;
            var existingSupporter =
                existingSupporters.FirstOrDefault(f => f.DiscordUserId == userEntitlements.DiscordUserId &&
                                                       f.SubscriptionType == type);

            if (existingSupporter == null && userEntitlements.Active)
            {
                Log.Information("Adding Discord supporter {discordUserId}", userEntitlements.DiscordUserId);

                var newSupporter = await AddDiscordSupporter(userEntitlements.DiscordUserId, userEntitlements);
                await ModifyGuildRole(userEntitlements.DiscordUserId);
                await RunFullUpdate(userEntitlements.DiscordUserId);

                var user = await this._client.Rest.GetUserAsync(userEntitlements.DiscordUserId);
                if (user != null)
                {
                    try
                    {
                        await SendSupporterWelcomeMessage(user, false, newSupporter);
                    }
                    catch (Exception e)
                    {
                        Log.Error("Could not send welcome dm to new Discord supporter {discordUserId}",
                            userEntitlements.DiscordUserId, e);
                    }
                }

                var subType = Enum.GetName(newSupporter.SubscriptionType.Value);
                var embed = new EmbedBuilder().WithDescription(
                    $"Added {subType} supporter {userEntitlements.DiscordUserId} - <@{userEntitlements.DiscordUserId}>");
                await supporterAuditLogChannel.SendMessageAsync(embeds: new[] { embed.Build() });

                Log.Information("Added supporter {discordUserId} - {subscriptionType}", userEntitlements.DiscordUserId,
                    newSupporter.SubscriptionType);

                continue;
            }

            if (existingSupporter != null)
            {
                if (existingSupporter.LastPayment != userEntitlements.EndsAt && existingSupporter.Expired != true)
                {
                    Log.Information("Updating Discord supporter {discordUserId}", userEntitlements.DiscordUserId);
                    var oldDate = existingSupporter.LastPayment;

                    existingSupporter.LastPayment = userEntitlements.EndsAt;
                    existingSupporter.Modified = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

                    db.Update(existingSupporter);
                    await db.SaveChangesAsync();

                    var endDate = userEntitlements.EndsAt.HasValue
                        ? $"<t:{((DateTimeOffset?)userEntitlements.EndsAt)?.ToUnixTimeSeconds()}:f>"
                        : "no end date";

                    var subType = Enum.GetName(existingSupporter.SubscriptionType.Value);
                    var embed = new EmbedBuilder().WithDescription(
                        $"Updated {subType} supporter {userEntitlements.DiscordUserId} - <@{userEntitlements.DiscordUserId}>\n" +
                        $"*End date from <t:{((DateTimeOffset?)oldDate)?.ToUnixTimeSeconds()}:f> to {endDate}*");
                    await supporterAuditLogChannel.SendMessageAsync(embeds: new[] { embed.Build() });
                }

                if (existingSupporter.Expired == true && userEntitlements.Active)
                {
                    Log.Information("Re-activating Discord supporter {discordUserId}", userEntitlements.DiscordUserId);

                    var reActivatedSupporter = await ReActivateSupporter(existingSupporter, userEntitlements);
                    await ModifyGuildRole(userEntitlements.DiscordUserId);
                    await RunFullUpdate(userEntitlements.DiscordUserId);

                    var user = await this._client.Rest.GetUserAsync(userEntitlements.DiscordUserId);
                    if (user != null)
                    {
                        await SendSupporterWelcomeMessage(user, false, reActivatedSupporter, true);
                    }

                    var subType = Enum.GetName(existingSupporter.SubscriptionType.Value);
                    var embed = new EmbedBuilder().WithDescription(
                        $"Re-activated {subType} supporter {userEntitlements.DiscordUserId} - <@{userEntitlements.DiscordUserId}>");
                    await supporterAuditLogChannel.SendMessageAsync(embeds: new[] { embed.Build() });

                    Log.Information("Re-activated Discord supporter {discordUserId}", userEntitlements.DiscordUserId);

                    continue;
                }

                if (existingSupporter.Expired != true && !userEntitlements.Active)
                {
                    if (await DiscordSubbedElsewhereExpiryFlow(existingSupporter, userEntitlements,
                            supporterAuditLogChannel))
                    {
                        continue;
                    }

                    Log.Information("Removing Discord supporter {discordUserId}", userEntitlements.DiscordUserId);

                    var fmbotUser = await
                        db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == userEntitlements.DiscordUserId);

                    var hadImported = fmbotUser != null && fmbotUser.DataSource != DataSource.LastFm;

                    await ExpireSupporter(userEntitlements.DiscordUserId, existingSupporter);
                    await ModifyGuildRole(userEntitlements.DiscordUserId, false);
                    await RunFullUpdate(userEntitlements.DiscordUserId);

                    var user = await this._client.Rest.GetUserAsync(userEntitlements.DiscordUserId);
                    if (user != null)
                    {
                        await SendSupporterGoodbyeMessage(user, false, hadImported);
                    }

                    var subType = Enum.GetName(existingSupporter.SubscriptionType.Value);
                    var embed = new EmbedBuilder().WithDescription(
                        $"Removed {subType} supporter {userEntitlements.DiscordUserId} - <@{userEntitlements.DiscordUserId}>");
                    await supporterAuditLogChannel.SendMessageAsync(embeds: new[] { embed.Build() });

                    Log.Information("Removed Discord supporter {discordUserId}", userEntitlements.DiscordUserId);
                }
            }
        }

        supporterAuditLogChannel.Dispose();
    }

    public async Task CheckExpiredDiscordSupporters()
    {
        var expiredDate = DateTime.UtcNow.AddDays(-3);
        var modifiedDate = DateTime.UtcNow.AddDays(-20);

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var possiblyExpiredSupporters = await db.Supporters
            .Where(w =>
                w.DiscordUserId != null &&
                (w.SubscriptionType == SubscriptionType.Discord || w.SubscriptionType == SubscriptionType.Stripe) &&
                w.Expired != true &&
                (w.LastPayment.HasValue &&
                 w.LastPayment.Value < expiredDate ||
                 w.Modified.HasValue &&
                 w.Modified < modifiedDate ||
                 w.Modified == null))
            .ToListAsync();

        Log.Information("Checking expired supporters - {count} possibly expired", possiblyExpiredSupporters.Count);

        var supporterAuditLogChannel =
            new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);

        foreach (var existingSupporter in possiblyExpiredSupporters)
        {
            var userEntitlements =
                await this._discordSkuService.GetGroupedEntitlements(existingSupporter.DiscordUserId.Value);

            var discordSupporter = userEntitlements.FirstOrDefault();

            if (discordSupporter == null)
            {
                Log.Information("Expired Discord supporter is not found in Discord API - {discordUserId}",
                    existingSupporter.DiscordUserId);
                continue;
            }

            if (existingSupporter.LastPayment != discordSupporter.EndsAt)
            {
                Log.Information("Updating Discord supporter {discordUserId}", discordSupporter.DiscordUserId);

                var oldDate = existingSupporter.LastPayment;

                existingSupporter.LastPayment = discordSupporter.EndsAt;
                existingSupporter.Modified = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                db.Update(existingSupporter);
                await db.SaveChangesAsync();

                var endDate = discordSupporter.EndsAt.HasValue
                    ? $"<t:{((DateTimeOffset?)discordSupporter.EndsAt)?.ToUnixTimeSeconds()}:f>"
                    : "no end date";

                var embed = new EmbedBuilder().WithDescription(
                    $"Updated Discord supporter {discordSupporter.DiscordUserId} - <@{discordSupporter.DiscordUserId}>\n" +
                    $"*End date from <t:{((DateTimeOffset?)oldDate)?.ToUnixTimeSeconds()}:f> to {endDate}*");

                await supporterAuditLogChannel.SendMessageAsync(embeds: new[] { embed.Build() });

                continue;
            }

            if (existingSupporter.Expired != true && !discordSupporter.Active)
            {
                if (await DiscordSubbedElsewhereExpiryFlow(existingSupporter, discordSupporter,
                        supporterAuditLogChannel))
                {
                    continue;
                }

                Log.Information("Removing Discord supporter {discordUserId}", discordSupporter.DiscordUserId);

                var fmbotUser = await
                    db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordSupporter.DiscordUserId);

                var hadImported = fmbotUser != null && fmbotUser.DataSource != DataSource.LastFm;

                await ExpireSupporter(discordSupporter.DiscordUserId, existingSupporter);
                await ModifyGuildRole(discordSupporter.DiscordUserId, false);
                await RunFullUpdate(discordSupporter.DiscordUserId);

                var user = await this._client.Rest.GetUserAsync(discordSupporter.DiscordUserId);
                if (user != null)
                {
                    await SendSupporterGoodbyeMessage(user, false, hadImported);
                }

                var embed = new EmbedBuilder().WithDescription(
                    $"Removed Discord supporter {discordSupporter.DiscordUserId} - <@{discordSupporter.DiscordUserId}>");
                await supporterAuditLogChannel.SendMessageAsync(embeds: new[] { embed.Build() });

                Log.Information("Removed Discord supporter {discordUserId}", discordSupporter.DiscordUserId);

                continue;
            }

            if (discordSupporter.Active)
            {
                existingSupporter.Modified = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                db.Update(existingSupporter);
                await db.SaveChangesAsync();
            }

            await Task.Delay(500);
        }

        supporterAuditLogChannel.Dispose();
    }

    private async Task<bool> DiscordSubbedElsewhereExpiryFlow(Supporter existingSupporter,
        DiscordEntitlement discordSupporter,
        DiscordWebhookClient supporterAuditLogChannel)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var otherSupporterSubscription = await db.Supporters
            .Where(w =>
                w.DiscordUserId != null &&
                w.SupporterId != existingSupporter.SupporterId &&
                w.Expired != true)
            .FirstOrDefaultAsync(f => f.DiscordUserId == existingSupporter.DiscordUserId.Value);

        if (otherSupporterSubscription != null)
        {
            if (otherSupporterSubscription.SubscriptionType == SubscriptionType.LifetimeOpenCollective ||
                otherSupporterSubscription.SubscriptionType == SubscriptionType.YearlyOpenCollective ||
                otherSupporterSubscription.SubscriptionType == SubscriptionType.MonthlyOpenCollective)
            {
                Log.Information("Not removing Discord supporter because active OpenCollective sub - {discordUserId}",
                    discordSupporter.DiscordUserId);

                var notCancellingEmbed = new EmbedBuilder().WithDescription(
                    $"Prevented removal of Discord supporter who also has active OpenCollective sub\n" +
                    $"{discordSupporter.DiscordUserId} - <@{discordSupporter.DiscordUserId}>");
                await supporterAuditLogChannel.SendMessageAsync(embeds: new[] { notCancellingEmbed.Build() });
            }
            else if (otherSupporterSubscription.SubscriptionType == SubscriptionType.Stripe &&
                     existingSupporter.SubscriptionType == SubscriptionType.Discord)
            {
                Log.Information("Not removing Discord supporter because active Stripe sub - {discordUserId}",
                    discordSupporter.DiscordUserId);

                var notCancellingEmbed = new EmbedBuilder().WithDescription(
                    $"Prevented removal of Discord supporter who also has active Stripe sub\n" +
                    $"{discordSupporter.DiscordUserId} - <@{discordSupporter.DiscordUserId}>");
                await supporterAuditLogChannel.SendMessageAsync(embeds: new[] { notCancellingEmbed.Build() });
            }
            else if (otherSupporterSubscription.SubscriptionType == SubscriptionType.Discord &&
                     existingSupporter.SubscriptionType == SubscriptionType.Stripe)
            {
                Log.Information("Not removing Discord supporter because active Stripe sub - {discordUserId}",
                    discordSupporter.DiscordUserId);

                var notCancellingEmbed = new EmbedBuilder().WithDescription(
                    $"Prevented removal of Discord supporter who also has active Stripe sub\n" +
                    $"{discordSupporter.DiscordUserId} - <@{discordSupporter.DiscordUserId}>");
                await supporterAuditLogChannel.SendMessageAsync(embeds: new[] { notCancellingEmbed.Build() });
            }
            else
            {
                Log.Information("Not removing Discord supporter because active other {otherType} sub - {discordUserId}",
                    otherSupporterSubscription.SubscriptionType, discordSupporter.DiscordUserId);

                var notCancellingEmbed = new EmbedBuilder().WithDescription(
                    $"Prevented removal of Discord supporter who also has active {otherSupporterSubscription.SubscriptionType} sub\n" +
                    $"{discordSupporter.DiscordUserId} - <@{discordSupporter.DiscordUserId}>");
                await supporterAuditLogChannel.SendMessageAsync(embeds: new[] { notCancellingEmbed.Build() });
            }

            await ExpireSupporter(discordSupporter.DiscordUserId, existingSupporter, false);

            return true;
        }

        return false;
    }

    public async Task CheckIfDiscordSupportersHaveCorrectUserType()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var activeSupporters = await db.Supporters
            .AsQueryable()
            .Where(w =>
                w.DiscordUserId != null &&
                (w.SubscriptionType == SubscriptionType.Discord || w.SubscriptionType == SubscriptionType.Stripe) &&
                w.Expired != true)
            .ToListAsync();

        var ids = activeSupporters.Select(s => s.DiscordUserId.Value).ToHashSet();
        var usersThatShouldHaveSupporter = await db.Users
            .Where(w => ids.Contains(w.DiscordUserId) && w.UserType == UserType.User)
            .ToListAsync();

        Log.Information(
            "Found {supporterCount} Discord supporters that should have supporter, but don't have the usertype",
            usersThatShouldHaveSupporter.Count);

        var supporterAuditLogChannel =
            new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);

        foreach (var dbUser in usersThatShouldHaveSupporter)
        {
            var discordSupporter = activeSupporters.First(f => f.DiscordUserId == dbUser.DiscordUserId);

            Log.Information("Re-activating supporter (user was missing type) {discordUserId}",
                discordSupporter.DiscordUserId);

            await ReActivateSupporterUser(discordSupporter);
            await ModifyGuildRole(discordSupporter.DiscordUserId.Value);
            await RunFullUpdate(discordSupporter.DiscordUserId.Value);

            var embed = new EmbedBuilder().WithDescription(
                $"Re-activated supporter {discordSupporter.DiscordUserId} - <@{discordSupporter.DiscordUserId}>\n" +
                $"*User had an active subscription, but their .fmbot account didn't have supporter*");
            await supporterAuditLogChannel.SendMessageAsync(embeds: new[] { embed.Build() });

            Log.Information("Re-activated supporter (user was missing type) {discordUserId}",
                discordSupporter.DiscordUserId);
        }

        supporterAuditLogChannel.Dispose();
    }

    public async Task CheckExpiredStripeSupporters()
    {
        var expiredDate = DateTime.UtcNow.AddDays(-10);

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var possiblyExpiredSupporters = await db.StripeSupporters
            .Where(w =>
                w.DateEnding.HasValue &&
                w.DateEnding < expiredDate &&
                !w.EntitlementDeleted)
            .ToListAsync();

        Log.Information("Checking expired Stripe supporters - {count} expired",
            possiblyExpiredSupporters.Count);

        var supporterAuditLogChannel =
            new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);

        foreach (var existingStripeSupporter in possiblyExpiredSupporters)
        {
            var discordUserId = existingStripeSupporter.PurchaserDiscordUserId;

            var existingSupporters = await db.Supporters
                .Where(w => w.DiscordUserId == discordUserId &&
                            w.Expired != true)
                .ToListAsync();

            var existingSupporter = existingSupporters
                .FirstOrDefault(f => f.SubscriptionType == SubscriptionType.Stripe);

            var otherSupporterSubscription = existingSupporters
                .FirstOrDefault(f => f.SubscriptionType != SubscriptionType.Stripe);

            if (otherSupporterSubscription == null && existingSupporter != null)
            {
                Log.Information("Removing Stripe supporter {discordUserId}", discordUserId);

                var fmbotUser = await
                    db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

                var hadImported = fmbotUser != null && fmbotUser.DataSource != DataSource.LastFm;

                await ExpireSupporter(discordUserId, existingSupporter);
                await ModifyGuildRole(discordUserId, false);
                await RunFullUpdate(discordUserId);

                var user = await this._client.Rest.GetUserAsync(discordUserId);
                if (user != null)
                {
                    await SendSupporterGoodbyeMessage(user, false, hadImported);
                }

                var embed = new EmbedBuilder().WithDescription(
                    $"Removed Stripe supporter {discordUserId} - <@{discordUserId}>");
                await supporterAuditLogChannel.SendMessageAsync(embeds: [embed.Build()]);

                Log.Information("Removed Stripe supporter {discordUserId}", discordUserId);
            }
            else if (otherSupporterSubscription != null)
            {
                Log.Information("Not removing Stripe supporter because active other {otherType} sub - {discordUserId}",
                    otherSupporterSubscription.SubscriptionType, discordUserId);

                var notCancellingEmbed = new EmbedBuilder().WithDescription(
                    $"Prevented removal of Stripe supporter who also has active {otherSupporterSubscription.SubscriptionType} sub (entitlement is still deleted though)\n" +
                    $"{discordUserId} - <@{discordUserId}>");
                await supporterAuditLogChannel.SendMessageAsync(embeds: [notCancellingEmbed.Build()]);
            }
            else
            {
                Log.Information("Not removing Stripe supporter because there is no main supporter? - {discordUserId}",
                    discordUserId);

                var notCancellingEmbed = new EmbedBuilder().WithDescription(
                    $"Prevented removal of Stripe supporter that has no main supporter in the database, this should never happen but I'm adding this code anyway\n" +
                    $"{discordUserId} - <@{discordUserId}>");
                await supporterAuditLogChannel.SendMessageAsync(embeds: [notCancellingEmbed.Build()]);
            }

            var entitlements =
                await this._discordSkuService.GetRawEntitlementsFromDiscord(discordUserId: discordUserId);
            var entitlementToRemove = entitlements.FirstOrDefault(w => !w.EndsAt.HasValue && w.Deleted != true);
            if (entitlementToRemove != null)
            {
                Log.Information("Removing entitlement {entitlementId} from {discordUserId}", entitlementToRemove.Id,
                    discordUserId);
                await this._discordSkuService.RemoveEntitlement(entitlementId: entitlementToRemove.Id);
            }

            existingStripeSupporter.EntitlementDeleted = true;
            db.Update(existingStripeSupporter);
            await db.SaveChangesAsync();

            await Task.Delay(500);
        }

        supporterAuditLogChannel.Dispose();
    }

    public async Task MigrateDiscordForSupporter(ulong oldDiscordUserId, ulong newDiscordUserId)
    {
        Log.Information("Migrating supporter from {oldDiscordUserId} to {newDiscordUserId}", oldDiscordUserId,
            newDiscordUserId);

        await using var db = await this._contextFactory.CreateDbContextAsync();

        var supporter = await db.Supporters.FirstAsync(f => f.DiscordUserId == oldDiscordUserId);
        supporter.Notes = $"Migrated from {oldDiscordUserId} to {newDiscordUserId}";
        supporter.DiscordUserId = newDiscordUserId;
        db.Update(supporter);

        await db.SaveChangesAsync();
        await Task.Delay(200);

        var oldUser = await db.Users.FirstAsync(f => f.DiscordUserId == oldDiscordUserId);
        if (oldUser.UserType == UserType.Supporter)
        {
            oldUser.UserType = UserType.User;
        }

        oldUser.DataSource = DataSource.LastFm;
        db.Update(oldUser);
        await ModifyGuildRole(oldDiscordUserId, false);

        var newUser = await db.Users.FirstAsync(f => f.DiscordUserId == newDiscordUserId);
        if (newUser.UserType == UserType.User)
        {
            newUser.UserType = UserType.Supporter;
        }

        db.Update(newUser);
        await ModifyGuildRole(newDiscordUserId);

        await db.SaveChangesAsync();
        await Task.Delay(200);

        if (supporter.SubscriptionType == SubscriptionType.Stripe)
        {
            var entitlements =
                await this._discordSkuService.GetRawEntitlementsFromDiscord(discordUserId: oldDiscordUserId);
            var entitlementToRemove = entitlements.FirstOrDefault(w => !w.EndsAt.HasValue && w.Deleted != true);
            if (entitlementToRemove != null)
            {
                Log.Information("Removing entitlement {entitlementId} from {oldDiscordUserId}", entitlementToRemove.Id,
                    oldDiscordUserId);
                await this._discordSkuService.RemoveEntitlement(entitlementId: entitlementToRemove.Id);
            }

            await this._discordSkuService.AddStripeEntitlement(newDiscordUserId);

            var stripeSupporter =
                await db.StripeSupporters.FirstOrDefaultAsync(f => f.PurchaserDiscordUserId == oldDiscordUserId);
            if (stripeSupporter != null)
            {
                stripeSupporter.PurchaserDiscordUserId = newDiscordUserId;

                if (!stripeSupporter.TimesTransferred.HasValue)
                {
                    stripeSupporter.TimesTransferred = 1;
                }
                else
                {
                    stripeSupporter.TimesTransferred++;
                }

                db.Update(stripeSupporter);

                await this._supporterLinkService.MigrateDiscordForStripeSupporterAsync(
                    new MigrateDiscordForStripeSupporterRequest
                    {
                        StripeCustomerId = stripeSupporter.StripeCustomerId,
                        StripeSubscriptionId = stripeSupporter.StripeSubscriptionId,
                        OldDiscordUserId = (long)oldDiscordUserId,
                        NewDiscordUserId = (long)newDiscordUserId,
                        OldLastFmUserName = oldUser.UserNameLastFM,
                        NewLastFmUserName = newUser.UserNameLastFM
                    });
            }
            else
            {
                Log.Warning("Stripe supporter doesnt have a stripe supporter in database - {discordUserId}",
                    oldDiscordUserId);
            }
        }

        await db.SaveChangesAsync();
        await Task.Delay(200);

        await RunFullUpdate(oldDiscordUserId);
        await RunFullUpdate(newDiscordUserId);

        var supporterAuditLogChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);
        var embed = new EmbedBuilder().WithDescription(
            $"Moved supporter:\n" +
            $"- Old: {oldDiscordUserId} - <@{oldDiscordUserId}>\n" +
            $"- New: {newDiscordUserId} - <@{newDiscordUserId}>");
        await supporterAuditLogChannel.SendMessageAsync(embeds: [embed.Build()]);
        supporterAuditLogChannel.Dispose();
    }

    public async Task AddRoleToNewSupporters()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var dateFilter = DateTime.UtcNow.AddHours(-1);
        var newSupporters = await db.Supporters
            .AsQueryable()
            .Where(w =>
                w.DiscordUserId != null &&
                (w.SubscriptionType == SubscriptionType.Discord || w.SubscriptionType == SubscriptionType.Stripe) &&
                w.Expired != true &&
                w.Created >= dateFilter)
            .ToListAsync();

        foreach (var newSupporter in newSupporters)
        {
            await ModifyGuildRole(newSupporter.DiscordUserId.Value);
        }
    }

    public async Task ModifyGuildRole(ulong discordUserId, bool add = true)
    {
        var baseGuild = await this._client.Rest.GetGuildAsync(this._botSettings.Bot.BaseServerId);

        if (baseGuild != null)
        {
            try
            {
                var guildUser = await baseGuild.GetUserAsync(discordUserId);
                if (guildUser != null)
                {
                    var supporterRole = baseGuild.Roles.FirstOrDefault(x => x.Name == "Supporter");

                    if (add && supporterRole != null)
                    {
                        if (guildUser.RoleIds.All(a => a != supporterRole.Id))
                        {
                            await guildUser.AddRoleAsync(supporterRole, new RequestOptions
                            {
                                AuditLogReason = "Automated supporter integration"
                            });

                            Log.Information("Modifying supporter role succeeded for {id} - added", discordUserId);

                            return;
                        }
                    }
                    else if (supporterRole != null)
                    {
                        await guildUser.RemoveRoleAsync(supporterRole, new RequestOptions
                        {
                            AuditLogReason = "Automated supporter integration"
                        });

                        Log.Information("Modifying supporter role succeeded for {id} - removed", discordUserId);

                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("Modifying supporter role failed for {id} - {exceptionMessage}", discordUserId, e.Message, e);
            }
        }
    }

    private async Task<Supporter> AddDiscordSupporter(ulong id, DiscordEntitlement entitlement)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == id);

        if (user == null)
        {
            Log.Warning("Someone who isn't registered in .fmbot just got a Discord subscription - ID {discordUserId}",
                id);
        }
        else
        {
            if (user.UserType == UserType.User)
            {
                user.UserType = UserType.Supporter;
            }

            db.Update(user);
        }

        var type = entitlement.StartsAt == null ? SubscriptionType.Stripe : SubscriptionType.Discord;

        var supporterToAdd = new Supporter
        {
            DiscordUserId = id,
            Name = user?.UserNameLastFM,
            Created = DateTime.SpecifyKind(entitlement.StartsAt ?? DateTime.UtcNow, DateTimeKind.Utc),
            Modified = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
            LastPayment = entitlement.EndsAt,
            Notes = "Added through Discord Entitlements",
            SupporterMessagesEnabled = true,
            VisibleInOverview = true,
            SupporterType = SupporterType.User,
            SubscriptionType = type
        };

        await db.Supporters.AddAsync(supporterToAdd);
        await db.SaveChangesAsync();

        return supporterToAdd;
    }

    private async Task<Supporter> ReActivateSupporter(Supporter existingSupporter, DiscordEntitlement entitlement)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == existingSupporter.DiscordUserId.Value);

        if (user == null)
        {
            Log.Warning(
                "Someone who isn't registered in .fmbot just re-activated their Discord subscription - ID {discordUserId}",
                existingSupporter.DiscordUserId);
        }
        else
        {
            if (user.UserType == UserType.User)
            {
                user.UserType = UserType.Supporter;
            }

            db.Update(user);
        }

        existingSupporter.Expired = null;
        existingSupporter.SupporterMessagesEnabled = true;
        existingSupporter.VisibleInOverview = true;
        existingSupporter.LastPayment = entitlement.EndsAt;
        existingSupporter.Modified = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

        db.Update(existingSupporter);
        await db.SaveChangesAsync();

        return existingSupporter;
    }

    private async Task<Supporter> ReActivateSupporterUser(Supporter supporter)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == supporter.DiscordUserId.Value);

        if (user == null)
        {
            Log.Warning(
                "Someone who isn't registered in .fmbot just re-activated their Discord subscription - ID {discordUserId}",
                supporter.DiscordUserId);
        }
        else
        {
            if (user.UserType == UserType.User)
            {
                user.UserType = UserType.Supporter;
            }

            db.Update(user);
        }

        await db.SaveChangesAsync();

        return supporter;
    }

    private async Task ExpireSupporter(ulong id, Supporter supporter, bool removeSupporterStatus = true)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == id);

        if (user == null)
        {
            Log.Warning(
                "Someone who isn't registered in .fmbot just cancelled their Discord subscription - ID {discordUserId}",
                id);
        }
        else if (removeSupporterStatus)
        {
            if (user.UserType == UserType.Supporter)
            {
                user.UserType = UserType.User;
            }

            if (user.DataSource != DataSource.LastFm)
            {
                user.DataSource = DataSource.LastFm;
                _ = Task.Run(() => this._indexService.RecalculateTopLists(user));
            }

            db.Update(user);
        }
        else
        {
            Log.Information(
                "Expiring Discord supporter without removing supporter status from account - ID {discordUserId}", id);
        }

        supporter.Expired = true;
        supporter.VisibleInOverview = false;
        supporter.SupporterMessagesEnabled = false;

        db.Update(supporter);

        await db.SaveChangesAsync();
    }

    private async Task RunFullUpdate(ulong id)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == id);

        _ = this._indexService.IndexUser(user);
    }

    public async Task<IReadOnlyList<Supporter>> GetAllVisibleSupporters()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.Supporters
            .AsQueryable()
            .Where(w => w.VisibleInOverview)
            .Where(w => w.Expired != true)
            .OrderByDescending(o => o.Created)
            .ToListAsync();
    }

    public async Task<int> GetActiveSupporterCountAsync()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Supporters
            .AsQueryable()
            .CountAsync(c => c.Expired != true);
    }

    public async Task<int> GetActiveDiscordSupporterCountAsync()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Supporters
            .AsQueryable()
            .CountAsync(c => c.Expired != true && c.SubscriptionType == SubscriptionType.Discord);
    }

    public async Task<int> GetActiveStripeSupporterCountAsync()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Supporters
            .AsQueryable()
            .CountAsync(c => c.Expired != true && c.SubscriptionType == SubscriptionType.Stripe);
    }

    public async Task<IReadOnlyList<Supporter>> GetAllSupporters()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.Supporters
            .AsQueryable()
            .OrderByDescending(o => o.Created)
            .ToListAsync();
    }

    public async Task<string> GetSupporterCheckoutLink(ulong discordUserId, string lastFmUserName, string type,
        StripePricing pricing, StripeSupporter existingStripeSupporter = null)
    {
        var existingStripeCustomerId = "";
        if (existingStripeSupporter != null)
        {
            existingStripeCustomerId = existingStripeSupporter.StripeCustomerId;
        }

        var priceId = pricing.MonthlyPriceId;
        if (type.Equals("yearly", StringComparison.OrdinalIgnoreCase))
        {
            priceId = pricing.YearlyPriceId;
        }

        var url = await this._supporterLinkService.GetCheckoutLinkAsync(new CreateLinkOptions
        {
            DiscordUserId = (long)discordUserId,
            LastFmUserName = lastFmUserName,
            Type = type,
            ExistingCustomerId = existingStripeCustomerId,
            PriceId = priceId
        });

        return url?.CheckoutLink;
    }

    public async Task<string> GetSupporterManageLink(StripeSupporter stripeSupporter)
    {
        var url = await this._supporterLinkService.GetManageLinkAsync(new GetManageLinkOptions
        {
            StripeCustomerId = stripeSupporter.StripeCustomerId
        });

        return url?.ManageLink;
    }
}
