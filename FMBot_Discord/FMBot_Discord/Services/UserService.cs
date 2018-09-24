using Discord;
using Discord.Commands;
using FMBot.Data.Entities;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace FMBot.Services
{
    public class UserService
    {
        private FMBotDbContext db = new FMBotDbContext();

        // User settings
        public async Task<User> GetUserSettingsAsync(IUser discordUser)
        {
            string discordUserID = discordUser.Id.ToString();

            User user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserID == discordUserID);

            if (user == null)
            {
                return null;
            }

            return user;
        }


        // Discord nickname/username
        public async Task<string> GetNameAsync(ICommandContext context)
        {
            if (context.Guild == null)
            {
                return context.User.Username;
            }

            IGuildUser guildUser = await context.Guild.GetUserAsync(context.User.Id);

            return guildUser.Nickname ?? context.User.Username;
        }

        // Rank
        public async Task<UserType> GetRankAsync(IUser discordUser)
        {
            string discordUserID = discordUser.Id.ToString();

            User user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserID == discordUserID);

            if (user == null)
            {
                return UserType.User;
            }

            return user.UserType;
        }

        // Featured
        public async Task<bool?> GetFeaturedAsync(IUser discordUser)
        {
            string discordUserID = discordUser.Id.ToString();

            User user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserID == discordUserID);

            if (user == null)
            {
                return false;
            }

            return user.Featured;
        }

        // Server Blacklisting
        public async Task<bool?> GetBlacklistedAsync(IUser discordUser)
        {
            string discordUserID = discordUser.Id.ToString();

            User user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserID == discordUserID);

            if (user == null)
            {
                return false;
            }

            return user.Blacklisted;
        }

        // UserTitle
        public async Task<string> GetUserTitleAsync(ICommandContext context)
        {
            string name = await GetNameAsync(context);
            UserType rank = await GetRankAsync(context.User);
            bool? featured = await GetFeaturedAsync(context.User);
            string featuredUser = (featured == true) ? ", Featured User" : "";

            return name + " " + rank.ToString() + featuredUser;
        }

        // Set LastFM Name
        public void SetLastFM(IUser discordUser, string lastFMName, ChartType chartType)
        {
            string discordUserID = discordUser.Id.ToString();

            User user = db.Users.FirstOrDefault(f => f.DiscordUserID == discordUserID);

            if (user == null)
            {
                User newUser = new User
                {
                    DiscordUserID = discordUserID,
                    UserType = UserType.User,
                    UserNameLastFM = lastFMName,
                    TitlesEnabled = true,
                    ChartTimePeriod = ChartTimePeriod.Monthly,
                    ChartType = chartType,

                };

                db.Users.Add(newUser);

                db.SaveChanges();
            }
            else
            {
                user.UserNameLastFM = lastFMName;
                user.ChartType = chartType;

                db.Entry(user).State = EntityState.Modified;

                db.SaveChanges();
            }
        }
    }
}
