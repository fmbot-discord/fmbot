using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Serilog;

namespace FMBot.LastFM.Repositories;

public class SmallIndexRepository
{
    private readonly IMemoryCache _cache;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly string _connectionString;

    private readonly LastFmRepository _lastFmRepository;

    public SmallIndexRepository(
        IConfiguration configuration,
        LastFmRepository lastFmRepository,
        IDbContextFactory<FMBotDbContext> contextFactory,
        IMemoryCache cache)
    {
        this._lastFmRepository = lastFmRepository;
        this._contextFactory = contextFactory;
        this._cache = cache;
        this._connectionString = configuration.GetSection("Database:ConnectionString").Value;
    }

    public async Task SmallIndexUser(User indexUser)
    {

        var concurrencyCacheKey = $"small-index-started-{indexUser.UserId}";
        this._cache.Set(concurrencyCacheKey, true, TimeSpan.FromMinutes(1));

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(indexUser.UserId);

        if (user.LastUpdated < DateTime.UtcNow.AddMinutes(-2))
        {
            Log.Information($"Cancelled small index for {user.UserNameLastFM}");
            return;
        }

        Log.Information($"Starting small index for {user.UserNameLastFM}");

        await using var connection = new NpgsqlConnection(this._connectionString);
        await connection.OpenAsync();

        try
        {
            var userArtists = await GetUserArtists(user.UserId, connection);
            var userAlbums = await GetUserAlbums(user.UserId, connection);
            var userTracks = await GetUserTracks(user.UserId, connection);

            var artists = await GetArtistsForUserFromLastFm(user);
            await UpdateArtistsForUser(user, userArtists, artists, connection);

            var albums = await GetAlbumsForUserFromLastFm(user);
            await UpdateAlbumsForUser(user, userAlbums, albums, connection);

            var tracks = await GetTracksForUserFromLastFm(user);
            await UpdateTracksForUser(user, userTracks, tracks, connection);

            await SetUserSmallIndexTime(user, DateTime.UtcNow, connection);

            await connection.CloseAsync();
        }
        catch (Exception e)
        {
            await connection.CloseAsync();
            Log.Error("Error in smallindexuser", e);
            throw;
        }

        this._cache.Remove(concurrencyCacheKey);
        Statistics.SmallIndexedUsers.Inc();
    }

    public async Task UpdateUserArtists(User indexUser, List<TopArtist> artists)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(indexUser.UserId);

        if (user.LastUpdated < DateTime.UtcNow.AddMinutes(-2))
        {
            Log.Information($"Cancelled user artist update for {user.UserNameLastFM}");
            return;
        }

        Log.Information($"Starting user artist update for {user.UserNameLastFM}");

        await using var connection = new NpgsqlConnection(this._connectionString);
        await connection.OpenAsync();

        var userArtists = await GetUserArtists(user.UserId, connection);

        var currentTopArtists = artists.Select(s => new UserArtist
        {
            Name = s.ArtistName,
            Playcount = (int)s.UserPlaycount
        }).ToList();

        await UpdateArtistsForUser(user, userArtists, currentTopArtists, connection);

