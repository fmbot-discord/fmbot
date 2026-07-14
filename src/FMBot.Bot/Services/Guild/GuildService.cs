using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.Commands;
using Npgsql;
using Serilog;
using Channel = FMBot.Persistence.Domain.Models.Channel;
using GuildUser = FMBot.Persistence.Domain.Models.GuildUser;

namespace FMBot.Bot.Services.Guild;

public class GuildService(
    IDbContextFactory<FMBotDbContext> contextFactory,
    IMemoryCache cache,
    IOptions<BotSettings> botSettings)
{
    private readonly BotSettings _botSettings = botSettings.Value;

    // Message is in dm?
    public bool CheckIfDm(CommandContext context)
    {
        return context.Guild == null;
    }

    public async Task<Persistence.Domain.Models.Guild> GetGuildAsync(ulong? discordGuildId)
    {
        if (discordGuildId == null)
        {
            return null;
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        return await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);
    }

    public async Task<Persistence.Domain.Models.Guild> GetFullGuildAsync(ulong? discordGuildId = null,
        bool filterBots = true)
    {
        if (discordGuildId == null)
        {
            return null;
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        return await db.Guilds
            //.AsNoTracking()
            .Include(i => i.GuildUsers.Where(w => !filterBots || w.Bot != true))
            .ThenInclude(t => t.User)
            .Include(i => i.Channels)
            .Include(i => i.Webhooks)
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);
    }

    public async Task<Persistence.Domain.Models.Guild> GetGuildWithWebhooks(ulong? discordGuildId = null)
    {
        if (discordGuildId == null)
        {
            return null;
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        return await db.Guilds
            .Include(i => i.Channels)
            .Include(i => i.Webhooks)
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);
    }

    public async Task<Persistence.Domain.Models.Guild> GetGuildForWhoKnows(ulong? discordGuildId = null)
    {
        if (discordGuildId == null)
        {
            return null;
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        return await db.Guilds
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);
    }

    public async Task<IDictionary<int, FullGuildUser>> GetGuildUsers(ulong? discordGuildId = null)
    {
        if (discordGuildId == null)
        {
            return new Dictionary<int, FullGuildUser>();
        }

        var cacheKey = $"guild-users-{discordGuildId}";
        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(12);

            const string sql = "SELECT gu.user_id, " +
                               "gu.user_name, " +
                               "gu.bot, " +
                               "gu.last_message, " +
                               "gu.roles AS dto_roles, " +
                               "u.user_name_last_fm, " +
                               "u.discord_user_id, " +
                               "u.last_used, " +
                               "COALESCE(gbu.blocked_from_crowns, false) as blocked_from_crowns, " +
                               "COALESCE(gbu.blocked_from_who_knows, false) as blocked_from_who_knows, " +
                               "COALESCE(gbu.self_block_from_who_knows, false) as self_block_from_who_knows " +
                               "FROM public.guild_users AS gu " +
                               "LEFT JOIN users AS u ON gu.user_id = u.user_id " +
                               "LEFT JOIN guilds AS g ON gu.guild_id = g.guild_id " +
                               "LEFT OUTER JOIN guild_blocked_users AS gbu ON gu.user_id = gbu.user_id AND gbu.guild_id = gu.guild_id " +
                               "WHERE g.discord_guild_id = @discordGuildId";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var result = (await connection.QueryAsync<FullGuildUser>(sql, new
            {
                discordGuildId = Convert.ToInt64(discordGuildId)
            })).ToList();

            foreach (var row in result.Where(w => w.DtoRoles != null))
            {
                row.Roles = row.DtoRoles.Select(s => (ulong)s).ToArray();
            }

            return (IDictionary<int, FullGuildUser>)result.ToDictionary(d => d.UserId, d => d);
        });
    }

    public async Task<List<Persistence.Domain.Models.Guild>> GetPremiumGuilds()
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        var paidGuildIds = await db.PremiumGuildSubscriptions
            .AsNoTracking()
            .Where(w => !w.EntitlementDeleted &&
                        (w.DateEnding == null || w.DateEnding > DateTime.UtcNow))
            .Select(s => s.DiscordGuildId)
            .ToListAsync();

        return await db.Guilds
            .AsNoTracking()
            .Where(w => w.SpecialGuild == true ||
                        (w.GuildFlags.HasValue && (w.GuildFlags.Value.HasFlag(GuildFlags.LegacyWhoKnowsWhitelist) ||
                                                   w.GuildFlags.Value.HasFlag(GuildFlags.PremiumServerTester))) ||
                        paidGuildIds.Contains(w.DiscordGuildId))
            .ToListAsync();
    }

    public async Task<List<ulong>> RefreshPremiumGuilds(bool postAuditLog = true)
    {
        var premiumServers = await GetPremiumGuilds();
        var updatedPremiumServers = premiumServers.ToDictionary(d => d.DiscordGuildId, d => d.GuildId);

        var previousGuildIds = PublicProperties.PremiumServers.Keys.ToHashSet();

        foreach (var guild in updatedPremiumServers)
        {
            PublicProperties.PremiumServers.TryAdd(guild.Key, guild.Value);
        }

        foreach (var removedGuild in PublicProperties.PremiumServers.Where(w => !updatedPremiumServers.ContainsKey(w.Key)))
        {
            PublicProperties.PremiumServers.TryRemove(removedGuild);
        }

        if (previousGuildIds.Count == 0)
        {
            return [];
        }

        var addedGuildIds = updatedPremiumServers.Keys.Where(w => !previousGuildIds.Contains(w)).ToList();
        var removedGuildIds = previousGuildIds.Where(w => !updatedPremiumServers.ContainsKey(w)).ToList();

        if (!postAuditLog || (addedGuildIds.Count == 0 && removedGuildIds.Count == 0))
        {
            return addedGuildIds;
        }

        await using var db = await contextFactory.CreateDbContextAsync();

        var changedGuildIds = addedGuildIds.Concat(removedGuildIds).ToList();
        var subscriptions = await db.PremiumGuildSubscriptions
            .AsNoTracking()
            .Where(w => changedGuildIds.Contains(w.DiscordGuildId))
            .ToListAsync();

        var auditLogChannel = WebhookService.CreateWebhookClientFromUrl(this._botSettings.Bot.SupporterAuditLogWebhookUrl);

        foreach (var addedGuildId in addedGuildIds)
        {
            var guildName = premiumServers.First(f => f.DiscordGuildId == addedGuildId).Name;
            var subscription = subscriptions.FirstOrDefault(w => w.DiscordGuildId == addedGuildId);

            var embed = new EmbedProperties()
                .WithTitle("Premium server activated")
                .WithDescription($"Server: `{StringExtensions.Sanitize(guildName)}` — `{addedGuildId}`\n" +
                                 $"Purchaser: {(subscription?.PurchaserDiscordUserId != null ? $"<@{subscription.PurchaserDiscordUserId}>" : "`none`")}\n" +
                                 $"Source: `{subscription?.PurchaseSource ?? "manual / flag"}`");
            await auditLogChannel.ExecuteAsync(new WebhookMessageProperties { Embeds = [embed] });
        }

        foreach (var removedGuildId in removedGuildIds)
        {
            var subscription = subscriptions.FirstOrDefault(w => w.DiscordGuildId == removedGuildId);

            var embed = new EmbedProperties()
                .WithTitle("Premium server deactivated")
                .WithDescription($"Server: `{removedGuildId}`\n" +
                                 $"Purchaser: {(subscription?.PurchaserDiscordUserId != null ? $"<@{subscription.PurchaserDiscordUserId}>" : "`none`")}\n" +
                                 $"Source: `{subscription?.PurchaseSource ?? "manual / flag"}`");
            await auditLogChannel.ExecuteAsync(new WebhookMessageProperties { Embeds = [embed] });
        }

        return addedGuildIds;
    }

    public async Task<List<Persistence.Domain.Models.Guild>> GetCustomFeaturedGuilds()
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var guilds = await db.Guilds
            .AsNoTracking()
            .Where(w => w.FeaturedMode == GuildFeaturedMode.GuildFeatured)
            .ToListAsync();

        return guilds
            .Where(w => PublicProperties.PremiumServers.ContainsKey(w.DiscordGuildId))
            .ToList();
    }

    public async Task RemoveLapsedPremiumSettings(NetCord.Gateway.ShardedGatewayClient client)
    {
        if (PublicProperties.PremiumServers.IsEmpty)
        {
            return;
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        var brandedGuilds = await db.Guilds
            .Where(w => w.CustomLogo != null ||
                        (w.FeaturedMode != null && w.FeaturedMode != GuildFeaturedMode.GlobalFeatured))
            .ToListAsync();

        var lapsedGuilds = brandedGuilds
            .Where(w => !PublicProperties.PremiumServers.ContainsKey(w.DiscordGuildId))
            .ToList();

        if (lapsedGuilds.Count == 0)
        {
            return;
        }

        foreach (var guild in lapsedGuilds)
        {
            try
            {
                await client.Rest.ModifyCurrentGuildUserAsync(guild.DiscordGuildId,
                    o => o.Avatar = ImageProperties.Empty);
            }
            catch (Exception e)
            {
                Log.Information(e, "RemoveLapsedPremiumSettings: Could not reset bot profile in guild {discordGuildId}",
                    guild.DiscordGuildId);
            }

            guild.CustomLogo = null;
            guild.FeaturedMode = null;
            db.Update(guild);

            Log.Information("RemoveLapsedPremiumSettings: Removed custom branding for lapsed guild {guildId}",
                guild.GuildId);
        }

        await db.SaveChangesAsync();
    }

    private Task RemoveGuildFromCache(ulong discordGuildId)
    {
        cache.Remove(CacheKeyForGuild(discordGuildId));
        return Task.CompletedTask;
    }

    private static string CacheKeyForGuild(ulong discordGuildId)
    {
        return $"guild-{discordGuildId}";
    }

    public static (FilterStats Stats, IDictionary<int, FullGuildUser> FilteredGuildUsers) FilterGuildUsers(
        IDictionary<int, FullGuildUser> users,
        Persistence.Domain.Models.Guild guild,
        List<ulong> roles = null)
    {
        var stats = new FilterStats
        {
            StartCount = users.Count,
            Roles = roles
        };

        var premiumGuild = PublicProperties.PremiumServers.ContainsKey(guild.DiscordGuildId);

        if (guild.ActivityThresholdDays.HasValue)
        {
            var preFilterCount = users.Count;

            users = users.Where(w =>
                    w.Value.LastUsed != null &&
                    w.Value.LastUsed >= DateTime.UtcNow.AddDays(-guild.ActivityThresholdDays.Value))
                .ToDictionary(i => i.Key, i => i.Value);

            stats.ActivityThresholdFiltered = preFilterCount - users.Count;
        }

        if (premiumGuild && guild.UserActivityThresholdDays.HasValue)
        {
            var preFilterCount = users.Count;

            users = users.Where(w =>
                    w.Value.LastMessage != null &&
                    w.Value.LastMessage >= DateTime.UtcNow.AddDays(-guild.UserActivityThresholdDays.Value))
                .ToDictionary(i => i.Key, i => i.Value);

            stats.GuildActivityThresholdFiltered = preFilterCount - users.Count;
        }

        if (users.Any(a => a.Value.BlockedFromWhoKnows || a.Value.SelfBlockFromWhoKnows))
        {
            var preFilterCount = users.Count;

            var usersToFilter = users
                .Where(w => w.Value.BlockedFromWhoKnows || w.Value.SelfBlockFromWhoKnows)
                .Select(s => s.Key)
                .ToHashSet();

            users = users
                .Where(w => !usersToFilter.Contains(w.Value.UserId))
                .ToDictionary(i => i.Key, i => i.Value);

            stats.BlockedFiltered = preFilterCount - users.Count;
        }

        if (premiumGuild && guild.AllowedRoles != null && guild.AllowedRoles.Any())
        {
            var preFilterCount = users.Count;

            users = users
                .Where(w => w.Value.Roles != null && guild.AllowedRoles.Any(a => w.Value.Roles.Contains(a)))
                .ToDictionary(i => i.Key, i => i.Value);

            stats.AllowedRolesFiltered = preFilterCount - users.Count;
        }

        if (premiumGuild && guild.BlockedRoles != null && guild.BlockedRoles.Any())
        {
            var preFilterCount = users.Count;

            users = users
                .Where(w => w.Value.Roles == null || !guild.BlockedRoles.Any(a => w.Value.Roles.Contains(a)))
                .ToDictionary(i => i.Key, i => i.Value);

            stats.BlockedRolesFiltered = preFilterCount - users.Count;
        }

        if (roles != null && roles.Count != 0)
        {
            var preFilterCount = users.Count;

            users = users
                .Where(w => w.Value.Roles != null && roles.Any(a => w.Value.Roles.Contains(a)))
                .ToDictionary(i => i.Key, i => i.Value);
            ;

            stats.ManualRoleFilter = preFilterCount - users.Count;
        }

        stats.EndCount = users.Count;

        return (stats, users);
    }

    public static async Task<Permissions> GetChannelPermissionsAsync(CommandContext context)
    {
        var botUserId = context.Client.Id;
        var guild = context.Guild;
        var channel = context.Channel;

        PartialGuildUser guildUser;
        var cachedUsers = context.Client.Cache.Guilds[guild.Id]?.Users;
        if (cachedUsers != null && cachedUsers.TryGetValue(botUserId, out var cachedGuildUser))
        {
            guildUser = cachedGuildUser;
        }
        else
        {
            guildUser = await guild.GetUserAsync(botUserId);
        }

        var guildPermissions = guildUser.GetPermissions(guild);
        if (channel is IGuildChannel guildChannel)
        {
            return guildUser.GetChannelPermissions(guildPermissions, guildChannel);
        }

        return guildPermissions;
    }

    public async Task ChangeGuildAllowedRoles(NetCord.Gateway.Guild discordGuild, ulong[] allowedRoles)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstAsync(f => f.DiscordGuildId == discordGuild.Id);

        existingGuild.Name = discordGuild.Name;
        existingGuild.AllowedRoles = allowedRoles;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);
    }

    public async Task ChangeGuildBlockedRoles(NetCord.Gateway.Guild discordGuild, ulong[] blockedRoles)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstAsync(f => f.DiscordGuildId == discordGuild.Id);

        existingGuild.Name = discordGuild.Name;
        existingGuild.BlockedRoles = blockedRoles;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);
    }

    public async Task ChangeGuildBotManagementRoles(NetCord.Gateway.Guild discordGuild, ulong[] botManagementRoles)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstAsync(f => f.DiscordGuildId == discordGuild.Id);

        existingGuild.Name = discordGuild.Name;
        existingGuild.BotManagementRoles = botManagementRoles;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);
    }

    public async Task ChangeGuildSettingAsync(NetCord.Gateway.Guild discordGuild, FmEmbedType? embedType)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            var newGuild = new Persistence.Domain.Models.Guild
            {
                DiscordGuildId = discordGuild.Id,
                Name = discordGuild.Name,
                FmEmbedType = embedType
            };

            await db.Guilds.AddAsync(newGuild);

            await db.SaveChangesAsync();
        }
        else
        {
            existingGuild.Name = discordGuild.Name;
            existingGuild.FmEmbedType = embedType;

            db.Entry(existingGuild).State = EntityState.Modified;

            await db.SaveChangesAsync();
        }

        await RemoveGuildFromCache(discordGuild.Id);
    }

    public async Task SetGuildReactionsAsync(NetCord.Gateway.Guild discordGuild, string[] reactions)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            var newGuild = new Persistence.Domain.Models.Guild
            {
                DiscordGuildId = discordGuild.Id,
                EmoteReactions = reactions,
                Name = discordGuild.Name
            };

            await db.Guilds.AddAsync(newGuild);
        }
        else
        {
            existingGuild.EmoteReactions = reactions;
            existingGuild.Name = discordGuild.Name;

            db.Entry(existingGuild).State = EntityState.Modified;
        }

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);
    }

    public async Task<bool?> ToggleCrownsAsync(NetCord.Gateway.Guild discordGuild, bool disabled)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstAsync(f => f.DiscordGuildId == discordGuild.Id);

        existingGuild.Name = discordGuild.Name;
        existingGuild.CrownsDisabled = disabled;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        return existingGuild.CrownsDisabled;
    }

    public async Task<bool> SetFmbotActivityThresholdDaysAsync(NetCord.Gateway.Guild discordGuild, int? days)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            return false;
        }

        existingGuild.Name = discordGuild.Name;
        existingGuild.ActivityThresholdDays = days;
        existingGuild.CrownsActivityThresholdDays = days;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        return true;
    }

    public async Task<bool> SetGuildActivityThresholdDaysAsync(NetCord.Gateway.Guild discordGuild, int? days)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            return false;
        }

        existingGuild.Name = discordGuild.Name;
        existingGuild.UserActivityThresholdDays = days;
        existingGuild.CrownsActivityThresholdDays = days;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        return true;
    }

    public async Task<bool> SetFeaturedModeAsync(NetCord.Gateway.Guild discordGuild, GuildFeaturedMode? featuredMode)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            return false;
        }

        existingGuild.Name = discordGuild.Name;
        existingGuild.FeaturedMode = featuredMode;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        return true;
    }

    public async Task<bool> SetFeaturedFrequencyAsync(NetCord.Gateway.Guild discordGuild, GuildFeaturedFrequency? featuredFrequency)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            return false;
        }

        existingGuild.Name = discordGuild.Name;
        existingGuild.FeaturedFrequency = featuredFrequency;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        return true;
    }

    public async Task<bool> SetCustomLogoAsync(NetCord.Gateway.Guild discordGuild, string customLogoUrl)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            return false;
        }

        existingGuild.Name = discordGuild.Name;
        existingGuild.CustomLogo = customLogoUrl;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        return true;
    }

    public async Task SendBotBrandingAuditLog(string description, string imageUrl = null, bool warning = false)
    {
        if (string.IsNullOrWhiteSpace(this._botSettings.Bot.SupporterAuditLogWebhookUrl))
        {
            return;
        }

        try
        {
            using var auditLogChannel =
                WebhookService.CreateWebhookClientFromUrl(this._botSettings.Bot.SupporterAuditLogWebhookUrl);

            var embed = new EmbedProperties()
                .WithDescription(description)
                .WithColor(warning ? DiscordConstants.WarningColorOrange : DiscordConstants.InformationColorBlue)
                .WithTimestamp(DateTimeOffset.UtcNow);

            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                embed.WithImage(imageUrl);
            }

            await auditLogChannel.ExecuteAsync(new WebhookMessageProperties { Embeds = [embed] });
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to send bot branding audit log");
        }
    }

    public async Task<bool> SetAutomaticCrownSeederAsync(NetCord.Gateway.Guild discordGuild,
        AutomaticCrownSeeder? schedule)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            return false;
        }

        existingGuild.Name = discordGuild.Name;
        existingGuild.AutomaticCrownSeeder = schedule;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        return true;
    }

    public async Task<bool> SetCrownActivityThresholdDaysAsync(NetCord.Gateway.Guild discordGuild, int? days)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            return false;
        }

        existingGuild.Name = discordGuild.Name;
        existingGuild.CrownsActivityThresholdDays = days;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        return true;
    }

    public async Task<bool> SetMinimumCrownPlaycountThresholdAsync(NetCord.Gateway.Guild discordGuild, int? playcount)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            return false;
        }

        existingGuild.Name = discordGuild.Name;
        existingGuild.CrownsMinimumPlaycountThreshold = playcount;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        return true;
    }

    public async Task<bool> BlockGuildUserAsync(NetCord.Gateway.Guild discordGuild, int userId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            return false;
        }

        var existingBlockedUser = await db.GuildBlockedUsers
            .AsQueryable()
            .FirstOrDefaultAsync(a => a.GuildId == existingGuild.GuildId && a.UserId == userId);

        if (existingBlockedUser != null)
        {
            existingBlockedUser.BlockedFromWhoKnows = true;
            existingBlockedUser.BlockedFromCrowns = true;

            db.Entry(existingBlockedUser).State = EntityState.Modified;

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);

            return true;
        }

        var blockedGuildUserToAdd = new GuildBlockedUser
        {
            GuildId = existingGuild.GuildId,
            UserId = userId,
            BlockedFromCrowns = true,
            BlockedFromWhoKnows = true
        };

        await db.GuildBlockedUsers.AddAsync(blockedGuildUserToAdd);
        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        db.Entry(blockedGuildUserToAdd).State = EntityState.Detached;

        Log.Information("Added blocked user {userId} to guild {guildName}", userId, discordGuild.Name);

        return true;
    }

    public async Task<bool> CrownBlockGuildUserAsync(NetCord.Gateway.Guild discordGuild, int userId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            return false;
        }

        var existingBlockedUser = await db.GuildBlockedUsers
            .AsQueryable()
            .FirstOrDefaultAsync(a => a.GuildId == existingGuild.GuildId && a.UserId == userId);

        if (existingBlockedUser != null)
        {
            existingBlockedUser.BlockedFromCrowns = true;

            db.Entry(existingBlockedUser).State = EntityState.Modified;

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);

            return true;
        }

        var blockedGuildUserToAdd = new GuildBlockedUser
        {
            GuildId = existingGuild.GuildId,
            UserId = userId,
            BlockedFromCrowns = true
        };

        await db.GuildBlockedUsers.AddAsync(blockedGuildUserToAdd);
        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        db.Entry(blockedGuildUserToAdd).State = EntityState.Detached;

        Log.Information("Added crownblocked user {userId} to guild {guildName}", userId, discordGuild.Name);

        return true;
    }

    public async Task<bool> UnBlockGuildUserAsync(NetCord.Gateway.Guild discordGuild, int userId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            return false;
        }

        var existingBlockedUser = await db.GuildBlockedUsers
            .AsQueryable()
            .FirstOrDefaultAsync(a => a.GuildId == existingGuild.GuildId && a.UserId == userId);

        if (existingBlockedUser == null)
        {
            return true;
        }

        if (existingBlockedUser.SelfBlockFromWhoKnows)
        {
            existingBlockedUser.BlockedFromCrowns = false;
            existingBlockedUser.BlockedFromWhoKnows = false;

            db.Entry(existingBlockedUser).State = EntityState.Modified;
        }
        else
        {
            db.GuildBlockedUsers.Remove(existingBlockedUser);
        }

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        Log.Information("Removed blocked user {userId} from guild {guildName}", userId, discordGuild.Name);

        return true;
    }

    public async Task<bool> SelfBlockGuildUserAsync(NetCord.Gateway.Guild discordGuild, int userId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            return false;
        }

        var existingBlockedUser = await db.GuildBlockedUsers
            .AsQueryable()
            .FirstOrDefaultAsync(a => a.GuildId == existingGuild.GuildId && a.UserId == userId);

        if (existingBlockedUser != null)
        {
            existingBlockedUser.SelfBlockFromWhoKnows = true;

            db.Entry(existingBlockedUser).State = EntityState.Modified;

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);

            return true;
        }

        var blockedGuildUserToAdd = new GuildBlockedUser
        {
            GuildId = existingGuild.GuildId,
            UserId = userId,
            SelfBlockFromWhoKnows = true
        };

        await db.GuildBlockedUsers.AddAsync(blockedGuildUserToAdd);
        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        db.Entry(blockedGuildUserToAdd).State = EntityState.Detached;

        Log.Information("Added self-blocked user {userId} to guild {guildName}", userId, discordGuild.Name);

        return true;
    }

    public async Task<bool> SelfUnblockGuildUserAsync(NetCord.Gateway.Guild discordGuild, int userId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            return false;
        }

        var existingBlockedUser = await db.GuildBlockedUsers
            .AsQueryable()
            .FirstOrDefaultAsync(a => a.GuildId == existingGuild.GuildId && a.UserId == userId);

        if (existingBlockedUser == null || !existingBlockedUser.SelfBlockFromWhoKnows)
        {
            return false;
        }

        existingBlockedUser.SelfBlockFromWhoKnows = false;

        if (existingBlockedUser.BlockedFromCrowns || existingBlockedUser.BlockedFromWhoKnows)
        {
            db.Entry(existingBlockedUser).State = EntityState.Modified;
        }
        else
        {
            db.GuildBlockedUsers.Remove(existingBlockedUser);
        }

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        Log.Information("Removed self-blocked user {userId} from guild {guildName}", userId, discordGuild.Name);

        return true;
    }

    public async Task<Persistence.Domain.Models.Guild> SetGuildPrefixAsync(NetCord.Gateway.Guild discordGuild, string prefix)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            var newGuild = new Persistence.Domain.Models.Guild
            {
                DiscordGuildId = discordGuild.Id,
                Name = discordGuild.Name,
                Prefix = prefix
            };

            await db.Guilds.AddAsync(newGuild);

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);

            return newGuild;
        }
        else
        {
            existingGuild.Prefix = prefix;
            existingGuild.Name = discordGuild.Name;

            db.Entry(existingGuild).State = EntityState.Modified;

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);

            return existingGuild;
        }
    }

    public async Task SetGuildWhoKnowsWhitelistRoleAsync(NetCord.Gateway.Guild discordGuild, ulong? roleId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            var newGuild = new Persistence.Domain.Models.Guild
            {
                DiscordGuildId = discordGuild.Id,
                Name = discordGuild.Name,
                WhoKnowsWhitelistRoleId = roleId,
            };

            await db.Guilds.AddAsync(newGuild);
        }
        else
        {
            existingGuild.WhoKnowsWhitelistRoleId = roleId;
            existingGuild.Name = discordGuild.Name;

            db.Entry(existingGuild).State = EntityState.Modified;
        }

        await db.SaveChangesAsync();
        await RemoveGuildFromCache(discordGuild.Id);
    }

    public async Task<string[]> AddGuildDisabledCommandAsync(NetCord.Gateway.Guild discordGuild, string command)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            var newGuild = new Persistence.Domain.Models.Guild
            {
                DiscordGuildId = discordGuild.Id,
                Name = discordGuild.Name,
                DisabledCommands = [command]
            };

            await db.Guilds.AddAsync(newGuild);

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);

            return newGuild.DisabledCommands;
        }

        if (existingGuild.DisabledCommands is { Length: > 0 })
        {
            var newDisabledCommands = existingGuild.DisabledCommands;
            Array.Resize(ref newDisabledCommands, newDisabledCommands.Length + 1);
            newDisabledCommands[^1] = command;
            existingGuild.DisabledCommands = newDisabledCommands;
        }
        else
        {
            existingGuild.DisabledCommands = [command];
        }

        existingGuild.Name = discordGuild.Name;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        return existingGuild.DisabledCommands;
    }

    public async Task<string[]> RemoveGuildDisabledCommandsAsync(NetCord.Gateway.Guild discordGuild, IReadOnlyCollection<string> commands)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        existingGuild.DisabledCommands = existingGuild.DisabledCommands
            .Where(w => !commands.Any(c => string.Equals(w, c, StringComparison.OrdinalIgnoreCase))).ToArray();
        existingGuild.Name = discordGuild.Name;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        return existingGuild.DisabledCommands;
    }

    public async Task<string[]> ClearGuildDisabledCommandAsync(NetCord.Gateway.Guild discordGuild)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        existingGuild.DisabledCommands = null;
        existingGuild.Name = discordGuild.Name;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        return existingGuild.DisabledCommands;
    }

    public async Task<List<string>> GetDisabledCommandsForChannel(ulong discordChannelId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingChannel = await db.Channels
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannelId);

        return existingChannel?.DisabledCommands?.ToList();
    }

    public async Task<Channel> GetChannel(ulong discordChannelId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingChannel = await db.Channels
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannelId);

        return existingChannel;
    }

    public async Task DisableChannelCommandsAsync(IGuildChannel discordChannel, int guildId, List<string> commands,
        ulong discordGuildId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingChannel = await db.Channels
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannel.Id);

        if (existingChannel == null)
        {
            var newChannel = new Channel
            {
                DiscordChannelId = discordChannel.Id,
                Name = discordChannel.Name,
                GuildId = guildId,
                DisabledCommands = commands.ToArray()
            };

            await db.Channels.AddAsync(newChannel);
            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuildId);

            return;
        }

        if (existingChannel.DisabledCommands != null && existingChannel.DisabledCommands.Length > 0)
        {
            var newDisabledCommands = existingChannel.DisabledCommands;
            Array.Resize(ref newDisabledCommands, newDisabledCommands.Length + commands.Count);
            for (var index = 0; index < commands.Count; index++)
            {
                var command = commands[index];
                newDisabledCommands[^(index + 1)] = command;
            }

            existingChannel.DisabledCommands = newDisabledCommands;
        }
        else
        {
            existingChannel.DisabledCommands = commands.ToArray();
        }

        existingChannel.Name = existingChannel.Name;

        db.Entry(existingChannel).State = EntityState.Modified;

        await RemoveGuildFromCache(discordGuildId);

        await db.SaveChangesAsync();
    }

    public async Task SetChannelEmbedType(IGuildChannel discordChannel, int guildId, FmEmbedType? embedType,
        ulong discordGuildId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingChannel = await db.Channels
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannel.Id);

        if (existingChannel == null)
        {
            var newChannel = new Channel
            {
                DiscordChannelId = discordChannel.Id,
                Name = discordChannel.Name,
                GuildId = guildId,
                FmEmbedType = embedType
            };

            await db.Channels.AddAsync(newChannel);
            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuildId);

            return;
        }

        existingChannel.Name = existingChannel.Name;
        existingChannel.FmEmbedType = embedType;

        db.Entry(existingChannel).State = EntityState.Modified;

        await RemoveGuildFromCache(discordGuildId);

        await db.SaveChangesAsync();
    }

    public async Task<string[]> EnableChannelCommandsAsync(IGuildChannel discordChannel, List<string> commands,
        ulong discordGuildId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingChannel = await db.Channels
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannel.Id);

        existingChannel.DisabledCommands = existingChannel.DisabledCommands
            .Where(w => !commands.Any(c => string.Equals(w, c, StringComparison.OrdinalIgnoreCase))).ToArray();
        existingChannel.Name = discordChannel.Name;

        db.Entry(existingChannel).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuildId);

        return existingChannel.DisabledCommands;
    }

    public async Task<string[]> ClearDisabledChannelCommandsAsync(IGuildChannel discordChannel, ulong discordGuildId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingChannel = await db.Channels
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannel.Id);

        existingChannel.DisabledCommands = null;
        existingChannel.Name = discordChannel.Name;

        db.Entry(existingChannel).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuildId);

        return existingChannel.DisabledCommands;
    }

    public async Task DisableChannelAsync(IGuildChannel discordChannel, int guildId, ulong discordGuildId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingChannel = await db.Channels
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannel.Id);

        if (existingChannel == null)
        {
            var newChannel = new Channel
            {
                DiscordChannelId = discordChannel.Id,
                Name = discordChannel.Name,
                GuildId = guildId,
                BotDisabled = true
            };

            await db.Channels.AddAsync(newChannel);
            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuildId);

            return;
        }

        existingChannel.BotDisabled = true;
        existingChannel.Name = existingChannel.Name;

        db.Entry(existingChannel).State = EntityState.Modified;

        await RemoveGuildFromCache(discordGuildId);

        await db.SaveChangesAsync();
    }

    public async Task EnableChannelAsync(IGuildChannel discordChannel, ulong discordGuildId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingChannel = await db.Channels
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannel.Id);

        if (existingChannel == null)
        {
            return;
        }

        existingChannel.BotDisabled = null;
        existingChannel.Name = existingChannel.Name;

        db.Entry(existingChannel).State = EntityState.Modified;

        await RemoveGuildFromCache(discordGuildId);

        await db.SaveChangesAsync();
    }

    public async Task<int?> GetChannelCooldown(ulong? discordChannelId)
    {
        if (!discordChannelId.HasValue)
        {
            return null;
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        var existingChannel = await db.Channels
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannelId);

        return existingChannel?.FmCooldown;
    }

    public async Task<int?> SetChannelCooldownAsync(IGuildChannel discordChannel, int guildId, int? cooldown,
        ulong discordGuildId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingChannel = await db.Channels
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannel.Id);

        if (existingChannel == null)
        {
            var newChannel = new Channel
            {
                DiscordChannelId = discordChannel.Id,
                Name = discordChannel.Name,
                GuildId = guildId,
                FmCooldown = cooldown
            };

            await db.Channels.AddAsync(newChannel);

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuildId);

            return newChannel.FmCooldown;
        }

        existingChannel.FmCooldown = cooldown;

        existingChannel.Name = existingChannel.Name;

        db.Entry(existingChannel).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuildId);

        return existingChannel.FmCooldown;
    }

    public async Task<DateTime?> GetGuildIndexTimestampAsync(NetCord.Gateway.Guild discordGuild)
    {
        var discordGuildIdCacheKey = CacheKeyForGuild(discordGuild.Id);

        if (cache.TryGetValue(discordGuildIdCacheKey, out Persistence.Domain.Models.Guild guild))
        {
            return guild?.LastIndexed;
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        guild = await db.Guilds
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (guild?.LastIndexed != null && guild.LastIndexed > DateTime.UtcNow.AddDays(-120))
        {
            cache.Set(discordGuildIdCacheKey, guild, TimeSpan.FromHours(1));
        }

        return guild?.LastIndexed;
    }

    public async Task UpdateGuildIndexTimestampAsync(NetCord.Gateway.Guild discordGuild, DateTime? timestamp = null)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            var newGuild = new Persistence.Domain.Models.Guild
            {
                DiscordGuildId = discordGuild.Id,
                Name = discordGuild.Name,
                LastIndexed = timestamp ?? DateTime.UtcNow
            };

            await db.Guilds.AddAsync(newGuild);

            await RemoveGuildFromCache(discordGuild.Id);

            await db.SaveChangesAsync();
        }
        else
        {
            existingGuild.LastIndexed = timestamp ?? DateTime.UtcNow;
            existingGuild.Name = discordGuild.Name;

            db.Entry(existingGuild).State = EntityState.Modified;

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);
        }
    }

    public static bool ValidateReactions(IEnumerable<string> emoteString)
    {
        foreach (var emote in emoteString)
        {
            if (emote.Length is 1 or 2 or 3)
            {
                try
                {
                    var unused = EmojiProperties.Standard(emote);
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                try
                {
                    var unused = new ReactionEmojiProperties(emote);
                }
                catch
                {
                    return false;
                }
            }
        }

        return true;
    }

    public async Task AddGuildReactionsAsync(RestMessage message, NetCord.Gateway.Guild guild, bool partyingFace = false)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var dbGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

        if (dbGuild?.EmoteReactions == null || !dbGuild.EmoteReactions.Any())
        {
            return;
        }

        await AddReactionsAsync(message, dbGuild.EmoteReactions);

        if (partyingFace)
        {
            var emote = new ReactionEmojiProperties("🥳");
            await message.AddReactionAsync(emote);
        }
    }

    public async Task<string[]> GetGuildReactions(ulong discordGuildId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var dbGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);

        if (dbGuild?.EmoteReactions == null || !dbGuild.EmoteReactions.Any())
        {
            return null;
        }

        return dbGuild.EmoteReactions;
    }

    public static async Task AddReactionsAsync(RestMessage message, IEnumerable<string> reactions)
    {
        foreach (var emoteString in reactions)
        {
            await message.AddReactionAsync(ToReactionEmoji(emoteString));
        }
    }

    public static async Task AddReactionsAsync(RestClient client, ulong channelId, ulong messageId, IEnumerable<string> reactions)
    {
        foreach (var emoteString in reactions)
        {
            await client.AddMessageReactionAsync(channelId, messageId, ToReactionEmoji(emoteString));
        }
    }

    private static ReactionEmojiProperties ToReactionEmoji(string emoteString)
    {
        // Custom emote format: <:name:id> or <a:name:id>
        var parts = emoteString.Trim('<', '>').Split(':');
        if (parts.Length >= 2 && ulong.TryParse(parts[^1], out var id))
        {
            return new ReactionEmojiProperties(parts[^2], id);
        }

        return new ReactionEmojiProperties(emoteString);
    }


    public async Task RemoveGuildAsync(ulong discordGuildId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var guild = await db.Guilds.AsQueryable().FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);

        if (guild != null)
        {
            db.Guilds.Remove(guild);
            await db.SaveChangesAsync();
            await RemoveGuildFromCache(discordGuildId);
        }
    }

    public async Task<int> GetTotalGuildCountAsync()
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        return await db.Guilds
            .AsNoTracking()
            .CountAsync();
    }

    public async Task UpdateGuildUserLastMessageDate(NetCord.GuildUser discordGuildUser, int userId, int guildId)
    {
        try
        {
            const string sql = "UPDATE guild_users " +
                               "SET user_name = @UserName, last_message = @LastMessage " +
                               "WHERE guild_id = @GuildId AND user_id = @UserId ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync(sql, new
            {
                UserName = discordGuildUser.GetDisplayName(),
                LastMessage = DateTime.UtcNow,
                GuildId = guildId,
                UserId = userId
            });
        }
        catch (Exception e)
        {
            Log.Error(e, $"Exception in {nameof(UpdateGuildUserLastMessageDate)}");
        }
    }

    public async Task SetGuildFlags(int guildId, GuildFlags flags)
    {
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync();
            var guild = await db.Guilds.FirstOrDefaultAsync(g => g.GuildId == guildId);

            if (guild != null)
            {
                guild.GuildFlags = flags;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, $"Exception in {nameof(SetGuildFlags)}");
        }
    }
}
