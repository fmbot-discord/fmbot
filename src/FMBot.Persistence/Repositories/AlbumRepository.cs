using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Npgsql;
using PostgreSQLCopyHelper;
using Serilog;

namespace FMBot.Persistence.Repositories;

public static class AlbumRepository
{
    public static async Task<ulong> AddOrReplaceUserAlbumsInDatabase(IReadOnlyList<UserAlbum> albums, int userId,
        NpgsqlConnection connection)
    {
        Log.Information("Index: {userId} - Inserting {albumCount} top albums", userId, albums.Count);

        var copyHelper = new PostgreSQLCopyHelper<UserAlbum>("public", "user_albums")
            .MapText("name", x => x.Name)
            .MapText("artist_name", x => x.ArtistName)
            .MapInteger("user_id", x => x.UserId)
            .MapInteger("playcount", x => x.Playcount)
            .MapInteger("album_id", x => x.AlbumId);

        await using var deleteCurrentAlbums =
            new NpgsqlCommand($"DELETE FROM public.user_albums WHERE user_id = {userId};", connection);
        await deleteCurrentAlbums.ExecuteNonQueryAsync();

        return await copyHelper.SaveAllAsync(connection, albums);
    }

    public static async Task<string> GetAlbumBackgroundColor(int albumId, NpgsqlConnection connection)
    {
        const string sql = @"
            SELECT bg_color FROM album_images
            WHERE album_id = @albumId AND bg_color IS NOT NULL
            LIMIT 1";

        return await connection.QueryFirstOrDefaultAsync<string>(sql, new { albumId });
    }

    public static async Task<Album> GetAlbumForName(string artistName, string albumName, NpgsqlConnection connection)
    {
        const string getAlbumQuery = "SELECT * FROM public.albums " +
                                     "WHERE artist_name = CAST(@artistName AS CITEXT) AND " +
                                     "name = CAST(@albumName AS CITEXT)";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return await connection.QueryFirstOrDefaultAsync<Album>(getAlbumQuery, new
        {
            artistName,
            albumName
        });
    }

    public static Task AddOrUpdateAlbumGenres(int albumId, IEnumerable<string> genreNames,
        NpgsqlConnection connection)
    {
        return AddOrUpdateAlbumGenres(albumId, GenreSource.AppleMusic,
            genreNames.Select(g => (g, (int?)null)), connection);
    }

    public static async Task AddOrUpdateAlbumGenres(int albumId, GenreSource source,
        IEnumerable<(string Name, int? SourceGenreId)> genres, NpgsqlConnection connection)
    {
        const string deleteQuery = @"DELETE FROM public.album_genres WHERE album_id = @albumId AND source = @source";
        await connection.ExecuteAsync(deleteQuery, new { albumId, source = (int)source });

        const string insertQuery = @"INSERT INTO public.album_genres(album_id, name, source, source_genre_id) " +
                                   "VALUES (@albumId, @name, @source, @sourceGenreId) " +
                                   "ON CONFLICT (album_id, source, name) DO NOTHING";

        foreach (var genre in genres
                     .Where(g => !string.IsNullOrEmpty(g.Name) &&
                                 !string.Equals(g.Name, "Music", StringComparison.OrdinalIgnoreCase))
                     .GroupBy(g => g.Name))
        {
            await connection.ExecuteAsync(insertQuery, new
            {
                albumId,
                name = genre.Key,
                source = (int)source,
                sourceGenreId = genre.First().SourceGenreId
            });
        }
    }

