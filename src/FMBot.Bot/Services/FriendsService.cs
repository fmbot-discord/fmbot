using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

    public async Task<List<Friend>> GetFriendedAsync(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        
        var friended = await db.Friends
            .Include(f => f.User)
            .Where(f => f.FriendUserId == userId)
            .ToListAsync();
        
        return friended;
    }

    public async Task AddLastFmFriendAsync(User contextUser, string lastFmUserName, int? friendUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
            
        var friend = new Friend
        {
            UserId = contextUser.UserId,
            LastFMUserName = lastFmUserName,
            FriendUserId = friendUserId
        };

        await db.Friends.AddAsync(friend);

        await db.SaveChangesAsync();
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
