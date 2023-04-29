using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Objects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Npgsql;
using PostgreSQLCopyHelper;
using Serilog;

namespace FMBot.LastFM.Repositories;

public class IndexRepository
{
    private readonly LastfmClient _lastFMClient;

    private readonly IMemoryCache _cache;

    private readonly string _key;
    private readonly string _secret;

    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

    private readonly string _connectionString;

    private readonly LastFmRepository _lastFmRepository;

    public IndexRepository(
        IConfiguration configuration,
        LastFmRepository lastFmRepository,
        IDbContextFactory<FMBotDbContext> contextFactory,
        IMemoryCache cache)
    {
        this._lastFmRepository = lastFmRepository;
        this._contextFactory = contextFactory;
        this._cache = cache;
        this._key = configuration.GetSection("LastFm:PrivateKey").Value;
        this._secret = configuration.GetSection("LastFm:PrivateKeySecret").Value;
        this._connectionString = configuration.GetSection("Database:ConnectionString").Value;
        this._lastFMClient = new LastfmClient(this._key, this._secret);
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

        Log.Information($"Starting index for {user.UserNameLastFM}");
        var now = DateTime.UtcNow;

        await using var connection = new NpgsqlConnection(this._connectionString);
        await connection.OpenAsync();

        var userInfo = await this._lastFmRepository.GetLfmUserInfoAsync(user.UserNameLastFM);
        if (userInfo?.Registered?.Text != null)
        {
            await SetUserSignUpTime(user.UserId, userInfo.Registered.Text, connection, userInfo.Subscriber);
        }

        await SetUserPlaycount(user, connection);

        var plays = await GetPlaysForUserFromLastFm(user);
        await PlayRepository.InsertAllPlays(plays, user.UserId, connection);

        var artists = await GetArtistsForUserFromLastFm(user);
        await InsertArtistsIntoDatabase(artists, user.UserId, connection);

        var albums = await GetAlbumsForUserFromLastFm(user);
        await InsertAlbumsIntoDatabase(albums, user.UserId, connection);

        var tracks = await GetTracksForUserFromLastFm(user);
        await InsertTracksIntoDatabase(tracks, user.UserId, connection);

        var latestScrobbleDate = await GetLatestScrobbleDate(user);

        await SetUserIndexTime(user.UserId, now, latestScrobbleDate, connection);

        await connection.CloseAsync();

        Statistics.IndexedUsers.Inc();
        this._cache.Remove(concurrencyCacheKey);

        return new IndexedUserStats
        {
            PlayCount = plays.Count,
            ArtistCount = artists.Count,
            AlbumCount = albums.Count,
            TrackCount = tracks.Count
        };
    }

    public async Task<DateTime?> SetUserSignUpTime(User user)
    {
        await using var connection = new NpgsqlConnection(this._connectionString);
        await connection.OpenAsync();

        var userInfo = await this._lastFmRepository.GetLfmUserInfoAsync(user.UserNameLastFM);
        if (userInfo?.Registered?.Text != null)
        {
            return await SetUserSignUpTime(user.UserId, userInfo.Registered.Text, connection, userInfo.Subscriber);
        }

        return null;
    }

    private async Task<IReadOnlyList<UserArtist>> GetArtistsForUserFromLastFm(User user)
    {
        Log.Information($"Getting artists for user {user.UserNameLastFM}");

        var topArtists = new List<LastArtist>();

        var indexLimit = UserHasHigherIndexLimit(user) ? 200 : 4;

        for (var i = 1; i < indexLimit + 1; i++)
        {
            var artistResult = await this._lastFMClient.User.GetTopArtists(user.UserNameLastFM,
                LastStatsTimeSpan.Overall, i, 1000);
            Statistics.LastfmApiCalls.Inc();

            topArtists.AddRange(artistResult);

            if (artistResult.Count() < 1000)
            {
                break;
            }
        }

        if (topArtists.Count == 0)
        {
            return new List<UserArtist>();
        }

        return topArtists.Select(a => new UserArtist
        {
            Name = a.Name,
            Playcount = a.PlayCount.Value,
            UserId = user.UserId
        }).ToList();
    }

    private async Task<IReadOnlyList<UserPlayTs>> GetPlaysForUserFromLastFm(User user)
    {
        Log.Information($"Getting plays for user {user.UserNameLastFM}");

        var pages = UserHasHigherIndexLimit(user) ? 750 : 25;

        var recentPlays = await this._lastFmRepository.GetRecentTracksAsync(user.UserNameLastFM, 1000,
            sessionKey: user.SessionKeyLastFm, amountOfPages: pages);

        if (!recentPlays.Success || recentPlays.Content.RecentTracks.Count == 0)
        {
            return new List<UserPlayTs>();
        }

        return recentPlays.Content.RecentTracks
            .Where(w => !w.NowPlaying && w.TimePlayed.HasValue)
            .Select(t => new UserPlayTs
            {
                TrackName = t.TrackName,
                AlbumName = t.AlbumName,
                ArtistName = t.ArtistName,
                TimePlayed = t.TimePlayed.Value,
                UserId = user.UserId
            }).ToList();
    }

