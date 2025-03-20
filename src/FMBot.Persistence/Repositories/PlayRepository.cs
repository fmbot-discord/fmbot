using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Npgsql;
using PostgreSQLCopyHelper;
using Serilog;

namespace FMBot.Persistence.Repositories;

public static class PlayRepository
{
    public record PlayUpdate(List<UserPlay> NewPlays, List<UserPlay> RemovedPlays);

    public static async Task<PlayUpdate> InsertLatestPlays(IEnumerable<RecentTrack> recentTracks, int userId,
        NpgsqlConnection connection)
    {
        var lastPlays = recentTracks
            .Where(w => !w.NowPlaying &&
                        w.TimePlayed.HasValue)
            .Select(s => new UserPlay
            {
                ArtistName = s.ArtistName,
                AlbumName = s.AlbumName,
                TrackName = s.TrackName,
                TimePlayed = DateTime.SpecifyKind(s.TimePlayed.Value, DateTimeKind.Utc),
                PlaySource = PlaySource.LastFm,
                UserId = userId
            }).ToList();

        var existingPlays = await GetAllUserPlays(userId, connection, lastPlays.Count + 250);
        existingPlays = existingPlays.Where(w => w.PlaySource == PlaySource.LastFm).ToList();

        var firstExistingPlay = existingPlays.MinBy(o => o.TimePlayed);

        if (firstExistingPlay != null)
        {
            lastPlays = lastPlays
                .Where(w => w.TimePlayed >= firstExistingPlay.TimePlayed)
                .ToList();
        }

        var addedPlays = new List<UserPlay>();
        foreach (var newPlay in lastPlays)
        {
            if (existingPlays.All(a => a.TimePlayed != newPlay.TimePlayed))
            {
                addedPlays.Add(newPlay);
            }
        }

        var firstNewPlay = lastPlays.MinBy(o => o.TimePlayed);

        var removedPlays = new List<UserPlay>();
        if (firstNewPlay != null)
        {
            foreach (var existingPlay in existingPlays.Where(w => w.TimePlayed >= firstNewPlay.TimePlayed))
            {
                if (lastPlays.All(a => a.TimePlayed != existingPlay.TimePlayed))
                {
                    removedPlays.Add(existingPlay);
                }
            }

            if (removedPlays.Any())
            {
                Log.Information("Found {removedPlaysCount} time series plays to remove for {userId}",
                    removedPlays.Count, userId);
                await RemoveSpecificPlays(removedPlays, connection);
            }
        }

        if (addedPlays.Any())
        {
            Log.Information("Inserting {addedPlaysCount} new time series plays for user {userId}", addedPlays.Count,
                userId);
            await InsertTimeSeriesPlays(addedPlays, connection);
        }

        return new PlayUpdate(addedPlays, removedPlays);
    }

    public static async Task ReplaceAllPlays(IReadOnlyList<UserPlay> playsToInsert, int userId,
        NpgsqlConnection connection)
    {
        await RemoveAllCurrentLastFmPlays(userId, connection);

        Log.Information($"Inserting {playsToInsert.Count} time series plays for user {userId}");
        await InsertTimeSeriesPlays(playsToInsert, connection);
    }

    private static async Task RemoveAllCurrentLastFmPlays(int userId, NpgsqlConnection connection)
    {
        await using var deletePlays = new NpgsqlCommand("DELETE FROM public.user_plays " +
                                                        "WHERE user_id = @userId AND (play_source IS NULL OR play_source = 0);",
            connection);

        deletePlays.Parameters.AddWithValue("userId", userId);

        await deletePlays.ExecuteNonQueryAsync();
    }

    public static async Task RemoveAllImportedSpotifyPlays(int userId, NpgsqlConnection connection)
    {
        await using var deletePlays = new NpgsqlCommand("DELETE FROM public.user_plays " +
                                                        "WHERE user_id = @userId " +
                                                        "AND play_source = 1", connection);

        deletePlays.Parameters.AddWithValue("userId", userId);

        await deletePlays.ExecuteNonQueryAsync();
    }

    public static async Task RemoveAllImportedAppleMusicPlays(int userId, NpgsqlConnection connection)
    {
        await using var deletePlays = new NpgsqlCommand("DELETE FROM public.user_plays " +
                                                        "WHERE user_id = @userId " +
                                                        "AND play_source = 2", connection);

        deletePlays.Parameters.AddWithValue("userId", userId);

        await deletePlays.ExecuteNonQueryAsync();
    }