    public static async Task<IReadOnlyCollection<UserAlbum>> GetUserAlbums(int userId, NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM public.user_albums where user_id = @userId";
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserAlbum>(sql, new
        {
            userId
        })).ToList();
    }

    public static async Task<int> GetAlbumPlayCountForUser(NpgsqlConnection connection, int albumId, int userId)
    {
        const string sql = "SELECT ua.playcount " +
                           "FROM user_albums AS ua " +
                           "WHERE ua.user_id = @userId AND ua.album_id = @albumId " +
                           "ORDER BY playcount DESC";

        return await connection.QueryFirstOrDefaultAsync<int>(sql, new
        {
            userId,
            albumId
        });
    }

    public static async Task<List<TopAlbum>> GetTopUserAlbums(int userId, int limit, NpgsqlConnection connection)
    {
        const string sql = "SELECT ua.name AS album_name, ua.artist_name, ua.playcount AS user_playcount, " +
                           "COALESCE(a.spotify_image_url, a.lastfm_image_url) AS album_cover_url " +
                           "FROM public.user_albums ua " +
                           "LEFT JOIN public.albums a ON a.id = ua.album_id " +
                           "WHERE ua.user_id = @userId ORDER BY ua.playcount DESC LIMIT @limit";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<TopAlbum>(sql, new { userId, limit })).ToList();
    }

    public static async Task<List<UserAlbum>> GetUserAlbumsForArtist(int userId, string artistName,
        NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM public.user_albums " +
                           "WHERE LOWER(artist_name) = LOWER(@artistName) AND user_id = @userId " +
                           "ORDER BY playcount DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<UserAlbum>(sql, new
        {
            userId,
            artistName
        })).ToList();
    }

    public static async Task<List<AlbumEnrichmentRow>> GetAlbumEnrichmentRows(string[] artistNames,
        string[] albumNames, NpgsqlConnection connection)
    {
        const string sql = "SELECT a.name AS album_name, a.artist_name, a.release_date, a.release_date_precision, a.type AS album_type " +
                           "FROM albums a " +
                           "INNER JOIN unnest(@artistNames::citext[], @albumNames::citext[]) AS q(artist_name, album_name) " +
                           "  ON a.artist_name = q.artist_name AND a.name = q.album_name " +
                           "WHERE a.release_date IS NOT NULL AND a.release_date <> '0000'";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<AlbumEnrichmentRow>(sql, new { artistNames, albumNames })).ToList();
    }

    public static async Task<List<TopAlbum>> GetUserAllTimeTopAlbumsByReleasePrefix(int userId, string prefix,
        int prefixLength, NpgsqlConnection connection)
    {
        const string sql = @"
            SELECT ua.name AS album_name,
                   ua.artist_name,
                   ua.playcount AS user_playcount,
                   COALESCE(a.spotify_image_url, a.lastfm_image_url) AS album_cover_url,
                   TO_DATE(
                     CASE a.release_date_precision
                       WHEN 'year' THEN a.release_date || '-01-01'
                       WHEN 'month' THEN a.release_date || '-01'
                       ELSE a.release_date
                     END, 'YYYY-MM-DD')::timestamp AS release_date,
                   a.release_date_precision,
                   a.type AS album_type
            FROM user_albums ua
            INNER JOIN albums a ON ua.album_id = a.id
            WHERE ua.user_id = @userId
              AND a.release_date IS NOT NULL
              AND a.release_date <> '0000'
              AND LEFT(a.release_date, @prefixLength) = @prefix
            ORDER BY ua.playcount DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var albums = (await connection.QueryAsync<TopAlbum>(sql, new { userId, prefix, prefixLength })).ToList();

        foreach (var album in albums)
        {
            album.ArtistUrl = LastfmUrlExtensions.GetArtistUrl(album.ArtistName);
            album.AlbumUrl = LastfmUrlExtensions.GetAlbumUrl(album.ArtistName, album.AlbumName);
        }

        return albums;
    }

    public static async Task<int> GetUserAlbumCount(int userId, NpgsqlConnection connection)
    {
        const string sql = "SELECT COUNT(*) FROM public.user_albums WHERE user_id = @userId";
        return await connection.QueryFirstOrDefaultAsync<int>(sql, new { userId });
    }

    public record UserAlbumSearchResult(string Name, string ArtistName, int Playcount, int Rank);

    public static async Task<IReadOnlyList<UserAlbumSearchResult>> SearchUserAlbums(int userId, string query,
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
    FROM public.user_albums
    WHERE user_id = @userId
)
SELECT name, artist_name, playcount, rank
FROM ranked
WHERE (artist_name || ' ' || name) ILIKE ALL(@patterns)
ORDER BY playcount DESC
LIMIT @limit;";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserAlbumSearchResult>(sql, new { userId, patterns, limit })).ToList();
    }

    public static async Task<IReadOnlyList<FriendEntitySearchResult>> SearchFriendAlbums(int[] userIds, string query,
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
FROM public.user_albums
WHERE user_id = ANY(@userIds)
  AND (artist_name || ' ' || name) ILIKE ALL(@patterns)
GROUP BY artist_name, name
ORDER BY listeners DESC, playcount DESC
LIMIT @limit;";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<FriendEntitySearchResult>(sql, new { userIds, patterns, limit })).ToList();
    }

    public static async Task<List<EntitySearchDetails>> GetAlbumSearchDetails(string[] artistNames, string[] albumNames,
        NpgsqlConnection connection)
    {
        const string sql = "SELECT ab.artist_name, ab.name, ab.type AS album_type, ab.release_date, " +
                           "COALESCE(ab.spotify_image_url, ab.lastfm_image_url) AS image_url " +
                           "FROM unnest(@artistNames::citext[], @albumNames::citext[]) AS input(artist_name, name) " +
                           "INNER JOIN public.albums ab ON ab.artist_name = input.artist_name AND ab.name = input.name";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<EntitySearchDetails>(sql, new { artistNames, albumNames })).ToList();
    }

    private static string BuildAlbumSearchSql(string candidates, string result, string prefixBonus = "") => $@"
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
           count(*) OVER (PARTITION BY n.norm_artist, n.norm_core) AS variants
    FROM normalised n
), scored AS (
    SELECT c.id, c.name, c.artist_name, c.norm_artist, c.norm_core, COALESCE(c.group_popularity, 0) AS popularity,
           (  0.9 * (CASE WHEN c.norm_artist || ' ' || c.norm_name = q.norm
                            OR c.norm_name || ' ' || c.norm_artist = q.norm THEN 1 ELSE 0 END)
            + 1.25 * (CASE WHEN c.norm_core = q.core THEN 1 ELSE 0 END)
            + 0.2 * (CASE WHEN c.norm_name = q.norm THEN 1 ELSE 0 END)
            + 0.55 * similarity(c.norm_core, q.core)
            + 0.55 * (CASE WHEN c.norm_core LIKE q.core || '%' THEN 1 ELSE 0 END)
            + 1.65 * word_similarity(@searchTerm, c.name || ' ' || c.artist_name)
            + 2.4 * COALESCE(c.group_popularity, 0) / 100.0
            + 1.15 * COALESCE(ar.popularity, 0) / 100.0
            + 1.0 * COALESCE(c.popularity, 0) / 100.0
            + 1.1 * ln(1 + c.variants) / ln(51)
            + (CASE c.type WHEN 'album' THEN 1.5 WHEN 'compilation' THEN 1.05 WHEN 'single' THEN 1.0 ELSE 0 END)
{prefixBonus}           ) AS score
    FROM pooled c
    LEFT JOIN public.artists ar ON ar.id = c.artist_id
    CROSS JOIN q
)
{result}";

    private const string AlbumSearchColumns =
        "t.id, COALESCE(t.name, ''::citext)::text AS name, COALESCE(t.artist_name, ''::citext)::text AS artist_name, " +
        "t.type, t.popularity, t.artist_id";

    private static string AlbumCandidates(string match) => $@"(SELECT {AlbumSearchColumns}
        FROM public.albums t
        WHERE {match} AND t.popularity IS NOT NULL
        ORDER BY t.popularity DESC
        LIMIT 3000)
    UNION ALL
    (SELECT {AlbumSearchColumns}
        FROM (SELECT t.id, t.name, t.artist_name, t.type, t.popularity, t.artist_id
              FROM public.albums t
              WHERE {match}
                AND (t.popularity IS NOT NULL OR t.artist_id IS NOT NULL) AND t.popularity IS NULL
              LIMIT 3000) t
        JOIN public.artists ar ON ar.id = t.artist_id
        ORDER BY ar.popularity DESC NULLS LAST
        LIMIT 1000)";

    private static readonly string[] AlbumSearchStages =
    [
        BuildAlbumSearchSql(AlbumCandidates(SearchSql.WholeWordMatch), SearchSql.BestMatches),
        BuildAlbumSearchSql($@"SELECT {AlbumSearchColumns}
        FROM public.albums t
        WHERE to_tsvector('english', (COALESCE(t.name, ''::citext) || ' '::citext || COALESCE(t.artist_name, ''::citext))::text)
              @@ plainto_tsquery('english', @searchTerm)
        ORDER BY t.popularity DESC NULLS LAST
        LIMIT 3000", SearchSql.BestMatches)
    ];

    private static readonly string AlbumAutocompleteSql = BuildAlbumSearchSql(AlbumCandidates(SearchSql.PrefixMatch),
        SearchSql.BestMatchPerTitle, prefixBonus: @"            + 0.5 * (CASE WHEN c.norm_name LIKE q.norm || '%' THEN 1 ELSE 0 END)
            + 1.0 * (CASE WHEN c.norm_artist = q.norm THEN 1 ELSE 0 END)
            + 1.5 * (CASE WHEN c.norm_artist || ' ' || c.norm_name LIKE q.norm || '%'
                            OR c.norm_name || ' ' || c.norm_artist LIKE q.norm || '%' THEN 1 ELSE 0 END)
");

    public static async Task<Album> SearchAlbum(string searchTerm, int? userId, NpgsqlConnection connection)
    {
        const string librarySql = "SELECT ua.artist_name, ua.name " +
                                  "FROM public.user_albums ua " +
                                  "JOIN unnest(CAST(@names AS citext[])) AS candidate(name) ON candidate.name = ua.name " +
                                  "WHERE ua.user_id = @userId AND ua.playcount >= 3";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var libraryWeight = userId.HasValue ? 2.0 : 0.0;

        foreach (var stage in AlbumSearchStages)
        {
            var candidates = (await connection.QueryAsync<SearchCandidate>(stage, new { searchTerm, libraryWeight }))
                .ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            var best = await SearchCandidate.PickBest(candidates, userId, libraryWeight, librarySql, connection);

            return await connection.QueryFirstOrDefaultAsync<Album>("SELECT * FROM public.albums WHERE id = @id",
                new { id = best.Id });
        }

        return null;
    }

    public static async Task<List<AutocompleteSearchResult>> AutocompleteAlbums(string searchTerm, NpgsqlConnection connection)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<AutocompleteSearchResult>(AlbumAutocompleteSql, new { searchTerm })).ToList();
    }

    public static async Task GetAlbumCovers(List<TopAlbum> topAlbums,
        NpgsqlConnection connection)
    {
        const string getAlbumQuery = @"
        SELECT
            ab.name,
            ab.artist_name,
            COALESCE(
                ab.spotify_image_url,
                REPLACE(REPLACE(ai.url, '{w}', ai.width::text), '{h}', ai.height::text)
            ) as stored_cover_url,
            ab.lastfm_image_url,
            dr.cover_url AS discogs_cover_url,
            ab.spotify_id,
            ab.release_date,
            ab.release_date_precision,
            ab.mbid
        FROM public.albums ab
        LEFT JOIN LATERAL (
            SELECT url, width, height
            FROM album_images
            WHERE ab.spotify_image_url IS NULL
              AND album_id = ab.id
              AND image_source = 3
              AND width IS NOT NULL
              AND height IS NOT NULL
            LIMIT 1
        ) ai ON TRUE
        LEFT JOIN LATERAL (
            SELECT cover_url
            FROM discogs_releases
            WHERE ab.spotify_image_url IS NULL
              AND ab.lastfm_image_url IS NULL
              AND ai.url IS NULL
              AND album_id = ab.id
              AND cover_url LIKE 'https://i.discogs.com/%'
            ORDER BY year NULLS LAST, discogs_id
            LIMIT 1
        ) dr ON TRUE
        WHERE (ab.artist_name, ab.name) IN (
            SELECT CAST(unnest(@artistNames) AS CITEXT),
                   CAST(unnest(@albumNames) AS CITEXT)
        )";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var albumData = await connection.QueryAsync<AlbumData>(getAlbumQuery, new
        {
            albumNames = topAlbums.Select(a => a.AlbumName).ToArray(),
            artistNames = topAlbums.Select(a => a.ArtistName).ToArray()
        });

        var albumLookup = albumData
            .Where(w => w.StoredCoverUrl != null || w.LastfmImageUrl != null || w.DiscogsCoverUrl != null)
            .GroupBy(a => (a.Name.ToLower(), a.ArtistName.ToLower()))
            .ToDictionary(
                g => g.Key,
                g => g.First()
            );

        foreach (var album in topAlbums)
        {
            var key = (album.AlbumName.ToLower(), album.ArtistName.ToLower());
            if (albumLookup.TryGetValue(key, out var dbAlbum))
            {
                album.AlbumCoverUrl = dbAlbum.StoredCoverUrl ?? album.AlbumCoverUrl ?? dbAlbum.LastfmImageUrl ??
                                      dbAlbum.DiscogsCoverUrl;

                var releaseDateString = dbAlbum.ReleaseDatePrecision switch
                {
                    "year" => $"{dbAlbum.ReleaseDate}-1-1",
                    "month" => $"{dbAlbum.ReleaseDate}-1",
                    "day" => dbAlbum.ReleaseDate,
                    _ => null
                };

                if (releaseDateString != null &&
                    DateTime.TryParse(releaseDateString, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var releaseDate))
                {
                    album.ReleaseDate = releaseDate;
                    album.ReleaseDatePrecision = dbAlbum.ReleaseDatePrecision;
                }
            }
        }
    }

    private class AlbumData
    {
        public string Name { get; set; }
        public string ArtistName { get; set; }
        public string StoredCoverUrl { get; set; }
        public string LastfmImageUrl { get; set; }
        public string DiscogsCoverUrl { get; set; }
        public string SpotifyId { get; set; }
        public string ReleaseDate { get; set; }
        public string ReleaseDatePrecision { get; set; }
    }

    public static async Task<Dictionary<(string ArtistName, string AlbumName), int?>> GetAlbumIdsForNames(
        List<(string ArtistName, string AlbumName)> albums, NpgsqlConnection connection)
    {
        const string query = @"
        SELECT a.name, a.artist_name, a.id
        FROM public.albums a
        WHERE (a.name, a.artist_name) IN (
            SELECT CAST(unnest(@albumNames) AS CITEXT),
                   CAST(unnest(@artistNames) AS CITEXT)
        )";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var results = await connection.QueryAsync<(string Name, string ArtistName, int Id)>(query, new
        {
            albumNames = albums.Select(a => a.AlbumName).ToArray(),
            artistNames = albums.Select(a => a.ArtistName).ToArray()
        });

        return results
            .GroupBy(r => (r.ArtistName.ToLower(), r.Name.ToLower()))
            .ToDictionary(
                g => g.Key,
                g => (int?)g.First().Id);
    }

    public static async Task<List<AlbumPopularity>> GetAlbumsPopularity(List<TopAlbum> topAlbums,
        NpgsqlConnection connection)
    {
        const string getAlbumsQuery = @"
        SELECT a.name, a.artist_name, a.popularity
        FROM public.albums a
        JOIN (
            SELECT CAST(unnest(@artistNames) AS CITEXT) AS artist_name,
                   CAST(unnest(@albumNames) AS CITEXT) AS name
        ) l ON a.artist_name = l.artist_name AND a.name = l.name
        WHERE a.popularity IS NOT NULL";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var albums = await connection.QueryAsync<AlbumPopularity>(getAlbumsQuery, new
        {
            artistNames = topAlbums.Select(a => a.ArtistName).ToArray(),
            albumNames = topAlbums.Select(a => a.AlbumName).ToArray()
        });

        return albums.ToList();
    }


    public static async Task<List<EntityImageUrl>> GetImageUrlsForAlbumIds(int[] albumIds, NpgsqlConnection connection)
    {
        const string sql = "SELECT ab.id, ab.artist_name, ab.name, COALESCE(ab.spotify_image_url, ab.lastfm_image_url) AS image_url " +
                           "FROM public.albums ab WHERE ab.id = ANY(@albumIds) " +
                           "AND COALESCE(ab.spotify_image_url, ab.lastfm_image_url) IS NOT NULL";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<EntityImageUrl>(sql, new { albumIds })).ToList();
    }
}
