using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

namespace FMBot.Bot.Services.Guild;

public class GuildService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly IMemoryCache _cache;
    private readonly BotSettings _botSettings;

    public GuildService(IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache, IOptions<BotSettings> botSettings)
    {
        this._contextFactory = contextFactory;
        this._cache = cache;
        this._botSettings = botSettings.Value;
    }

    // Message is in dm?
    public bool CheckIfDM(ICommandContext context)
    {
        return context.Guild == null;
    }

    public async Task<Persistence.Domain.Models.Guild> GetGuildAsync(ulong? discordGuildId)
    {
        if (discordGuildId == null)
        {
            return null;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);
    }

    public async Task<Persistence.Domain.Models.Guild> GetFullGuildAsync(ulong? discordGuildId = null, bool filterBots = true)
    {
        if (discordGuildId == null)
        {
            return null;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Guilds
            //.AsNoTracking()
            .Include(i => i.GuildBlockedUsers)
            .ThenInclude(t => t.User)
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

        await using var db = await this._contextFactory.CreateDbContextAsync();
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

        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Guilds
            .AsNoTracking()
            .Include(i => i.GuildBlockedUsers)
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);
    }

    public async Task<Persistence.Domain.Models.Guild> GetGuildWithGuildUsers(ulong? discordGuildId = null)
    {
        if (discordGuildId == null)
        {
            return null;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Guilds
            .AsNoTracking()
            .Include(i => i.GuildBlockedUsers)
            .Include(i => i.GuildUsers.Where(w => w.Bot != true))
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);
    }

    public async Task<IDictionary<int, FullGuildUser>> GetGuildUsers(ulong? discordGuildId = null)
    {
        if (discordGuildId == null)
        {
            return new Dictionary<int, FullGuildUser>();
        }

        const string sql = "SELECT gu.user_id, " +
                           "gu.user_name, " +
                           "gu.bot, " +
                           "gu.last_message, " +
                           "gu.roles AS dto_roles, " +
                           "u.user_name_last_fm, " +
                           "u.discord_user_id, " +
                           "u.last_used, " +
                           "COALESCE(gbu.blocked_from_crowns, false) as blocked_from_crowns, " +
                           "COALESCE(gbu.blocked_from_who_knows, false) as blocked_from_who_knows " +
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

        return result.ToDictionary(d => d.UserId, d => d);
    }

    public async Task<List<Persistence.Domain.Models.Guild>> GetPremiumGuilds()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Guilds
            .AsNoTracking()
            .Where(w => w.SpecialGuild == true ||
                        (w.GuildFlags.HasValue && w.GuildFlags.Value.HasFlag(GuildFlags.LegacyWhoKnowsWhitelist)))
            .ToListAsync();
    }

    public async Task RefreshPremiumGuilds()
    {
        foreach (var guild in PublicProperties.PremiumServers)
        {
            PublicProperties.PremiumServers.TryRemove(guild);
        }

        var premiumServers = await GetPremiumGuilds();
        foreach (var guild in premiumServers)
        {
            PublicProperties.PremiumServers.TryAdd(guild.DiscordGuildId, guild.GuildId);
        }
    }

    private Task RemoveGuildFromCache(ulong discordGuildId)
    {
        this._cache.Remove(CacheKeyForGuild(discordGuildId));
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
            RequesterFiltered = false,
            Roles = roles
        };

        if (guild.ActivityThresholdDays.HasValue)
        {
            var preFilterCount = users.Count;

            users = users.Where(w =>
                    w.Value.LastUsed != null &&
                    w.Value.LastUsed >= DateTime.UtcNow.AddDays(-guild.ActivityThresholdDays.Value))
                .ToDictionary(i => i.Key, i => i.Value);

            stats.ActivityThresholdFiltered = preFilterCount - users.Count;
        }
        if (guild.UserActivityThresholdDays.HasValue)
        {
            var preFilterCount = users.Count;

            users = users.Where(w =>
                    w.Value.LastMessage != null &&
                    w.Value.LastMessage >= DateTime.UtcNow.AddDays(-guild.UserActivityThresholdDays.Value))
                .ToDictionary(i => i.Key, i => i.Value);

            stats.GuildActivityThresholdFiltered = preFilterCount - users.Count;
        }
        if (guild.GuildBlockedUsers != null && guild.GuildBlockedUsers.Any(a => a.BlockedFromWhoKnows))
        {
            var preFilterCount = users.Count;

            var usersToFilter = guild.GuildBlockedUsers
                .DistinctBy(d => d.UserId)
                .Where(w => w.BlockedFromWhoKnows)
                .Select(s => s.UserId)
                .ToHashSet();

            users = users
                .Where(w => !usersToFilter.Contains(w.Value.UserId))
                .ToDictionary(i => i.Key, i => i.Value);

            stats.BlockedFiltered = preFilterCount - users.Count;
        }
        if (guild.AllowedRoles != null && guild.AllowedRoles.Any())
        {
            var preFilterCount = users.Count;

            users = users
                .Where(w => w.Value.Roles != null && guild.AllowedRoles.Any(a => w.Value.Roles.Contains(a)))
                .ToDictionary(i => i.Key, i => i.Value);

            stats.AllowedRolesFiltered = preFilterCount - users.Count;
        }
        if (guild.BlockedRoles != null && guild.BlockedRoles.Any())
        {
            var preFilterCount = users.Count;

            users = users
                .Where(w => w.Value.Roles != null && !guild.BlockedRoles.Any(a => w.Value.Roles.Contains(a)))
                .ToDictionary(i => i.Key, i => i.Value); 

            stats.BlockedRolesFiltered = preFilterCount - users.Count;
        }
        if (roles != null && roles.Any())
        {
            var preFilterCount = users.Count;

            users = users
                .Where(w => w.Value.Roles != null && roles.Any(a => w.Value.Roles.Contains(a)))
                .ToDictionary(i => i.Key, i => i.Value); ;

            stats.ManualRoleFilter = preFilterCount - users.Count;
        }

        stats.EndCount = users.Count;

        return (stats, users);
    }

    public static async Task<GuildPermissions> GetGuildPermissionsAsync(ICommandContext context)
    {
        var socketCommandContext = (SocketCommandContext)context;
        var guildUser = await context.Guild.GetUserAsync(socketCommandContext.Client.CurrentUser.Id);
        return guildUser.GuildPermissions;
    }

    public static async Task<GuildPermissions> GetGuildPermissionsAsync(IInteractionContext context)
    {
        var socketCommandContext = (ShardedInteractionContext)context;
        var guildUser = await context.Guild.GetUserAsync(socketCommandContext.Client.CurrentUser.Id);
        return guildUser.GuildPermissions;
    }

    public static GuildUser GetUserFromGuild(Persistence.Domain.Models.Guild guild, int userId)
    {
        return guild.GuildUsers
            .FirstOrDefault(f => f.UserId == userId);
    }

    public async Task StaleGuildLastIndexedAsync(IGuild discordGuild)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstAsync(f => f.DiscordGuildId == discordGuild.Id);

        existingGuild.Name = discordGuild.Name;
        existingGuild.LastIndexed = null;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);
    }

    public async Task ChangeGuildAllowedRoles(IGuild discordGuild, ulong[] allowedRoles)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstAsync(f => f.DiscordGuildId == discordGuild.Id);

        existingGuild.Name = discordGuild.Name;
        existingGuild.AllowedRoles = allowedRoles;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);
    }

    public async Task ChangeGuildBlockedRoles(IGuild discordGuild, ulong[] blockedRoles)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstAsync(f => f.DiscordGuildId == discordGuild.Id);

        existingGuild.Name = discordGuild.Name;
        existingGuild.BlockedRoles = blockedRoles;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);
    }

    public async Task ChangeGuildBotManagementRoles(IGuild discordGuild, ulong[] botManagementRoles)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstAsync(f => f.DiscordGuildId == discordGuild.Id);

        existingGuild.Name = discordGuild.Name;
        existingGuild.BotManagementRoles = botManagementRoles;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);
    }

    public async Task ChangeGuildSettingAsync(IGuild discordGuild, FmEmbedType? embedType)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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

    public async Task SetGuildReactionsAsync(IGuild discordGuild, string[] reactions)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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

    public async Task<bool?> ToggleCrownsAsync(IGuild discordGuild)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstAsync(f => f.DiscordGuildId == discordGuild.Id);

        existingGuild.Name = discordGuild.Name;

        if (existingGuild.CrownsDisabled == true)
        {
            existingGuild.CrownsDisabled = false;
        }
        else
        {
            existingGuild.CrownsDisabled = true;
        }

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        return existingGuild.CrownsDisabled;
    }

    public async Task<bool> SetFmbotActivityThresholdDaysAsync(IGuild discordGuild, int? days)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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

    public async Task<bool> SetGuildActivityThresholdDaysAsync(IGuild discordGuild, int? days)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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

    public async Task<bool> SetCrownActivityThresholdDaysAsync(IGuild discordGuild, int? days)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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

    public async Task<bool> SetMinimumCrownPlaycountThresholdAsync(IGuild discordGuild, int? playcount)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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

    public async Task<bool> BlockGuildUserAsync(IGuild discordGuild, int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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

    public async Task<bool> CrownBlockGuildUserAsync(IGuild discordGuild, int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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

    public async Task<bool> UnBlockGuildUserAsync(IGuild discordGuild, int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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

        db.GuildBlockedUsers.Remove(existingBlockedUser);
        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        Log.Information("Removed blocked user {userId} from guild {guildName}", userId, discordGuild.Name);

        return true;
    }

    public async Task<Persistence.Domain.Models.Guild> SetGuildPrefixAsync(IGuild discordGuild, string prefix)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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

    public async Task SetGuildWhoKnowsWhitelistRoleAsync(IGuild discordGuild, ulong? roleId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);
        }
        else
        {
            existingGuild.WhoKnowsWhitelistRoleId = roleId;
            existingGuild.Name = discordGuild.Name;

            db.Entry(existingGuild).State = EntityState.Modified;

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);
        }
    }

    public async Task<string[]> AddGuildDisabledCommandAsync(IGuild discordGuild, string command)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            var newGuild = new Persistence.Domain.Models.Guild
            {
                DiscordGuildId = discordGuild.Id,
                Name = discordGuild.Name,
                DisabledCommands = new[] { command }
            };

            await db.Guilds.AddAsync(newGuild);

            await db.SaveChangesAsync();

            await RemoveGuildFromCache(discordGuild.Id);

            return newGuild.DisabledCommands;
        }

        if (existingGuild.DisabledCommands != null && existingGuild.DisabledCommands.Length > 0)
        {
            var newDisabledCommands = existingGuild.DisabledCommands;
            Array.Resize(ref newDisabledCommands, newDisabledCommands.Length + 1);
            newDisabledCommands[^1] = command;
            existingGuild.DisabledCommands = newDisabledCommands;
        }
        else
        {
            existingGuild.DisabledCommands = new[] { command };
        }

        existingGuild.Name = discordGuild.Name;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        return existingGuild.DisabledCommands;
    }

    public async Task<string[]> RemoveGuildDisabledCommandAsync(IGuild discordGuild, string command)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        existingGuild.DisabledCommands = existingGuild.DisabledCommands.Where(w => !w.Contains(command)).ToArray();
        existingGuild.Name = discordGuild.Name;

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuild.Id);

        return existingGuild.DisabledCommands;
    }

    public async Task<string[]> ClearGuildDisabledCommandAsync(IGuild discordGuild)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingChannel = await db.Channels
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannelId);

        return existingChannel?.DisabledCommands?.ToList();
    }

    public async Task<Channel> GetChannel(ulong discordChannelId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingChannel = await db.Channels
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannelId);

        return existingChannel;
    }

    public async Task DisableChannelCommandsAsync(IChannel discordChannel, int guildId, List<string> commands, ulong discordGuildId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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

    public async Task SetChannelEmbedType(IChannel discordChannel, int guildId, FmEmbedType? embedType, ulong discordGuildId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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

    public async Task<string[]> EnableChannelCommandsAsync(IChannel discordChannel, List<string> commands, ulong discordGuildId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingChannel = await db.Channels
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannel.Id);

        existingChannel.DisabledCommands = existingChannel.DisabledCommands.Where(w => !commands.Contains(w)).ToArray();
        existingChannel.Name = discordChannel.Name;

        db.Entry(existingChannel).State = EntityState.Modified;

        await db.SaveChangesAsync();

        await RemoveGuildFromCache(discordGuildId);

        return existingChannel.DisabledCommands;
    }

    public async Task<string[]> ClearDisabledChannelCommandsAsync(IChannel discordChannel, ulong discordGuildId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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

    public async Task DisableChannelAsync(IChannel discordChannel, int guildId, ulong discordGuildId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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

    public async Task EnableChannelAsync(IChannel discordChannel, ulong discordGuildId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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
        
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingChannel = await db.Channels
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordChannelId == discordChannelId);

        return existingChannel?.FmCooldown;
    }

    public async Task<int?> SetChannelCooldownAsync(IChannel discordChannel, int guildId, int? cooldown, ulong discordGuildId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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

    public async Task<DateTime?> GetGuildIndexTimestampAsync(IGuild discordGuild)
    {
        var discordGuildIdCacheKey = CacheKeyForGuild(discordGuild.Id);

        if (this._cache.TryGetValue(discordGuildIdCacheKey, out Persistence.Domain.Models.Guild guild))
        {
            return guild?.LastIndexed;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        guild = await db.Guilds
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (guild?.LastIndexed != null && guild.LastIndexed > DateTime.UtcNow.AddDays(-120))
        {
            this._cache.Set(discordGuildIdCacheKey, guild, TimeSpan.FromHours(1));
        }

        return guild?.LastIndexed;
    }

    public async Task UpdateGuildIndexTimestampAsync(IGuild discordGuild, DateTime? timestamp = null)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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
                    var unused = new Emoji(emote);
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
                    Emote.Parse(emote);
                }
                catch
                {
                    return false;
                }
            }
        }

        return true;
    }

    public async Task AddGuildReactionsAsync(IUserMessage message, IGuild guild, bool partyingFace = false)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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
            var emote = new Emoji("🥳");
            await message.AddReactionAsync(emote);
        }
    }

    public static async Task AddReactionsAsync(IUserMessage message, IEnumerable<string> reactions)
    {
        foreach (var emoteString in reactions)
        {
            if (emoteString.Length is 1 or 2 or 3)
            {
                var emote = new Emoji(emoteString);
                await message.AddReactionAsync(emote);
            }
            else
            {
                var emote = Emote.Parse(emoteString);
                await message.AddReactionAsync(emote);
            }
        }
    }


    public async Task RemoveGuildAsync(ulong discordGuildId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
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
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Guilds
            .AsNoTracking()
            .CountAsync();
    }

    public async Task UpdateGuildUserLastMessageDate(IGuildUser discordGuildUser, int userId, int guildId)
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
                UserName = discordGuildUser.DisplayName,
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
}
