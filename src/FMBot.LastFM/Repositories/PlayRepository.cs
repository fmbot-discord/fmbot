using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Npgsql;
using PostgreSQLCopyHelper;
using Serilog;

namespace FMBot.LastFM.Repositories;

public static class PlayRepository
{
    public record PlayUpdate(List<UserPlayTs> NewPlays, List<UserPlayTs> RemovedPlays);

    public static async Task<PlayUpdate> InsertLatestPlays(IEnumerable<RecentTrack> recentTracks, int userId, NpgsqlConnection connection)
    {
        var plays = recentTracks
            .Where(w => !w.NowPlaying &&
                        w.TimePlayed.HasValue)
            .Select(s => new UserPlayTs
            {
                ArtistName = s.ArtistName,
                AlbumName = s.AlbumName,
                TrackName = s.TrackName,
                TimePlayed = DateTime.SpecifyKind(s.TimePlayed.Value, DateTimeKind.Utc),
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

        var addedPlays = new List<UserPlayTs>();
        foreach (var newPlay in plays)
        {
            if (existingPlays.All(a => a.TimePlayed != newPlay.TimePlayed))
            {
                addedPlays.Add(newPlay);
            }
        }

        var firstNewPlay = plays.MinBy(o => o.TimePlayed);

        var removedPlays = new List<UserPlayTs>();
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

    public static async Task InsertAllPlays(IReadOnlyList<UserPlayTs> playsToInsert, int userId, NpgsqlConnection connection)
    {
        await RemoveAllCurrentPlays(userId, connection);

        Log.Information($"Inserting {playsToInsert.Count} time series plays for user {userId}");
        await InsertTimeSeriesPlays(playsToInsert, connection);
    }

    private static async Task RemoveAllCurrentPlays(int userId, NpgsqlConnection connection)
    {
        await using var deletePlays = new NpgsqlCommand("DELETE FROM public.user_play_ts " +
                                                        "WHERE user_id = @userId", connection);

        deletePlays.Parameters.AddWithValue("userId", userId);

        await deletePlays.ExecuteNonQueryAsync();
    }

    private static async Task RemoveSpecificPlays(IEnumerable<UserPlayTs> playsToRemove, NpgsqlConnection connection)
    {
        foreach (var playToRemove in playsToRemove)
        {
            await using var deletePlays = new NpgsqlCommand("DELETE FROM public.user_play_ts " +
                                                            "WHERE user_id = @userId AND time_played = @timePlayed", connection);

            deletePlays.Parameters.AddWithValue("userId", playToRemove.UserId);
            deletePlays.Parameters.AddWithValue("timePlayed", playToRemove.TimePlayed);

            await deletePlays.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertTimeSeriesPlays(IEnumerable<UserPlayTs> plays, NpgsqlConnection connection)
    {
        var copyHelper = new PostgreSQLCopyHelper<UserPlayTs>("public", "user_play_ts")
            .MapText("track_name", x => x.TrackName)
            .MapText("album_name", x => x.AlbumName)
            .MapText("artist_name", x => x.ArtistName)
            .MapTimeStampTz("time_played", x => DateTime.SpecifyKind(x.TimePlayed, DateTimeKind.Utc))
            .MapInteger("user_id", x => x.UserId);

        await copyHelper.SaveAllAsync(connection, plays);
    }

    public static async Task<IReadOnlyList<UserPlayTs>> GetUserPlays(int userId, NpgsqlConnection connection, int limit)
    {
        const string sql = "SELECT * FROM public.user_play_ts WHERE user_id = @userId " +
                           "ORDER BY time_played DESC LIMIT @limit";
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserPlayTs>(sql, new
        {
            userId,
            limit
        })).ToList();
    }

    public static async Task<IReadOnlyCollection<UserPlayTs>> GetUserPlaysWithinTimeRange(int userId, NpgsqlConnection connection, DateTime start, DateTime? end = null)
    {
        end ??= DateTime.UtcNow;

        const string sql = "SELECT * FROM public.user_play_ts WHERE user_id = @userId " +
                           "AND time_played >= @start AND time_played <= @end  " +
                           "ORDER BY time_played DESC ";
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserPlayTs>(sql, new
        {
            userId,
            start,
            end
        })).ToList();
    }
}
