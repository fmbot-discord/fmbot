using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Configurations;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FMBot.Bot.Services
{
    public class FriendsService
    {
        private readonly IMemoryCache _cache;

        public FriendsService(IMemoryCache cache)
        {
            this._cache = cache;
        }

        public async Task<IReadOnlyList<string>> GetFMFriendsAsync(IUser discordUser)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var user = await db.Users
                .Include(i => i.Friends)
                .ThenInclude(i => i.FriendUser)
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

            var friends = user.Friends
                .Where(w => w.LastFMUserName != null || w.FriendUser.UserNameLastFM != null)
                .Select(
                    s => s.LastFMUserName ?? s.FriendUser.UserNameLastFM)
                .ToList();

            return friends;
        }

        public async Task AddLastFMFriendAsync(ulong discordSenderId, string lastfmusername, int? discordFriendId)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
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
                LastFMUserName = lastfmusername,
                FriendUserId = discordFriendId
            };

            await db.Friends.AddAsync(friend);

            await db.SaveChangesAsync();

            await Task.CompletedTask;
        }


        public async Task<bool> RemoveLastFMFriendAsync(int userID, string lastfmusername)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var friend = db.Friends
                .Include(i => i.FriendUser)
                .FirstOrDefault(f => f.UserId == userID &&
                                     (f.LastFMUserName.ToLower() == lastfmusername.ToLower() || f.FriendUser != null && f.FriendUser.UserNameLastFM.ToLower() == lastfmusername.ToLower()));

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
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
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
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
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
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            return await db.Friends.AsQueryable().CountAsync();
        }
    }
}
