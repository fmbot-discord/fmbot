using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Npgsql;
using PostgreSQLCopyHelper;
using Serilog;

namespace FMBot.Persistence.Repositories;

public static class TrackRepository
{
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

    public static async Task<IReadOnlyList<Track>> GetTrackPreviewSources(string artistName, string trackName,
        NpgsqlConnection connection)
    {
        const string getPreviewSourcesQuery = "SELECT id, spotify_preview_url, apple_music_preview_url, deezer_id FROM public.tracks " +
                                              "WHERE artist_name = CAST(@artistName AS CITEXT) AND " +
                                              "name = CAST(@trackName AS CITEXT) AND " +
                                              "(spotify_preview_url IS NOT NULL OR apple_music_preview_url IS NOT NULL OR deezer_id IS NOT NULL) " +
                                              "ORDER BY id";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<Track>(getPreviewSourcesQuery, new
        {
            artistName,
            trackName
        })).ToList();
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

    public static async Task<List<TopTrack>> GetTopUserTracks(int userId, int limit, NpgsqlConnection connection)
    {
        var sql = "SELECT ut.name AS track_name, ut.artist_name, ut.playcount AS user_playcount, cover.album_name, " +
                  "cover.image_url AS album_cover_url " +
                  "FROM (SELECT name, artist_name, playcount FROM public.user_tracks " +
                  "WHERE user_id = @userId ORDER BY playcount DESC LIMIT @limit) ut " +
                  $"LEFT JOIN LATERAL ({TrackCoverLateral("ut")}) cover ON TRUE " +
                  "ORDER BY ut.playcount DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<TopTrack>(sql, new { userId, limit })).ToList();
    }

    public static async Task<List<UserTrack>> GetUserTracksForArtist(int userId, string artistName,
        NpgsqlConnection connection)
    {
        const string sql = "SELECT ut.user_track_id, ut.user_id, t.name, t.artist_name, ut.playcount" +
                           " FROM public.user_tracks ut" +
                           " INNER JOIN public.tracks t ON t.id = ut.track_id" +
                           " WHERE ut.user_id = @userId AND UPPER(t.artist_name) = UPPER(CAST(@artistName AS CITEXT))" +
                           " ORDER BY ut.playcount DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<UserTrack>(sql, new
        {
            userId,
            artistName
        })).ToList();
    }

    public static async Task<int> GetUserTrackCount(int userId, NpgsqlConnection connection)
    {
        const string sql = "SELECT COUNT(*) FROM public.user_tracks WHERE user_id = @userId";
        return await connection.QueryFirstOrDefaultAsync<int>(sql, new { userId });
    }

    public record UserTrackSearchResult(string Name, string ArtistName, int Playcount, int Rank);

    public static async Task<IReadOnlyList<UserTrackSearchResult>> SearchUserTracks(int userId, string query,
        int? limit, NpgsqlConnection connection)
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
ORDER BY playcount DESC
LIMIT @limit;";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserTrackSearchResult>(sql, new { userId, patterns, limit })).ToList();
    }

    public static async Task<IReadOnlyList<FriendEntitySearchResult>> SearchFriendTracks(int[] userIds, string query,
        int limit, NpgsqlConnection connection)
    {
        var patterns = UserLibrarySearch.BuildPatterns(query);
        if (patterns.Length == 0 || userIds.Length == 0)
        {
            return [];
        }

        const string sql = @"
SELECT name,
       artist_name,
       CAST(COUNT(DISTINCT user_id) AS int) AS listeners,
       SUM(playcount) AS playcount,
       (array_agg(user_id ORDER BY playcount DESC))[1:3] AS user_ids,
       (array_agg(playcount ORDER BY playcount DESC))[1:3] AS user_playcounts
FROM public.user_tracks
WHERE user_id = ANY(@userIds)
  AND (artist_name || ' ' || name) ILIKE ALL(@patterns)
GROUP BY artist_name, name
ORDER BY listeners DESC, playcount DESC
LIMIT @limit;";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<FriendEntitySearchResult>(sql, new { userIds, patterns, limit })).ToList();
    }

    public static async Task<List<EntitySearchDetails>> GetTrackSearchDetails(string[] artistNames, string[] trackNames,
        NpgsqlConnection connection)
    {
        var sql = "SELECT input.artist_name, input.name, t.album_name, t.duration_ms, t.danceability, t.energy, " +
                  "t.acousticness, t.instrumentalness, t.valence, rel.release_date, rel.type AS album_type, cover.image_url " +
                  "FROM unnest(@artistNames::citext[], @trackNames::citext[]) AS input(artist_name, name) " +
                  "CROSS JOIN LATERAL (SELECT tr.album_name, tr.album_id, tr.duration_ms, tr.danceability, tr.energy, " +
                  "  tr.acousticness, tr.instrumentalness, tr.valence FROM public.tracks tr " +
                  "  WHERE tr.artist_name = input.artist_name AND tr.name = input.name " +
                  "  ORDER BY tr.popularity DESC NULLS LAST, tr.id LIMIT 1) t " +
                  "LEFT JOIN public.albums rel ON rel.id = t.album_id " +
                  $"LEFT JOIN LATERAL ({TrackCoverLateral("input")}) cover ON TRUE";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<EntitySearchDetails>(sql, new { artistNames, trackNames })).ToList();
    }

    private static string BuildTrackSearchSql(string candidates) => $@"
