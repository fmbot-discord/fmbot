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
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

namespace FMBot.Bot.Services;

public class UserService
{
    private readonly IMemoryCache _cache;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly LastFmRepository _lastFmRepository;
    private readonly BotSettings _botSettings;

    public UserService(IMemoryCache cache, IDbContextFactory<FMBotDbContext> contextFactory, LastFmRepository lastFmRepository, IOptions<BotSettings> botSettings)
    {
        this._cache = cache;
        this._contextFactory = contextFactory;
        this._lastFmRepository = lastFmRepository;
        this._botSettings = botSettings.Value;
    }

    public async Task<bool> UserRegisteredAsync(IUser discordUser)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var isRegistered = await db.Users
            .AsNoTracking()
            .AnyAsync(f => f.DiscordUserId == discordUser.Id);

        return isRegistered;
    }

    public async Task<bool> UserBlockedAsync(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        const string cacheKey = "blocked-users";

        if (this._cache.TryGetValue(cacheKey, out IReadOnlyList<User> blockedUsers))
        {
            return blockedUsers.Select(s => s.DiscordUserId).Contains(discordUserId);
        }

        blockedUsers = await db.Users
            .AsNoTracking()
            .Where(w => w.Blocked == true)
            .ToListAsync();

        this._cache.Set(cacheKey, blockedUsers, TimeSpan.FromMinutes(15));

        return blockedUsers.Select(s => s.DiscordUserId).Contains(discordUserId);
    }

    public async Task<bool> UserHasSessionAsync(IUser discordUser)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

        if (user == null)
        {
            return false;
        }

        return !string.IsNullOrEmpty(user.SessionKeyLastFm);
    }

    public async Task UpdateUserLastUsedAsync(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

        if (user != null)
        {
            user.LastUsed = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

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

    public async Task<User> GetUserSettingsAsync(IUser discordUser)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);
    }

    public async Task<User> GetUserWithDiscogs(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .Include(i => i.UserDiscogs)
            .Include(i => i.DiscogsReleases)
            .ThenInclude(i => i.Release)
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);
    }

    public async Task<User> GetUserWithFriendsAsync(IUser discordUser)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .Include(i => i.Friends)
            .ThenInclude(i => i.FriendUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);
    }

    public async Task<User> GetUserAsync(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);
    }

    public async Task<User> GetUserForIdAsync(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.UserId == userId);
    }

    // User settings
    public async Task<User> GetFullUserAsync(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var query = db.Users
            .Include(i => i.UserDiscogs)
            .Include(i => i.Friends)
            .Include(i => i.FriendedByUsers);

        return await query
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);
    }

    // Discord nickname/username
    public static async Task<string> GetNameAsync(IGuild guild, IUser user)
    {
        if (guild == null)
        {
            return user.Username;
        }

        var guildUser = await guild.GetUserAsync(user.Id);

        return guildUser?.Nickname ?? user.Username;
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

    // UserTitle
    public async Task<string> GetUserTitleAsync(ICommandContext context)
    {
        var name = await GetNameAsync(context.Guild, context.User);
        var userType = await GetRankAsync(context.User);

        var title = name;

        title += $"{userType.UserTypeToIcon()}";

        return title;
    }

    // UserTitle
    public async Task<string> GetUserTitleAsync(IGuild guild, IUser user)
    {
        var name = await GetNameAsync(guild, user);
        var userType = await GetRankAsync(user);

        var title = name;

        title += $"{userType.UserTypeToIcon()}";

        return title;
    }

    // Set LastFM Name
    public async Task SetLastFm(IUser discordUser, User newUserSettings, bool updateSessionKey = false)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

        if (user == null)
        {
            var newUser = new User
            {
                DiscordUserId = discordUser.Id,
                UserType = UserType.User,
                UserNameLastFM = newUserSettings.UserNameLastFM,
                FmEmbedType = newUserSettings.FmEmbedType,
                FmCountType = newUserSettings.FmCountType,
                SessionKeyLastFm = newUserSettings.SessionKeyLastFm,
                DataSource = DataSource.LastFm,
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
            user.Mode = newUserSettings.Mode;
            if (updateSessionKey)
            {
                user.SessionKeyLastFm = newUserSettings.SessionKeyLastFm;
            }

            db.Update(user);

            await db.SaveChangesAsync();
        }
    }

    public async Task<bool> GetAndStoreAuthSession(IUser contextUser, string token)
    {
        Log.Information("LastfmAuth: Login session starting for {user} | {discordUserId}", contextUser.Username, contextUser.Id);

        var loginDelay = 8000;
        for (var i = 0; i < 11; i++)
        {
            await Task.Delay(loginDelay);

            var authSession = await this._lastFmRepository.GetAuthSession(token);

            if (authSession.Success)
            {
                var userSettings = new User
                {
                    UserNameLastFM = authSession.Content.Session.Name,
                    DataSource = DataSource.LastFm,
                    SessionKeyLastFm = authSession.Content.Session.Key
                };

                Log.Information("LastfmAuth: User {userName} logged in with auth session (discordUserId: {discordUserId})", authSession.Content.Session.Name, contextUser.Id);
                await SetLastFm(contextUser, userSettings, true);
                return true;
            }

            if (!authSession.Success && i == 10)
            {
                Log.Information("LastfmAuth: Login timed out or auth not successful (discordUserId: {discordUserId})", contextUser.Id);
                return false;
            }
            if (!authSession.Success)
            {
                loginDelay += 3000;
            }
        }

        return false;
    }

    public async Task<PrivacyLevel> SetPrivacy(User userToUpdate, string[] extraOptions)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

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

    public async Task<PrivacyLevel> SetPrivacyLevel(User userToUpdate, PrivacyLevel privacyLevel)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        userToUpdate.PrivacyLevel = privacyLevel;

        db.Update(userToUpdate);

        await db.SaveChangesAsync();

        return userToUpdate.PrivacyLevel;
    }

    public static User SetSettings(User userSettings, string[] extraOptions)
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
        else if (extraOptions.Contains("embedtiny") || extraOptions.Contains("et"))
        {
            userSettings.FmEmbedType = FmEmbedType.EmbedTiny;
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

    public static User SetWkMode(User userSettings, string[] extraOptions)
    {
        extraOptions = extraOptions.Select(s => s.ToLower()).ToArray();
        if (extraOptions.Contains("image") || extraOptions.Contains("img"))
        {
            userSettings.Mode = WhoKnowsMode.Image;
        }
        else if (extraOptions.Contains("embed") || extraOptions.Contains("embd"))
        {
            userSettings.Mode = WhoKnowsMode.Embed;
        }

        return userSettings;
    }

    public async Task<User> SetSettings(User userToUpdate, FmEmbedType embedType, FmCountType? countType)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users.FirstAsync(f => f.UserId == userToUpdate.UserId);

        user.FmEmbedType = embedType;
        user.FmCountType = countType == FmCountType.None ? null : countType;

        db.Update(user);

        await db.SaveChangesAsync();

        return user;
    }

    public async Task<User> SetWkMode(User userToUpdate, WhoKnowsMode mode)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users.FirstAsync(f => f.UserId == userToUpdate.UserId);

        user.Mode = mode;

        db.Update(user);

        await db.SaveChangesAsync();

        return user;
    }

    // Remove user
    public async Task DeleteUser(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        try
        {
            var user = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.UserId == userId);

            this._cache.Remove($"user-settings-{user.DiscordUserId}");
            this._cache.Remove($"user-isRegistered-{user.DiscordUserId}");

            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            await using var deleteRelatedTables = new NpgsqlCommand(
                $"DELETE FROM public.user_artists WHERE user_id = {user.UserId}; " +
                $"DELETE FROM public.user_albums WHERE user_id = {user.UserId}; " +
                $"DELETE FROM public.user_tracks WHERE user_id = {user.UserId}; " +
                $"DELETE FROM public.friends WHERE user_id = {user.UserId} OR friend_user_id = {user.UserId}; " +
                $"DELETE FROM public.featured_logs WHERE user_id = {user.UserId}; ",
                connection);

            await deleteRelatedTables.ExecuteNonQueryAsync();

            db.Users.Remove(user);

            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            Log.Error(e, "Error while deleting user!");
        }
    }

    public async Task<bool?> ToggleRymAsync(User user)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        if (user.RymEnabled != true)
        {
            user.RymEnabled = true;
        }
        else
        {
            user.RymEnabled = false;
        }

        db.Update(user);

        await db.SaveChangesAsync();

        return user.RymEnabled;
    }

    public async Task<bool?> ToggleBotScrobblingAsync(User user, string option)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        if (option != null && (option.ToLower() == "on" || option.ToLower() == "true" || option.ToLower() == "yes"))
        {
            user.MusicBotTrackingDisabled = false;
        }
        else if (option != null && (option.ToLower() == "off" || option.ToLower() == "false" || option.ToLower() == "no"))
        {
            user.MusicBotTrackingDisabled = true;
        }

        db.Update(user);

        await db.SaveChangesAsync();

        return user.MusicBotTrackingDisabled;
    }

    public async Task<int> GetTotalUserCountAsync()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsQueryable()
            .CountAsync();
    }

    public async Task<int> GetTotalAuthorizedUserCountAsync()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsQueryable()
            .Where(w => w.SessionKeyLastFm != null)
            .CountAsync();
    }

    public async Task<int> DeleteInactiveUsers()
    {
        var deletedInactiveUsers = 0;

        await using var db = await this._contextFactory.CreateDbContextAsync();
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
                if (!await this._lastFmRepository.LastFmUserExistsAsync(user.UserNameLastFM))
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
