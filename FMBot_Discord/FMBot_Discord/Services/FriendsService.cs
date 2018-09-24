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
        private FMBotDbContext db = new FMBotDbContext();

        public async Task<List<Friend>> GetFMFriendsAsync(IUser discordUser)
        {
            var id = discordUser.Id.ToString();

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


        public async Task AddDiscordFriendAsync(string discordUserID, string friendDiscordUserID)
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

            User friendUser = db.Users.FirstOrDefault(f => f.DiscordUserID == friendDiscordUserID);

            if (friendUser == null)
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
