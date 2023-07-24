using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Webhook;
using Discord.WebSocket;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
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
using User = FMBot.Persistence.Domain.Models.User;

namespace FMBot.Bot.Services;

public class SupporterService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly OpenCollectiveService _openCollectiveService;
    private readonly DiscordSkuService _discordSkuService;
    private readonly BotSettings _botSettings;
    private readonly IMemoryCache _cache;
    private readonly IIndexService _indexService;
    private readonly DiscordShardedClient _client;

    public SupporterService(IDbContextFactory<FMBotDbContext> contextFactory,
        OpenCollectiveService openCollectiveService,
        IOptions<BotSettings> botSettings,
        IMemoryCache cache,
        IIndexService indexService,
        DiscordSkuService discordSkuService, DiscordShardedClient client)
    {
        this._contextFactory = contextFactory;
        this._openCollectiveService = openCollectiveService;
        this._cache = cache;
        this._indexService = indexService;
        this._discordSkuService = discordSkuService;
        this._client = client;
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

            SetGuildPromoCache(guild.Id);

            return randomSupporter.Name;
        }

        return null;
    }

    public bool ShowPromotionalMessage(UserType userType, ulong? guildId)
    {
        if (userType != UserType.User)
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

    public static string GetSupporterLink()
    {
        var pick = RandomNumberGenerator.GetInt32(0, 2);

        return pick switch
        {
            1 => Constants.GetSupporterDiscordLink,
            _ => Constants.GetSupporterOverviewLink,
        };
    }

    public static async Task SendSupporterWelcomeMessage(IUser discordUser, bool hasDiscogs, Supporter supporter)
    {
        var thankYouEmbed = new EmbedBuilder();
        thankYouEmbed.WithColor(DiscordConstants.InformationColorBlue);

        var thankYouMessage = new StringBuilder();

        if (supporter.SubscriptionType != SubscriptionType.Discord)
        {
            thankYouMessage.AppendLine($"**Thank you for getting .fmbot {supporter.SubscriptionType.ToString().ToLower()} supporter!**");
        }
        else
        {
            thankYouMessage.AppendLine($"**Thank you for getting .fmbot supporter!**");
        }

        thankYouMessage.AppendLine(supporter.SubscriptionType == SubscriptionType.Lifetime
            ? "Thanks to your purchase we can continue to improve and host the bot, while you get some nice perks in return. Here's a brief reminder of the features available to supporters:"
            : "Thanks to your subscription we can continue to improve and host the bot, while you get some nice perks in return. Here's a brief reminder of the features available to supporters:");

        thankYouMessage.AppendLine();
        thankYouMessage.AppendLine("**Expanded statistics**\n" +
                                   "We've started a full update for you. " +
                                   "After a few minutes the following commands should be expanded:\n" +
                                   "- `artist`, `album` and `track` with first listen dates\n" +
                                   "- `stats` command with overall history\n" +
                                   "- `year` with artist discoveries and monthly overview");
        thankYouMessage.AppendLine();
        thankYouMessage.AppendLine("**Expanded commands**\n" +
                                   "- `fm` footer with up to 8 + 1 options (configured with `/fmmode`)\n" +
                                   "- Your own personal `fm` reactions with `userreactions`\n" +
                                   "- Use the `judge` command up to 25 times a day and on others\n" +
                                   $"- Friend limit raised to {Constants.MaxFriendsSupporter} (up from {Constants.MaxFriends})");

        thankYouMessage.AppendLine();
        thankYouMessage.AppendLine("**Get featured**\n" +
                                   "Every first Sunday of the month is Supporter Sunday, where you have a higher chance of getting featured. " +
                                   $"The next Supporter Sunday is in {FeaturedService.GetDaysUntilNextSupporterSunday()} {StringExtensions.GetDaysString(FeaturedService.GetDaysUntilNextSupporterSunday())}.");

        if (hasDiscogs)
        {
            thankYouMessage.AppendLine();
            thankYouMessage.AppendLine("**View your full Discogs collection**\n" +
                                       "If you use the `collection` command it will fetch your full collection from Discogs.\n" +
                                       $"This is also visible in other commands, like `artist`, `album`, `track` and `stats`.");
        }

        thankYouMessage.AppendLine();
        thankYouMessage.Append("**Your info**\n" +
                                   $"Your name in the `supporters` command will be shown as `{supporter.Name}`. This is also the name that will be shown when you sponsor charts. ");

        if (supporter.OpenCollectiveId != null)
        {
            thankYouMessage.Append("You can update this through your OpenCollective settings.");
        }

        thankYouEmbed.WithDescription(thankYouMessage.ToString());
        await discordUser.SendMessageAsync(embed: thankYouEmbed.Build());
    }

    public static async Task SendSupporterGoodbyeMessage(IUser discordUser, bool openCollective = true)
    {
        var goodbyeEmbed = new EmbedBuilder();
        goodbyeEmbed.WithColor(DiscordConstants.InformationColorBlue);

        var goodbyeMessage = new StringBuilder();

        goodbyeMessage.AppendLine("Your .fmbot supporter subscription has expired. Sorry to see you go!");
        goodbyeMessage.AppendLine();

        if (openCollective)
        {
            goodbyeMessage.AppendLine("If you ever want to come back in the future you can re-subscribe through the same OpenCollective account. Your supporter will then be automatically re-activated.");
            goodbyeMessage.AppendLine();
        }

        goodbyeMessage.AppendLine("Thanks for having supported the bot! If you have any feedback about the bot or the supporter program feel free to open a thread in #help on [our server](https://discord.gg/fmbot). You can also DM the developer who is identified on the server if preferable.");

        if (openCollective)
        {
            goodbyeMessage.AppendLine();
            goodbyeMessage.AppendLine(
                "Didn't cancel? It could be that there was an issue with your payment going through. Feel free to open a help thread if you need assistance.");
        }

        goodbyeEmbed.WithDescription(goodbyeMessage.ToString());
        await discordUser.SendMessageAsync(embed: goodbyeEmbed.Build());
    }

    public async Task<string> GetPromotionalUpdateMessage(User user, string prfx, IDiscordClient contextClient,
        ulong? guildId = null)
    {
        if (!ShowPromotionalMessage(user.UserType, guildId))
        {
            return null;
        }

        var randomHintNumber = new Random().Next(0, 30);

        switch (randomHintNumber)
        {
            case 1:
                SetGuildPromoCache(guildId);
                return  
                    $"*.fmbot stores all artists/albums/tracks instead of just the top 4/5/6k for supporters. " +
                    $"[See all the benefits of becoming a supporter here.]({GetSupporterLink()})*";
            case 2: 
                SetGuildPromoCache(guildId);
                return
                    $"*Supporters get extra statistics like first listen dates, full history in `stats`, artist discoveries in `year`, extra options in their `fm` footer and more. " +
                    $"[See all the perks of getting supporter here.]({GetSupporterLink()})*";
            case 3:
                {
                    await using var db = await this._contextFactory.CreateDbContextAsync();
                    if (await db.UserDiscogs.AnyAsync(a => a.UserId == user.UserId))
                    {
                        SetGuildPromoCache(guildId);
                        return
                            $"*Supporters can fetch and view their entire Discogs collection (up from last 100). " +
                            $"[Get .fmbot supporter here.]({Constants.GetSupporterOverviewLink})*";
                    }

                    return
                        $"*Using Discogs to keep track of your vinyl collection? Connect your account with the `{prfx}discogs` command.*";
                }
            case 4:
                {
                    if (user.FmFooterOptions == FmFooterOption.TotalScrobbles)
                    {
                        return
                            $"*Customize your `{prfx}fm` with the new custom footer options. Get started by using `/fmmode`.*";
                    }

                    SetGuildPromoCache(guildId);
                    return
                        $"*Want more custom options in your `{prfx}fm` footer? Supporters can set up to 8 + 1 options. " +
                        $"[Get .fmbot supporter here.]({GetSupporterLink()})*";
                }
            case 5:
                {
                    SetGuildPromoCache(guildId);
                    return
                        $"*Supporters get an improved GPT-4 powered `{prfx}judge` command. They also get higher usage limits and the ability to use the command on others. " +
                        $"[Get .fmbot supporter here.]({GetSupporterLink()})*";
                }
            case 6:
                {
                    SetGuildPromoCache(guildId);
                    return
                        $"*Supporters can now import their full Spotify history into the bot. " +
                        $"[Get .fmbot supporter here.]({GetSupporterLink()})*";
                }
            default:
                return null;
        }
    }

    private static string GetGuildPromoCacheKey(ulong? guildId = null)
    {
        return $"guild-supporter-promo-{guildId}";
    }

    public void SetGuildPromoCache(ulong? guildId = null)
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
            Created = DateTime.UtcNow,
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

        Log.Information("Removing supporter status for {supporterName} - {openCollectiveId}", supporter.Name, supporter.Name);
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

                Log.Information("Removed supporter status from Discord account {discordUserId} - {lastFmUsername}", user.DiscordUserId, user.UserNameLastFM);
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
        }

        return supporter;
    }

    public async Task CheckForNewSupporters()
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

        foreach (var newSupporter in openCollectiveSupporters.Users.Where(w => w.LastPayment >= DateTime.UtcNow.AddHours(-6)))
        {
            var cacheKey = $"new-supporter-{newSupporter.Id}";
            if (this._cache.TryGetValue(cacheKey, out _))
            {
                return;
            }

            var supporterUpdateChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterUpdatesWebhookUrl);
            var supporterAuditLogChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);

            var embed = new EmbedBuilder();

            var existingSupporter = existingSupporters.FirstOrDefault(f => f.OpenCollectiveId == newSupporter.Id);
            if (existingSupporter is { Expired: true })
            {
                embed.WithTitle("Monthly supporter has re-activated their subscription");
                embed.WithDescription($"Name: `{newSupporter.Name}`\n" +
                                      $"OpenCollective ID: `{newSupporter.Id}`\n" +
                                      $"Subscription type: `{newSupporter.SubscriptionType}`\n\n" +
                                      $"No action should be required. Check botlog channel or ask frik.");

                await supporterAuditLogChannel.SendMessageAsync($"`.addsupporter \"discordUserId\" \"{newSupporter.Id}\"`", false, new[] { embed.Build() });
                await supporterUpdateChannel.SendMessageAsync($"`.addsupporter \"discordUserId\" \"{newSupporter.Id}\"`", false, new[] { embed.Build() });

                this._cache.Set(cacheKey, 1, TimeSpan.FromDays(1));

                return;
            }
            if (existingSupporter != null)
            {
                return;
            }

            embed.WithTitle("New supporter on OpenCollective!");
            embed.WithDescription($"Name: `{newSupporter.Name}`\n" +
                                  $"OpenCollective ID: `{newSupporter.Id}`\n" +
                                  $"Subscription type: `{newSupporter.SubscriptionType}`\n\n");

            await supporterAuditLogChannel.SendMessageAsync($"`.addsupporter \"discordUserId\" \"{newSupporter.Id}\"`", false, new[] { embed.Build() });
            await supporterUpdateChannel.SendMessageAsync($"`.addsupporter \"discordUserId\" \"{newSupporter.Id}\"`", false, new[] { embed.Build() });

            this._cache.Set(cacheKey, 1, TimeSpan.FromDays(1));
        }
    }

    public async Task UpdateExistingOpenCollectiveSupporters()
    {
        var openCollectiveSupporters = await this._openCollectiveService.GetOpenCollectiveOverview();

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingSupporters = await db.Supporters
            .Where(w => w.OpenCollectiveId != null)
            .ToListAsync();

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
                    var supporterAuditLogChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);

                    Log.Information("Updating last payment date for supporter {supporterName} from {currentDate} to {newDate}", existingSupporter.Name, existingSupporter.LastPayment, openCollectiveSupporter.LastPayment);
                    existingSupporter.LastPayment = openCollectiveSupporter.LastPayment;

                    Log.Information("Updating name for supporter {supporterName} to {newName}", existingSupporter.Name, openCollectiveSupporter.Name);
                    existingSupporter.Name = openCollectiveSupporter.Name;

                    if (existingSupporter.Expired == true && openCollectiveSupporter.LastPayment >= DateTime.UtcNow.AddHours(-3))
                    {
                        Log.Information("Re-activating supporter status for {supporterName} - {openCollectiveId}", existingSupporter.Name, existingSupporter.Name);
                        if (existingSupporter.DiscordUserId.HasValue)
                        {
                            var user = await db.Users
                                .AsQueryable()
                                .FirstOrDefaultAsync(f => f.DiscordUserId == existingSupporter.DiscordUserId);

                            if (user != null)
                            {
                                user.UserType = UserType.Supporter;
                                db.Update(user);

                                Log.Information("Re-activated supporter status from Discord account {discordUserId} - {lastFmUsername}", user.DiscordUserId, user.UserNameLastFM);

                                _ = this._indexService.IndexUser(user);
                            }
                        }

                        existingSupporter.Expired = false;
                        existingSupporter.SupporterMessagesEnabled = true;
                        existingSupporter.VisibleInOverview = true;

                        var reActivateDescription = new StringBuilder();
                        reActivateDescription.AppendLine($"Name: `{existingSupporter.Name}`");
                        reActivateDescription.AppendLine($"LastPayment: `{existingSupporter.LastPayment}`");
                        if (currentSubscriptionType != newSubscriptionType)
                        {
                            reActivateDescription.AppendLine($"Subscription type: `{Enum.GetName(newSubscriptionType)}` **(updated from `{Enum.GetName(currentSubscriptionType)}`)**");
                        }
                        else
                        {
                            reActivateDescription.AppendLine($"Subscription type: `{Enum.GetName(newSubscriptionType)}`");
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
                        updatedDescription.AppendLine($"Subscription type: `{Enum.GetName(newSubscriptionType)}` **(updated from `{Enum.GetName(currentSubscriptionType)}`)**");
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

            if (existingSupporter.SubscriptionType == SubscriptionType.Monthly)
            {
                if (existingSupporter.Expired != true && existingSupporter.LastPayment > DateTime.UtcNow.AddDays(-63) && existingSupporter.LastPayment < DateTime.UtcNow.AddDays(-60))
                {
                    Log.Information("Monthly supporter expiration detected for {supporterName} - {discordUserId}", existingSupporter.Name, existingSupporter.DiscordUserId);

                    var cacheKey = $"supporter-monthly-expired-{existingSupporter.OpenCollectiveId}";
                    if (this._cache.TryGetValue(cacheKey, out _))
                    {
                        return;
                    }

                    var supporterUpdateChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterUpdatesWebhookUrl);
                    var supporterAuditLogChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);

                    var embed = new EmbedBuilder();

                    embed.WithTitle("Monthly supporter expired");
                    embed.WithDescription(OpenCollectiveSupporterToEmbedDescription(existingSupporter));

                    await supporterAuditLogChannel.SendMessageAsync($"`.removesupporter {existingSupporter.DiscordUserId}`", false, new[] { embed.Build() });
                    await supporterUpdateChannel.SendMessageAsync($"`.removesupporter {existingSupporter.DiscordUserId}`", false, new[] { embed.Build() });

                    this._cache.Set(cacheKey, 1, TimeSpan.FromDays(2));
                }
            }

            if (existingSupporter.SubscriptionType == SubscriptionType.Yearly)
            {
                if (existingSupporter.Expired != true && existingSupporter.LastPayment > DateTime.UtcNow.AddDays(-388) && existingSupporter.LastPayment < DateTime.UtcNow.AddDays(-385))
                {
                    Log.Information("Yearly supporter expiration detected for {supporterName} - {discordUserId}", existingSupporter.Name, existingSupporter.DiscordUserId);

                    var cacheKey = $"supporter-yearly-expired-{existingSupporter.OpenCollectiveId}";
                    if (this._cache.TryGetValue(cacheKey, out _))
                    {
                        return;
                    }

                    var supporterUpdateChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterUpdatesWebhookUrl);
                    var supporterAuditLogChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);

                    var embed = new EmbedBuilder();

                    embed.WithTitle("Yearly supporter expired");
                    embed.WithDescription(OpenCollectiveSupporterToEmbedDescription(existingSupporter));

                    await supporterAuditLogChannel.SendMessageAsync($"`.removesupporter {existingSupporter.DiscordUserId}`", false, new[] { embed.Build() });
                    await supporterUpdateChannel.SendMessageAsync(null, false, new[] { embed.Build() });

                    this._cache.Set(cacheKey, 1, TimeSpan.FromDays(2));
                }
            }
        }
    }

    private string OpenCollectiveSupporterToEmbedDescription(Supporter supporter)
    {
        return $"Name: `{supporter.Name}`\n" +
               $"OC ID: `{supporter.OpenCollectiveId}`\n" +
               $"Discord ID: `{supporter.DiscordUserId}`\n" +
               $"Notes: `{supporter.Notes}`";
    }

    public async Task<List<DiscordEntitlement>> GetDiscordEntitlements()
    {
        return await this._discordSkuService.GetEntitlements();
    }

    public async Task UpdateDiscordSupporters()
    {
        var discordSupporters = await this._discordSkuService.GetEntitlements();

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingSupporters = await db.Supporters
            .Where(w =>
                w.DiscordUserId != null &&
                w.SubscriptionType == SubscriptionType.Discord)
            .ToListAsync();

        var discordUsersLeft = existingSupporters
            .OrderByDescending(o => o.LastPayment)
            .Select(s => s.DiscordUserId.Value)
            .Distinct()
            .ToHashSet();

        foreach (var discordSupporter in discordSupporters)
        {
            var existingSupporter =
                existingSupporters.FirstOrDefault(f => f.DiscordUserId == discordSupporter.DiscordUserId);

            if (existingSupporter == null && discordSupporter.Active)
            {
                Log.Information("Adding Discord supporter {discordUserId}", discordSupporter.DiscordUserId);

                var newSupporter = await AddDiscordSupporter(discordSupporter.DiscordUserId, discordSupporter);
                await ModifyGuildRole(discordSupporter.DiscordUserId);
                await RunFullUpdate(discordSupporter.DiscordUserId);

                var user = await this._client.Rest.GetUserAsync(discordSupporter.DiscordUserId);
                if (user != null)
                {
                    await SendSupporterWelcomeMessage(user, false, newSupporter);
                }

                discordUsersLeft.Remove(discordSupporter.DiscordUserId);

                var supporterAuditLogChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);
                var embed = new EmbedBuilder().WithDescription(
                    $"Added Discord supporter {discordSupporter.DiscordUserId} - <@{discordSupporter.DiscordUserId}>");
                await supporterAuditLogChannel.SendMessageAsync(embeds: new[] { embed.Build() });

                Log.Information("Added Discord supporter {discordUserId}", discordSupporter.DiscordUserId);

                continue;
            }

            if (existingSupporter != null)
            {
                discordUsersLeft.Remove(discordSupporter.DiscordUserId);

                if (existingSupporter.LastPayment != discordSupporter.EndsAt && existingSupporter.Expired != true)
                {
                    Log.Information("Updating Discord supporter {discordUserId}", discordSupporter.DiscordUserId);

                    existingSupporter.LastPayment = discordSupporter.EndsAt;
                    db.Update(existingSupporter);
                    await db.SaveChangesAsync();

                    var supporterAuditLogChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);
                    var embed = new EmbedBuilder().WithDescription(
                        $"Updated Discord supporter {discordSupporter.DiscordUserId} - <@{discordSupporter.DiscordUserId}>");
                    await supporterAuditLogChannel.SendMessageAsync(embeds: new[] { embed.Build() });
                }

                if (existingSupporter.Expired == true && discordSupporter.Active)
                {
                    Log.Information("Re-activating Discord supporter {discordUserId}", discordSupporter.DiscordUserId);

                    var reActivatedSupporter = await ReActivateSupporter(existingSupporter, discordSupporter);
                    await ModifyGuildRole(discordSupporter.DiscordUserId);
                    await RunFullUpdate(discordSupporter.DiscordUserId);

                    var user = await this._client.Rest.GetUserAsync(discordSupporter.DiscordUserId);
                    if (user != null)
                    {
                        await SendSupporterWelcomeMessage(user, false, reActivatedSupporter);
                    }

                    discordUsersLeft.Remove(discordSupporter.DiscordUserId);

                    var supporterAuditLogChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);
                    var embed = new EmbedBuilder().WithDescription(
                        $"Re-activated Discord supporter {discordSupporter.DiscordUserId} - <@{discordSupporter.DiscordUserId}>");
                    await supporterAuditLogChannel.SendMessageAsync(embeds: new[] { embed.Build() });

                    Log.Information("Re-activated Discord supporter {discordUserId}", discordSupporter.DiscordUserId);

                    continue;
                }

                if (existingSupporter.Expired != true && !discordSupporter.Active)
                {
                    Log.Information("Removing Discord supporter {discordUserId}", discordSupporter.DiscordUserId);

                    await ExpireSupporter(discordSupporter.DiscordUserId, existingSupporter);
                    await ModifyGuildRole(discordSupporter.DiscordUserId, false);
                    await RunFullUpdate(discordSupporter.DiscordUserId);

                    var user = await this._client.Rest.GetUserAsync(discordSupporter.DiscordUserId);
                    if (user != null)
                    {
                        await SendSupporterGoodbyeMessage(user, false);
                    }

                    var supporterAuditLogChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);
                    var embed = new EmbedBuilder().WithDescription(
                        $"Removed Discord supporter {discordSupporter.DiscordUserId} - <@{discordSupporter.DiscordUserId}>");
                    await supporterAuditLogChannel.SendMessageAsync(embeds: new[] { embed.Build() });

                    Log.Information("Removed Discord supporter {discordUserId}", discordSupporter.DiscordUserId);
                }
            }
        }

        if (discordUsersLeft.Any())
        {
            Log.Warning("Found {count} Discord supporters without attached entitlement", discordUsersLeft.Count);
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
                    var role = baseGuild.Roles.FirstOrDefault(x => x.Name == "Supporter");

                    if (add)
                    {
                        await guildUser.AddRoleAsync(role, new RequestOptions
                        {
                            AuditLogReason = "Automated supporter integration"
                        });
                    }
                    else
                    {
                        await guildUser.RemoveRoleAsync(role, new RequestOptions
                        {
                            AuditLogReason = "Automated supporter integration"
                        });
                    }

                    Log.Information("Modifying supporter role succeeded for {id}", discordUserId);
                    return;
                }
            }
            catch (Exception e)
            {
                Log.Error("Modifying supporter role failed for {id}", discordUserId, e);
            }
        }

        Log.Error("Modifying supporter role failed for {id}", discordUserId);
    }

    private async Task<Supporter> AddDiscordSupporter(ulong id, DiscordEntitlement entitlement)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == id);

        if (user == null)
        {
            Log.Warning("Someone who isn't registered in .fmbot just got a Discord subscription - ID {discordUserId}", id);
        }
        else
        {
            if (user.UserType == UserType.User)
            {
                user.UserType = UserType.Supporter;
            }

            db.Update(user);
        }

        var supporterToAdd = new Supporter
        {
            DiscordUserId = id,
            Name = user?.UserNameLastFM,
            Created = entitlement.StartsAt ?? DateTime.UtcNow,
            LastPayment = entitlement.EndsAt,
            Notes = "Added through Discord SKU",
            SupporterMessagesEnabled = true,
            VisibleInOverview = true,
            SupporterType = SupporterType.User,
            SubscriptionType = SubscriptionType.Discord
        };

        await db.Supporters.AddAsync(supporterToAdd);
        await db.SaveChangesAsync();

        return supporterToAdd;
    }

    private async Task<Supporter> ReActivateSupporter(Supporter supporter, DiscordEntitlement entitlement)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == supporter.DiscordUserId.Value);

        if (user == null)
        {
            Log.Warning("Someone who isn't registered in .fmbot just re-activated their Discord subscription - ID {discordUserId}", supporter.DiscordUserId);
        }
        else
        {
            if (user.UserType == UserType.User)
            {
                user.UserType = UserType.Supporter;
            }

            db.Update(user);
        }

        supporter.Expired = null;
        supporter.SupporterMessagesEnabled = true;
        supporter.VisibleInOverview = true;
        supporter.LastPayment = entitlement.EndsAt;

        db.Update(supporter);
        await db.SaveChangesAsync();

        return supporter;
    }

    private async Task ExpireSupporter(ulong id, Supporter supporter)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == id);

        if (user == null)
        {
            Log.Warning("Someone who isn't registered in .fmbot just cancelled their Discord subscription - ID {discordUserId}", id);
        }
        else
        {
            if (user.UserType == UserType.Supporter)
            {
                user.UserType = UserType.User;
            }

            if (user.DataSource != DataSource.LastFm)
            {
                user.DataSource = DataSource.LastFm;
                _ = this._indexService.RecalculateTopLists(user);
            }

            db.Update(user);
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

    public async Task<IReadOnlyList<Supporter>> GetAllSupporters()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.Supporters
            .AsQueryable()
            .OrderByDescending(o => o.Created)
            .ToListAsync();
    }
}
