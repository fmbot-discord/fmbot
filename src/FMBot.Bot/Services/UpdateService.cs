using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Flags;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

namespace FMBot.Bot.Services;

public class UpdateService : IUpdateService
{
    private readonly IUserUpdateQueue _userUpdateQueue;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly IMemoryCache _cache;
    private readonly BotSettings _botSettings;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly SmallIndexRepository _smallIndexRepository;
    private readonly AliasService _aliasService;

    public UpdateService(IUserUpdateQueue userUpdateQueue,
        IDbContextFactory<FMBotDbContext> contextFactory,
        IMemoryCache cache,
        IOptions<BotSettings> botSettings,
        IDataSourceFactory dataSourceFactory,
        SmallIndexRepository smallIndexRepository,
        AliasService aliasService)
    {
        this._userUpdateQueue = userUpdateQueue;
        this._userUpdateQueue.UsersToUpdate.SubscribeAsync(OnNextAsync);
        this._contextFactory = contextFactory;
        this._cache = cache;
        this._dataSourceFactory = dataSourceFactory;
        this._smallIndexRepository = smallIndexRepository;
        this._aliasService = aliasService;
        this._botSettings = botSettings.Value;
    }

    private async Task OnNextAsync(UpdateUserQueueItem user)
    {
        await this.UpdateUser(user);
    }

    public void AddUsersToUpdateQueue(IReadOnlyList<User> users)
    {
        Log.Information($"Adding {users.Count} users to update queue");

        this._userUpdateQueue.Publish(users.ToList());
    }

    public async Task<int> UpdateUser(User user)
    {
        var updatedUser = await this.UpdateUser(new UpdateUserQueueItem(user.UserId));
        return (int)updatedUser.Content.NewRecentTracksAmount;
    }

    public async Task<Response<RecentTrackList>> UpdateUserAndGetRecentTracks(User user, bool bypassIndexPending = false)
    {
        if (this._cache.TryGetValue(IndexService.IndexConcurrencyCacheKey(user.UserId), out bool _) && !bypassIndexPending)
        {
            return new Response<RecentTrackList>
            {
                Success = false,
                Message =
                    "All your data is still being fetched from Last.fm for .fmbot. Please wait a moment for this to complete and try again.",
            };
        }

        return await this.UpdateUser(new UpdateUserQueueItem(user.UserId));
    }

