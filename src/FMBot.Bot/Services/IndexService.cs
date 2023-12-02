using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Discord;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Genius.Models.Song;
using Genius.Models.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using PostgreSQLCopyHelper;
using Serilog;
using User = FMBot.Persistence.Domain.Models.User;

namespace FMBot.Bot.Services;

public class IndexService : IIndexService
{
    private readonly IUserIndexQueue _userIndexQueue;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly IMemoryCache _cache;
    private readonly BotSettings _botSettings;
    private readonly IDataSourceFactory _dataSourceFactory;

    public IndexService(IUserIndexQueue userIndexQueue,
        IDbContextFactory<FMBotDbContext> contextFactory,
        IMemoryCache cache,
        IOptions<BotSettings> botSettings,
        IDataSourceFactory dataSourceFactory)
    {
        this._userIndexQueue = userIndexQueue;
        this._userIndexQueue.UsersToIndex.SubscribeAsync(OnNextAsync);
        this._contextFactory = contextFactory;
        this._cache = cache;
        this._dataSourceFactory = dataSourceFactory;
        this._botSettings = botSettings.Value;
    }

    private async Task OnNextAsync(IndexUserQueueItem user)
    {
        await IndexUser(user);
    }

    public void AddUsersToIndexQueue(IReadOnlyList<User> users)
    {
        Log.Information($"Adding {users.Count} users to index queue");

        this._userIndexQueue.Publish(users);
    }

    public async Task<IndexedUserStats> IndexUser(User user)
    {
        Log.Information("Starting index for {UserNameLastFM}", user.UserNameLastFM);

        if (!this._cache.TryGetValue($"index-started-{user.UserId}", out bool _))
        {
            return await IndexUser(new IndexUserQueueItem(user.UserId));
        }
        else
        {
            Log.Information("Index for {UserNameLastFM} already in progress, skipping.", user.UserNameLastFM);
        }

        return null;
    }

