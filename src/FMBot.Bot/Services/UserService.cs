using Discord;
using Discord.Commands;
using FMBot.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
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

            return await db.Users.FirstOrDefaultAsync(f => f.DiscordUserID == discordUserID).ConfigureAwait(false);
        }



        // Discord nickname/username
        public async Task<string> GetNameAsync(ICommandContext context)
        {
            if (context.Guild == null)
            {
                return context.User.Username;
            }

            IGuildUser guildUser = await context.Guild.GetUserAsync(context.User.Id).ConfigureAwait(false);

            return guildUser.Nickname ?? context.User.Username;
        }

        // Rank
        public async Task<UserType> GetRankAsync(IUser discordUser)
        {
            string discordUserID = discordUser.Id.ToString();

            User user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserID == discordUserID).ConfigureAwait(false);

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

            User user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserID == discordUserID).ConfigureAwait(false);

            if (user == null)
            {
                return false;
            }

            return user.Featured;
        }

        // Featured
        public async Task<User> GetFeaturedUserAsync()
        {
            return await db.Users.FirstOrDefaultAsync(f => f.Featured == true).ConfigureAwait(false);
        }



        // Random user
        public async Task<string> GetRandomLastFMUserAsync()
        {
            User featuredUser = await db.Users.FirstOrDefaultAsync(f => f.Featured == true).ConfigureAwait(false);
            if (featuredUser != null)
            {
                featuredUser.Featured = false;

                db.Entry(featuredUser).State = EntityState.Modified;
            }

            List<User> users = db.Users.Where(w => w.Blacklisted != true).ToList();

            Random rand = new Random();
            User user = users[rand.Next(users.Count)];

            user.Featured = true;

            db.Entry(user).State = EntityState.Modified;
            db.SaveChanges();

            return user.UserNameLastFM;
        }


        // Server Blacklisting
        public async Task<bool> GetBlacklistedAsync(IUser discordUser)
        {
            string discordUserID = discordUser.Id.ToString();

            User user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserID == discordUserID).ConfigureAwait(false);

            if (user == null)
            {
                return false;
            }

            return user.Blacklisted ?? false;
        }

        // UserTitle
        public async Task<string> GetUserTitleAsync(ICommandContext context)
        {
            string name = await GetNameAsync(context).ConfigureAwait(false);
            UserType rank = await GetRankAsync(context.User).ConfigureAwait(false);
            bool? featured = await GetFeaturedAsync(context.User).ConfigureAwait(false);

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
        public async Task ResetChartTimerAsync(User user)
        {
            user.LastGeneratedChartDateTimeUtc = DateTime.UtcNow;

            db.Entry(user).State = EntityState.Modified;

            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        // Remove user
        public async Task DeleteUser(int userID)
        {
            User user = await db.Users.FirstOrDefaultAsync(f => f.UserID == userID).ConfigureAwait(false);

            db.Users.Remove(user);

            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        // Usercount
        public async Task<int> GetUserCountAsync()
        {
            return await db.Users.CountAsync().ConfigureAwait(false);
        }
    }
}
