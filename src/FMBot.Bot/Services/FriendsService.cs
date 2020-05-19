using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services
{
    public class FriendsService
    {
        public async Task<IReadOnlyList<string>> GetFMFriendsAsync(IUser discordUser)
        {
            using var db = new FMBotDbContext();
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

        public async Task AddLastFMFriendAsync(ulong discordUserId, string lastfmusername)
        {
            using var db = new FMBotDbContext();
            var user = db.Users.FirstOrDefault(f => f.DiscordUserId == discordUserId);

            if (user == null)
            {
                var newUser = new User
                {
                    DiscordUserId = discordUserId,
                    UserType = UserType.User
                };

                db.Users.Add(newUser);
                user = newUser;
            }

            var friend = new Friend
            {
                User = user,
                LastFMUserName = lastfmusername
            };

            db.Friends.Add(friend);

            db.SaveChanges();

            await Task.CompletedTask;
        }


        public async Task RemoveLastFMFriendAsync(int userID, string lastfmusername)
        {
            using var db = new FMBotDbContext();
            var friend = db.Friends.FirstOrDefault(f => f.UserId == userID && f.LastFMUserName.ToLower() == lastfmusername.ToLower());

            if (friend != null)
            {
                db.Friends.Remove(friend);

                db.SaveChanges();

                await Task.CompletedTask;
            }
        }

        public async Task RemoveAllLastFMFriendsAsync(int userID)
        {
            using var db = new FMBotDbContext();
            var friends = db.Friends.Where(f => f.UserId == userID || f.FriendUserId == userID).ToList();

            if (friends.Count > 0)
            {
                db.Friends.RemoveRange(friends);
                db.SaveChanges();
            }

            await Task.CompletedTask;
        }


        public async Task AddDiscordFriendAsync(ulong discordUserId, ulong friendDiscordUserId)
        {
            using var db = new FMBotDbContext();
            var user = await db.Users
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

            if (user == null)
            {
                var newUser = new User
                {
                    DiscordUserId = discordUserId,
                    UserType = UserType.User
                };

                db.Users.Add(newUser);
                user = newUser;
            }

            var friendUser = await db.Users
                .FirstOrDefaultAsync(f => f.DiscordUserId == friendDiscordUserId);

            if (friendUser == null)
            {
                return;
            }

            if (await db.Friends.FirstOrDefaultAsync(f =>
                    f.UserId == user.UserId && f.LastFMUserName == friendUser.UserNameLastFM) != null)
            {
                return;
            }

            var friend = new Friend
            {
                User = user,
                FriendUser = friendUser
            };

            db.Friends.Add(friend);

            db.SaveChanges();

            await Task.CompletedTask;
        }

        public async Task<int> GetTotalFriendCountAsync()
        {
            using var db = new FMBotDbContext();
            return await db.Friends.CountAsync();
        }
    }
}