    public async Task<IndexedUserStats> IndexUser(IndexUserQueueItem queueItem)
    {
        if (queueItem.IndexQueue)
        {
            Thread.Sleep(16000);
        }

        this._cache.Set(IndexConcurrencyCacheKey(queueItem.UserId), true, TimeSpan.FromMinutes(3));

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(queueItem.UserId);

        if (queueItem.IndexQueue)
        {
            if (user == null)
            {
                return null;
            }
            if (user.LastIndexed > DateTime.UtcNow.AddHours(-24))
            {
                Log.Debug("Index: Skipped for {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
                return null;
            }
        }

        try
        {
            return await this.ModularUpdate(user, UpdateType.Full);
        }
        catch (Exception e)
        {
            Log.Error($"Index: Error happened! User {user.DiscordUserId} / {user.UserId} - {e.Message} - {e.InnerException} - {e.StackTrace}", e);
            throw;
        }
    }

    public static string IndexConcurrencyCacheKey(int userId)
    {
        return $"index-started-{userId}";
    }

    public bool IndexStarted(int userId)
    {
        if (this._cache.TryGetValue(IndexConcurrencyCacheKey(userId), out bool _))
        {
            return false;
        }

        this._cache.Set(IndexConcurrencyCacheKey(userId), true, TimeSpan.FromMinutes(3));
        return true;
    }

    public async Task<IndexedUserStats> ModularUpdate(User user, UpdateType updateType)
    {
        Log.Information("Index: {userId} / {discordUserId} / {UserNameLastFM} - Starting", user.UserId, user.DiscordUserId, user.UserNameLastFM);

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var stats = new IndexedUserStats();

        var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(user.UserNameLastFM);
        if (userInfo?.Registered != null)
        {
            await UserRepository.SetUserPlayStats(user, connection, userInfo);
        }
        else
        {
            Log.Warning("Index: {userId} / {discordUserId} / {UserNameLastFM} - Fetching UserInfo failed", user.UserId, user.DiscordUserId, user.UserNameLastFM);

            stats.UpdateError = true;
            stats.FailedUpdates = UpdateType.Full;

            this._cache.Remove(IndexConcurrencyCacheKey(user.UserId));
            return stats;
        }

        if (updateType.HasFlag(UpdateType.AllPlays) || updateType.HasFlag(UpdateType.Full))
        {
            var plays = await GetPlaysForUserFromLastFm(user);

            if (userInfo.Playcount >= 1000 && plays.Count < 200)
            {
                Log.Warning("Index: {userId} / {discordUserId} / {UserNameLastFM} - Fetching AllPlays failed - {playCount} expected, {fetchedPlayCount} fetched",
                    user.UserId, user.DiscordUserId, user.UserNameLastFM, userInfo.Playcount, plays.Count);

                stats.UpdateError = true;
                stats.FailedUpdates |= UpdateType.AllPlays;
            }
            else
            {
                await PlayRepository.ReplaceAllPlays(plays, user.UserId, connection);

                stats.PlayCount = plays.Count;

                await UserRepository.SetUserIndexTime(user.UserId, connection, plays);
            }
        }

        if (updateType.HasFlag(UpdateType.Artist) || updateType.HasFlag(UpdateType.Full))
        {
            var artists = await GetTopArtistsForUser(user);

            if (userInfo.ArtistCount >= 1000 && artists.Count < 200)
            {
                Log.Warning("Index: {userId} / {discordUserId} / {UserNameLastFM} - Fetching artists failed - {artistCount} expected, {fetchedArtistCount} fetched",
                    user.UserId, user.DiscordUserId, user.UserNameLastFM, userInfo.ArtistCount, artists.Count);

                stats.UpdateError = true;
                stats.FailedUpdates |= UpdateType.Artist;
            }
            else
            {
                await ArtistRepository.AddOrReplaceUserArtistsInDatabase(artists, user.UserId, connection);
                stats.ArtistCount = artists.Count;
            }
        }

        if (updateType.HasFlag(UpdateType.Albums) || updateType.HasFlag(UpdateType.Full))
        {
            var albums = await GetTopAlbumsForUser(user);

            if (userInfo.AlbumCount >= 1000 && albums.Count < 200)
            {
                Log.Warning("Index: {userId} / {discordUserId} / {UserNameLastFM} - Fetching albums failed - {albumCount} expected, {fetchedAlbumCount} fetched",
                    user.UserId, user.DiscordUserId, user.UserNameLastFM, userInfo.AlbumCount, albums.Count);

                stats.UpdateError = true;
                stats.FailedUpdates |= UpdateType.Albums;
            }
            else
            {
                await AlbumRepository.AddOrReplaceUserAlbumsInDatabase(albums, user.UserId, connection);

                stats.AlbumCount = albums.Count;
            }
        }

        if (updateType.HasFlag(UpdateType.Tracks) || updateType.HasFlag(UpdateType.Full))
        {
            var tracks = await GetTopTracksForUser(user);

            if (userInfo.TrackCount >= 1000 && tracks.Count < 200)
            {
                Log.Warning("Index: {userId} / {discordUserId} / {UserNameLastFM} - Fetching tracks failed - {trackCount} expected, {fetchedTrackCount} fetched",
                    user.UserId, user.DiscordUserId, user.UserNameLastFM, userInfo.TrackCount, tracks.Count);

                stats.UpdateError = true;
                stats.FailedUpdates |= UpdateType.Tracks;
            }
            else
            {
                await TrackRepository.AddOrReplaceUserTracksInDatabase(tracks, user.UserId, connection);

                stats.TrackCount = tracks.Count;
            }
        }

        var importUser = await UserRepository.GetImportUserForUserId(user.UserId, connection);
        if (importUser != null)
        {
            var finalPlays = await PlayRepository.GetAllUserPlays(user.UserId, connection);
            var filteredPlays = await PlayRepository.GetUserPlays(user.UserId, connection, user.DataSource);

            stats.ImportCount = finalPlays.Count(w => w.PlaySource != PlaySource.LastFm);
            stats.TotalCount = filteredPlays.Count;
        }

        await connection.CloseAsync();

        Statistics.IndexedUsers.Inc();

        this._cache.Remove(IndexConcurrencyCacheKey(user.UserId));

        return stats;
    }

    public async Task RecalculateTopLists(User user)
    {
        try
        {
            await this.ModularUpdate(user, UpdateType.Artist | UpdateType.Albums | UpdateType.Tracks);
        }
        catch (Exception e)
        {
            Log.Error("Index: Reculculate toplists error happened! User {userDiscordId} / {userId} - {exceptionMessage} - {innerException} - {stackTrace}", user.DiscordUserId, user.UserId, e.Message, e.InnerException, e.StackTrace, e);
            throw;
        }
    }

    private async Task<IReadOnlyList<UserArtist>> GetTopArtistsForUser(User user)
    {
        Log.Information("Index: {userId} / {discordUserId} / {UserNameLastFM} - Getting top artists",
            user.UserId, user.DiscordUserId, user.UserNameLastFM);

        var indexLimit = UserHasHigherIndexLimit(user) ? 200 : 4;

        var topArtists = await this._dataSourceFactory.GetTopArtistsAsync(user.UserNameLastFM,
            new TimeSettingsModel { TimePeriod = TimePeriod.AllTime, PlayDays = 99999, StartDateTime = user.RegisteredLastFm, ApiParameter = "overall" }, 1000, indexLimit);

        if (!topArtists.Success || topArtists.Content.TopArtists == null)
        {
            return new List<UserArtist>();
        }

        return topArtists.Content.TopArtists.Select(a => new UserArtist
        {
            Name = a.ArtistName,
            Playcount = (int)a.UserPlaycount,
            UserId = user.UserId
        }).ToList();
    }

    private async Task<IReadOnlyList<UserPlay>> GetPlaysForUserFromLastFm(User user)
    {
        Log.Information("Index: {userId} / {discordUserId} / {UserNameLastFM} - Getting plays",
            user.UserId, user.DiscordUserId, user.UserNameLastFM);

        var pages = UserHasHigherIndexLimit(user) ? 750 : 50;

        var recentPlays = await this._dataSourceFactory.GetRecentTracksAsync(user.UserNameLastFM, 1000,
            sessionKey: user.SessionKeyLastFm, amountOfPages: pages);

        if (!recentPlays.Success || recentPlays.Content.RecentTracks.Count == 0)
        {
            return new List<UserPlay>();
        }

        var indexLimitFilter = DateTime.UtcNow.AddYears(-2);
        return recentPlays.Content.RecentTracks
            .Where(w => !w.NowPlaying && w.TimePlayed.HasValue)
            .Where(w => UserHasHigherIndexLimit(user) || w.TimePlayed > indexLimitFilter)
            .Select(t => new UserPlay
            {
                TrackName = t.TrackName,
                AlbumName = t.AlbumName,
                ArtistName = t.ArtistName,
                TimePlayed = t.TimePlayed.Value,
                UserId = user.UserId,
                PlaySource = PlaySource.LastFm
            }).ToList();
    }

    private async Task<IReadOnlyList<UserAlbum>> GetTopAlbumsForUser(User user)
    {
        Log.Information("Index: {userId} / {discordUserId} / {UserNameLastFM} - Getting top albums",
            user.UserId, user.DiscordUserId, user.UserNameLastFM);

        var indexLimit = UserHasHigherIndexLimit(user) ? 200 : 5;

        var topAlbumsList = new List<TopAlbum>();

        var topAlbums =
            await this._dataSourceFactory.GetTopAlbumsAsync(user.UserNameLastFM,
                new TimeSettingsModel { TimePeriod = TimePeriod.AllTime, PlayDays = 99999, StartDateTime = user.RegisteredLastFm, ApiParameter = "overall" }, 1000, indexLimit);

        if (!topAlbums.Success || topAlbums.Content.TopAlbums == null)
        {
            return new List<UserAlbum>();
        }

        return topAlbums.Content.TopAlbums.Select(a => new UserAlbum
        {
            Name = a.AlbumName,
            ArtistName = a.ArtistName,
            Playcount = (int)a.UserPlaycount,
            UserId = user.UserId
        }).ToList();
    }

    private async Task<IReadOnlyList<UserTrack>> GetTopTracksForUser(User user)
    {
        Log.Information("Index: {userId} / {discordUserId} / {UserNameLastFM} - Getting top tracks",
            user.UserId, user.DiscordUserId, user.UserNameLastFM);

        var indexLimit = UserHasHigherIndexLimit(user) ? 200 : 6;

        var trackResult = await this._dataSourceFactory.GetTopTracksAsync(user.UserNameLastFM,
            new TimeSettingsModel { TimePeriod = TimePeriod.AllTime, PlayDays = 99999, StartDateTime = user.RegisteredLastFm, ApiParameter = "overall" }, 1000, indexLimit);

        if (!trackResult.Success || trackResult.Content.TopTracks.Count == 0)
        {
            return new List<UserTrack>();
        }

        return trackResult.Content.TopTracks.Select(a => new UserTrack
        {
            Name = a.TrackName,
            ArtistName = a.ArtistName,
            Playcount = Convert.ToInt32(a.UserPlaycount),
            UserId = user.UserId
        }).ToList();
    }

    private static bool UserHasHigherIndexLimit(User user)
    {
        return user.UserType switch
        {
            UserType.Supporter => true,
            UserType.Contributor => true,
            UserType.Admin => true,
            UserType.Owner => true,
            UserType.User => false,
            _ => false
        };
    }

    public async Task<int> StoreGuildUsers(IGuild discordGuild, IReadOnlyCollection<IGuildUser> discordGuildUsers)
    {
        var userIds = discordGuildUsers.Select(s => s.Id).ToList();

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .Include(i => i.GuildUsers)
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuild.Id);

        if (existingGuild == null)
        {
            var newGuild = new Persistence.Domain.Models.Guild
            {
                DiscordGuildId = discordGuild.Id,
                Name = discordGuild.Name
            };

            await db.Guilds.AddAsync(newGuild);

            await db.SaveChangesAsync();

            existingGuild = await db.Guilds
                .Include(i => i.GuildUsers)
                .FirstAsync(f => f.DiscordGuildId == discordGuild.Id);
        }

        var users = await db.Users
            .AsQueryable()
            .Where(w => userIds.Contains(w.DiscordUserId))
            .Select(s => new GuildUser
            {
                GuildId = existingGuild.GuildId,
                UserId = s.UserId,
                User = s,
            })
            .ToListAsync();

        foreach (var user in users)
        {
            var discordUser = discordGuildUsers.First(f => f.Id == user.User.DiscordUserId);

            user.UserName = discordUser.DisplayName;
            user.Bot = discordUser.IsBot;

            if (PublicProperties.PremiumServers.ContainsKey(discordGuild.Id))
            {
                user.Roles = discordUser.RoleIds.ToArray();
            }

            if (existingGuild.GuildUsers != null && existingGuild.GuildUsers.Any())
            {
                var existingGuildUser = existingGuild.GuildUsers.FirstOrDefault(f => f.UserId == user.UserId);
                if (existingGuildUser != null)
                {
                    user.LastMessage = existingGuildUser.LastMessage;
                }
            }
        }

        var connString = db.Database.GetDbConnection().ConnectionString;
        var copyHelper = new PostgreSQLCopyHelper<GuildUser>("public", "guild_users")
            .MapInteger("guild_id", x => x.GuildId)
            .MapInteger("user_id", x => x.UserId)
            .MapText("user_name", x => x.UserName)
            .MapBoolean("bot", x => x.Bot == true)
            .MapArray("roles", x => x.Roles?.Select(s => (decimal)s).ToArray())
            .MapTimeStampTz("last_message", x => x.LastMessage.HasValue ? DateTime.SpecifyKind(x.LastMessage.Value, DateTimeKind.Utc) : null);

        await using var connection = new NpgsqlConnection(connString);
        await connection.OpenAsync();

        await using var deleteCurrentUsers = new NpgsqlCommand($"DELETE FROM public.guild_users WHERE guild_id = {existingGuild.GuildId};", connection);
        await deleteCurrentUsers.ExecuteNonQueryAsync();

        await copyHelper.SaveAllAsync(connection, users);

        Log.Information("GuildUserUpdate: Stored guild users for guild with id {guildId}", existingGuild.GuildId);

        await connection.CloseAsync();

        return users.Count();
    }

