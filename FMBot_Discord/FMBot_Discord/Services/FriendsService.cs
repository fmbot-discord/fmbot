using Discord;
using FMBot.Data.Entities;
using System.Linq;
using System.Threading.Tasks;

namespace FMBot.Bot.Services
{
    public class FriendsService
    {
        private FMBotDbContext db = new FMBotDbContext();

        public async Task AddLastFMFriendAsync(IUser discordUser, string lastfmusername)
        {
            User user = db.Users.FirstOrDefault(f => f.DiscordUserID == discordUser.Id.ToString());

            if (user == null)
            {
                User newUser = new User
                {
                    DiscordUserID = discordUser.Id.ToString(),
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
        }


        //public async Task AddFriendAsync(string lastfmusername)
        //{
            
        //}
    }
}
