using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services
{
    public class UserService
    {
        // User settings
        public async Task<bool> UserRegisteredAsync(IUser discordUser)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            return await db.Users
                .AsQueryable()
                .AnyAsync(f => f.DiscordUserId == discordUser.Id);
        }

        // User settings
        public async Task<User> GetUserSettingsAsync(IUser discordUser)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            return await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);
        }

        // User settings
        public async Task<User> GetFullUserAsync(IUser discordUser)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            return await db.Users
                .Include(i => i.Artists)
                .Include(i => i.Friends)
                .Include(i => i.FriendedByUsers)
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);
        }

        // Discord nickname/username
        public async Task<string> GetNameAsync(ICommandContext context)
        {
            if (context.Guild == null)
            {
                return context.User.Username;
            }

            var guildUser = await context.Guild.GetUserAsync(context.User.Id);

            return guildUser.Nickname ?? context.User.Username;
        }

        // Rank
        public async Task<UserType> GetRankAsync(IUser discordUser)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var user = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

            if (user == null)
            {
                return UserType.User;
            }

            return user.UserType;
        }

        // Featured
        public async Task<bool?> GetFeaturedAsync(IUser discordUser)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var user = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

            if (user == null)
            {
                return false;
            }

            return user.Featured;
        }

        // Featured
        public async Task<User> GetFeaturedUserAsync()
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            return await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.Featured == true);
        }

        // Random user
        public async Task<string> GetRandomLastFMUserAsync()
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var featuredUser = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.Featured == true);
            if (featuredUser != null)
            {
                featuredUser.Featured = false;

                db.Entry(featuredUser).State = EntityState.Modified;
            }

            var users = db.Users
                .AsQueryable()
                .Where(w => w.Blacklisted != true).ToList();

            var rand = new Random();
            var user = users[rand.Next(users.Count)];

            user.Featured = true;

            db.Entry(user).State = EntityState.Modified;
            await db.SaveChangesAsync();

            return user.UserNameLastFM;
        }


        // Server Blacklisting
        public async Task<bool> GetBlacklistedAsync(IUser discordUser)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var user = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

            if (user == null)
            {
                return false;
            }

            return user.Blacklisted ?? false;
        }

        // UserTitle
        public async Task<string> GetUserTitleAsync(ICommandContext context)
        {
            var name = await GetNameAsync(context);
            var rank = await GetRankAsync(context.User);
            var featured = await GetFeaturedAsync(context.User);

            var title = name;

            if (featured == true)
            {
                title = name + " (Currently Featured)";
            }

            if (rank == UserType.Owner)
            {
                title += " 👑";
            }

            if (rank == UserType.Admin)
            {
                title += " 🛡️";
            }

            if (rank == UserType.Contributor)
            {
                title += " 🔥";
            }

            return title;
        }

        // Set LastFM Name
        public void SetLastFM(IUser discordUser, User userSettings)
        {
            using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var user = db.Users.FirstOrDefault(f => f.DiscordUserId == discordUser.Id);

            if (user == null)
            {
                var newUser = new User
                {
                    DiscordUserId = discordUser.Id,
                    UserType = UserType.User,
                    UserNameLastFM = userSettings.UserNameLastFM,
                    TitlesEnabled = true,
                    ChartTimePeriod = ChartTimePeriod.Monthly,
                    FmEmbedType = userSettings.FmEmbedType,
                    FmCountType = userSettings.FmCountType
                };

                db.Users.Add(newUser);

                try
                {
                    db.SaveChanges();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
            }
            else
            {
                user.UserNameLastFM = userSettings.UserNameLastFM;
                user.FmEmbedType = userSettings.FmEmbedType;
                user.FmCountType = userSettings.FmCountType;

                db.Entry(user).State = EntityState.Modified;

                db.SaveChanges();
            }
        }

        public User SetSettings(User userSettings, string[] extraOptions)
        {

            if (extraOptions.Contains("embedfull") || extraOptions.Contains("ef"))
            {
                userSettings.FmEmbedType = FmEmbedType.embedfull;
            }
            else if (extraOptions.Contains("textmini") || extraOptions.Contains("tm"))
            {
                userSettings.FmEmbedType = FmEmbedType.textmini;
            }
            else if (extraOptions.Contains("textfull") || extraOptions.Contains("tf"))
            {
                userSettings.FmEmbedType = FmEmbedType.textfull;
            }
            else
            {
                userSettings.FmEmbedType = FmEmbedType.embedmini;
            }


            if (extraOptions.Contains("artist"))
            {
                userSettings.FmCountType = FmCountType.Artist;
            }
            else if (extraOptions.Contains("album"))
            {
                userSettings.FmCountType = FmCountType.Album;
            }
            else if (extraOptions.Contains("track"))
            {
                userSettings.FmCountType = FmCountType.Track;
            }
            else
            {
                userSettings.FmCountType = null;
            }

            return userSettings;
        }


        public async Task ResetChartTimerAsync(User user)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            user.LastGeneratedChartDateTimeUtc = DateTime.Now;

            db.Entry(user).State = EntityState.Modified;

            await db.SaveChangesAsync();
        }

        // Remove user
        public async Task DeleteUser(int userID)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var user = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.UserId == userID);

            db.Users.Remove(user);

            await db.SaveChangesAsync();
        }

        public async Task<int> GetTotalUserCountAsync()
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            return await db.Users
                .AsQueryable()
                .CountAsync();
        }
    }
}