    public static async Task RemoveOldPlays(int userId, NpgsqlConnection connection)
    {
        await using var deletePlays = new NpgsqlCommand(
            @"DELETE FROM public.user_plays
        WHERE user_id = @userId
        AND (play_source = 0 OR play_source IS NULL)
        AND time_played <= CURRENT_DATE - INTERVAL '14 months'
        AND user_play_id NOT IN (
            SELECT user_play_id
            FROM (
                SELECT user_play_id
                FROM public.user_plays
                WHERE user_id = @userId
                ORDER BY time_played DESC
                LIMIT 500
            ) recent_plays
        )", connection);

        deletePlays.Parameters.AddWithValue("userId", userId);
        await deletePlays.ExecuteNonQueryAsync();
    }

    private static async Task RemoveSpecificPlays(IEnumerable<UserPlay> playsToRemove, NpgsqlConnection connection)
    {
        foreach (var playToRemove in playsToRemove)
        {
            await using var deletePlays = new NpgsqlCommand("DELETE FROM public.user_plays " +
                                                            "WHERE user_id = @userId AND time_played = @timePlayed " +
                                                            "AND play_source != 1 AND play_source != 2", connection);

            deletePlays.Parameters.AddWithValue("userId", playToRemove.UserId);
            deletePlays.Parameters.AddWithValue("timePlayed", playToRemove.TimePlayed);

            await deletePlays.ExecuteNonQueryAsync();
        }
    }

    public static async Task<ulong> InsertTimeSeriesPlays(IEnumerable<UserPlay> plays, NpgsqlConnection connection)
    {
        var copyHelper = new PostgreSQLCopyHelper<UserPlay>("public", "user_plays")
            .MapText("track_name", x => x.TrackName)
            .MapText("album_name", x => x.AlbumName)
            .MapText("artist_name", x => x.ArtistName)
            .MapTimeStampTz("time_played", x => DateTime.SpecifyKind(x.TimePlayed, DateTimeKind.Utc))
            .MapInteger("user_id", x => x.UserId)
            .MapBigInt("ms_played", x => x.MsPlayed)
            .MapInteger("play_source", x => (int?)x.PlaySource);

        return await copyHelper.SaveAllAsync(connection, plays);
    }

    public static async Task<ICollection<UserPlay>> GetAllUserPlays(int userId, NpgsqlConnection connection,
        int limit = 99999999)
    {
        const string sql = "SELECT * FROM public.user_plays WHERE user_id = @userId " +
                           "ORDER BY time_played DESC LIMIT @limit";
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserPlay>(sql, new
        {
            userId,
            limit
        })).ToList();
    }

    private static string GetUserPlaysSqlString(string initialSql, DataSource dataSource, DateTime? start = null,
        DateTime? end = null)
    {
        var sql = initialSql;

        sql += dataSource switch
        {
            DataSource.LastFm =>
                " FROM public.user_plays WHERE user_id = @userId AND artist_name IS NOT NULL AND play_source = 0 ",
            DataSource.FullImportThenLastFm =>
                " FROM public.user_plays WHERE user_id = @userId AND artist_name IS NOT NULL AND ( " +
                "(play_source = 1 OR play_source = 2) OR  " +
                "(play_source = 0 AND time_played >= ( " +
                "SELECT MAX(time_played) FROM public.user_plays WHERE user_id = @userId AND (play_source = 1 OR play_source = 2) " +
                ")) OR  " +
                "(play_source = 0 AND time_played <= ( " +
                "SELECT MIN(time_played) FROM public.user_plays WHERE user_id = @userId AND (play_source = 1 OR play_source = 2) " +
                "))) ",
            DataSource.ImportThenFullLastFm =>
                " FROM public.user_plays WHERE user_id = @userId  AND artist_name IS NOT NULL AND ( " +
                "play_source = 0 OR " +
                "((play_source = 1 OR play_source = 2) AND time_played < ( " +
                "SELECT MIN(time_played) FROM public.user_plays WHERE user_id = @userId AND play_source = 0 " +
                "))) ",
            _ => " FROM public.user_plays WHERE user_id = @userId "
        };

        if (start.HasValue)
        {
            sql += " AND time_played >= @start ";
        }

        if (end.HasValue)
        {
            sql += " AND time_played <= @end ";
        }

        if (!initialSql.Contains("COUNT(*)", StringComparison.OrdinalIgnoreCase))
        {
            sql += " ORDER BY time_played DESC LIMIT @limit ";
        }

        return sql;
    }

