using Discord;
using FMBot.Data.Entities;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace FMBot.Services
{
    public class FriendsService
    {
        private readonly FMBotDbContext db = new FMBotDbContext();

        public async Task<List<Friend>> GetFMFriendsAsync(IUser discordUser)
        {
            string id = discordUser.Id.ToString();

            User user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserID == id);

            List<Friend> friends = await db.Friends.Where(w => w.UserID == user.UserID).ToListAsync();

            return friends;
        }

        public async Task AddLastFMFriendAsync(string discordUserID, string lastfmusername)
        {
            User user = db.Users.FirstOrDefault(f => f.DiscordUserID == discordUserID);

            if (user == null)
            {
                User newUser = new User
                {
                    DiscordUserID = discordUserID,
                    UserType = UserType.User
                };

                db.Users.Add(newUser);
                user = newUser;
            }

            Friend friend = new Friend
            {
                User = user,
                LastFMUserName = lastfmusername,
            };

            db.Friends.Add(friend);

            db.SaveChanges();

            await Task.CompletedTask;
        }


        public async Task RemoveLastFMFriendAsync(int userID, string lastfmusername)
        {
            Friend friend = db.Friends.FirstOrDefault(f => f.UserID == userID && f.LastFMUserName == lastfmusername);

            db.Friends.Remove(friend);

            db.SaveChanges();

            await Task.CompletedTask;
        }

        public async Task RemoveAllLastFMFriendsAsync(int userID)
        {
            List<Friend> friends = db.Friends.Where(f => f.UserID == userID || f.FriendUserID == userID).ToList();

            if (friends.Count > 0)
            {
                db.Friends.RemoveRange(friends);
                db.SaveChanges();
            }

            await Task.CompletedTask;
        }


        public async Task AddDiscordFriendAsync(string discordUserID, string friendDiscordUserID)
        {
            User user = await db.Users
                .FirstOrDefaultAsync(f => f.DiscordUserID == discordUserID).ConfigureAwait(false);

            if (user == null)
            {
                User newUser = new User
                {
                    DiscordUserID = discordUserID,
                    UserType = UserType.User
                };

                db.Users.Add(newUser);
                user = newUser;
            }

            User friendUser = await db.Users
                .FirstOrDefaultAsync(f => f.DiscordUserID == friendDiscordUserID).ConfigureAwait(false);

            if (friendUser == null)
            {
                return;
            }

            if (await db.Friends.FirstOrDefaultAsync(f => f.UserID == user.UserID && f.LastFMUserName == friendUser.UserNameLastFM).ConfigureAwait(false) != null)
            {
                return;
            }

            Friend friend = new Friend
            {
                User = user,
                FriendUser = friendUser
            };

            db.Friends.Add(friend);

            db.SaveChanges();

            await Task.CompletedTask;
        }


    }
}
