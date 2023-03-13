using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Swan;
using System.Linq;

namespace FMBot.Bot.Services;

public class AdminService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly IMemoryCache _cache;

    public AdminService(IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache)
    {
        this._contextFactory = contextFactory;
        this._cache = cache;
    }

    public async Task<bool> HasCommandAccessAsync(IUser discordUser, UserType userType)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

        if (user == null)
        {
            return false;
        }

        switch (user.UserType)
        {
            case UserType.Admin:
                switch (userType)
                {
                    case UserType.User:
                        return true;
                    case UserType.Admin:
                        return true;
                    default:
                        return false;
                }
            case UserType.Owner:
                switch (userType)
                {
                    case UserType.User:
                        return true;
                    case UserType.Admin:
                        return true;
                    default:
                        return true;
                }
            default:
                switch (userType)
                {
                    case UserType.User:
                        return true;
                    default:
                        return false;
                }
        }
    }

    public async Task<bool> SetUserTypeAsync(ulong discordUserId, UserType userType)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

        if (user == null)
        {
            return false;
        }

        user.UserType = userType;

        db.Entry(user).State = EntityState.Modified;

        await db.SaveChangesAsync();

        return true;
    }

    public async Task<bool> AddUserToBlocklistAsync(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

        if (user == null || user.UserType == UserType.Owner)
        {
            return false;
        }

        user.Blocked = true;

        db.Entry(user).State = EntityState.Modified;

        await db.SaveChangesAsync();

        this._cache.Remove("blocked-users");

        return true;
    }

    public async Task<bool> RemoveUserFromBlocklistAsync(ulong discordUserId)
    {
        await using var db = this._contextFactory.CreateDbContext();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

        if (user == null || user.UserType == UserType.Owner)
        {
            return false;
        }

        user.Blocked = false;

        db.Entry(user).State = EntityState.Modified;

        this._cache.Remove("blocked-users");

        await db.SaveChangesAsync();

        return true;
    }

    public async Task<BottedUser> GetBottedUserAsync(string lastFmUserName)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.BottedUsers
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.UserNameLastFM.ToLower() == lastFmUserName.ToLower());
    }

    public async Task<List<User>> GetUsersWithLfmUsernameAsync(string lastFmUserName)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsQueryable()
            .Where(w => w.UserNameLastFM.ToLower() == lastFmUserName.ToLower())
            .ToListAsync();
    }

    public async Task<bool> DisableBottedUserBanAsync(string lastFmUserName)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var bottedUser = await db.BottedUsers
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.UserNameLastFM.ToLower() == lastFmUserName.ToLower());

        if (bottedUser == null)
        {
            return false;
        }

        bottedUser.BanActive = false;

        var stringToAdd = $"*[Unbanned <t:{DateTime.UtcNow.ToUnixEpochDate()}:F>]*";

        if (bottedUser.Notes == null)
        {
            bottedUser.Notes = stringToAdd;
        }
        else
        {
            bottedUser.Notes += $"\n{stringToAdd}";
        }

        db.Entry(bottedUser).State = EntityState.Modified;

        await db.SaveChangesAsync();

        return true;
    }

    public async Task<bool> EnableBottedUserBanAsync(string lastFmUserName, string reason, DateTime? registeredDate = null)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var bottedUser = await db.BottedUsers
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.UserNameLastFM.ToLower() == lastFmUserName.ToLower());

        if (bottedUser == null)
        {
            return false;
        }

        bottedUser.BanActive = true;

        if (registeredDate.HasValue)
        {
            bottedUser.LastFmRegistered = DateTime.SpecifyKind(registeredDate.Value, DateTimeKind.Utc);
        }

        var stringToAdd = $"*[Re-banned <t:{DateTime.UtcNow.ToUnixEpochDate()}:F>]*";

        if (bottedUser.Notes == null)
        {
            if (reason != null)
            {
                bottedUser.Notes = stringToAdd;
                bottedUser.Notes = $"\n{reason}";
            }
            else
            {
                bottedUser.Notes = stringToAdd;
            }
        }
        else
        {
            bottedUser.Notes += $"\n\n{stringToAdd}";
            if (reason != null)
            {
                bottedUser.Notes += $"\n{reason}";
            }
        }

        db.Entry(bottedUser).State = EntityState.Modified;

        await db.SaveChangesAsync();

        return true;
    }

    public async Task<bool> AddBottedUserAsync(string lastFmUserName, string reason, DateTime? registeredDate = null)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var bottedUser = await db.BottedUsers
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.UserNameLastFM.ToLower() == lastFmUserName.ToLower());

        if (bottedUser != null)
        {
            return false;
        }

        if (registeredDate.HasValue)
        {
            registeredDate = DateTime.SpecifyKind(registeredDate.Value, DateTimeKind.Utc);
        }

        var newBottedUser = new BottedUser
        {
            UserNameLastFM = lastFmUserName,
            BanActive = true,
            Notes = reason,
            LastFmRegistered = registeredDate
        };

        await db.BottedUsers.AddAsync(newBottedUser);

        await db.SaveChangesAsync();

        return true;
    }

    public string FormatBytes(long bytes)
    {
        string[] suffix = { "B", "KB", "MB", "GB", "TB" };
        int i;
        double dblSByte = bytes;
        for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblSByte = bytes / 1024.0;
        }

        return string.Format("{0:0.##} {1}", dblSByte, suffix[i]);
    }

    public async Task<bool?> ToggleSpecialGuildAsync(IGuild guild)
    {
        await using var db = this._contextFactory.CreateDbContext();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstAsync(f => f.DiscordGuildId == guild.Id);

        existingGuild.Name = guild.Name;

        if (existingGuild.SpecialGuild == true)
        {
            existingGuild.SpecialGuild = false;
        }
        else
        {
            existingGuild.SpecialGuild = true;
        }

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        return existingGuild.SpecialGuild;
    }

    public async Task FixValues()
    {
        await using var db = this._contextFactory.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_catalog.setval(pg_get_serial_sequence('users', 'user_id'), (SELECT MAX(user_id) FROM users)+1);");
        Console.WriteLine("User key value has been fixed.");

        await db.Database.ExecuteSqlRawAsync("SELECT pg_catalog.setval(pg_get_serial_sequence('friends', 'friend_id'), (SELECT MAX(friend_id) FROM friends)+1);");
        Console.WriteLine("Friend key value has been fixed.");

        await db.Database.ExecuteSqlRawAsync("SELECT pg_catalog.setval(pg_get_serial_sequence('guilds', 'guild_id'), (SELECT MAX(guild_id) FROM guilds)+1);");
        Console.WriteLine("Guild key value has been fixed.");
    }
}
