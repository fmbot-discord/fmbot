using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FMBot.Data;
using FMBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services
{
    public class FriendsService
    {
        private readonly FMBotDbContext _db = new FMBotDbContext();

        public async Task<IReadOnlyList<string>> GetFMFriendsAsync(IUser discordUser)
        {
            var user = await this._db.Users
                .Include(i => i.Friends)
                .ThenInclude(i => i.FriendUser)
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

            var friends = user.Friends.Select(
                    s => s.LastFMUserName ?? s.FriendUser.UserNameLastFM)
                .ToList();

            return friends;
        }

        public async Task AddLastFMFriendAsync(ulong discordUserId, string lastfmusername)
        {
            var user = this._db.Users.FirstOrDefault(f => f.DiscordUserId == discordUserId);

            if (user == null)
            {
                var newUser = new User
                {
                    DiscordUserId = discordUserId,
                    UserType = UserType.User
                };

                this._db.Users.Add(newUser);
                user = newUser;
            }

            var friend = new Friend
            {
                User = user,
                LastFMUserName = lastfmusername
            };

            this._db.Friends.Add(friend);

            this._db.SaveChanges();

            await Task.CompletedTask;
        }


        public async Task RemoveLastFMFriendAsync(int userID, string lastfmusername)
        {
            var friend = this._db.Friends.FirstOrDefault(f => f.UserId == userID && f.LastFMUserName.ToLower() == lastfmusername.ToLower());

            if (friend != null)
            {
                this._db.Friends.Remove(friend);

                this._db.SaveChanges();

                await Task.CompletedTask;
            }
        }

        public async Task RemoveAllLastFMFriendsAsync(int userID)
        {
            var friends = this._db.Friends.Where(f => f.UserId == userID || f.FriendUserId == userID).ToList();

            if (friends.Count > 0)
            {
                this._db.Friends.RemoveRange(friends);
                this._db.SaveChanges();
            }

            await Task.CompletedTask;
        }


        public async Task AddDiscordFriendAsync(ulong discordUserId, ulong friendDiscordUserId)
        {
            var user = await this._db.Users
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

            if (user == null)
            {
                var newUser = new User
                {
                    DiscordUserId = discordUserId,
                    UserType = UserType.User
                };

                this._db.Users.Add(newUser);
                user = newUser;
            }

            var friendUser = await this._db.Users
                .FirstOrDefaultAsync(f => f.DiscordUserId == friendDiscordUserId);

            if (friendUser == null)
            {
                return;
            }

            if (await this._db.Friends.FirstOrDefaultAsync(f =>
                    f.UserId == user.UserId && f.LastFMUserName == friendUser.UserNameLastFM) != null)
            {
                return;
            }

            var friend = new Friend
            {
                User = user,
                FriendUser = friendUser
            };

            this._db.Friends.Add(friend);

            this._db.SaveChanges();

            await Task.CompletedTask;
        }

        public async Task<int> GetTotalFriendCountAsync()
        {
            return await this._db.Friends.CountAsync();
        }
    }
}
