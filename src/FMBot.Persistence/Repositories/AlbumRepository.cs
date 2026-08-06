using System;
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

public class AlbumRepository
{
    private readonly BotSettings _botSettings;

    public AlbumRepository(IOptions<BotSettings> botSettings)
    {
        this._botSettings = botSettings.Value;
    }

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

    public static async Task AddOrUpdateAlbumGenres(int albumId, IEnumerable<string> genreNames,
        NpgsqlConnection connection)
    {
        const string deleteQuery = @"DELETE FROM public.album_genres WHERE album_id = @albumId";
        await connection.ExecuteAsync(deleteQuery, new { albumId });

        const string insertQuery = @"INSERT INTO public.album_genres(album_id, name) " +
                                   "VALUES (@albumId, @name) " +
                                   "ON CONFLICT (album_id, name) DO NOTHING";

        foreach (var genreName in genreNames
                     .Where(g => !string.Equals(g, "Music", StringComparison.OrdinalIgnoreCase))
                     .GroupBy(g => g))
        {
            await connection.ExecuteAsync(insertQuery, new
            {
                albumId,
                name = genreName.Key
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

    public static async Task<int> GetUserAlbumCount(int userId, NpgsqlConnection connection)
    {
        const string sql = "SELECT COUNT(*) FROM public.user_albums WHERE user_id = @userId";
        return await connection.QueryFirstOrDefaultAsync<int>(sql, new { userId });
    }

    public record UserAlbumSearchResult(string Name, string ArtistName, int Playcount, int Rank);

    public static async Task<IReadOnlyList<UserAlbumSearchResult>> SearchUserAlbums(int userId, string query,
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
    FROM public.user_albums
    WHERE user_id = @userId
)
SELECT name, artist_name, playcount, rank
FROM ranked
WHERE (artist_name || ' ' || name) ILIKE ALL(@patterns)
ORDER BY playcount DESC;";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserAlbumSearchResult>(sql, new { userId, patterns })).ToList();
    }

    private static string BuildAlbumSearchSql(string candidateFilter) => $@"
SELECT s.*
FROM public.albums s
WHERE s.id = (
    WITH candidates AS MATERIALIZED (
        SELECT t.id,
               COALESCE(t.name, ''::citext)::text AS name,
               COALESCE(t.artist_name, ''::citext)::text AS artist_name,
               t.popularity,
               t.artist_id
        FROM public.albums t
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
            + 0.5 * word_similarity(@searchTerm, c.name || ' ' || c.artist_name)
            + 0.4 * word_similarity(c.artist_name, @searchTerm)
            + 0.3 * (CASE WHEN c.norm_name LIKE '%' || q.norm || '%' THEN 1 ELSE 0 END)
            + 2.5 * (ln(COALESCE(c.group_popularity, 0) + 1) / 4.6151)
            + 1.5 * (ln(COALESCE(ar.popularity, 0) + 1) / 4.6151)
        ) DESC NULLS LAST, length(c.name) ASC
    LIMIT 1
);";

    private const string AlbumSearchMatch =
        "public.f_search_vector((COALESCE(t.name, ''::citext) || ' '::citext || COALESCE(t.artist_name, ''::citext))::text) " +
        "@@ public.f_search_query(@searchTerm)";

    private static readonly string[] AlbumSearchStages =
    [
        BuildAlbumSearchSql($"{AlbumSearchMatch} AND t.popularity IS NOT NULL"),
        BuildAlbumSearchSql($"{AlbumSearchMatch} AND (t.popularity IS NOT NULL OR t.artist_id IS NOT NULL)"),
        BuildAlbumSearchSql(
            "to_tsvector('english', (COALESCE(t.name, ''::citext) || ' '::citext || COALESCE(t.artist_name, ''::citext))::text) " +
            "@@ plainto_tsquery('english', @searchTerm)")
    ];

    public static async Task<Album> SearchAlbum(string searchTerm, NpgsqlConnection connection)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        foreach (var stage in AlbumSearchStages)
        {
            var album = await connection.QueryFirstOrDefaultAsync<Album>(stage, new { searchTerm });
            if (album != null)
            {
                return album;
            }
        }

        return null;
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
                REPLACE(REPLACE(ai.url, '{w}', ai.width::text), '{h}', ai.height::text),
                ab.lastfm_image_url
            ) as album_cover_url,
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
        WHERE (ab.artist_name, ab.name) IN (
            SELECT CAST(unnest(@artistNames) AS CITEXT),
                   CAST(unnest(@albumNames) AS CITEXT)
        ) AND ab.release_date != '0000'";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var albumData = await connection.QueryAsync<AlbumData>(getAlbumQuery, new
        {
            albumNames = topAlbums.Select(a => a.AlbumName).ToArray(),
            artistNames = topAlbums.Select(a => a.ArtistName).ToArray()
        });

        var albumLookup = albumData
            .Where(w => w.AlbumCoverUrl != null)
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
                album.AlbumCoverUrl = dbAlbum.AlbumCoverUrl;
                album.ReleaseDatePrecision = dbAlbum.ReleaseDatePrecision;

                album.ReleaseDate = dbAlbum.ReleaseDatePrecision switch
                {
                    "year" => DateTime.Parse($"{dbAlbum.ReleaseDate}-1-1"),
                    "month" => DateTime.Parse($"{dbAlbum.ReleaseDate}-1"),
                    "day" => DateTime.Parse(dbAlbum.ReleaseDate),
                    _ => null
                };
            }
        }
    }

    private class AlbumData
    {
        public string Name { get; set; }
        public string ArtistName { get; set; }
        public string AlbumCoverUrl { get; set; }
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
        WHERE (UPPER(a.name), UPPER(a.artist_name)) IN (
            SELECT UPPER(CAST(unnest(@albumNames) AS CITEXT)),
                   UPPER(CAST(unnest(@artistNames) AS CITEXT))
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
        WHERE (UPPER(a.artist_name), UPPER(a.name)) IN (
            SELECT UPPER(CAST(unnest(@artistNames) AS CITEXT)),
                   UPPER(CAST(unnest(@albumNames) AS CITEXT))
        ) AND a.popularity IS NOT NULL";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var albums = await connection.QueryAsync<AlbumPopularity>(getAlbumsQuery, new
        {
            artistNames = topAlbums.Select(a => a.ArtistName).ToArray(),
            albumNames = topAlbums.Select(a => a.AlbumName).ToArray()
        });

        return albums.ToList();
    }
}
