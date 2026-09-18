using System;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FMBot.Core;

public class UserLookup
{
    private readonly IMemoryCache _cache;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

    public UserLookup(IMemoryCache cache, IDbContextFactory<FMBotDbContext> contextFactory)
    {
        this._cache = cache;
        this._contextFactory = contextFactory;
    }

    public static string UserInternalIdCacheKey(int userId)
    {
        return $"user-i{userId}";
    }

    public static string UserDiscordIdCacheKey(ulong discordUserId)
    {
        return $"user-{discordUserId}";
    }

    public static string UserLastFmCacheKey(string userNameLastFm)
    {
        return $"user-{userNameLastFm.ToLower()}";
    }

    public async Task<User> GetMostRecentUserForDiscordIdAsync(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsNoTracking()
            .Where(w => w.DiscordUserId == discordUserId)
            .OrderByDescending(o => o.LastUsed)
            .FirstOrDefaultAsync();
    }

    public async Task SetUserLastUsedAsync(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        await db.Users
            .Where(w => w.UserId == userId)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(p => p.LastUsed, DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)));
    }

    public async Task<User> GetUserForIdAsync(int userId)
    {
        var userIdCacheKey = UserInternalIdCacheKey(userId);
        if (this._cache.TryGetValue(userIdCacheKey, out User user))
        {
            return user;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.UserId == userId);
    }
}
