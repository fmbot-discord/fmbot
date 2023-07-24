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
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using PostgreSQLCopyHelper;
using Serilog;

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
            Thread.Sleep(15000);
        }

        var concurrencyCacheKey = $"index-started-{queueItem.UserId}";
        this._cache.Set(concurrencyCacheKey, true, TimeSpan.FromMinutes(3));

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

        Log.Information($"Index: Starting for {user.UserNameLastFM}");
        var now = DateTime.UtcNow;

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(user.UserNameLastFM);
        if (userInfo?.Registered != null)
        {
            await UserRepository.SetUserSignUpTime(user, userInfo.Registered, connection, userInfo.Subscriber);
        }

        await SetUserPlaycount(user, connection);

        try
        {
            var plays = await GetPlaysForUserFromLastFm(user);
            await PlayRepository.ReplaceAllPlays(plays, user.UserId, connection);

            var artists = await GetArtistsForUserFromLastFm(user);
            var artistsInserted = await ArtistRepository.AddOrReplaceUserArtistsInDatabase(artists, user.UserId, connection);

            var albums = await GetAlbumsForUserFromLastFm(user);
            var albumsInserted = await AlbumRepository.AddOrReplaceUserAlbumsInDatabase(albums, user.UserId, connection);

            var tracks = await GetTracksForUserFromLastFm(user);
            var tracksInserted = await TrackRepository.AddOrReplaceUserTracksInDatabase(tracks, user.UserId, connection);

            Log.Information("Index complete for {userId}: Artists found {artistCount}, inserted {artistsInserted} - " +
                            "Albums found {albumCount}, inserted {albumsInserted} - "+
                            "Tracks found {trackCount}, inserted {tracksInserted}",
                user.UserId, artists.Count, artistsInserted, albums.Count, albumsInserted, tracks.Count, tracksInserted);

            var latestScrobbleDate = await GetLatestScrobbleDate(user);

            await UserRepository.SetUserIndexTime(user.UserId, now, latestScrobbleDate, connection);

            var stats = new IndexedUserStats
            {
                PlayCount = plays.Count,
                ArtistCount = artists.Count,
                AlbumCount = albums.Count,
                TrackCount = tracks.Count
            };

            var importUser = await UserRepository.GetImportUserForUserId(user.UserId, connection);
            if (importUser != null)
            {
                var finalPlays = await PlayRepository.GetUserPlays(user.UserId, connection, 9999999);
                var filteredPlays = PlayDataSourceRepository.GetFinalUserPlays(importUser, finalPlays);

                stats.ImportCount = finalPlays.Count(w => w.PlaySource != PlaySource.LastFm);
                stats.TotalCount = filteredPlays.Count;
            }

            await connection.CloseAsync();

            Statistics.IndexedUsers.Inc();
            this._cache.Remove(concurrencyCacheKey);

            return stats;
        }
        catch (Exception e)
        {
            Log.Error("Index: Error happened!", e);
            throw;
        }
    }

    public async Task RecalculateTopLists(User user)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var artists = await GetArtistsForUserFromLastFm(user);
        await ArtistRepository.AddOrReplaceUserArtistsInDatabase(artists, user.UserId, connection);

        var albums = await GetAlbumsForUserFromLastFm(user);
        await AlbumRepository.AddOrReplaceUserAlbumsInDatabase(albums, user.UserId, connection);

        var tracks = await GetTracksForUserFromLastFm(user);
        await TrackRepository.AddOrReplaceUserTracksInDatabase(tracks, user.UserId, connection);
    }

    private async Task<IReadOnlyList<UserArtist>> GetArtistsForUserFromLastFm(User user)
    {
        Log.Information($"Getting artists for user {user.UserNameLastFM}");

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
        Log.Information($"Getting plays for user {user.UserNameLastFM}");

        var pages = UserHasHigherIndexLimit(user) ? 750 : 25;

        var recentPlays = await this._dataSourceFactory.GetRecentTracksAsync(user.UserNameLastFM, 1000,
            sessionKey: user.SessionKeyLastFm, amountOfPages: pages);

        if (!recentPlays.Success || recentPlays.Content.RecentTracks.Count == 0)
        {
            return new List<UserPlay>();
        }

        var indexLimitFilter = DateTime.UtcNow.AddYears(-1).AddDays(-180);
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

    private async Task<IReadOnlyList<UserAlbum>> GetAlbumsForUserFromLastFm(User user)
    {
        Log.Information($"Getting albums for user {user.UserNameLastFM}");

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

    private async Task<IReadOnlyList<UserTrack>> GetTracksForUserFromLastFm(User user)
    {
        Log.Information($"Getting tracks for user {user.UserNameLastFM}");

        var indexLimit = UserHasHigherIndexLimit(user) ? 200 : 6;

        var trackResult = await this._dataSourceFactory.GetTopTracksAsync(user.UserNameLastFM,
            new TimeSettingsModel { TimePeriod = TimePeriod.AllTime, PlayDays = 99999, StartDateTime = user.RegisteredLastFm, ApiParameter = "overall"}, 1000, indexLimit);

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

    private async Task<DateTime> GetLatestScrobbleDate(User user)
    {
        var recentTracks = await this._dataSourceFactory.GetRecentTracksAsync(user.UserNameLastFM, count: 2);

        if (!recentTracks.Success ||
            recentTracks.Content?.RecentTracks == null ||
            !recentTracks.Content.RecentTracks.Any(a => a.TimePlayed.HasValue))
        {
            Log.Information("Recent track call to get latest scrobble date failed!");
            return DateTime.UtcNow;
        }

        return recentTracks.Content.RecentTracks.First(f => f.TimePlayed != null).TimePlayed.Value;
    }

    private async Task SetUserPlaycount(User user, NpgsqlConnection connection)
    {
        var recentTracks = await this._dataSourceFactory.GetRecentTracksAsync(
            user.UserNameLastFM,
            count: 1,
            useCache: false,
            user.SessionKeyLastFm);

        if (recentTracks.Success)
        {
            await using var setPlaycount = new NpgsqlCommand($"UPDATE public.users SET total_playcount = {recentTracks.Content.TotalAmount} WHERE user_id = {user.UserId};", connection);

            await setPlaycount.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
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

    public async Task UpdateGuildUser(IGuildUser discordGuildUser, int userId, Persistence.Domain.Models.Guild guild)
    {
        try
        {
            if (discordGuildUser == null)
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

    public async Task AddOrUpdateGuildUser(IGuildUser discordGuildUser)
    {
        try
        {
            if (!PublicProperties.RegisteredUsers.TryGetValue(discordGuildUser.Id, out var userId))
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
            return await UserRepository.SetUserSignUpTime(user, userInfo.Registered, connection, userInfo.Subscriber);
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

        var recentlyUsed = DateTime.UtcNow.AddDays(-7);
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
