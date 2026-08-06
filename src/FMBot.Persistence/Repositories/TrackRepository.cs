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
        Log.Information("Index: {userId} - Inserting {trackCount} top tracks", userId, tracks.Count);

        var copyHelper = new PostgreSQLCopyHelper<UserTrack>("public", "user_tracks")
            .MapText("name", x => x.Name)
            .MapText("artist_name", x => x.ArtistName)
            .MapInteger("user_id", x => x.UserId)
            .MapInteger("playcount", x => x.Playcount)
            .MapInteger("track_id", x => x.TrackId);

        await using var deleteCurrentTracks = new NpgsqlCommand($"DELETE FROM public.user_tracks WHERE user_id = {userId};", connection);
        await deleteCurrentTracks.ExecuteNonQueryAsync();

        return await copyHelper.SaveAllAsync(connection, tracks);
    }

    public static async Task<Track> GetTrackForName(string artistName, string trackName, NpgsqlConnection connection,
        bool includeSyncedLyrics = false)
    {
        const string getTrackQuery = "SELECT * FROM public.tracks " +
                                     "WHERE artist_name = CAST(@artistName AS CITEXT) AND " +
                                     "name = CAST(@trackName AS CITEXT) " +
                                     "ORDER BY id " +
                                     "LIMIT 1";

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

    public static async Task<int> GetTrackPlayCountForUser(NpgsqlConnection connection, int trackId, int userId)
    {
        const string sql = "SELECT ut.playcount " +
                           "FROM user_tracks AS ut " +
                           "WHERE ut.user_id = @userId AND ut.track_id = @trackId " +
                           "ORDER BY playcount DESC";

        return await connection.QueryFirstOrDefaultAsync<int>(sql, new
        {
            userId,
            trackId
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

    public static async Task<int> GetUserTrackCount(int userId, NpgsqlConnection connection)
    {
        const string sql = "SELECT COUNT(*) FROM public.user_tracks WHERE user_id = @userId";
        return await connection.QueryFirstOrDefaultAsync<int>(sql, new { userId });
    }

    public record UserTrackSearchResult(string Name, string ArtistName, int Playcount, int Rank);

    public static async Task<IReadOnlyList<UserTrackSearchResult>> SearchUserTracks(int userId, string query,
        NpgsqlConnection connection)
    {
        var patterns = UserLibrarySearch.BuildPatterns(query);
        if (patterns.Length == 0)
        {
            return [];
        }

        const string sql = @"
WITH ranked AS (
    SELECT name, artist_name, playcount,
           CAST(ROW_NUMBER() OVER (ORDER BY playcount DESC) AS int) AS rank
    FROM public.user_tracks
    WHERE user_id = @userId
)
SELECT name, artist_name, playcount, rank
FROM ranked
WHERE (artist_name || ' ' || name) ILIKE ALL(@patterns)
ORDER BY playcount DESC;";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserTrackSearchResult>(sql, new { userId, patterns })).ToList();
    }

    private static string BuildTrackSearchSql(string candidateFilter) => $@"
SELECT s.*
FROM public.tracks s
WHERE s.id = (
    WITH candidates AS MATERIALIZED (
        SELECT t.id,
               COALESCE(t.name, ''::citext)::text AS name,
               COALESCE(t.artist_name, ''::citext)::text AS artist_name,
               COALESCE(t.album_name, ''::citext)::text AS album_name,
               t.popularity,
               t.artist_id
        FROM public.tracks t
        WHERE {candidateFilter}
        ORDER BY t.popularity DESC NULLS LAST
        LIMIT 3000
    ), normalised AS (
        SELECT c.*,
               btrim(lower(public.f_search_text(c.name))) AS norm_name,
               btrim(lower(public.f_search_core(c.name))) AS norm_core,
               btrim(lower(public.f_search_text(c.artist_name))) AS norm_artist
        FROM candidates c
    ), pooled AS (
        SELECT n.*,
               max(n.popularity) OVER (PARTITION BY n.norm_artist, n.norm_core) AS group_popularity
        FROM normalised n
    )
    SELECT c.id
    FROM pooled c
    LEFT JOIN public.artists ar ON ar.id = c.artist_id
    CROSS JOIN LATERAL (
        SELECT btrim(lower(public.f_search_text(@searchTerm))) AS norm,
               btrim(lower(public.f_search_core(@searchTerm))) AS core
    ) q
    ORDER BY (
              2.5 * (CASE WHEN c.norm_artist || ' ' || c.norm_name = q.norm
                            OR c.norm_name || ' ' || c.norm_artist = q.norm THEN 1 ELSE 0 END)
            + 1.0 * (CASE WHEN c.norm_core = q.core THEN 1 ELSE 0 END)
            + 1.0 * similarity(c.norm_core, q.core)
            + 0.4 * word_similarity(q.core, c.norm_core)
            + 0.4 * (CASE WHEN c.norm_core LIKE q.core || '%' THEN 1 ELSE 0 END)
            + 0.5 * word_similarity(@searchTerm, c.name || ' ' || c.artist_name || ' ' || c.album_name)
            + 0.4 * word_similarity(c.artist_name, @searchTerm)
            + 0.3 * (CASE WHEN c.norm_name LIKE '%' || q.norm || '%' THEN 1 ELSE 0 END)
            + 2.5 * (ln(COALESCE(c.group_popularity, 0) + 1) / 4.6151)
            + 3.0 * (ln(COALESCE(ar.popularity, 0) + 1) / 4.6151)
        ) DESC NULLS LAST, length(c.name) ASC
    LIMIT 1
);";

    private const string TrackSearchMatch =
        "public.f_search_vector((COALESCE(t.name, ''::citext) || ' '::citext || COALESCE(t.artist_name, ''::citext))::text) " +
        "@@ public.f_search_query(@searchTerm)";

    private static readonly string[] TrackSearchStages =
    [
        BuildTrackSearchSql($"{TrackSearchMatch} AND t.popularity IS NOT NULL"),
        BuildTrackSearchSql($"{TrackSearchMatch} AND (t.popularity IS NOT NULL OR t.artist_id IS NOT NULL)"),
        BuildTrackSearchSql(
            "to_tsvector('english', (COALESCE(t.name, ''::citext) || ' '::citext || COALESCE(t.artist_name, ''::citext) " +
            "|| ' '::citext || COALESCE(t.album_name, ''::citext))::text) @@ plainto_tsquery('english', @searchTerm)")
    ];

    public static async Task<Track> SearchTrack(string searchTerm, NpgsqlConnection connection)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        foreach (var stage in TrackSearchStages)
        {
            var track = await connection.QueryFirstOrDefaultAsync<Track>(stage, new { searchTerm });
            if (track != null)
            {
                return track;
            }
        }

        return null;
    }
}
