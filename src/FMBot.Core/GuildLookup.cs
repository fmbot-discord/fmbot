using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Core;

public class GuildLookup
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly IMemoryCache _cache;
    private readonly BotSettings _botSettings;

    public GuildLookup(IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache,
        IOptions<BotSettings> botSettings)
    {
        this._contextFactory = contextFactory;
        this._cache = cache;
        this._botSettings = botSettings.Value;
    }

    public static string CacheKeyForGuild(ulong discordGuildId)
    {
        return $"guild-{discordGuildId}";
    }

    public static string CacheKeyForGuildUsers(ulong discordGuildId)
    {
        return $"guild-users-{discordGuildId}";
    }

    public async Task<Guild> GetGuild(ulong discordGuildId)
    {
        var cacheKey = CacheKeyForGuild(discordGuildId);
        if (this._cache.TryGetValue(cacheKey, out Guild guild))
        {
            return guild;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        guild = await db.Guilds
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);

        if (guild != null)
        {
            this._cache.Set(cacheKey, guild, TimeSpan.FromMinutes(5));
        }

        return guild;
    }

    public void RemoveGuildFromCache(ulong discordGuildId)
    {
        this._cache.Remove(CacheKeyForGuild(discordGuildId));
    }

    public async Task<IDictionary<int, FullGuildUser>> GetGuildUsers(ulong discordGuildId)
    {
        var cacheKey = CacheKeyForGuildUsers(discordGuildId);
        return await this._cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(12);

            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            return await GuildRepository.GetGuildUsers(discordGuildId, connection);
        });
    }

    public async Task<List<Guild>> GetPremiumGuilds()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var expiryCutoff = DateTime.UtcNow.AddHours(-6);
        var paidGuildIds = await db.PremiumGuildSubscriptions
            .AsNoTracking()
            .Where(w => !w.EntitlementDeleted &&
                        (w.DateEnding == null || w.DateEnding > expiryCutoff))
            .Select(s => s.DiscordGuildId)
            .ToListAsync();

        return await db.Guilds
            .AsNoTracking()
            .Where(w => (w.GuildFlags.HasValue && (w.GuildFlags.Value.HasFlag(GuildFlags.LegacyWhoKnowsWhitelist) ||
                                                   w.GuildFlags.Value.HasFlag(GuildFlags.PremiumServerTester) ||
                                                   w.GuildFlags.Value.HasFlag(GuildFlags.StaffCommandsAvailable))) ||
                        paidGuildIds.Contains(w.DiscordGuildId))
            .ToListAsync();
    }
}