    public async Task<IReadOnlyList<User>> GetOutdatedUsers(DateTime timeAuthorizedLastUpdated, DateTime timeUnauthorizedFilter)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var lastUsed = DateTime.UtcNow.AddMonths(-3);
        return await db.Users
            .AsQueryable()
            .Where(f => f.LastIndexed != null &&
                        f.LastUpdated != null &&
                        f.LastUsed != null &&
                        f.LastUsed > lastUsed &&
                        (f.SessionKeyLastFm != null && f.LastUpdated <= timeAuthorizedLastUpdated ||
                         f.SessionKeyLastFm == null && f.LastUpdated <= timeUnauthorizedFilter))
            .OrderBy(o => o.LastUpdated)
            .ToListAsync();
    }

    public async Task<Response<RecentTrackList>> UpdateUser(UpdateUserQueueItem queueItem)
    {
        await this._aliasService.CacheArtistAliases();
        if (queueItem.UpdateQueue)
        {
            Thread.Sleep(1200);
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(queueItem.UserId);

        if (queueItem.UpdateQueue)
        {
            if (user == null)
            {
                return null;
            }
            if (user.LastUpdated > DateTime.UtcNow.AddHours(-44))
            {
                Log.Debug("Update: Skipped for {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
                return null;
            }
        }

        Log.Debug("Update: Started on {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);

        string sessionKey = null;
        if (!string.IsNullOrEmpty(user.SessionKeyLastFm))
        {
            sessionKey = user.SessionKeyLastFm;
        }

        var dateFromFilter = user.LastScrobbleUpdate?.AddHours(-3) ?? DateTime.UtcNow.AddDays(-14);
        var timeFrom = (long?)((DateTimeOffset)dateFromFilter).ToUnixTimeSeconds();

        var count = 1000;
        var pages = 3;
        var totalPlaycountCorrect = false;
        var now = DateTime.UtcNow;
        if (dateFromFilter > now.AddHours(-22) && queueItem.GetAccurateTotalPlaycount)
        {
            var playsToGet = (int)((DateTime.UtcNow - dateFromFilter).TotalMinutes / 3);
            count = 100 + playsToGet;
            pages = 1;
            timeFrom = null;
            totalPlaycountCorrect = true;
        }

        var recentTracks = await this._dataSourceFactory.GetRecentTracksAsync(
            user.UserNameLastFM,
            count,
            true,
            sessionKey,
            timeFrom,
            pages);

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        if (!recentTracks.Success)
        {
            Log.Information("Update: Something went wrong getting tracks for {userId} | {userNameLastFm} | {responseStatus}", user.UserId, user.UserNameLastFM, recentTracks.Error);

            if (recentTracks.Error != null)
            {
                await AddLfmInactiveUserLog(user, recentTracks.Error.Value);
            }

            await SetUserUpdateTime(user, DateTime.UtcNow.AddHours(-2), connection);

            await connection.CloseAsync();

            recentTracks.Content = new RecentTrackList
            {
                NewRecentTracksAmount = 0
            };
            return recentTracks;
        }

        AddRecentPlayToMemoryCache(user.UserId, recentTracks.Content.RecentTracks);

        if (!recentTracks.Content.RecentTracks.Any())
        {
            Log.Debug("Update: No new tracks for {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
            await SetUserUpdateTime(user, DateTime.UtcNow, connection);

            await connection.CloseAsync();

            recentTracks.Content.NewRecentTracksAmount = 0;
            return recentTracks;
        }

        try
        {
            var playUpdate = await PlayRepository.InsertLatestPlays(recentTracks.Content.RecentTracks, user.UserId, connection);

            recentTracks.Content.NewRecentTracksAmount = playUpdate.NewPlays.Count;
            recentTracks.Content.RemovedRecentTracksAmount = playUpdate.RemovedPlays.Count;

            if (!playUpdate.NewPlays.Any())
            {
                Log.Debug("Update: After local filter no new tracks for {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
                await SetUserUpdateTime(user, DateTime.UtcNow, connection);

                if (!user.TotalPlaycount.HasValue)
                {
                    recentTracks.Content.TotalAmount = await SetOrUpdateUserPlaycount(user, playUpdate.NewPlays.Count, connection, totalPlaycountCorrect ? recentTracks.Content.TotalAmount : null);
                }
                else if (totalPlaycountCorrect)
                {
                    await SetOrUpdateUserPlaycount(user, playUpdate.NewPlays.Count, connection, recentTracks.Content.TotalAmount);
                }
                else
                {
                    recentTracks.Content.TotalAmount = user.TotalPlaycount.Value;
                }

                await connection.CloseAsync();

                return recentTracks;
            }

            var cacheKey = $"{user.UserId}-update-in-progress";
            if (this._cache.TryGetValue(cacheKey, out bool _))
            {
                await connection.CloseAsync();

                return recentTracks;
            }

            this._cache.Set(cacheKey, true, TimeSpan.FromSeconds(1));

            recentTracks.Content.TotalAmount = await SetOrUpdateUserPlaycount(user, playUpdate.NewPlays.Count, connection, totalPlaycountCorrect ? recentTracks.Content.TotalAmount : null);

            var userArtists = await GetUserArtists(user.UserId, connection);
            var userAlbums = await GetUserAlbums(user.UserId, connection);
            var userTracks = await GetUserTracks(user.UserId, connection);

            await UpdateArtistsForUser(user, playUpdate.NewPlays, connection, userArtists);
            await UpdateAlbumsForUser(user, playUpdate.NewPlays, connection, userAlbums);
            await UpdateTracksForUser(user, playUpdate.NewPlays, connection, userTracks);

            var lastNewScrobble = playUpdate.NewPlays.MaxBy(o => o.TimePlayed);
            if (lastNewScrobble?.TimePlayed != null)
            {
                await SetUserLastScrobbleTime(user, lastNewScrobble.TimePlayed, connection);
            }

            await SetUserUpdateTime(user, DateTime.UtcNow, connection);

            this._cache.Remove($"user-{user.UserId}-topartists-alltime");
        }
        catch (Exception e)
        {
            Log.Error(e, "Update: Error in update process for user {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
        }

        Statistics.UpdatedUsers.Inc();

        await connection.CloseAsync();

        _ = SmallIndex(user);

        return recentTracks;
    }

    private async Task SmallIndex(User user)
    {
        if (user.DataSource != DataSource.LastFm)
        {
            return;
        }

        if (user.LastSmallIndexed > DateTime.UtcNow.AddDays(-15) || user.LastIndexed == null)
        {
            return;
        }

        if (RandomNumberGenerator.GetInt32(1, 10) != 2)
        {
            return;
        }

        await this._smallIndexRepository.SmallIndexUser(user);
    }

    private static async Task<IReadOnlyDictionary<string, UserArtist>> GetUserArtists(int userId, IDbConnection connection)
    {
        const string sql = "SELECT DISTINCT ON (LOWER(name)) user_id, name, playcount, user_artist_id " +
                           "FROM public.user_artists where user_id = @userId " +
                           "ORDER BY LOWER(name), playcount DESC";
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var result = await connection.QueryAsync<UserArtist>(sql, new
        {
            userId
        });

        return result.ToDictionary(d => d.Name.ToLower(), d => d);
    }

    private async Task UpdateArtistsForUser(User user,
        IEnumerable<UserPlay> newScrobbles,
        NpgsqlConnection connection,
        IReadOnlyDictionary<string, UserArtist> userArtists)
    {
        var updateExistingArtists = new StringBuilder();

        foreach (var artist in newScrobbles.GroupBy(g => g.ArtistName.ToLower()))
        {
            var alias = await this._aliasService.GetAlias(artist.Key.ToLower());

            var artistName = artist.First().ArtistName;
            if (alias != null && !alias.Options.HasFlag(AliasOption.DisableInPlays))
            {
                artistName = alias.ArtistName;
            }

            userArtists.TryGetValue(artistName.ToLower(), out var existingUserArtist);

            if (existingUserArtist != null)
            {
                updateExistingArtists.Append($"UPDATE public.user_artists SET playcount = {existingUserArtist.Playcount + artist.Count()} " +
                                             $"WHERE user_artist_id = {existingUserArtist.UserArtistId}; ");

                Log.Debug($"Updated artist {artistName} for {user.UserNameLastFM}");
            }
            else
            {
                await using var addUserArtist =
                    new NpgsqlCommand("INSERT INTO public.user_artists(user_id, name, playcount)" +
                                      "VALUES(@userId, @artistName, @artistPlaycount); ",
                        connection);

                addUserArtist.Parameters.AddWithValue("userId", user.UserId);
                addUserArtist.Parameters.AddWithValue("artistName", artistName);
                addUserArtist.Parameters.AddWithValue("artistPlaycount", artist.Count());

                Log.Debug($"Added artist {artistName} for {user.UserNameLastFM}");

                await addUserArtist.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        if (updateExistingArtists.Length > 0)
        {
            await using var updateUserArtist =
                new NpgsqlCommand(updateExistingArtists.ToString(), connection);

            await updateUserArtist.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        Log.Debug("Update: Updated artists for user {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);

    }

    private static async Task<IReadOnlyDictionary<string, List<UserAlbum>>> GetUserAlbums(int userId, IDbConnection connection)
    {
        const string sql = "SELECT * FROM public.user_albums where user_id = @userId";
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var result = await connection.QueryAsync<UserAlbum>(sql, new
        {
            userId
        });

        return result
            .GroupBy(g => g.ArtistName.ToLower())
            .ToDictionary(d => d.Key, d => d.ToList());
    }

    private async Task UpdateAlbumsForUser(User user,
        IEnumerable<UserPlay> newScrobbles,
        NpgsqlConnection connection,
        IReadOnlyDictionary<string, List<UserAlbum>> userAlbums)
    {
        var updateExistingAlbums = new StringBuilder();

        foreach (var album in newScrobbles
                     .Where(w => w.AlbumName != null)
                     .GroupBy(x => new
                     {
                         ArtistName = x.ArtistName.ToLower(),
                         AlbumName = x.AlbumName.ToLower()
                     }))
        {
            var alias = await this._aliasService.GetAlias(album.Key.ArtistName.ToLower());

            var artistName = album.First().ArtistName;
            if (alias != null && !alias.Options.HasFlag(AliasOption.DisableInPlays))
            {
                artistName = alias.ArtistName;
            }

            userAlbums.TryGetValue(artistName.ToLower(), out var userArtistAlbums);

            var existingUserAlbum =
                userArtistAlbums?.FirstOrDefault(a => a.Name.ToLower() == album.Key.AlbumName.ToLower());

            if (existingUserAlbum != null)
            {
                updateExistingAlbums.Append($"UPDATE public.user_albums SET playcount = {existingUserAlbum.Playcount + album.Count()} " +
                                            $"WHERE user_album_id = {existingUserAlbum.UserAlbumId}; ");

                Log.Debug($"Updated album {album.Key.AlbumName} for {user.UserNameLastFM} (+{album.Count()} plays)");
            }
            else
            {
                await using var addUserAlbum =
                    new NpgsqlCommand("INSERT INTO public.user_albums(user_id, name, artist_name, playcount)" +
                                      "VALUES(@userId, @albumName, @artistName, @albumPlaycount); ",
                        connection);

                var capitalizedAlbumName = album.First().AlbumName;

                addUserAlbum.Parameters.AddWithValue("userId", user.UserId);
                addUserAlbum.Parameters.AddWithValue("albumName", capitalizedAlbumName);
                addUserAlbum.Parameters.AddWithValue("artistName", artistName);
                addUserAlbum.Parameters.AddWithValue("albumPlaycount", album.Count());

                Log.Debug($"Added album {album.Key.ArtistName} - {capitalizedAlbumName} for {user.UserNameLastFM}");

                await addUserAlbum.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        if (updateExistingAlbums.Length > 0)
        {
            await using var updateUserAlbum =
                new NpgsqlCommand(updateExistingAlbums.ToString(), connection);

            await updateUserAlbum.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        Log.Debug("Update: Updated albums for user {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
    }

    private static async Task<IReadOnlyDictionary<string, List<UserTrack>>> GetUserTracks(int userId, IDbConnection connection)
    {
        const string sql = "SELECT * FROM public.user_tracks where user_id = @userId";
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var result = await connection.QueryAsync<UserTrack>(sql, new
        {
            userId
        });

        return result
            .GroupBy(g => g.ArtistName.ToLower())
            .ToDictionary(d => d.Key, d => d.ToList());
    }

    private async Task UpdateTracksForUser(User user,
        IEnumerable<UserPlay> newScrobbles,
        NpgsqlConnection connection,
        IReadOnlyDictionary<string, List<UserTrack>> userTracks)
    {
        var updateExistingTracks = new StringBuilder();

        foreach (var track in newScrobbles.GroupBy(x => new
        {
            ArtistName = x.ArtistName.ToLower(),
            TrackName = x.TrackName.ToLower()
        }))
        {
            var alias = await this._aliasService.GetAlias(track.Key.ArtistName.ToLower());

            var artistName = track.First().ArtistName;
            if (alias != null && !alias.Options.HasFlag(AliasOption.DisableInPlays))
            {
                artistName = alias.ArtistName;
            }

            userTracks.TryGetValue(artistName.ToLower(), out var userArtistTracks);

            var existingUserTrack =
                userArtistTracks?.FirstOrDefault(a => a.Name.ToLower() == track.Key.TrackName.ToLower());

            if (existingUserTrack != null)
            {
                updateExistingTracks.Append(
                    $"UPDATE public.user_tracks SET playcount = {existingUserTrack.Playcount + track.Count()} " +
                    $"WHERE user_track_id = {existingUserTrack.UserTrackId}; ");

                Log.Debug($"Updated track {track.Key.TrackName} for {user.UserNameLastFM} (+{track.Count()} plays)");
            }
            else
            {
                await using var addUserTrack =
                    new NpgsqlCommand("INSERT INTO public.user_tracks(user_id, name, artist_name, playcount)" +
                                      "VALUES(@userId, @trackName, @artistName, @trackPlaycount); ",
                        connection);

                var capitalizedTrackName = track.First().TrackName;

                addUserTrack.Parameters.AddWithValue("userId", user.UserId);
                addUserTrack.Parameters.AddWithValue("trackName", capitalizedTrackName);
                addUserTrack.Parameters.AddWithValue("artistName", artistName);
                addUserTrack.Parameters.AddWithValue("trackPlaycount", track.Count());

                Log.Debug($"Added track {track.Key.ArtistName} - {capitalizedTrackName} for {user.UserNameLastFM}");

                await addUserTrack.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        if (updateExistingTracks.Length > 0)
        {
            await using var updateExistingUserTracks =
                new NpgsqlCommand(updateExistingTracks.ToString(), connection);

            await updateExistingUserTracks.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private static async Task SetUserLastScrobbleTime(User user, DateTime lastScrobble, NpgsqlConnection connection)
    {
        user.LastScrobbleUpdate = lastScrobble;
        await using var setIndexTime =
            new NpgsqlCommand($"UPDATE public.users SET last_scrobble_update = '{lastScrobble:u}' WHERE user_id = {user.UserId};", connection);
        await setIndexTime.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task SetUserUpdateTime(User user, DateTime updateTime, NpgsqlConnection connection)
    {
        user.LastUpdated = updateTime;
        await using var setUpdateTime =
            new NpgsqlCommand($"UPDATE public.users SET last_updated = '{updateTime:u}' WHERE user_id = {user.UserId};", connection);
        await setUpdateTime.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private async Task<long> SetOrUpdateUserPlaycount(User user, long playcountToAdd, NpgsqlConnection connection, long? correctPlaycount = null)
    {
        if (!correctPlaycount.HasValue)
        {
            if (!user.TotalPlaycount.HasValue)
            {
                var recentTracks = await this._dataSourceFactory.GetRecentTracksAsync(
                    user.UserNameLastFM,
                    count: 1,
                    useCache: false,
                    user.SessionKeyLastFm);

                await using var setPlaycount = new NpgsqlCommand($"UPDATE public.users SET total_playcount = {recentTracks.Content.TotalAmount} WHERE user_id = {user.UserId};", connection);
                await setPlaycount.ExecuteNonQueryAsync().ConfigureAwait(false);

                user.TotalPlaycount = recentTracks.Content.TotalAmount;

                return recentTracks.Content.TotalAmount;
            }

            var updatedPlaycount = user.TotalPlaycount.Value + playcountToAdd;
            await using var updatePlaycount = new NpgsqlCommand($"UPDATE public.users SET total_playcount = {updatedPlaycount} WHERE user_id = {user.UserId};", connection);
            await updatePlaycount.ExecuteNonQueryAsync().ConfigureAwait(false);

            return updatedPlaycount;
        }

        await using var updateCorrectPlaycount =
            new NpgsqlCommand($"UPDATE public.users SET total_playcount = {correctPlaycount} WHERE user_id = {user.UserId};", connection);
        await updateCorrectPlaycount.ExecuteNonQueryAsync().ConfigureAwait(false);

        return correctPlaycount.Value;
    }

    private void AddRecentPlayToMemoryCache(int userId, IEnumerable<RecentTrack> tracks)
    {
        const int minutesToCache = 30;
        var filter = DateTime.UtcNow.AddMinutes(-minutesToCache);

        var playsToCache = tracks
            .Where(w => w.NowPlaying || w.TimePlayed.HasValue && w.TimePlayed.Value > filter)
            .Select(s => new UserPlay
            {
                ArtistName = s.ArtistName.ToLower(),
                AlbumName = s.AlbumName?.ToLower(),
                TrackName = s.TrackName.ToLower(),
                UserId = userId,
                TimePlayed = s.TimePlayed ?? DateTime.UtcNow
            });

        foreach (var userPlay in playsToCache.OrderBy(o => o.TimePlayed))
        {
            var timeToCache = CalculateTimeToCache(userPlay.TimePlayed, minutesToCache);

            var timeSpan = TimeSpan.FromMinutes(timeToCache);

            this._cache.Set($"{userId}-lp-artist-{userPlay.ArtistName}", userPlay, timeSpan);
            this._cache.Set($"{userId}-lp-track-{userPlay.ArtistName}-{userPlay.TrackName}", userPlay, timeSpan);

            if (userPlay.AlbumName != null)
            {
                this._cache.Set($"{userId}-lp-album-{userPlay.ArtistName}-{userPlay.AlbumName}", userPlay, timeSpan);
            }
        }
    }

    private static int CalculateTimeToCache(DateTime timePlayed, int minutesToCache)
    {
        var elapsedTime = DateTime.UtcNow - timePlayed;

        var minutes = (int)elapsedTime.TotalMinutes;
        var timeToCache = minutesToCache - (minutes % minutesToCache);
        return timeToCache;
    }

    private async Task AddLfmInactiveUserLog(User user, ResponseStatus responseStatus)
    {
        if (user.LastUsed > DateTime.UtcNow.AddDays(-10))
        {
            return;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();

        await db.InactiveUserLog.AddAsync(new InactiveUserLog
        {
            Created = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
            ResponseStatus = responseStatus,
            UserId = user.UserId,
            UserNameLastFM = user.UserNameLastFM
        });

        await db.SaveChangesAsync();
    }

    public async Task CorrectUserArtistPlaycount(int userId, string artistName, long correctPlaycount)
    {
        if (correctPlaycount < 30)
        {
            return;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .Include(i => i.Artists)
            .FirstOrDefaultAsync(f => f.UserId == userId);

        if (user?.LastUpdated == null || !user.Artists.Any() || user.LastUpdated < DateTime.UtcNow.AddMinutes(-2))
        {
            var random = new Random();
            if (random.Next(0, 5) == 1 && user?.LastUpdated != null)
            {
                await UpdateUser(new UpdateUserQueueItem(userId));
            }
            else
            {
                return;
            }
        }

        var userArtist = user.Artists.FirstOrDefault(f => f.Name.ToLower() == artistName.ToLower());

        if (userArtist == null ||
            userArtist.Playcount < 20 ||
            userArtist.Playcount > (correctPlaycount - 3) && userArtist.Playcount < (correctPlaycount + 3))
        {
            return;
        }

        Log.Debug("Corrected artist playcount for user {userId} | {lastFmUserName} for artist {artistName} from {oldPlaycount} to {newPlaycount}",
            user.UserId, user.UserNameLastFM, userArtist.Name, userArtist.Playcount, correctPlaycount);

        userArtist.Playcount = (int)correctPlaycount;

        db.Entry(userArtist).State = EntityState.Modified;

        await db.SaveChangesAsync();
    }

    public async Task CorrectUserAlbumPlaycount(int userId, string artistName, string albumName, long correctPlaycount)
    {
        if (correctPlaycount < 30)
        {
            return;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .Include(i => i.Albums)
            .FirstOrDefaultAsync(f => f.UserId == userId);

        if (user?.LastUpdated == null || !user.Albums.Any() || user.LastUpdated < DateTime.UtcNow.AddMinutes(-2))
        {
            var random = new Random();
            if (random.Next(0, 5) == 1 && user?.LastUpdated != null)
            {
                await UpdateUser(new UpdateUserQueueItem(userId));
            }
            else
            {
                return;
            }
        }

        var userAlbum = user.Albums.FirstOrDefault(f => f.Name.ToLower() == albumName.ToLower() &&
                                                        f.ArtistName.ToLower() == artistName.ToLower());

        if (userAlbum == null ||
            userAlbum.Playcount < 20 ||
            userAlbum.Playcount > (correctPlaycount - 3) && userAlbum.Playcount < (correctPlaycount + 3))
        {
            return;
        }

        Log.Debug("Corrected album playcount for user {userId} | {lastFmUserName} for album {artistName} - {albumName} from {oldPlaycount} to {newPlaycount}",
            user.UserId, user.UserNameLastFM, userAlbum.ArtistName, userAlbum.Name, userAlbum.Playcount, correctPlaycount);

        userAlbum.Playcount = (int)correctPlaycount;

        db.Entry(userAlbum).State = EntityState.Modified;

        await db.SaveChangesAsync();
    }

    public async Task CorrectUserTrackPlaycount(int userId, string artistName, string trackName, long correctPlaycount)
    {
        if (correctPlaycount < 30)
        {
            return;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .Include(i => i.Tracks)
            .FirstOrDefaultAsync(f => f.UserId == userId);

        if (user?.LastUpdated == null || !user.Tracks.Any() || user.LastUpdated < DateTime.UtcNow.AddMinutes(-2))
        {
            var random = new Random();
            if (random.Next(0, 5) == 1 && user?.LastUpdated != null)
            {
                await UpdateUser(new UpdateUserQueueItem(userId));
            }
            else
            {
                return;
            }
        }

        var userTrack = user.Tracks.FirstOrDefault(f => f.Name.ToLower() == trackName.ToLower() &&
                                                        f.ArtistName.ToLower() == artistName.ToLower());

        if (userTrack == null ||
            userTrack.Playcount < 20 ||
            userTrack.Playcount > (correctPlaycount - 3) && userTrack.Playcount < (correctPlaycount + 3))
        {
            return;
        }

        Log.Debug("Corrected track playcount for user {userId} | {lastFmUserName} for track {artistName} - {trackName} from {oldPlaycount} to {newPlaycount}",
            user.UserId, user.UserNameLastFM, userTrack.ArtistName, userTrack.Name, userTrack.Playcount, correctPlaycount);

        userTrack.Playcount = (int)correctPlaycount;

        db.Entry(userTrack).State = EntityState.Modified;

        await db.SaveChangesAsync();
    }
}