    public async Task<GuildUser> GetOrAddUserToGuild(
        IDictionary<int, FullGuildUser> guildUsers,
        Persistence.Domain.Models.Guild guild,
        IGuildUser discordGuildUser,
        User user)
    {
        try
        {
            if (!guildUsers.TryGetValue(user.UserId, out var guildUser))
            {
                var guildUserToAdd = new GuildUser
                {
                    GuildId = guild.GuildId,
                    UserId = user.UserId,
                    UserName = discordGuildUser.DisplayName
                };

                if (PublicProperties.PremiumServers.ContainsKey(guild.DiscordGuildId))
                {
                    guildUserToAdd.Roles = discordGuildUser.RoleIds.ToArray();
                }

                await AddGuildUserToDatabase(guildUserToAdd);

                guildUserToAdd.User = user;

                return guildUserToAdd;
            }
            else
            {
                guildUser.UserName = discordGuildUser.DisplayName;

                return new GuildUser
                {
                    GuildId = guild.GuildId,
                    UserId = user.UserId,
                    UserName = discordGuildUser.DisplayName,
                    Roles = discordGuildUser.RoleIds.ToArray(),
                    Bot = false,
                    LastMessage = DateTime.UtcNow,
                    User = user
                };
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "GuildUserUpdate: Error while attempting to add user {userId} to guild {guildId}", user.UserId, guild.GuildId);
            return new GuildUser
            {
                GuildId = guild.GuildId,
                UserId = user.UserId,
                User = user,
                UserName = discordGuildUser?.DisplayName ?? user.UserNameLastFM
            };
        }
    }

