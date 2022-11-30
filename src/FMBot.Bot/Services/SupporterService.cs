using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Webhook;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.OpenCollective.Models;
using FMBot.OpenCollective.Services;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog;

namespace FMBot.Bot.Services;

public class SupporterService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly OpenCollectiveService _openCollectiveService;
    private readonly BotSettings _botSettings;
    private readonly IMemoryCache _cache;

    public SupporterService(IDbContextFactory<FMBotDbContext> contextFactory, OpenCollectiveService openCollectiveService, IOptions<BotSettings> botSettings, IMemoryCache cache)
    {
        this._contextFactory = contextFactory;
        this._openCollectiveService = openCollectiveService;
        this._cache = cache;
        this._botSettings = botSettings.Value;
    }

    public async Task<string> GetRandomSupporter(IGuild guild, UserType userUserType)
    {
        if (guild == null)
        {
            return null;
        }

        if (userUserType == UserType.Supporter)
        {
            return null;
        }

        if (this._cache.TryGetValue(GetGuildPromoCacheKey(guild.Id), out _))
        {
            return null;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var guildSettings = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

        if (guildSettings is { DisableSupporterMessages: true })
        {
            return null;
        }

        var rnd = new Random();
        var randomHintNumber = rnd.Next(0, Constants.SupporterMessageChance);

        if (randomHintNumber == 1)
        {
            var supporters = db.Supporters
                .AsQueryable()
                .Where(w => w.SupporterMessagesEnabled)
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

    public async Task<string> GetPromotionalUpdateMessage(User user, string prfx, IDiscordClient contextClient,
        ulong? guildId = null)
    {
        if (!ShowPromotionalMessage(user.UserType, guildId))
        {
            return null;
        }

        var randomHintNumber = new Random().Next(0, 18);

        switch (randomHintNumber)
        {
            case 1:
                SetGuildPromoCache(guildId);
                return
                    $"*Did you know that .fmbot stores all artists/albums/tracks for supporters instead of just the top 4/5/6k? " +
                    $"[See all the benefits of becoming a supporter here.]({Constants.GetSupporterOverviewLink})*";
            case 2:
                SetGuildPromoCache(guildId);
                return
                    $"*Supporters get extra statistics like first listen dates in artist/album/track, full history in `stats`, artist discoveries in `year` and more. " +
                    $"[See all the perks of getting supporter here.]({Constants.GetSupporterOverviewLink})*";
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
                db.Update(user);

                Log.Information("Removing supporter status from Discord account {discordUserId} - {lastFmUsername}", user.DiscordUserId, user.UserNameLastFM);
            }
        }

        supporter.Expired = true;
        supporter.SupporterMessagesEnabled = false;
        supporter.VisibleInOverview = false;

        db.Update(supporter);

        await db.SaveChangesAsync();

        var supporterAuditLogChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);

        var embed = new EmbedBuilder();

        embed.WithTitle("Supporter expiry processed");
        embed.WithDescription($"Name: `{supporter.Name}`\n" +
                              $"OpenCollective ID: `{supporter.OpenCollectiveId}`\n" +
                              $"Subscription type: `{supporter.SubscriptionType}`");

        await supporterAuditLogChannel.SendMessageAsync(null, false, new[] { embed.Build() });

        return supporter;
    }

    public async Task CheckForNewSupporters()
    {
        var openCollectiveSupporters = await this._openCollectiveService.GetOpenCollectiveOverview();

        if (openCollectiveSupporters == null)
        {
            Log.Error("Error while checking newsupporters - response null");
        }

        foreach (var newSupporter in openCollectiveSupporters.Users.Where(w => w.CreatedAt >= DateTime.UtcNow.AddHours(-5)))
        {
            var cacheKey = $"new-supporter-{newSupporter.Id}";
            if (this._cache.TryGetValue(cacheKey, out _))
            {
                return;
            }

            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingSupporter = await db.Supporters.FirstOrDefaultAsync(f => f.OpenCollectiveId == newSupporter.Id);
            if (existingSupporter != null)
            {
                return;
            }

            var supporterUpdateChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterUpdatesWebhookUrl);
            var supporterAuditLogChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);

            var embed = new EmbedBuilder();

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
                if (existingSupporter.LastPayment != openCollectiveSupporter.LastPayment ||
                    existingSupporter.Name != openCollectiveSupporter.Name)
                {
                    Log.Information("Updating last payment date for supporter {supporterName} from {currentDate} to {newDate}", existingSupporter.Name, existingSupporter.LastPayment, openCollectiveSupporter.LastPayment);
                    existingSupporter.LastPayment = openCollectiveSupporter.LastPayment;

                    Log.Information("Updating name for supporter {supporterName} to {newName}", existingSupporter.Name, openCollectiveSupporter.Name);
                    existingSupporter.Name = openCollectiveSupporter.Name;

                    db.Update(existingSupporter);
                    await db.SaveChangesAsync();

                    var supporterAuditLogChannel = new DiscordWebhookClient(this._botSettings.Bot.SupporterAuditLogWebhookUrl);

                    var embed = new EmbedBuilder();
                    embed.WithTitle("Updated supporter");
                    embed.WithDescription($"Name: `{existingSupporter.Name}`\n" +
                                          $"LastPayment: `{existingSupporter.LastPayment}`\n" +
                                          $"Subscription type: `{Enum.GetName(existingSupporter.SubscriptionType.GetValueOrDefault())}`");

                    await supporterAuditLogChannel.SendMessageAsync(null, false, new[] { embed.Build() });
                }
            }

            if (existingSupporter.SubscriptionType == SubscriptionType.Monthly)
            {
                if (existingSupporter.Expired != true && existingSupporter.LastPayment > DateTime.UtcNow.AddDays(-40) && existingSupporter.LastPayment < DateTime.UtcNow.AddDays(-38))
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
                    embed.WithDescription($"Name: `{existingSupporter.Name}`\n" +
                                          $"ID: `{existingSupporter.OpenCollectiveId}`");

                    await supporterAuditLogChannel.SendMessageAsync($"`.removesupporter {existingSupporter.DiscordUserId}`", false, new[] { embed.Build() });
                    await supporterUpdateChannel.SendMessageAsync($"`.removesupporter {existingSupporter.DiscordUserId}`", false, new[] { embed.Build() });

                    this._cache.Set(cacheKey, 1, TimeSpan.FromDays(2));
                }
            }

            if (existingSupporter.SubscriptionType == SubscriptionType.Yearly)
            {
                if (existingSupporter.Expired != true && existingSupporter.LastPayment > DateTime.UtcNow.AddDays(-372) && existingSupporter.LastPayment < DateTime.UtcNow.AddDays(-370))
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
                    embed.WithDescription($"Name: `{existingSupporter.Name}`\n" +
                                          $"ID: `{existingSupporter.OpenCollectiveId}`\n\n" +
                                          $"`.removesupporter {existingSupporter.DiscordUserId}`");

                    await supporterAuditLogChannel.SendMessageAsync(null, false, new[] { embed.Build() });
                    await supporterUpdateChannel.SendMessageAsync(null, false, new[] { embed.Build() });

                    this._cache.Set(cacheKey, 1, TimeSpan.FromDays(2));
                }
            }
        }
    }

    public async Task<IReadOnlyList<Supporter>> GetAllVisibleSupporters()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.Supporters
            .AsQueryable()
            .Where(w => w.VisibleInOverview)
            .Where(w => w.Expired != true)
            .Where(w => w.Name != "Incognito" && w.Name != "Guest")
            .OrderByDescending(o => o.Created)
            .ToListAsync();
    }
}