        await connection.CloseAsync();
    }

    private async Task UpdateArtistsForUser(User user,
        IReadOnlyCollection<UserArtist> existingUserArtists,
        IReadOnlyCollection<UserArtist> currentUserArtists,
        NpgsqlConnection connection)
    {
        var query = new StringBuilder();

        foreach (var currentTopArtist in currentUserArtists)
        {
            var alias = (string)this._cache.Get(CacheKeyForAlias(currentTopArtist.Name.ToLower()));
            var artistName = alias ?? currentTopArtist.Name;

            var existingPlaycountToCorrect = existingUserArtists
                .FirstOrDefault(f => f.Name.ToLower() == artistName.ToLower() &&
                                     f.Playcount != currentTopArtist.Playcount);

            if (existingPlaycountToCorrect != null)
            {
                query.Append($"UPDATE public.user_artists SET playcount = {currentTopArtist.Playcount} " +
                             $"WHERE user_artist_id = {existingPlaycountToCorrect.UserArtistId}; ");

#if DEBUG
                Log.Information($"Updated artist {artistName} for {user.UserNameLastFM} ({existingPlaycountToCorrect.Playcount} to {currentTopArtist.Playcount})");
#endif
            }
        }

        if (query.Length > 0)
        {
            await using var updateUserArtist =
                new NpgsqlCommand(query.ToString(), connection);

            await updateUserArtist.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        Log.Verbose("Update: Corrected artists for user {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
    }

    private async Task UpdateAlbumsForUser(User user,
        IReadOnlyCollection<UserAlbum> existingUserAlbums,
        IReadOnlyCollection<UserAlbum> currentUserAlbums,
        NpgsqlConnection connection)
    {
        var query = new StringBuilder();

        foreach (var currentTopAlbum in currentUserAlbums)
        {
            var alias = (string)this._cache.Get(CacheKeyForAlias(currentTopAlbum.Name.ToLower()));
            var artistName = alias ?? currentTopAlbum.Name;

            var existingPlaycountToCorrect = existingUserAlbums
                .FirstOrDefault(f => f.ArtistName.ToLower() == artistName.ToLower() &&
                                     f.Name.ToLower() == currentTopAlbum.Name.ToLower() &&
                                     f.Playcount != currentTopAlbum.Playcount);

            if (existingPlaycountToCorrect != null)
            {
                query.Append($"UPDATE public.user_albums SET playcount = {currentTopAlbum.Playcount} " +
                             $"WHERE user_album_id = {existingPlaycountToCorrect.UserAlbumId}; ");

#if DEBUG
                Log.Information($"Updated album {currentTopAlbum.Name} from {artistName} for {user.UserNameLastFM} ({existingPlaycountToCorrect.Playcount} to {currentTopAlbum.Playcount})");
#endif
            }
        }

        if (query.Length > 0)
        {
            await using var updateUserArtist =
                new NpgsqlCommand(query.ToString(), connection);

            await updateUserArtist.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        Log.Verbose("Update: Corrected albums for user {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
    }

    private async Task UpdateTracksForUser(User user,
        IReadOnlyCollection<UserTrack> existingUserTracks,
        IReadOnlyCollection<UserTrack> currentUserTracks,
        NpgsqlConnection connection)
    {
        var query = new StringBuilder();

        foreach (var currentTopTrack in currentUserTracks)
        {
            var alias = (string)this._cache.Get(CacheKeyForAlias(currentTopTrack.Name.ToLower()));
            var artistName = alias ?? currentTopTrack.Name;

            var existingPlaycountToCorrect = existingUserTracks
                .FirstOrDefault(f => f.ArtistName.ToLower() == artistName.ToLower() &&
                                     f.Name.ToLower() == currentTopTrack.Name.ToLower() &&
                                     f.Playcount != currentTopTrack.Playcount);

            if (existingPlaycountToCorrect != null)
            {
                query.Append($"UPDATE public.user_tracks SET playcount = {currentTopTrack.Playcount} " +
                             $"WHERE user_track_id = {existingPlaycountToCorrect.UserTrackId}; ");

#if DEBUG
                Log.Information($"Updated track {currentTopTrack.Name} from {artistName} for {user.UserNameLastFM} ({existingPlaycountToCorrect.Playcount} to {currentTopTrack.Playcount})");
#endif
            }
        }

        if (query.Length > 0)
        {
            await using var updateUserArtist =
                new NpgsqlCommand(query.ToString(), connection);

            await updateUserArtist.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        Log.Verbose("Update: Corrected albums for user {userId} | {userNameLastFm}", user.UserId, user.UserNameLastFM);
    }

    private async Task<IReadOnlyList<UserArtist>> GetArtistsForUserFromLastFm(User user)
    {
        var albumResult = await this._lastFmRepository.GetTopArtistsAsync(user.UserNameLastFM, TimePeriod.AllTime, count: 1000);

        if (!albumResult.Success || albumResult.Content.TopArtists.Count == 0)
        {
            return new List<UserArtist>();
        }

        return albumResult.Content.TopArtists.Select(a => new UserArtist
        {
            Name = a.ArtistName,
            Playcount = (int)a.UserPlaycount,
            UserId = user.UserId
        }).ToList();
    }

    private async Task<IReadOnlyList<UserAlbum>> GetAlbumsForUserFromLastFm(User user)
    {
        var albumResult = await this._lastFmRepository.GetTopAlbumsAsync(user.UserNameLastFM, TimePeriod.AllTime, count: 1000);

        if (!albumResult.Success || albumResult.Content.TopAlbums.Count == 0)
        {
            return new List<UserAlbum>();
        }

        return albumResult.Content.TopAlbums.Select(a => new UserAlbum
        {
            Name = a.AlbumName,
            ArtistName = a.ArtistName,
            Playcount = (int)a.UserPlaycount,
            UserId = user.UserId
        }).ToList();
    }

    private async Task<IReadOnlyList<UserTrack>> GetTracksForUserFromLastFm(User user)
    {
        var trackResult = await this._lastFmRepository.GetTopTracksAsync(user.UserNameLastFM, "overall", 1000);

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

    private async Task<IReadOnlyCollection<UserArtist>> GetUserArtists(int userId, NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM public.user_artists where user_id = @userId";
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserArtist>(sql, new
        {
            userId
        })).ToList();
    }

    private async Task<IReadOnlyCollection<UserAlbum>> GetUserAlbums(int userId, NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM public.user_albums where user_id = @userId";
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserAlbum>(sql, new
        {
            userId
        })).ToList();
    }

    private async Task<IReadOnlyCollection<UserTrack>> GetUserTracks(int userId, NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM public.user_tracks where user_id = @userId";
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserTrack>(sql, new
        {
            userId
        })).ToList();
    }

    private async Task SetUserSmallIndexTime(User user, DateTime updateTime, NpgsqlConnection connection)
    {
        user.LastUpdated = updateTime;
        await using var setUpdateTime =
            new NpgsqlCommand($"UPDATE public.users SET last_small_indexed = '{updateTime:u}' WHERE user_id = {user.UserId};", connection);
        await setUpdateTime.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static string CacheKeyForAlias(string aliasName)
    {
        return $"artist-alias-{aliasName}";
    }
}