    public static async Task<ICollection<UserPlay>> GetUserPlays(int userId, NpgsqlConnection connection,
        DataSource dataSource, int limit = 9999999, DateTime? start = null, DateTime? end = null)
    {
        var sql = GetUserPlaysSqlString("SELECT * ", dataSource, start, end);

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserPlay>(sql, new
        {
            userId,
            limit,
            start,
            end
        })).ToList();
    }

    public static async Task<int> GetUserPlayCount(int userId, NpgsqlConnection connection, DataSource dataSource,
        DateTime? start = null, DateTime? end = null)
    {
        var sql = GetUserPlaysSqlString("SELECT COUNT(*) ", dataSource, start, end);

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return await connection.QueryFirstOrDefaultAsync<int>(sql, new
        {
            userId,
            limit = 9999999,
            start,
            end
        });
    }

    public static async Task<ICollection<UserPlay>> GetUserPlaysWithinTimeRange(int userId, NpgsqlConnection connection,
        DateTime start, DateTime? end = null)
    {
        end ??= DateTime.UtcNow;

        const string sql = "SELECT * FROM public.user_plays WHERE user_id = @userId " +
                           "AND time_played >= @start AND time_played <= @end AND artist_name IS NOT NULL " +
                           "ORDER BY time_played DESC ";
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserPlay>(sql, new
        {
            userId,
            start,
            end
        })).ToList();
    }

    public static async Task SetDefaultSourceForPlays(int userId, NpgsqlConnection connection)
    {
        const string sql =
            "UPDATE public.user_plays SET play_source = 0 WHERE user_id = @userId AND play_source IS null";

        await connection.ExecuteAsync(sql, new
        {
            userId,
        });
    }

    public static async Task<bool> HasImported(int userId, NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM public.user_plays WHERE user_id = @userId " +
                           "AND play_source IS NOT NULL and play_source != 0 " +
                           "LIMIT 1";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var play = await connection.QueryFirstOrDefaultAsync(sql, new
        {
            userId,
        });

        return play != null;
    }

    public static async Task MoveImports(int oldUserId, int newUserId, NpgsqlConnection connection)
    {
        const string sql = "UPDATE public.user_plays SET user_id = @newUserId " +
                           "WHERE user_id = @oldUserId AND play_source != 0;";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await connection.QueryFirstOrDefaultAsync(sql, new
        {
            oldUserId,
            newUserId
        });
    }

    public static async Task MoveStreaks(int oldUserId, int newUserId, NpgsqlConnection connection)
    {
        const string sql = "UPDATE public.user_streaks SET user_id = @newUserId " +
                           "WHERE user_id = @oldUserId";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await connection.QueryFirstOrDefaultAsync(sql, new
        {
            oldUserId,
            newUserId
        });
    }

    public static async Task MoveFeaturedLogs(int oldUserId, int newUserId, NpgsqlConnection connection)
    {
        const string sql = "UPDATE public.featured_logs SET user_id = @newUserId " +
                           "WHERE user_id = @oldUserId";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await connection.QueryFirstOrDefaultAsync(sql, new
        {
            oldUserId,
            newUserId
        });
    }

    public static async Task MoveFriends(int oldUserId, int newUserId, NpgsqlConnection connection)
    {
        const string sql = "UPDATE public.friends SET friend_user_id = @newUserId " +
                           "WHERE friend_user_id = @oldUserId";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await connection.QueryFirstOrDefaultAsync(sql, new
        {
            oldUserId,
            newUserId
        });
    }

    public static async Task RenameArtistImports(int userId, NpgsqlConnection connection, string oldArtistName, string newArtistName)
    {
        if (string.IsNullOrEmpty(oldArtistName) || string.IsNullOrEmpty(newArtistName))
        {
            throw new ArgumentException("Artist names cannot be null or empty");
        }

        await using var renameArtistImports = new NpgsqlCommand("UPDATE public.user_plays " +
                                                        "SET artist_name = @newArtistName " +
                                                        "WHERE user_id = @userId " +
                                                        "AND play_source != 0 " +
                                                        "AND LOWER(artist_name) = LOWER(@oldArtistName)", connection);

        renameArtistImports.Parameters.AddWithValue("userId", userId);
        renameArtistImports.Parameters.AddWithValue("oldArtistName", oldArtistName);
        renameArtistImports.Parameters.AddWithValue("newArtistName", newArtistName);

        await renameArtistImports.ExecuteNonQueryAsync();
    }

    public static async Task DeleteArtistImports(int userId, NpgsqlConnection connection, string artistName)
    {
        if (string.IsNullOrEmpty(artistName))
        {
            throw new ArgumentException("Artist name cannot be null or empty");
        }

        await using var deleteArtistImports = new NpgsqlCommand("DELETE FROM public.user_plays " +
                                                        "WHERE user_id = @userId " +
                                                        "AND play_source != 0 " +
                                                        "AND LOWER(artist_name) = LOWER(@artistName)", connection);

        deleteArtistImports.Parameters.AddWithValue("userId", userId);
        deleteArtistImports.Parameters.AddWithValue("artistName", artistName);

        await deleteArtistImports.ExecuteNonQueryAsync();
    }

    public static async Task RenameAlbumImports(int userId, NpgsqlConnection connection, string artistName, string oldAlbumName, string newArtistName, string newAlbumName)
    {
        if (string.IsNullOrEmpty(artistName) || string.IsNullOrEmpty(oldAlbumName) ||
            string.IsNullOrEmpty(newArtistName) || string.IsNullOrEmpty(newAlbumName))
        {
            throw new ArgumentException("Artist and album names cannot be null or empty");
        }

        await using var renameAlbumImports = new NpgsqlCommand("UPDATE public.user_plays " +
                                                        "SET artist_name = @newArtistName, " +
                                                        "album_name = @newAlbumName " +
                                                        "WHERE user_id = @userId " +
                                                        "AND play_source != 0 " +
                                                        "AND LOWER(artist_name) = LOWER(@artistName) " +
                                                        "AND LOWER(album_name) = LOWER(@oldAlbumName)", connection);

        renameAlbumImports.Parameters.AddWithValue("userId", userId);
        renameAlbumImports.Parameters.AddWithValue("artistName", artistName);
        renameAlbumImports.Parameters.AddWithValue("oldAlbumName", oldAlbumName);
        renameAlbumImports.Parameters.AddWithValue("newArtistName", newArtistName);
        renameAlbumImports.Parameters.AddWithValue("newAlbumName", newAlbumName);

        await renameAlbumImports.ExecuteNonQueryAsync();
    }

    public static async Task DeleteAlbumImports(int userId, NpgsqlConnection connection, string artistName, string albumName)
    {
        if (string.IsNullOrEmpty(artistName) || string.IsNullOrEmpty(albumName))
        {
            throw new ArgumentException("Artist and album names cannot be null or empty");
        }

        await using var deleteAlbumImports = new NpgsqlCommand("DELETE FROM public.user_plays " +
                                                        "WHERE user_id = @userId " +
                                                        "AND play_source != 0 " +
                                                        "AND LOWER(artist_name) = LOWER(@artistName) " +
                                                        "AND LOWER(album_name) = LOWER(@albumName)", connection);

        deleteAlbumImports.Parameters.AddWithValue("userId", userId);
        deleteAlbumImports.Parameters.AddWithValue("artistName", artistName);
        deleteAlbumImports.Parameters.AddWithValue("albumName", albumName);

        await deleteAlbumImports.ExecuteNonQueryAsync();
    }

    public static async Task RenameTrackImports(int userId, NpgsqlConnection connection, string artistName, string oldTrackName, string newArtistName, string newTrackName)
    {
        if (string.IsNullOrEmpty(artistName) || string.IsNullOrEmpty(oldTrackName) ||
            string.IsNullOrEmpty(newArtistName) || string.IsNullOrEmpty(newTrackName))
        {
            throw new ArgumentException("Artist and track names cannot be null or empty");
        }

        await using var renameTrackImports = new NpgsqlCommand("UPDATE public.user_plays " +
                                                        "SET artist_name = @newArtistName, " +
                                                        "track_name = @newTrackName " +
                                                        "WHERE user_id = @userId " +
                                                        "AND play_source != 0 " +
                                                        "AND LOWER(artist_name) = LOWER(@artistName) " +
                                                        "AND LOWER(track_name) = LOWER(@oldTrackName)", connection);

        renameTrackImports.Parameters.AddWithValue("userId", userId);
        renameTrackImports.Parameters.AddWithValue("artistName", artistName);
        renameTrackImports.Parameters.AddWithValue("oldTrackName", oldTrackName);
        renameTrackImports.Parameters.AddWithValue("newArtistName", newArtistName);
        renameTrackImports.Parameters.AddWithValue("newTrackName", newTrackName);

        // await renameTrackImports.ExecuteNonQueryAsync();
    }

    public static async Task DeleteTrackImports(int userId, NpgsqlConnection connection, string artistName, string trackName)
    {
        if (string.IsNullOrEmpty(artistName) || string.IsNullOrEmpty(trackName))
        {
            throw new ArgumentException("Artist and track names cannot be null or empty");
        }

        await using var deleteTrackImports = new NpgsqlCommand("DELETE FROM public.user_plays " +
                                                        "WHERE user_id = @userId " +
                                                        "AND play_source != 0 " +
                                                        "AND LOWER(artist_name) = LOWER(@artistName) " +
                                                        "AND LOWER(track_name) = LOWER(@trackName)", connection);

        deleteTrackImports.Parameters.AddWithValue("userId", userId);
        deleteTrackImports.Parameters.AddWithValue("artistName", artistName);
        deleteTrackImports.Parameters.AddWithValue("trackName", trackName);

        await deleteTrackImports.ExecuteNonQueryAsync();
    }
}
