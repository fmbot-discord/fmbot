using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
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

        public async Task<bool> UserRegisteredAsync(IUser discordUser)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var isRegistered = await db.Users
                .AsQueryable()
                .AnyAsync(f => f.DiscordUserId == discordUser.Id);

            return isRegistered;
        }

        public async Task<bool> UserBlockedAsync(ulong discordUserId)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var cacheKey = "blocked-users";

            if (this._cache.TryGetValue(cacheKey, out IReadOnlyList<User> blockedUsers))
            {
                return blockedUsers.Select(s => s.DiscordUserId).Contains(discordUserId);
            }

            blockedUsers = await db.Users
                .AsQueryable()
                .Where(w => w.Blocked == true)
                .ToListAsync();

            this._cache.Set(cacheKey, blockedUsers, TimeSpan.FromMinutes(15));

            return blockedUsers.Select(s => s.DiscordUserId).Contains(discordUserId);
        }

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

        public async Task UpdateUserLastUsedAsync(ulong discordUserId)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var user = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

            if (user != null)
            {
                user.LastUsed = DateTime.UtcNow;

                db.Update(user);

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

        public async Task LogFeatured(int userId, FeaturedMode mode, BotType botType, string description, string artistName, string albumName, string trackName = null)
        {
            await using var db = this._contextFactory.CreateDbContext();

            var featuredLog = new FeaturedLog
            {
                UserId = userId,
                Description = description,
                BotType = botType,
                AlbumName = albumName,
                TrackName = trackName,
                ArtistName = artistName,
                FeaturedMode = mode,
                DateTime = DateTime.UtcNow
            };

            await db.FeaturedLogs.AddAsync(featuredLog);

            await db.SaveChangesAsync();
        }

        // Log featured
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
            await using var db = this._contextFactory.CreateDbContext();
            var user = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

            return user?.UserType ?? UserType.User;
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
        public async Task<User> GetRandomUserAsync()
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

            var filterDate = DateTime.UtcNow.AddDays(-3);
            var users = db.Users
                .AsQueryable()
                .Where(w => w.Blocked != true &&
                            w.LastUsed > filterDate).ToList();

            var rand = new Random();
            var user = users[rand.Next(users.Count)];

            user.Featured = true;

            db.Entry(user).State = EntityState.Modified;
            await db.SaveChangesAsync();

            return user;
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
            var userType = await GetRankAsync(context.User);

            var title = name;

            title += $" {userType.UserTypeToIcon()}";

            return title;
        }

        // Set LastFM Name
        public async Task SetLastFm(IUser discordUser, User newUserSettings, bool updateSessionKey = false)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var user = db.Users.FirstOrDefault(f => f.DiscordUserId == discordUser.Id);

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
                    SessionKeyLastFm = newUserSettings.SessionKeyLastFm,
                    PrivacyLevel = PrivacyLevel.Server
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

                db.Update(user);

                await db.SaveChangesAsync();
            }
        }

        // Set Privacy
        public async Task<PrivacyLevel> SetPrivacy(User userToUpdate, string[] extraOptions)
        {
            await using var db = this._contextFactory.CreateDbContext();

            if (extraOptions.Contains("global") || extraOptions.Contains("Global"))
            {
                userToUpdate.PrivacyLevel = PrivacyLevel.Global;
            }
            else if (extraOptions.Contains("server") || extraOptions.Contains("Server"))
            {
                userToUpdate.PrivacyLevel = PrivacyLevel.Server;
            }

            db.Update(userToUpdate);

            await db.SaveChangesAsync();

            return userToUpdate.PrivacyLevel;
        }

        public User SetSettings(User userSettings, string[] extraOptions)
        {
            extraOptions = extraOptions.Select(s => s.ToLower()).ToArray();
            if (extraOptions.Contains("embedfull") || extraOptions.Contains("ef"))
            {
                userSettings.FmEmbedType = FmEmbedType.EmbedFull;
            }
            else if (extraOptions.Contains("textmini") || extraOptions.Contains("tm"))
            {
                userSettings.FmEmbedType = FmEmbedType.TextMini;
            }
            else if (extraOptions.Contains("textfull") || extraOptions.Contains("tf"))
            {
                userSettings.FmEmbedType = FmEmbedType.TextFull;
            }
            else
            {
                userSettings.FmEmbedType = FmEmbedType.EmbedMini;
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

                await using var deleteFriends = new NpgsqlCommand($"DELETE FROM public.friends WHERE user_id = {user.UserId};", connection);
                await deleteFriends.ExecuteNonQueryAsync();

                await using var deleteOtherFriends = new NpgsqlCommand($"DELETE FROM public.friends WHERE friend_user_id = {user.UserId};", connection);
                await deleteOtherFriends.ExecuteNonQueryAsync();

                db.Users.Remove(user);

                await db.SaveChangesAsync();
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while deleting user!");
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
                .Where(w => w.MissingParametersErrorCount >= 1 && w.Updated > DateTime.UtcNow.AddDays(-3))
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