    public async Task AddGuildUserToDatabase(GuildUser guildUserToAdd)
    {
        const string sql = "INSERT INTO guild_users (guild_id, user_id, user_name, bot, roles, last_message) " +
                           "VALUES (@guildId, @userId, @userName, false, @roles, @lastMessage) " +
                           "ON CONFLICT DO NOTHING";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync(sql, new
        {
            guildId = guildUserToAdd.GuildId,
            userId = guildUserToAdd.UserId,
            userName = guildUserToAdd.UserName,
            roles = guildUserToAdd.Roles?.Select(s => (decimal)s).ToArray(),
            lastMessage = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
        });

        Log.Information("Added user {guildUserName} | {userId} to guild {guildName}", guildUserToAdd.UserName, guildUserToAdd.UserId, guildUserToAdd.GuildId);
    }

    public async Task UpdateGuildUser(IDictionary<int, FullGuildUser> fullGuildUsers, IGuildUser discordGuildUser,
        int userId, Persistence.Domain.Models.Guild guild)
    {
        try
        {
            if (discordGuildUser == null)
            {
                return;
            }

            fullGuildUsers.TryGetValue(userId, out var existingGuildUser);
            if (existingGuildUser != null &&
                existingGuildUser.UserName == discordGuildUser.DisplayName &&
                !PublicProperties.PremiumServers.ContainsKey(guild.DiscordGuildId))
            {
                return;
            }

            const string sql = "UPDATE guild_users " +
                               "SET user_name =  @UserName, " +
                               "roles =  @Roles " +
                               "WHERE guild_id = @GuildId AND user_id = @UserId ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var dto = new IndexedUserUpdateDto
            {
                UserName = discordGuildUser.DisplayName,
                GuildId = guild.GuildId,
                UserId = userId
            };

            if (PublicProperties.PremiumServers.ContainsKey(guild.DiscordGuildId))
            {
                dto.Roles = discordGuildUser.RoleIds.Select(s => (decimal)s).ToArray();
            }

            await connection.ExecuteAsync(sql, dto);
        }
        catch (Exception e)
        {
            Log.Error(e, "GuildUserUpdate: Exception in UpdateUser!");
        }
    }

