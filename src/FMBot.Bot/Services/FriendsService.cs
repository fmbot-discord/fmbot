using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services;

public class FriendsService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

    public FriendsService(IDbContextFactory<FMBotDbContext> contextFactory)
    {
        this._contextFactory = contextFactory;
    }

    public async Task<List<Friend>> GetFriendsAsync(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .Include(i => i.Friends)
            .ThenInclude(i => i.FriendUser)
            .FirstAsync(f => f.DiscordUserId == discordUserId);

        var friends = user.Friends
            .Where(w => w.LastFMUserName != null || w.FriendUser.UserNameLastFM != null)
            .ToList();

        return friends;
    }

    public async Task<Friend> GetFriendAsync(int friendId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Friends
            .Include(f => f.FriendUser)
            .FirstOrDefaultAsync(f => f.FriendId == friendId);
    }

    public async Task<List<Friend>> GetFriendedAsync(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        
        var friended = await db.Friends
            .Include(f => f.User)
            .Where(f => f.FriendUserId == userId)
            .ToListAsync();
        
        return friended;
    }

    public async Task AddLastFmFriendAsync(User contextUser, string lastFmUserName, int? friendUserId,
        FriendType friendType = FriendType.VisibleInNowPlaying, bool lastFmFriend = false)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var now = DateTime.UtcNow;
        var friend = new Friend
        {
            UserId = contextUser.UserId,
            LastFMUserName = lastFmUserName,
            FriendUserId = friendUserId,
            FriendType = friendType,
            LastFmFriend = lastFmFriend,
            Created = now,
            Modified = now
        };

        await db.Friends.AddAsync(friend);

        await db.SaveChangesAsync();
    }

    public async Task<Dictionary<string, int>> GetRegisteredUserIdsAsync(IReadOnlyList<string> lastFmUserNames)
    {
        if (lastFmUserNames.Count == 0)
        {
            return new Dictionary<string, int>();
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();

        var lowered = lastFmUserNames.Select(s => s.ToLower()).Distinct().ToList();
        var matchedUsers = await db.Users
            .Where(u => lowered.Contains(u.UserNameLastFM.ToLower()))
            .OrderBy(u => u.LastUsed == null)
            .ThenByDescending(u => u.LastUsed)
            .Select(u => new { u.UserId, u.UserNameLastFM })
            .ToListAsync();

        return matchedUsers
            .GroupBy(g => g.UserNameLastFM.ToLower())
            .ToDictionary(g => g.Key, g => g.First().UserId);
    }

    public async Task<int> AddLastFmFriendsAsync(User contextUser, IReadOnlyList<string> lastFmUserNames,
        IReadOnlyDictionary<string, int> userIdByName)
    {
        if (lastFmUserNames.Count == 0)
        {
            return 0;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();

        var now = DateTime.UtcNow;
        var newFriends = lastFmUserNames.Select(name => new Friend
        {
            UserId = contextUser.UserId,
            LastFMUserName = name,
            FriendUserId = userIdByName.TryGetValue(name.ToLower(), out var id) ? id : null,
            FriendType = FriendType.Normal,
            LastFmFriend = true,
            Created = now,
            Modified = now
        }).ToList();

        await db.Friends.AddRangeAsync(newFriends);
        await db.SaveChangesAsync();

        return newFriends.Count;
    }

    public async Task SetFriendTypeAsync(int friendId, FriendType friendType)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var friend = await db.Friends.FirstOrDefaultAsync(f => f.FriendId == friendId);

        if (friend == null)
        {
            return;
        }

        friend.FriendType = friendType;
        friend.LastFmFriend = false;
        friend.Modified = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    public async Task<HashSet<int>> GetCloseFriendUserIdsAsync(User contextUser)
    {
        if (contextUser.UserType == UserType.User)
        {
            return [];
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var ids = await db.Friends
            .Where(f => f.UserId == contextUser.UserId && f.FriendType == FriendType.CloseFriend && f.FriendUserId.HasValue)
            .Select(f => f.FriendUserId.Value)
            .ToListAsync();

        return ids.ToHashSet();
    }

    public async Task RemoveFriendByIdAsync(int friendId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var friend = await db.Friends.FirstOrDefaultAsync(f => f.FriendId == friendId);

        if (friend == null)
        {
            return;
        }

        db.Friends.Remove(friend);
        await db.SaveChangesAsync();
    }

    public async Task<int> RemoveSyncedLastFmFriendsAsync(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var synced = await db.Friends
            .Where(f => f.UserId == userId && f.LastFmFriend)
            .ToListAsync();

        if (synced.Count == 0)
        {
            return 0;
        }

        db.Friends.RemoveRange(synced);
        await db.SaveChangesAsync();

        return synced.Count;
    }

    public async Task<bool> RemoveLastFmFriendAsync(int userId, string lastFmUserName)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var friend = db.Friends
            .Include(i => i.FriendUser)
            .FirstOrDefault(f => f.UserId == userId &&
                                 (f.LastFMUserName.ToLower() == lastFmUserName.ToLower() || f.FriendUser != null && f.FriendUser.UserNameLastFM.ToLower() == lastFmUserName.ToLower()));

        if (friend != null)
        {
            db.Friends.Remove(friend);

            await db.SaveChangesAsync();

            return true;
        }
        else
        {
            return false;
        }
    }

    public async Task RemoveAllFriendsAsync(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var friends = db.Friends
            .AsQueryable()
            .Where(f => f.UserId == userId).ToList();

        if (friends.Count > 0)
        {
            db.Friends.RemoveRange(friends);
            await db.SaveChangesAsync();
        }
    }

    public async Task RemoveUserFromOtherFriendsAsync(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var friends = db.Friends
            .AsQueryable()
            .Where(f => f.FriendUserId == userId).ToList();

        if (friends.Count > 0)
        {
            db.Friends.RemoveRange(friends);
            await db.SaveChangesAsync();
        }
    }

    public async Task<int> GetTotalFriendCountAsync()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Friends.AsQueryable().CountAsync();
    }
}
