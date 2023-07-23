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

    public static async Task<PlayUpdate> InsertLatestPlays(IEnumerable<RecentTrack> recentTracks, int userId, NpgsqlConnection connection)
    {
        var plays = recentTracks
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

        var existingPlays = await GetUserPlays(userId, connection, plays.Count + 250);

        var firstExistingPlay = existingPlays.MinBy(o => o.TimePlayed);

        if (firstExistingPlay != null)
        {
            plays = plays
                .Where(w => w.TimePlayed >= firstExistingPlay.TimePlayed)
                .ToList();
        }

        var addedPlays = new List<UserPlay>();
        foreach (var newPlay in plays)
        {
            if (existingPlays.All(a => a.TimePlayed != newPlay.TimePlayed))
            {
                addedPlays.Add(newPlay);
            }
        }

        var firstNewPlay = plays.MinBy(o => o.TimePlayed);

        var removedPlays = new List<UserPlay>();
        if (firstNewPlay != null)
        {
            foreach (var existingPlay in existingPlays.Where(w => w.TimePlayed >= firstNewPlay.TimePlayed))
            {
                if (plays.All(a => a.TimePlayed != existingPlay.TimePlayed))
                {
                    removedPlays.Add(existingPlay);
                }
            }

            if (removedPlays.Any())
            {
                Log.Information("Found {removedPlaysCount} time series plays to remove for {userId}", removedPlays.Count, userId);
                await RemoveSpecificPlays(removedPlays, connection);
            }
        }

        if (addedPlays.Any())
        {
            Log.Information("Inserting {addedPlaysCount} new time series plays for user {userId}", addedPlays.Count, userId);
            await InsertTimeSeriesPlays(addedPlays, connection);
        }
        
        return new PlayUpdate(addedPlays, removedPlays);
    }

    public static async Task ReplaceAllPlays(IReadOnlyList<UserPlay> playsToInsert, int userId, NpgsqlConnection connection)
    {
        await RemoveAllCurrentLastFmPlays(userId, connection);

        Log.Information($"Inserting {playsToInsert.Count} time series plays for user {userId}");
        await InsertTimeSeriesPlays(playsToInsert, connection);
    }

    private static async Task RemoveAllCurrentLastFmPlays(int userId, NpgsqlConnection connection)
    {
        await using var deletePlays = new NpgsqlCommand("DELETE FROM public.user_plays " +
                                                        "WHERE user_id = @userId AND (play_source IS NULL OR play_source = 0);", connection);

        deletePlays.Parameters.AddWithValue("userId", userId);

        await deletePlays.ExecuteNonQueryAsync();
    }

    public static async Task RemoveAllImportPlays(int userId, NpgsqlConnection connection)
    {
        await using var deletePlays = new NpgsqlCommand("DELETE FROM public.user_plays " +
                                                        "WHERE user_id = @userId " +
                                                        "AND play_source = 1", connection);

        deletePlays.Parameters.AddWithValue("userId", userId);

        await deletePlays.ExecuteNonQueryAsync();
    }

    private static async Task RemoveSpecificPlays(IEnumerable<UserPlay> playsToRemove, NpgsqlConnection connection)
    {
        foreach (var playToRemove in playsToRemove)
        {
            await using var deletePlays = new NpgsqlCommand("DELETE FROM public.user_plays " +
                                                            "WHERE user_id = @userId AND time_played = @timePlayed " +
                                                            "AND play_source != 1", connection);

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

    public static async Task<ICollection<UserPlay>> GetUserPlays(int userId, NpgsqlConnection connection, int limit)
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

    public static async Task<ICollection<UserPlay>> GetUserPlaysWithinTimeRange(int userId, NpgsqlConnection connection, DateTime start, DateTime? end = null)
    {
        end ??= DateTime.UtcNow;

        const string sql = "SELECT * FROM public.user_plays WHERE user_id = @userId " +
                           "AND time_played >= @start AND time_played <= @end  " +
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
        const string sql = "UPDATE public.user_plays SET play_source = 0 WHERE user_id = @userId AND play_source IS null";

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
}