    public async Task AddOrUpdateGuildUser(IGuildUser discordGuildUser, bool checkIfRegistered = true)
    {
        try
        {
            if (!PublicProperties.RegisteredUsers.TryGetValue(discordGuildUser.Id, out var userId) && checkIfRegistered)
            {
                return;
            }

            await using var db = await this._contextFactory.CreateDbContextAsync();

            var guild = await db.Guilds
                .Include(i => i.GuildUsers.Where(w => w.UserId == userId))
                .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildUser.GuildId);

            if (guild?.GuildUsers == null || !guild.GuildUsers.Any())
            {
                return;
            }

            var existingGuildUser = guild.GuildUsers.FirstOrDefault(f => f.UserId == userId);

            if (existingGuildUser == null)
            {
                var newGuildUser = new GuildUser
                {
                    Bot = false,
                    GuildId = guild.GuildId,
                    UserId = userId,
                    UserName = discordGuildUser?.DisplayName,
                };

                if (PublicProperties.PremiumServers.ContainsKey(guild.DiscordGuildId))
                {
                    newGuildUser.Roles = discordGuildUser.RoleIds.ToArray();
                }

                await AddGuildUserToDatabase(newGuildUser);
                return;
            }

            const string sql = "UPDATE guild_users " +
                               "SET user_name =  @UserName, " +
                               "roles =  @Roles " +
                               "WHERE guild_id = @guildId AND user_id = @userId ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var dto = new IndexedUserUpdateDto
            {
                UserName = discordGuildUser.DisplayName,
                GuildId = guild.GuildId,
                UserId = userId
            };

            if (PublicProperties.PremiumServers.ContainsKey(guild.DiscordGuildId))
            {
                dto.Roles = discordGuildUser.RoleIds.Select(s => (decimal)s).ToArray();
            }

            await connection.ExecuteAsync(sql, dto);
        }
        catch (Exception e)
        {
            Log.Error(e, "GuildUserUpdate: Exception in UpdateDiscordUser!");
        }
    }

    public async Task RemoveUserFromGuild(ulong discordUserId, ulong discordGuildId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var userThatLeft = await db.Users
            .Include(i => i.GuildUsers)
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

        if (userThatLeft == null)
        {
            return;
        }

        var guild = await db.Guilds
            .Include(i => i.GuildUsers)
            .FirstOrDefaultAsync(f => f.DiscordGuildId == discordGuildId);

        if (guild?.GuildUsers != null && guild.GuildUsers.Any() && guild.GuildUsers.Select(g => g.UserId).Contains(userThatLeft.UserId))
        {
            var guildUser = guild
                .GuildUsers
                .FirstOrDefault(f => f.UserId == userThatLeft.UserId && f.GuildId == guild.GuildId);

            if (guildUser != null)
            {
                db.GuildUsers.Remove(guildUser);

                await db.SaveChangesAsync();
            }
        }
    }

    public async Task<DateTime?> AddUserRegisteredLfmDate(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .Include(i => i.GuildUsers)
            .FirstOrDefaultAsync(f => f.UserId == userId);

        if (user == null)
        {
            return null;
        }

        return await SetUserSignUpTime(user);
    }

    private async Task<DateTime?> SetUserSignUpTime(User user)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(user.UserNameLastFM);
        if (userInfo?.Registered != null)
        {
            return await UserRepository.SetUserPlayStats(user, connection, userInfo);
        }

        return null;
    }

    public async Task<IReadOnlyList<User>> GetUsersToFullyUpdate(IReadOnlyCollection<IGuildUser> discordGuildUsers)
    {
        var userIds = discordGuildUsers.Select(s => s.Id).ToList();

        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsQueryable()
            .Where(w => userIds.Contains(w.DiscordUserId) &&
                        (w.LastIndexed == null || w.LastUpdated == null))
            .ToListAsync();
    }

    public async Task<int> GetIndexedUsersCount(IReadOnlyCollection<IGuildUser> discordGuildUsers)
    {
        var userIds = discordGuildUsers.Select(s => s.Id).ToList();

        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsQueryable()
            .Where(w => userIds.Contains(w.DiscordUserId)
                        && w.LastIndexed != null)
            .CountAsync();
    }

    public async Task<IReadOnlyList<User>> GetOutdatedUsers(DateTime timeLastIndexed)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var recentlyUsed = DateTime.UtcNow.AddDays(-15);
        return await db.Users
            .AsQueryable()
            .Where(f => f.LastIndexed != null &&
                        f.LastUpdated != null &&
                        f.LastUsed >= recentlyUsed &&
                        f.LastIndexed <= timeLastIndexed)
            .OrderBy(o => o.LastUsed)
            .ToListAsync();
    }
}