WITH q AS MATERIALIZED (
    SELECT btrim(lower(public.f_search_text(@searchTerm))) AS norm,
           btrim(lower(public.f_search_core(@searchTerm))) AS core
), candidates AS MATERIALIZED (
    {candidates}
), normalised AS (
    SELECT c.*,
           btrim(lower(public.f_search_text(c.name))) AS norm_name,
           btrim(lower(public.f_search_core(c.name))) AS norm_core,
           btrim(lower(public.f_search_text(c.artist_name))) AS norm_artist
    FROM candidates c
), pooled AS (
    SELECT n.*,
           max(n.popularity) OVER (PARTITION BY n.norm_artist, n.norm_core) AS group_popularity,
           count(*) OVER (PARTITION BY n.norm_artist, n.norm_core) AS variants,
           (SELECT count(*) FROM unnest(string_to_array(q.norm, ' ')) w
            WHERE w = ANY (string_to_array(n.norm_name || ' ' || n.norm_artist, ' ')))::float
               / greatest(1, array_length(string_to_array(q.norm, ' '), 1)) AS coverage
    FROM normalised n
    CROSS JOIN q
), scored AS (
    SELECT c.id, c.name, c.artist_name,
           (  2.65 * (CASE WHEN c.norm_artist || ' ' || c.norm_name = q.norm
                             OR c.norm_name || ' ' || c.norm_artist = q.norm THEN 1 ELSE 0 END)
            + 1.2 * (CASE WHEN c.norm_name || ' by ' || c.norm_artist = q.norm
                            OR c.norm_core || ' by ' || c.norm_artist = q.core THEN 1 ELSE 0 END)
            + 1.7 * (CASE WHEN c.norm_core = q.core THEN 1 ELSE 0 END)
            + 0.25 * (CASE WHEN c.norm_name = q.norm THEN 1 ELSE 0 END)
            + 0.9 * similarity(c.norm_core, q.core)
            + 2.05 * word_similarity(@searchTerm, c.name || ' ' || c.artist_name || ' ' || c.album_name)
            + 2.75 * COALESCE(c.group_popularity, 0) / 100.0
            + 1.15 * COALESCE(ar.popularity, 0) / 100.0
            + 0.6 * COALESCE(c.popularity, 0) / 100.0
            + 3.25 * ln(1 + c.variants) / ln(51)
            + 1.75 * c.coverage
           ) AS score
    FROM pooled c
    LEFT JOIN public.artists ar ON ar.id = c.artist_id
    CROSS JOIN q
)
SELECT s.id, s.name, s.artist_name, s.score
FROM scored s
WHERE s.score >= (SELECT max(score) FROM scored) - @libraryWeight
ORDER BY s.score DESC, length(s.name) ASC
LIMIT 200;";

    private const string TrackSearchMatch =
        "public.f_search_vector((COALESCE(t.name, ''::citext) || ' '::citext || COALESCE(t.artist_name, ''::citext))::text) " +
        "@@ public.f_search_query(@searchTerm)";

    private const string TrackSearchColumns =
        "t.id, COALESCE(t.name, ''::citext)::text AS name, COALESCE(t.artist_name, ''::citext)::text AS artist_name, " +
        "COALESCE(t.album_name, ''::citext)::text AS album_name, t.popularity, t.artist_id";

    private static readonly string[] TrackSearchStages =
    [
        BuildTrackSearchSql($@"(SELECT {TrackSearchColumns}
        FROM public.tracks t
        WHERE {TrackSearchMatch} AND t.popularity IS NOT NULL
        ORDER BY t.popularity DESC
        LIMIT 3000)
    UNION ALL
    (SELECT {TrackSearchColumns}
        FROM (SELECT t.id, t.name, t.artist_name, t.album_name, t.popularity, t.artist_id
              FROM public.tracks t
              WHERE {TrackSearchMatch}
                AND (t.popularity IS NOT NULL OR t.artist_id IS NOT NULL) AND t.popularity IS NULL
              LIMIT 3000) t
        JOIN public.artists ar ON ar.id = t.artist_id
        ORDER BY ar.popularity DESC NULLS LAST
        LIMIT 1000)"),
        BuildTrackSearchSql($@"SELECT {TrackSearchColumns}
        FROM public.tracks t
        WHERE to_tsvector('english', (COALESCE(t.name, ''::citext) || ' '::citext || COALESCE(t.artist_name, ''::citext)
              || ' '::citext || COALESCE(t.album_name, ''::citext))::text) @@ plainto_tsquery('english', @searchTerm)
        ORDER BY t.popularity DESC NULLS LAST
        LIMIT 3000")
    ];

    public static async Task<Track> SearchTrack(string searchTerm, int? userId, NpgsqlConnection connection)
    {
        const string librarySql = "SELECT ut.artist_name, ut.name " +
                                  "FROM public.user_tracks ut " +
                                  "JOIN unnest(CAST(@names AS citext[])) AS candidate(name) ON candidate.name = ut.name " +
                                  "WHERE ut.user_id = @userId AND ut.playcount >= 3";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var libraryWeight = userId.HasValue ? 3.0 : 0.0;

        foreach (var stage in TrackSearchStages)
        {
            var candidates = (await connection.QueryAsync<SearchCandidate>(stage, new { searchTerm, libraryWeight }))
                .ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            var best = await SearchCandidate.PickBest(candidates, userId, libraryWeight, librarySql, connection);

            return await connection.QueryFirstOrDefaultAsync<Track>("SELECT * FROM public.tracks WHERE id = @id",
                new { id = best.Id });
        }

        return null;
    }


    private static string TrackCoverLateral(string source) =>
        "SELECT ab.name AS album_name, COALESCE(ab.spotify_image_url, ab.lastfm_image_url) AS image_url " +
        "FROM public.tracks t " +
        "INNER JOIN public.albums ab ON ab.artist_name = t.artist_name AND ab.name = t.album_name " +
        $"WHERE t.artist_name = {source}.artist_name AND t.name = {source}.name " +
        "AND COALESCE(ab.spotify_image_url, ab.lastfm_image_url) IS NOT NULL " +
        "ORDER BY ab.popularity DESC NULLS LAST, t.id " +
        "LIMIT 1";

    public static async Task<List<EntityImageUrl>> GetImageUrlsForTracks(string[] artistNames, string[] trackNames,
        NpgsqlConnection connection)
    {
        var sql = "SELECT input.artist_name, input.name, cover.album_name, cover.image_url " +
                  "FROM unnest(@artistNames::citext[], @trackNames::citext[]) AS input(artist_name, name) " +
                  $"CROSS JOIN LATERAL ({TrackCoverLateral("input")}) cover";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<EntityImageUrl>(sql, new { artistNames, trackNames })).ToList();
    }
}
