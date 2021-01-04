using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FMBot.Bot.Services
{
    public class FriendsService
    {
        private readonly IMemoryCache _cache;
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        public FriendsService(IMemoryCache cache, IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._cache = cache;
            this._contextFactory = contextFactory;
        }

        public async Task<List<Friend>> GetFmFriendsAsync(IUser discordUser)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var user = await db.Users
                .Include(i => i.Friends)
                .ThenInclude(i => i.FriendUser)
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

            var friends = user.Friends
                .Where(w => w.LastFMUserName != null || w.FriendUser.UserNameLastFM != null)
                .ToList();

            return friends;
        }

        public async Task AddLastFmFriendAsync(ulong discordSenderId, string lastFmUserName, int? discordFriendId)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var user = db.Users.FirstOrDefault(f => f.DiscordUserId == discordSenderId);

            this._cache.Remove($"user-settings-{discordSenderId}");

            if (user == null)
            {
                var newUser = new User
                {
                    DiscordUserId = discordSenderId,
                    UserType = UserType.User
                };

                await db.Users.AddAsync(newUser);
                user = newUser;
            }

            var friend = new Friend
            {
                User = user,
                LastFMUserName = lastFmUserName,
                FriendUserId = discordFriendId
            };

            await db.Friends.AddAsync(friend);

            await db.SaveChangesAsync();

            await Task.CompletedTask;
        }


        public async Task<bool> RemoveLastFmFriendAsync(int userId, string lastFmUserName)
        {
            await using var db = this._contextFactory.CreateDbContext();
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
            await using var db = this._contextFactory.CreateDbContext();
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
            await using var db = this._contextFactory.CreateDbContext();
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
            await using var db = this._contextFactory.CreateDbContext();
            return await db.Friends.AsQueryable().CountAsync();
        }
    }
}
