using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;
using Npgsql;
using PostgreSQLCopyHelper;
using Serilog;

namespace FMBot.Persistence.Repositories;

public class TrackRepository
{
    private readonly BotSettings _botSettings;

    public TrackRepository(IOptions<BotSettings> botSettings)
    {
        this._botSettings = botSettings.Value;
    }

    public static async Task<ulong> AddOrReplaceUserTracksInDatabase(IReadOnlyList<UserTrack> tracks, int userId,
        NpgsqlConnection connection)
    {
        Log.Information("Index: {userId} - Inserting {albumCount} top tracks", userId, tracks.Count);

        var copyHelper = new PostgreSQLCopyHelper<UserTrack>("public", "user_tracks")
            .MapText("name", x => x.Name)
            .MapText("artist_name", x => x.ArtistName)
            .MapInteger("user_id", x => x.UserId)
            .MapInteger("playcount", x => x.Playcount);

        await using var deleteCurrentTracks = new NpgsqlCommand($"DELETE FROM public.user_tracks WHERE user_id = {userId};", connection);
        await deleteCurrentTracks.ExecuteNonQueryAsync();

        return await copyHelper.SaveAllAsync(connection, tracks);
    }

    public static async Task<Track> GetTrackForName(string artistName, string trackName, NpgsqlConnection connection, bool includeSyncedLyrics = false)
    {
        const string getTrackQuery = "SELECT * FROM public.tracks " +
                                     "WHERE UPPER(artist_name) = UPPER(CAST(@artistName AS CITEXT)) AND " +
                                     "UPPER(name) = UPPER(CAST(@trackName AS CITEXT))";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var track = await connection.QueryFirstOrDefaultAsync<Track>(getTrackQuery, new
        {
            artistName,
            trackName
        });

        if (includeSyncedLyrics && track != null)
        {
            track.SyncedLyrics = await GetSyncedLyrics(track.Id, connection);
        }

        return track;
    }

    private static async Task<ICollection<TrackSyncedLyrics>> GetSyncedLyrics(int trackId, NpgsqlConnection connection)
    {
        const string getTrackSyncedLyricsQuery = "SELECT * FROM public.track_synced_lyrics " +
                                          "WHERE track_id = @trackId";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<TrackSyncedLyrics>(getTrackSyncedLyricsQuery, new
        {
            trackId
        })).ToList();
    }

    public static async Task<ICollection<Track>> GetAlbumTracks(int albumId, NpgsqlConnection connection)
    {
        const string getTrackQuery = "SELECT * FROM public.tracks " +
                                     "WHERE album_id = @albumId ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<Track>(getTrackQuery, new
        {
            albumId
        })).ToList();
    }

    public static async Task<int> GetTrackPlayCountForUser(NpgsqlConnection connection, string artistName, string trackName, int userId)
    {
        const string sql = "SELECT ut.playcount " +
                           "FROM user_tracks AS ut " +
                           "WHERE ut.user_id = @userId AND " +
                           "UPPER(ut.name) = UPPER(CAST(@trackName AS CITEXT)) AND " +
                           "UPPER(ut.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                           "ORDER BY playcount DESC";

        return await connection.QueryFirstOrDefaultAsync<int>(sql, new
        {
            userId,
            trackName,
            artistName
        });
    }

    public static async Task<IReadOnlyCollection<UserTrack>> GetUserTracks(int userId, NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM public.user_tracks where user_id = @userId";
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserTrack>(sql, new
        {
            userId
        })).ToList();
    }
}
