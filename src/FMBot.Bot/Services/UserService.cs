using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Domain.Models;
using FMBot.LastFM.Services;
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
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly LastFmService _lastFmService;

        public UserService(IMemoryCache cache, IDbContextFactory<FMBotDbContext> contextFactory, LastFmService lastFmService)
        {
            this._cache = cache;
            this._contextFactory = contextFactory;
            this._lastFmService = lastFmService;
        }

        // User settings
        public async Task<bool> UserRegisteredAsync(IUser discordUser)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var isRegistered = await db.Users
                .AsQueryable()
                .AnyAsync(f => f.DiscordUserId == discordUser.Id);

            return isRegistered;
        }

        public async Task<bool> UserBlockedAsync(IUser discordUser)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var isBlocked = await db.Users
                .AsQueryable()
                .AnyAsync(f => f.DiscordUserId == discordUser.Id && f.Blocked == true);

            return isBlocked;
        }

        // User settings
        public async Task<bool> UserHasSessionAsync(IUser discordUser)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var user = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

            if (user == null)
            {
                return false;
            }

            return !string.IsNullOrEmpty(user.SessionKeyLastFm);
        }

        // User settings
        public async Task UpdateUserLastUsedAsync(IUser discordUser)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var user = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

            if (user != null)
            {
                user.LastUsed = DateTime.UtcNow;

                db.Entry(user).State = EntityState.Modified;

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (Exception e)
                {
                    Log.Error(e, "Something went wrong while attempting to update user {userId} last used", user.UserId);
                }
            }
        }

        // User settings
        public async Task<User> GetUserSettingsAsync(IUser discordUser)
        {
            await using var db = this._contextFactory.CreateDbContext();
            return await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);
        }

        // User settings
        public async Task<User> GetUserAsync(ulong discordUserId)
        {
            await using var db = this._contextFactory.CreateDbContext();
            return await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);
        }

        // User settings
        public async Task<User> GetFullUserAsync(ulong discordUserId)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var query = db.Users
                .Include(i => i.Friends)
                .Include(i => i.FriendedByUsers);

            return await query
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);
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

            await using var db = this._contextFactory.CreateDbContext();
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
            await using var db = this._contextFactory.CreateDbContext();
            return await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.Featured == true);
        }

        // Random user
        public async Task<string> GetRandomLastFMUserAsync()
        {
            await using var db = this._contextFactory.CreateDbContext();
            var featuredUsers = await db.Users
                .AsQueryable()
                .Where(f => f.Featured == true)
                .ToListAsync();

            if (featuredUsers.Any())
            {

                foreach (var featuredUser in featuredUsers)
                {
                    featuredUser.Featured = false;
                    db.Entry(featuredUser).State = EntityState.Modified;
                }
            }

            var users = db.Users
                .AsQueryable()
                .Where(w => w.Blocked != true).ToList();

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
            await using var db = this._contextFactory.CreateDbContext();
            var user = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

            if (user == null)
            {
                return false;
            }

            return user.Blocked ?? false;
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

            if (rank == UserType.Backer)
            {
                title += " ⭐";
            }

            return title;
        }

        // Set LastFM Name
        public async Task SetLastFm(IUser discordUser, User newUserSettings, bool updateSessionKey = false)
        {
            await using var db = this._contextFactory.CreateDbContext();
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

                await db.Users.AddAsync(newUser);

                try
                {
                    await db.SaveChangesAsync();
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
                if (updateSessionKey)
                {
                    user.SessionKeyLastFm = newUserSettings.SessionKeyLastFm;
                }

                db.Entry(user).State = EntityState.Modified;

                await db.SaveChangesAsync();
            }
        }

        public User SetSettings(User userSettings, string[] extraOptions)
        {
            extraOptions = extraOptions.Select(s => s.ToLower()).ToArray();
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
            await using var db = this._contextFactory.CreateDbContext();
            user.LastGeneratedChartDateTimeUtc = DateTime.Now;

            db.Entry(user).State = EntityState.Modified;

            await db.SaveChangesAsync();
        }

        // Remove user
        public async Task DeleteUser(int userId)
        {
            await using var db = this._contextFactory.CreateDbContext();

            try
            {
                var user = await db.Users
                    .AsQueryable()
                    .FirstOrDefaultAsync(f => f.UserId == userId);

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
            await using var db = this._contextFactory.CreateDbContext();
            return await db.Users
                .AsQueryable()
                .CountAsync();
        }

        public async Task<int> GetTotalAuthorizedUserCountAsync()
        {
            await using var db = this._contextFactory.CreateDbContext();
            return await db.Users
                .AsQueryable()
                .Where(w => w.SessionKeyLastFm != null)
                .CountAsync();
        }

        public async Task<int> DeleteInactiveUsers()
        {
            var deletedInactiveUsers = 0;

            await using var db = this._contextFactory.CreateDbContext();
            var inactiveUsers = await db.InactiveUsers
                .AsQueryable()
                .Where(w => w.MissingParametersErrorCount > 1 && w.Updated > DateTime.UtcNow.AddDays(-3))
                .ToListAsync();

            foreach (var inactiveUser in inactiveUsers)
            {
                var user = await db.Users
                    .AsQueryable()
                    .FirstOrDefaultAsync(f => f.UserId == inactiveUser.UserId &&
                                              (f.LastUsed == null || f.LastUsed < DateTime.UtcNow.AddDays(-30)) &&
                                              string.IsNullOrWhiteSpace(f.SessionKeyLastFm));

                if (user != null)
                {
                    if (!await this._lastFmService.LastFmUserExistsAsync(user.UserNameLastFM))
                    {
                        await DeleteUser(user.UserId);
                        Log.Information("DeleteInactiveUsers: User {userNameLastFm} | {userId} | {discordUserId} deleted", user.UserNameLastFM, user.UserId, user.DiscordUserId);
                        deletedInactiveUsers++;
                    }
                    else
                    {
                        Log.Information("DeleteInactiveUsers: User {userNameLastFm} exists, so deletion cancelled", user.UserNameLastFM);
                    }

                    Thread.Sleep(250);
                }
            }

            return deletedInactiveUsers;
        }
    }
}
