using Discord;
using Discord.Commands;
using FMBot.Data.Entities;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace FMBot.Services
{
    public class UserService
    {
        private readonly FMBotDbContext db = new FMBotDbContext();

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

        // Featured
        public async Task<User> GetFeaturedUserAsync()
        {
            User user = await db.Users.FirstOrDefaultAsync(f => f.Featured == true);

            if (user == null)
            {
                return null;
            }

            return user;
        }



        // Random user
        public async Task<string> GetRandomLastFMUserAsync()
        {
            User featuredUser = await db.Users.FirstOrDefaultAsync(f => f.Featured == true);
            if (featuredUser != null)
            {
                featuredUser.Featured = false;

                db.Entry(featuredUser).State = EntityState.Modified;
            }

            List<User> users = db.Users.Where(w => w.Blacklisted != true).ToList();

            Random rand = new Random();
            User user = users[rand.Next(users.Count())];

            user.Featured = true;

            db.Entry(user).State = EntityState.Modified;
            db.SaveChanges();

            return user.UserNameLastFM;
        }


        // Server Blacklisting
        public async Task<bool> GetBlacklistedAsync(IUser discordUser)
        {
            string discordUserID = discordUser.Id.ToString();

            User user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserID == discordUserID);

            if (user == null)
            {
                return false;
            }

            return user.Blacklisted ?? false;
        }

        // UserTitle
        public async Task<string> GetUserTitleAsync(ICommandContext context)
        {
            string name = await GetNameAsync(context);
            UserType rank = await GetRankAsync(context.User);
            bool? featured = await GetFeaturedAsync(context.User);

            string title = name;

            if (featured == true)
            {
                title = name + ", Featured User";
            }
            if (rank != UserType.User)
            {
                title = title + " - " + rank.ToString();
            }

            return title;
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

        // Set LastFM Name
        public void ResetChartTimer(User user)
        {
            user.LastGeneratedChartDateTimeUtc = DateTime.UtcNow;

            db.Entry(user).State = EntityState.Modified;

            db.SaveChanges();
        }

        // Remove user
        public async Task DeleteUser(IUser discordUser)
        {
            string discordUserID = discordUser.Id.ToString();

            User user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserID == discordUserID);

            db.Users.Remove(user);

            db.SaveChanges();
        }

        // Usercount
        public async Task<int> GetUserCountAsync()
        {
            return await db.Users.CountAsync();
        }
    }
}
