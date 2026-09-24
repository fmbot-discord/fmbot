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
        NpgsqlConnection connection, Func<IReadOnlyList<UserPlay>, Task> resolveIds = null)
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
            if (resolveIds != null)
            {
                await resolveIds(addedPlays);
            }

            Log.Debug("Inserting {addedPlaysCount} new time series plays for user {userId}", addedPlays.Count,
                userId);
            await InsertTimeSeriesPlays(addedPlays, connection);
        }

        return new PlayUpdate(addedPlays, removedPlays);
    }

    public static async Task ReplaceAllPlays(IReadOnlyList<UserPlay> playsToInsert, int userId,
        NpgsqlConnection connection)
    {
        await RemoveAllCurrentLastFmPlays(userId, connection);

        Log.Debug("Inserting {playCount} time series plays for user {userId}", playsToInsert.Count, userId);
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
            @"
    WITH plays_to_keep AS (
        SELECT user_play_id
        FROM public.user_plays
        WHERE user_id = @userId
        ORDER BY time_played DESC
        LIMIT 500
    ),
    recent_plays AS (
        SELECT user_play_id
        FROM public.user_plays
        WHERE user_id = @userId
        ORDER BY time_played DESC
        LIMIT 17500
    )
    DELETE FROM public.user_plays p
    WHERE
        p.user_id = @userId
        AND (p.play_source = 0 OR p.play_source IS NULL)
        -- Never delete the last 500 plays
        AND p.user_play_id NOT IN (SELECT user_play_id FROM plays_to_keep)
        -- Delete if older than 12 months OR outside last 17.5k
        AND (
            p.time_played < CURRENT_DATE - INTERVAL '12 months'
            OR p.user_play_id NOT IN (SELECT user_play_id FROM recent_plays)
        );", connection);

        deletePlays.Parameters.AddWithValue("userId", userId);
        await deletePlays.ExecuteNonQueryAsync();
    }

    public static async Task PruneUserPlaysToRecentOnly(int userId, NpgsqlConnection connection)
    {
        await using var deleteCommand = new NpgsqlCommand(
            @"WITH recent_plays AS (
            SELECT user_play_id
            FROM public.user_plays
            WHERE user_id = @userId AND play_source = 0
            ORDER BY time_played DESC
            LIMIT 1000
        )
        DELETE FROM public.user_plays
        WHERE user_id = @userId
        AND user_play_id NOT IN (SELECT user_play_id FROM recent_plays)
        AND play_source = 0;",
            connection);

        deleteCommand.Parameters.AddWithValue("userId", userId);
        await deleteCommand.ExecuteNonQueryAsync();
    }

    private static async Task RemoveSpecificPlays(IEnumerable<UserPlay> playsToRemove, NpgsqlConnection connection)
    {
        foreach (var playToRemove in playsToRemove)
        {
            await using var deletePlays = new NpgsqlCommand(
                "DELETE FROM public.user_plays " +
                "WHERE user_play_id = @id AND user_id = @userId " +
                "AND play_source != 1 AND play_source != 2",
                connection
            );

            deletePlays.Parameters.AddWithValue("id", playToRemove.UserPlayId);
            deletePlays.Parameters.AddWithValue("userId", playToRemove.UserId);

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
            .MapInteger("play_source", x => (int?)x.PlaySource)
            .MapInteger("artist_id", x => x.ArtistId)
            .MapInteger("album_id", x => x.AlbumId)
            .MapInteger("track_id", x => x.TrackId);

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

    public record UserPlaySearchResult(string TrackName, string AlbumName, string ArtistName, DateTime TimePlayed, int PlaySource);

    public static async Task<IReadOnlyList<UserPlaySearchResult>> SearchUserPlays(int userId, string query,
        NpgsqlConnection connection)
    {
        var patterns = UserLibrarySearch.BuildPatterns(query);
        if (patterns.Length == 0)
        {
            return [];
        }

        const string sql = @"
SELECT track_name, album_name, artist_name, time_played, play_source
FROM public.user_plays
WHERE user_id = @userId
  AND (artist_name || ' ' || COALESCE(album_name, '') || ' ' || track_name) ILIKE ALL(@patterns)
ORDER BY time_played DESC;";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserPlaySearchResult>(sql, new { userId, patterns })).ToList();
    }

    public record UserPlayDay(DateTime Day, int Plays, int LastFmPlays, DateTime FirstPlay, DateTime? LastPlay,
        int WeekPlays, int MonthPlays);

    public static async Task<DateTime?> GetLastPlayTime(int userId, NpgsqlConnection connection,
        DataSource dataSource)
    {
        var sql = GetUserPlaysSqlString("SELECT time_played ", dataSource);

        return await connection.QueryFirstOrDefaultAsync<DateTime?>(sql, new
        {
            userId,
            limit = 1
        });
    }

    public static async Task<IReadOnlyList<UserPlayDay>> GetUserPlayDays(int userId, NpgsqlConnection connection,
        DataSource dataSource, string artistName, string albumName, string trackName, DateTime weekAgo,
        DateTime monthAgo, DateTime? lastPlayCutoff, DateTime? start = null, DateTime? end = null)
    {
        const string initialSql =
            "SELECT (date_trunc('day', time_played AT TIME ZONE 'UTC')) AT TIME ZONE 'UTC' AS day, " +
            "COUNT(*)::int AS plays, " +
            "(COUNT(*) FILTER (WHERE play_source = 0))::int AS last_fm_plays, " +
            "MIN(time_played) AS first_play, " +
            "MAX(CASE WHEN time_played < @lastPlayCutoff THEN time_played END) AS last_play, " +
            "(COUNT(*) FILTER (WHERE time_played >= @weekAgo))::int AS week_plays, " +
            "(COUNT(*) FILTER (WHERE time_played >= @monthAgo))::int AS month_plays ";

        var sql = GetUserPlaysSqlString(initialSql, dataSource, start, end);

        if (artistName != null)
        {
            sql += " AND UPPER(artist_name) = UPPER(CAST(@artistName AS CITEXT)) ";
        }

        if (albumName != null)
        {
            sql += " AND UPPER(album_name) = UPPER(CAST(@albumName AS CITEXT)) ";
        }

        if (trackName != null)
        {
            sql += " AND UPPER(track_name) = UPPER(CAST(@trackName AS CITEXT)) ";
        }

        sql += " GROUP BY 1 ORDER BY 1 ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return
        [
            .. await connection.QueryAsync<UserPlayDay>(sql, new
            {
                userId,
                artistName,
                albumName,
                trackName,
                weekAgo,
                monthAgo,
                lastPlayCutoff,
                start,
                end
            })
        ];
    }

    public record RecentPlaycounts(int Week, int Month);

    public static async Task<RecentPlaycounts> GetRecentEntityPlaycounts(int userId, NpgsqlConnection connection,
        DataSource dataSource, string artistName, string albumName, string trackName, DateTime weekAgo,
        DateTime monthAgo)
    {
        const string initialSql = "SELECT (COUNT(*) FILTER (WHERE time_played >= @weekAgo))::int AS week, " +
                                  "COUNT(*)::int AS month ";

        var sql = GetUserPlaysSqlString(initialSql, dataSource, monthAgo) +
                  " AND UPPER(artist_name) = UPPER(CAST(@artistName AS CITEXT)) ";

        if (albumName != null)
        {
            sql += " AND UPPER(album_name) = UPPER(CAST(@albumName AS CITEXT)) ";
        }

        if (trackName != null)
        {
            sql += " AND UPPER(track_name) = UPPER(CAST(@trackName AS CITEXT)) ";
        }

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return await connection.QueryFirstOrDefaultAsync<RecentPlaycounts>(sql, new
        {
            userId,
            artistName,
            albumName,
            trackName,
            weekAgo,
            start = monthAgo
        });
    }

    public static async Task<DateTime?> GetFirstPlayDateForEntity(int userId, NpgsqlConnection connection,
        DataSource dataSource, string artistName, string albumName = null, string trackName = null)
    {
        var sql = GetUserPlaysSqlString("SELECT MIN(time_played) ", dataSource);
        sql += GetEntityFilterSql(albumName, trackName);

        return await connection.QueryFirstOrDefaultAsync<DateTime?>(sql, new
        {
            userId,
            artistName,
            albumName,
            trackName
        });
    }

    public static async Task<DateTime?> GetLastPlayDateForEntity(int userId, NpgsqlConnection connection,
        DataSource dataSource, DateTime cutoff, string artistName, string albumName = null, string trackName = null)
    {
        var sql = GetUserPlaysSqlString("SELECT MAX(time_played) ", dataSource);
        sql += " AND time_played < @cutoff ";
        sql += GetEntityFilterSql(albumName, trackName);

        return await connection.QueryFirstOrDefaultAsync<DateTime?>(sql, new
        {
            userId,
            artistName,
            albumName,
            trackName,
            cutoff
        });
    }

    public static async Task<DateTime?> GetLatestPlayDate(int userId, NpgsqlConnection connection,
        DataSource dataSource)
    {
        var sql = GetUserPlaysSqlString("SELECT MAX(time_played) ", dataSource);

        return await connection.QueryFirstOrDefaultAsync<DateTime?>(sql, new
        {
            userId
        });
    }

    private static string GetEntityFilterSql(string albumName, string trackName)
    {
        var sql = " AND UPPER(artist_name) = UPPER(CAST(@artistName AS CITEXT)) ";

        if (albumName != null)
        {
            sql += " AND UPPER(album_name) = UPPER(CAST(@albumName AS CITEXT)) ";
        }

        if (trackName != null)
        {
            sql += " AND UPPER(track_name) = UPPER(CAST(@trackName AS CITEXT)) ";
        }

        return sql;
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
            DataSource.MergedDeduplicated =>
                " FROM public.user_plays up WHERE up.user_id = @userId AND up.artist_name IS NOT NULL AND ( " +
                "up.play_source = 0 OR " +
                "((up.play_source = 1 OR up.play_source = 2) AND NOT EXISTS ( " +
                "SELECT 1 FROM public.user_plays lfm " +
                "WHERE lfm.user_id = @userId AND lfm.play_source = 0 " +
                "AND lfm.artist_name = up.artist_name AND lfm.track_name = up.track_name " +
                "AND lfm.time_played BETWEEN " +
                "(CASE WHEN COALESCE(up.ms_played, 0) > 0 " +
                "THEN up.time_played - (up.ms_played::float8 * INTERVAL '1 millisecond') - INTERVAL '2 hours' " +
                "ELSE up.time_played - INTERVAL '2 hours' END) " +
                "AND up.time_played + INTERVAL '2 hours' " +
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

        if (!initialSql.Contains("COUNT(*)", StringComparison.OrdinalIgnoreCase) &&
            !initialSql.Contains("MIN(", StringComparison.OrdinalIgnoreCase) &&
            !initialSql.Contains("MAX(", StringComparison.OrdinalIgnoreCase))
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

    public static async Task<bool> HasPlayNearTimestamp(int userId, NpgsqlConnection connection,
        DateTime timestamp, int secondsRange = 60)
    {
        var start = timestamp.AddSeconds(-secondsRange);
        var end = timestamp.AddSeconds(secondsRange);

        const string sql = "SELECT EXISTS(SELECT 1 FROM public.user_plays WHERE user_id = @userId " +
                           "AND time_played >= @start AND time_played <= @end)";

        return await connection.QueryFirstOrDefaultAsync<bool>(sql, new { userId, start, end });
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
                                                                "SET artist_name = @newArtistName, " +
                                                                "artist_id = (SELECT id FROM public.artists WHERE name = CAST(@newArtistName AS CITEXT) LIMIT 1), " +
                                                                "album_id = (SELECT id FROM public.albums WHERE artist_name = CAST(@newArtistName AS CITEXT) AND name = user_plays.album_name LIMIT 1), " +
                                                                "track_id = (SELECT id FROM public.tracks WHERE artist_name = CAST(@newArtistName AS CITEXT) AND name = user_plays.track_name LIMIT 1) " +
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

    public static async Task RenameAlbumImports(int userId, NpgsqlConnection connection, string artistName, string oldAlbumName,
        string newArtistName, string newAlbumName)
    {
        if (string.IsNullOrEmpty(artistName) || string.IsNullOrEmpty(oldAlbumName) ||
            string.IsNullOrEmpty(newArtistName) || string.IsNullOrEmpty(newAlbumName))
        {
            throw new ArgumentException("Artist and album names cannot be null or empty");
        }

        await using var renameAlbumImports = new NpgsqlCommand("UPDATE public.user_plays " +
                                                               "SET artist_name = @newArtistName, " +
                                                               "album_name = @newAlbumName, " +
                                                               "artist_id = (SELECT id FROM public.artists WHERE name = CAST(@newArtistName AS CITEXT) LIMIT 1), " +
                                                               "album_id = (SELECT id FROM public.albums WHERE artist_name = CAST(@newArtistName AS CITEXT) AND name = CAST(@newAlbumName AS CITEXT) LIMIT 1), " +
                                                               "track_id = (SELECT id FROM public.tracks WHERE artist_name = CAST(@newArtistName AS CITEXT) AND name = user_plays.track_name LIMIT 1) " +
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

    public static async Task RenameTrackImports(int userId, NpgsqlConnection connection, string artistName, string oldTrackName,
        string newArtistName, string newTrackName)
    {
        if (string.IsNullOrEmpty(artistName) || string.IsNullOrEmpty(oldTrackName) ||
            string.IsNullOrEmpty(newArtistName) || string.IsNullOrEmpty(newTrackName))
        {
            throw new ArgumentException("Artist and track names cannot be null or empty");
        }

        await using var renameTrackImports = new NpgsqlCommand("UPDATE public.user_plays " +
                                                               "SET artist_name = @newArtistName, " +
                                                               "track_name = @newTrackName, " +
                                                               "artist_id = (SELECT id FROM public.artists WHERE name = CAST(@newArtistName AS CITEXT) LIMIT 1), " +
                                                               "album_id = (SELECT id FROM public.albums WHERE artist_name = CAST(@newArtistName AS CITEXT) AND name = user_plays.album_name LIMIT 1), " +
                                                               "track_id = (SELECT id FROM public.tracks WHERE artist_name = CAST(@newArtistName AS CITEXT) AND name = CAST(@newTrackName AS CITEXT) LIMIT 1) " +
                                                               "WHERE user_id = @userId " +
                                                               "AND play_source != 0 " +
                                                               "AND LOWER(artist_name) = LOWER(@artistName) " +
                                                               "AND LOWER(track_name) = LOWER(@oldTrackName)", connection);

        renameTrackImports.Parameters.AddWithValue("userId", userId);
        renameTrackImports.Parameters.AddWithValue("artistName", artistName);
        renameTrackImports.Parameters.AddWithValue("oldTrackName", oldTrackName);
        renameTrackImports.Parameters.AddWithValue("newArtistName", newArtistName);
        renameTrackImports.Parameters.AddWithValue("newTrackName", newTrackName);

        await renameTrackImports.ExecuteNonQueryAsync();
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

    public static async Task<List<WhoKnowsObjectWithUser>> GetGuildUsersTotalPlaycount(
        IDictionary<int, FullGuildUser> guildUsers, int guildId, NpgsqlConnection connection)
    {
        const string sql = "SELECT u.total_playcount AS playcount, " +
                           "u.user_id " +
                           "FROM users AS u " +
                           "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                           "WHERE gu.guild_id = @guildId AND u.total_playcount is not null " +
                           "ORDER BY u.total_playcount DESC ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var userPlaycounts = (await connection.QueryAsync<WhoKnowsAlbumDto>(sql, new
        {
            guildId,
        })).ToList();

        var whoKnowsList = new List<WhoKnowsObjectWithUser>();

        foreach (var userPlaycount in userPlaycounts)
        {
            if (!guildUsers.TryGetValue(userPlaycount.UserId, out var guildUser))
            {
                continue;
            }

            whoKnowsList.Add(new WhoKnowsObjectWithUser
            {
                DiscordName = guildUser.UserName ?? guildUser.UserNameLastFM,
                Playcount = userPlaycount.Playcount,
                LastFMUsername = guildUser.UserNameLastFM,
                UserId = userPlaycount.UserId,
                LastUsed = guildUser.LastUsed,
                LastMessage = guildUser.LastMessage,
                Roles = guildUser.Roles
            });
        }

        return whoKnowsList;
    }

    public static async Task<List<GuildTrack>> GetGuildTopTracksPlays(int guildId, DateTime startDateTime,
        OrderType orderType, string searchValue, DateTime? endDateTime, int limit, int[] userIds,
        NpgsqlConnection connection)
    {
        var artistFilter = !string.IsNullOrWhiteSpace(searchValue)
            ? "AND UPPER(up.artist_name) = UPPER(CAST(@searchValue AS CITEXT)) "
            : "";

        var endDateFilter = endDateTime.HasValue
            ? "AND up.time_played < @endDateTime "
            : "";

        var userFilter = userIds != null
            ? "AND up.user_id = ANY(@userIds) "
            : "";

        var orderColumn = orderType == OrderType.Listeners ? "ListenerCount" : "TotalPlaycount";
        var thenByColumn = orderType == OrderType.Listeners ? "TotalPlaycount" : "ListenerCount";

        var sql = "SELECT t.name AS TrackName, " +
                  "t.artist_name AS ArtistName, " +
                  "agg.track_id AS TrackId, " +
                  "agg.TotalPlaycount, " +
                  "agg.ListenerCount " +
                  "FROM ( " +
                  "    SELECT up.track_id, " +
                  "           COUNT(*)::int AS TotalPlaycount, " +
                  "           COUNT(DISTINCT up.user_id)::int AS ListenerCount " +
                  "    FROM user_plays up " +
                  "    INNER JOIN guild_users gu ON gu.user_id = up.user_id " +
                  "    WHERE gu.guild_id = @guildId " +
                  "      AND gu.bot != true " +
                  "      AND up.time_played > @startDateTime " +
                  $"      {endDateFilter}" +
                  "      AND up.track_id IS NOT NULL " +
                  $"      {artistFilter}" +
                  $"      {userFilter}" +
                  "      AND NOT up.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                  "      AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                  "    GROUP BY up.track_id " +
                  $"    ORDER BY {orderColumn} DESC, {thenByColumn} DESC " +
                  "    LIMIT @limit " +
                  ") agg " +
                  "INNER JOIN tracks t ON t.id = agg.track_id " +
                  $"ORDER BY agg.{orderColumn} DESC, agg.{thenByColumn} DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<GuildTrack>(sql, new
        {
            guildId,
            startDateTime,
            endDateTime,
            searchValue,
            limit,
            userIds
        }, commandTimeout: 300)).ToList();
    }

    public static async Task<List<GuildArtist>> GetGuildTopArtistsPlays(int guildId, DateTime startDateTime,
        OrderType orderType, DateTime? endDateTime, int limit, int[] userIds, NpgsqlConnection connection)
    {
        var endDateFilter = endDateTime.HasValue
            ? "AND up.time_played < @endDateTime "
            : "";

        var userFilter = userIds != null
            ? "AND up.user_id = ANY(@userIds) "
            : "";

        var orderColumn = orderType == OrderType.Listeners ? "ListenerCount" : "TotalPlaycount";
        var thenByColumn = orderType == OrderType.Listeners ? "TotalPlaycount" : "ListenerCount";

        var sql = "SELECT a.name AS ArtistName, " +
                  "agg.artist_id AS ArtistId, " +
                  "agg.TotalPlaycount, " +
                  "agg.ListenerCount " +
                  "FROM ( " +
                  "    SELECT up.artist_id, " +
                  "           COUNT(*)::int AS TotalPlaycount, " +
                  "           COUNT(DISTINCT up.user_id)::int AS ListenerCount " +
                  "    FROM user_plays up " +
                  "    INNER JOIN guild_users gu ON gu.user_id = up.user_id " +
                  "    WHERE gu.guild_id = @guildId " +
                  "      AND gu.bot != true " +
                  "      AND up.time_played > @startDateTime " +
                  $"      {endDateFilter}" +
                  "      AND up.artist_id IS NOT NULL " +
                  $"      {userFilter}" +
                  "      AND NOT up.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                  "      AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                  "    GROUP BY up.artist_id " +
                  $"    ORDER BY {orderColumn} DESC, {thenByColumn} DESC " +
                  "    LIMIT @limit " +
                  ") agg " +
                  "INNER JOIN artists a ON a.id = agg.artist_id " +
                  $"ORDER BY agg.{orderColumn} DESC, agg.{thenByColumn} DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<GuildArtist>(sql, new
        {
            guildId,
            startDateTime,
            endDateTime,
            limit,
            userIds
        }, commandTimeout: 300)).ToList();
    }

    public static async Task<List<GuildAlbum>> GetGuildTopAlbumsPlays(int guildId, DateTime startDateTime,
        OrderType orderType, string searchValue, DateTime? endDateTime, int limit, int[] userIds,
        NpgsqlConnection connection)
    {
        var artistFilter = !string.IsNullOrWhiteSpace(searchValue)
            ? "AND UPPER(up.artist_name) = UPPER(CAST(@searchValue AS CITEXT)) "
            : "";

        var endDateFilter = endDateTime.HasValue
            ? "AND up.time_played < @endDateTime "
            : "";

        var userFilter = userIds != null
            ? "AND up.user_id = ANY(@userIds) "
            : "";

        var orderColumn = orderType == OrderType.Listeners ? "ListenerCount" : "TotalPlaycount";
        var thenByColumn = orderType == OrderType.Listeners ? "TotalPlaycount" : "ListenerCount";

        var sql = "SELECT al.name AS AlbumName, " +
                  "al.artist_name AS ArtistName, " +
                  "agg.album_id AS AlbumId, " +
                  "agg.TotalPlaycount, " +
                  "agg.ListenerCount " +
                  "FROM ( " +
                  "    SELECT up.album_id, " +
                  "           COUNT(*)::int AS TotalPlaycount, " +
                  "           COUNT(DISTINCT up.user_id)::int AS ListenerCount " +
                  "    FROM user_plays up " +
                  "    INNER JOIN guild_users gu ON gu.user_id = up.user_id " +
                  "    WHERE gu.guild_id = @guildId " +
                  "      AND gu.bot != true " +
                  "      AND up.time_played > @startDateTime " +
                  $"      {endDateFilter}" +
                  "      AND up.album_id IS NOT NULL " +
                  $"      {artistFilter}" +
                  $"      {userFilter}" +
                  "      AND NOT up.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                  "      AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                  "    GROUP BY up.album_id " +
                  $"    ORDER BY {orderColumn} DESC, {thenByColumn} DESC " +
                  "    LIMIT @limit " +
                  ") agg " +
                  "INNER JOIN albums al ON al.id = agg.album_id " +
                  $"ORDER BY agg.{orderColumn} DESC, agg.{thenByColumn} DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<GuildAlbum>(sql, new
        {
            guildId,
            startDateTime,
            endDateTime,
            searchValue,
            limit,
            userIds
        }, commandTimeout: 300)).ToList();
    }

    public static async Task<List<GuildMemberLatestPlay>> GetLatestPlayPerGuildMember(int guildId, int memberCap,
        int limit, NpgsqlConnection connection)
    {
        const string sql =
            "WITH members AS ( " +
            "  SELECT gu.user_id FROM guild_users gu INNER JOIN users u ON u.user_id = gu.user_id " +
            "  WHERE gu.guild_id = @guildId AND gu.bot IS NOT TRUE " +
            "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
            "AND NOT EXISTS (SELECT 1 FROM guild_blocked_users gbu WHERE gbu.guild_id = gu.guild_id AND gbu.user_id = gu.user_id AND gbu.blocked_from_who_knows) " +
            "AND u.last_used > now() - interval '60 days' " +
            "  ORDER BY u.last_used DESC LIMIT @memberCap " +
            ") " +
            "SELECT m.user_id, p.track_name, p.artist_name, p.album_name, p.time_played, " +
            "  COALESCE(ab.spotify_image_url, ab.lastfm_image_url) AS cover_url " +
            "FROM members m " +
            "CROSS JOIN LATERAL ( " +
            "  SELECT up.track_name, up.artist_name, up.album_name, up.time_played " +
            "  FROM user_plays up WHERE up.user_id = m.user_id ORDER BY up.time_played DESC LIMIT 1 " +
            ") p " +
            "LEFT JOIN albums ab ON p.album_name IS NOT NULL AND ab.artist_name = p.artist_name AND ab.name = p.album_name " +
            "ORDER BY p.time_played DESC " +
            "LIMIT @limit";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<GuildMemberLatestPlay>(sql, new { guildId, memberCap, limit })).ToList();
    }


    public static async Task<List<EntityPlaycount>> GetUserPeriodArtistPlaycounts(int userId, DateTime since,
        string[] artistNames, NpgsqlConnection connection)
    {
        const string sql = "SELECT artist_name, artist_name AS name, COUNT(*) AS playcount FROM user_plays " +
                           "WHERE user_id = @userId AND time_played > @since AND artist_name = ANY(@artistNames::citext[]) " +
                           "GROUP BY artist_name";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<EntityPlaycount>(sql, new { userId, since, artistNames })).ToList();
    }

    public static async Task<List<EntityPlaycount>> GetUserPeriodAlbumPlaycounts(int userId, DateTime since,
        string[] artistNames, string[] albumNames, NpgsqlConnection connection)
    {
        const string sql = "SELECT up.artist_name, up.album_name AS name, COUNT(*) AS playcount FROM user_plays up " +
                           "INNER JOIN unnest(@artistNames::citext[], @albumNames::citext[]) AS k(artist_name, name) " +
                           "ON k.artist_name = up.artist_name AND k.name = up.album_name " +
                           "WHERE up.user_id = @userId AND up.time_played > @since " +
                           "GROUP BY up.artist_name, up.album_name";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<EntityPlaycount>(sql, new { userId, since, artistNames, albumNames })).ToList();
    }

    public static async Task<List<EntityPlaycount>> GetUserPeriodTrackPlaycounts(int userId, DateTime since,
        string[] artistNames, string[] trackNames, NpgsqlConnection connection)
    {
        const string sql = "SELECT up.artist_name, up.track_name AS name, COUNT(*) AS playcount FROM user_plays up " +
                           "INNER JOIN unnest(@artistNames::citext[], @trackNames::citext[]) AS k(artist_name, name) " +
                           "ON k.artist_name = up.artist_name AND k.name = up.track_name " +
                           "WHERE up.user_id = @userId AND up.time_played > @since " +
                           "GROUP BY up.artist_name, up.track_name";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<EntityPlaycount>(sql, new { userId, since, artistNames, trackNames })).ToList();
    }
}
