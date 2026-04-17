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

    public static async Task<Album> SearchAlbum(string searchTerm, NpgsqlConnection connection)
    {
        const string sql = @"
SELECT
    *
FROM
    public.albums
WHERE
    to_tsvector('english', coalesce(name, '') || ' ' || coalesce(artist_name, '')) @@ plainto_tsquery('english', @searchTerm)
ORDER BY
    -- 1. TIERING: Prioritize albums with complete data
    (CASE
        WHEN spotify_id IS NOT NULL AND apple_music_id IS NOT NULL AND popularity IS NOT NULL THEN 0 -- Tier 1: Highest quality
        WHEN spotify_id IS NOT NULL OR apple_music_id IS NOT NULL THEN 1 -- Tier 2: Good quality
        ELSE 2 -- Tier 3: Everything else
    END) ASC,

    -- 2. SCORING: Apply a rebalanced weighted score within each tier
    (
        -- Holistic similarity on all text fields has the highest weight.
        similarity(coalesce(name, '') || ' ' || coalesce(artist_name, ''), @searchTerm) * 1.5 +

        -- Popularity bonus
        coalesce(log(popularity + 1), 0) * 0.7 +

        -- Direct album name similarity
        similarity(name, @searchTerm) * 0.5
    ) DESC,

    -- 3. TIE-BREAKERS: Final sorting for records with identical scores
    length(name) ASC,
    release_date DESC NULLS LAST
LIMIT 1;";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return await connection.QueryFirstOrDefaultAsync<Album>(sql, new { searchTerm });
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