    private async Task<IReadOnlyList<UserAlbum>> GetAlbumsForUserFromLastFm(User user)
    {
        Log.Information($"Getting albums for user {user.UserNameLastFM}");

        var topAlbums = new List<LastAlbum>();

        var indexLimit = UserHasHigherIndexLimit(user) ? 200 : 5;

        for (var i = 1; i < indexLimit + 1; i++)
        {
            var albumResult = await this._lastFMClient.User.GetTopAlbums(user.UserNameLastFM,
                LastStatsTimeSpan.Overall, i, 1000);
            Statistics.LastfmApiCalls.Inc();

            topAlbums.AddRange(albumResult);

            if (albumResult.Count() < 1000)
            {
                break;
            }
        }

        if (topAlbums.Count == 0)
        {
            return new List<UserAlbum>();
        }

        return topAlbums.Select(a => new UserAlbum
        {
            Name = a.Name,
            ArtistName = a.ArtistName,
            Playcount = a.PlayCount.Value,
            UserId = user.UserId
        }).ToList();
    }

    private async Task<IReadOnlyList<UserTrack>> GetTracksForUserFromLastFm(User user)
    {
        Log.Information($"Getting tracks for user {user.UserNameLastFM}");

        var indexLimit = UserHasHigherIndexLimit(user) ? 200 : 6;

        var trackResult = await this._lastFmRepository.GetTopTracksAsync(user.UserNameLastFM, "overall", 1000, indexLimit);

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

    private static async Task InsertArtistsIntoDatabase(IReadOnlyList<UserArtist> artists, int userId,
        NpgsqlConnection connection)
    {
        Log.Information($"Inserting {artists.Count} artists for user {userId}");

        var copyHelper = new PostgreSQLCopyHelper<UserArtist>("public", "user_artists")
            .MapText("name", x => x.Name)
            .MapInteger("user_id", x => x.UserId)
            .MapInteger("playcount", x => x.Playcount);

        await using var deleteCurrentArtists = new NpgsqlCommand($"DELETE FROM public.user_artists WHERE user_id = {userId};", connection);
        await deleteCurrentArtists.ExecuteNonQueryAsync();

        await copyHelper.SaveAllAsync(connection, artists);
    }

    private static async Task InsertAlbumsIntoDatabase(IReadOnlyList<UserAlbum> albums, int userId,
        NpgsqlConnection connection)
    {
        Log.Information($"Inserting {albums.Count} albums for user {userId}");

        var copyHelper = new PostgreSQLCopyHelper<UserAlbum>("public", "user_albums")
            .MapText("name", x => x.Name)
            .MapText("artist_name", x => x.ArtistName)
            .MapInteger("user_id", x => x.UserId)
            .MapInteger("playcount", x => x.Playcount);

        await using var deleteCurrentAlbums = new NpgsqlCommand($"DELETE FROM public.user_albums WHERE user_id = {userId};", connection);
        await deleteCurrentAlbums.ExecuteNonQueryAsync();

        await copyHelper.SaveAllAsync(connection, albums);
    }

    private static async Task InsertTracksIntoDatabase(IReadOnlyList<UserTrack> artists, int userId,
        NpgsqlConnection connection)
    {
        Log.Information($"Inserting {artists.Count} tracks for user {userId}");

        var copyHelper = new PostgreSQLCopyHelper<UserTrack>("public", "user_tracks")
            .MapText("name", x => x.Name)
            .MapText("artist_name", x => x.ArtistName)
            .MapInteger("user_id", x => x.UserId)
            .MapInteger("playcount", x => x.Playcount);

        await using var deleteCurrentTracks = new NpgsqlCommand($"DELETE FROM public.user_tracks WHERE user_id = {userId};", connection);
        await deleteCurrentTracks.ExecuteNonQueryAsync();

        await copyHelper.SaveAllAsync(connection, artists);

    }

    private async Task SetUserIndexTime(int userId, DateTime now, DateTime lastScrobble, NpgsqlConnection connection)
    {
        Log.Information($"Setting user index time for user {userId}");

        await using var setIndexTime = new NpgsqlCommand($"UPDATE public.users SET last_indexed='{now:u}', last_updated='{now:u}', last_scrobble_update = '{lastScrobble:u}' WHERE user_id = {userId};", connection);
        await setIndexTime.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private async Task<DateTime> SetUserSignUpTime(int userId, long signUpDateTimeLong, NpgsqlConnection connection,
        long lastfmPro)
    {
        var signUpDateTime = DateTime.UnixEpoch.AddSeconds(signUpDateTimeLong).ToUniversalTime();

        Log.Information($"Setting user index signup time ({signUpDateTime}) for user {userId}");

        await using var setIndexTime = new NpgsqlCommand($"UPDATE public.users SET registered_last_fm='{signUpDateTime:u}', lastfm_pro = '{lastfmPro}' WHERE user_id = {userId};", connection);
        await setIndexTime.ExecuteNonQueryAsync().ConfigureAwait(false);

        return signUpDateTime;
    }

    private async Task<DateTime> GetLatestScrobbleDate(User user)
    {
        var recentTracks = await this._lastFMClient.User.GetRecentScrobbles(user.UserNameLastFM, count: 1);
        Statistics.LastfmApiCalls.Inc();
        if (!recentTracks.Success || !recentTracks.Content.Any() || !recentTracks.Content.Any(a => a.TimePlayed.HasValue))
        {
            Log.Information("Recent track call to get latest scrobble date failed!");
            return DateTime.UtcNow;
        }

        return recentTracks.Content.First(f => f.TimePlayed.HasValue).TimePlayed.Value.DateTime;
    }

    private async Task SetUserPlaycount(User user, NpgsqlConnection connection)
    {
        var recentTracks = await this._lastFmRepository.GetRecentTracksAsync(
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
}
