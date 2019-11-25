using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FMBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services
{
    public class FriendsService
    {
        private readonly FMBotDbContext _db = new FMBotDbContext();

        public async Task<IReadOnlyList<string>> GetFMFriendsAsync(IUser discordUser)
        {
            var id = discordUser.Id.ToString();

            var user = await this._db.Users.FirstOrDefaultAsync(f => f.DiscordUserID == id);

            var dbFriends = await this._db.Friends
                .Include(i => i.FriendUser)
                .Where(w => w.UserID == user.UserID).ToListAsync();

            var friends = dbFriends.Select(
                    s => s.LastFMUserName ?? s.FriendUser.UserNameLastFM)
                .ToList();

            return friends;
        }

        public async Task AddLastFMFriendAsync(string discordUserID, string lastfmusername)
        {
            var user = this._db.Users.FirstOrDefault(f => f.DiscordUserID == discordUserID);

            if (user == null)
            {
                var newUser = new User
                {
                    DiscordUserID = discordUserID,
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
            var friend = this._db.Friends.FirstOrDefault(f => f.UserID == userID && f.LastFMUserName == lastfmusername);

            this._db.Friends.Remove(friend);

            this._db.SaveChanges();

            await Task.CompletedTask;
        }

        public async Task RemoveAllLastFMFriendsAsync(int userID)
        {
            var friends = this._db.Friends.Where(f => f.UserID == userID || f.FriendUserID == userID).ToList();

            if (friends.Count > 0)
            {
                this._db.Friends.RemoveRange(friends);
                this._db.SaveChanges();
            }

            await Task.CompletedTask;
        }


        public async Task AddDiscordFriendAsync(string discordUserID, string friendDiscordUserID)
        {
            var user = await this._db.Users
                .FirstOrDefaultAsync(f => f.DiscordUserID == discordUserID);

            if (user == null)
            {
                var newUser = new User
                {
                    DiscordUserID = discordUserID,
                    UserType = UserType.User
                };

                this._db.Users.Add(newUser);
                user = newUser;
            }

            var friendUser = await this._db.Users
                .FirstOrDefaultAsync(f => f.DiscordUserID == friendDiscordUserID);

            if (friendUser == null)
            {
                return;
            }

            if (await this._db.Friends.FirstOrDefaultAsync(f =>
                    f.UserID == user.UserID && f.LastFMUserName == friendUser.UserNameLastFM) != null)
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
    }
}
