using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using Serilog;

namespace FMBot.Bot.Services
{
    public class UserService
    {
        private readonly IMemoryCache _cache;

        public UserService(IMemoryCache cache)
        {
            this._cache = cache;
        }

        // User settings
        public async Task<bool> UserRegisteredAsync(IUser discordUser)
        {
            var cacheKey = $"user-isRegistered-{discordUser.Id}";

            if (this._cache.TryGetValue(cacheKey, out bool isRegistered))
            {
                return isRegistered;
            }

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            isRegistered = await db.Users
                .AsQueryable()
                .AnyAsync(f => f.DiscordUserId == discordUser.Id);

            if (isRegistered)
            {
                this._cache.Set(cacheKey, isRegistered, TimeSpan.FromHours(24));
            }

            return isRegistered;
        }

        // User settings
        public async Task<bool> UserHasSessionAsync(IUser discordUser)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var user = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

            return !string.IsNullOrEmpty(user.SessionKeyLastFm);
        }

        // User settings
        public async Task<User> GetUserSettingsAsync(IUser discordUser, bool bypassCache = false)
        {
            var cacheKey = $"user-settings-{discordUser.Id}";

            if (!bypassCache && this._cache.TryGetValue(cacheKey, out User user))
            {
                return user;
            }

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            user = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

            this._cache.Set(cacheKey, user, TimeSpan.FromHours(12));

            return user;
        }

        // User settings
        public async Task<User> GetFullUserAsync(IUser discordUser)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var query = db.Users
                .Include(i => i.Friends)
                .Include(i => i.FriendedByUsers);

            return await query
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
            var cacheKey = $"user-userType-{discordUser.Id}";

            if (this._cache.TryGetValue(cacheKey, out UserType rank))
            {
                return rank;
            }

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var user = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

            rank = user?.UserType ?? UserType.User;

            this._cache.Set(cacheKey, rank, TimeSpan.FromHours(6));

            return rank;
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

            var title = name;

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
        public void SetLastFM(IUser discordUser, User newUserSettings)
        {
            using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var user = db.Users.FirstOrDefault(f => f.DiscordUserId == discordUser.Id);

            this._cache.Remove($"user-settings-{discordUser.Id}");
            this._cache.Remove($"user-isRegistered-{discordUser.Id}");

            if (user == null)
            {
                var newUser = new User
                {
                    DiscordUserId = discordUser.Id,
                    UserType = UserType.User,
                    UserNameLastFM = newUserSettings.UserNameLastFM,
                    TitlesEnabled = true,
                    ChartTimePeriod = ChartTimePeriod.Monthly,
                    FmEmbedType = newUserSettings.FmEmbedType,
                    FmCountType = newUserSettings.FmCountType,
                    SessionKeyLastFm = newUserSettings.SessionKeyLastFm
                };

                db.Users.Add(newUser);

                try
                {
                    db.SaveChanges();
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error in SetLastFM");
                    throw;
                }
            }
            else
            {
                user.UserNameLastFM = newUserSettings.UserNameLastFM;
                user.FmEmbedType = newUserSettings.FmEmbedType;
                user.FmCountType = newUserSettings.FmCountType;
                user.SessionKeyLastFm = newUserSettings.SessionKeyLastFm;

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

            try
            {
                var user = await db.Users
                    .AsQueryable()
                    .FirstOrDefaultAsync(f => f.UserId == userID);

                this._cache.Remove($"user-settings-{user.DiscordUserId}");
                this._cache.Remove($"user-isRegistered-{user.DiscordUserId}");

                await using var connection = new NpgsqlConnection(ConfigData.Data.Database.ConnectionString);
                connection.Open();

                await using var deleteArtists = new NpgsqlCommand($"DELETE FROM public.user_artists WHERE user_id = {user.UserId};", connection);
                await deleteArtists.ExecuteNonQueryAsync();

                await using var deleteAlbums = new NpgsqlCommand($"DELETE FROM public.user_albums WHERE user_id = {user.UserId};", connection);
                await deleteAlbums.ExecuteNonQueryAsync();

                await using var deleteTracks = new NpgsqlCommand($"DELETE FROM public.user_tracks WHERE user_id = {user.UserId};", connection);
                await deleteTracks.ExecuteNonQueryAsync();

                db.Users.Remove(user);

                await db.SaveChangesAsync();
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while deleting user!");
                throw;
            }

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
