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

public class ArtistRepository
{
    private readonly BotSettings _botSettings;

    public ArtistRepository(IOptions<BotSettings> botSettings)
    {
        this._botSettings = botSettings.Value;
    }

    public static async Task<ulong> AddOrReplaceUserArtistsInDatabase(IReadOnlyList<UserArtist> artists, int userId,
        NpgsqlConnection connection)
    {
        Log.Information("Index: {userId} - Inserting {artistCount} top artists", userId, artists.Count);

        var copyHelper = new PostgreSQLCopyHelper<UserArtist>("public", "user_artists")
            .MapText("name", x => x.Name)
            .MapInteger("user_id", x => x.UserId)
            .MapInteger("playcount", x => x.Playcount)
            .MapInteger("artist_id", x => x.ArtistId);

        await using var deleteCurrentArtists =
            new NpgsqlCommand($"DELETE FROM public.user_artists WHERE user_id = {userId};", connection);
        await deleteCurrentArtists.ExecuteNonQueryAsync();

        return await copyHelper.SaveAllAsync(connection, artists);
    }

    public static async Task<Artist> GetArtistForName(string artistName, NpgsqlConnection connection,
        bool includeGenres = false, bool includeLinks = false, bool includeImages = false)
    {
        const string getArtistQuery = "SELECT * FROM public.artists " +
                                      "WHERE name = CAST(@artistName AS CITEXT)";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var artist = await connection.QueryFirstOrDefaultAsync<Artist>(getArtistQuery, new
        {
            artistName
        });

        if (includeGenres && artist != null)
        {
            artist.ArtistGenres = await GetArtistGenres(artist.Id, connection);
        }

        if (includeLinks && artist != null)
        {
            artist.ArtistLinks = await GetArtistLinks(artist.Id, connection);
        }

        if (includeImages && artist != null)
        {
            artist.Images = await GetArtistImages(artist.Id, connection);
        }

        return artist;
    }

    private static async Task<ICollection<ArtistGenre>> GetArtistGenres(int artistId, NpgsqlConnection connection)
    {
        const string getArtistGenreQuery = "SELECT DISTINCT ON (name) id, artist_id, name FROM public.artist_genres " +
                                           "WHERE artist_id = @artistId " +
                                           "ORDER BY name";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<ArtistGenre>(getArtistGenreQuery, new
        {
            artistId
        })).ToList();
    }

    private static async Task<ICollection<ArtistLink>> GetArtistLinks(int artistId, NpgsqlConnection connection)
    {
        const string getArtistLinkQuery = "SELECT * FROM public.artist_links " +
                                          "WHERE artist_id = @artistId";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<ArtistLink>(getArtistLinkQuery, new
        {
            artistId
        })).ToList();
    }

    public static async Task<ICollection<ArtistImage>> GetArtistImages(int artistId, NpgsqlConnection connection)
    {
        const string getArtistLinkQuery = "SELECT * FROM public.artist_images " +
                                          "WHERE artist_id = @artistId";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<ArtistImage>(getArtistLinkQuery, new
        {
            artistId
        })).ToList();
    }

    public static async Task AddOrUpdateArtistAlias(int artistId, string artistNameBeforeCorrect,
        NpgsqlConnection connection)
    {
        if (string.Equals(artistNameBeforeCorrect, "rnd", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(artistNameBeforeCorrect, "random", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(artistNameBeforeCorrect, "featured", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        const string selectQuery =
            @"SELECT * FROM public.artist_aliases WHERE artist_id = @artistId AND alias = @alias LIMIT 1";
        var result = await connection.QueryFirstOrDefaultAsync<ArtistAlias>(selectQuery, new
        {
            artistId,
            alias = artistNameBeforeCorrect.ToLower()
        });

        if (result == null)
        {
            const string insertQuery = @"INSERT INTO public.artist_aliases(artist_id, alias, corrects_in_scrobbles) " +
                                       "VALUES (@artistId, @alias, @correctsInScrobbles)";

            await connection.ExecuteAsync(insertQuery, new
            {
                artistId,
                alias = artistNameBeforeCorrect.ToLower(),
                correctsInScrobbles = true
            });
        }
    }

    public static async Task AddOrUpdateArtistGenres(int artistId, IEnumerable<string> genreNames,
        NpgsqlConnection connection)
    {
        const string deleteQuery = @"DELETE FROM public.artist_genres WHERE artist_id = @artistId";
        await connection.ExecuteAsync(deleteQuery, new { artistId });

        const string insertQuery = @"INSERT INTO public.artist_genres(artist_id, name) " +
                                   "VALUES (@artistId, @name)";

        foreach (var genreName in genreNames.GroupBy(g => g))
        {
            await connection.ExecuteAsync(insertQuery, new
            {
                artistId,
                name = genreName.Key
            });
        }
    }

    public static async Task AddOrUpdateArtistLinks(int artistId, IEnumerable<ArtistLink> links,
        NpgsqlConnection connection)
    {
        try
        {
            const string deleteQuery =
                @"DELETE FROM public.artist_links WHERE artist_id = @artistId AND manually_added = false";
            await connection.ExecuteAsync(deleteQuery, new { artistId });

            const string insertQuery =
                @"INSERT INTO public.artist_links(artist_id, url, username, type, manually_added) " +
                "VALUES (@artistId, @url, @username, @type, @manuallyAdded)";

            foreach (var link in links)
            {
                await connection.ExecuteAsync(insertQuery, new
                {
                    artistId = link.ArtistId,
                    url = link.Url,
                    username = link.Username,
                    type = link.Type,
                    manuallyAdded = link.ManuallyAdded
                });
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Error updating artist links for artist {ArtistId}", artistId);
            throw;
        }
    }

    public static async Task<IReadOnlyCollection<UserArtist>> GetUserArtists(int userId, NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM public.user_artists where user_id = @userId";
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserArtist>(sql, new
        {
            userId
        })).ToList();
    }

    public static async Task<int> GetUserArtistCount(int userId, NpgsqlConnection connection)
    {
        const string sql = "SELECT COUNT(*) FROM public.user_artists WHERE user_id = @userId";
        return await connection.QueryFirstOrDefaultAsync<int>(sql, new { userId });
    }

    public record UserArtistSearchResult(string Name, int Playcount, int Rank);

    public static async Task<IReadOnlyList<UserArtistSearchResult>> SearchUserArtists(int userId, string query,
        NpgsqlConnection connection)
    {
        var patterns = UserLibrarySearch.BuildPatterns(query);
        if (patterns.Length == 0)
        {
            return [];
        }

        const string sql = @"
WITH ranked AS (
    SELECT name, playcount,
           CAST(ROW_NUMBER() OVER (ORDER BY playcount DESC) AS int) AS rank
    FROM public.user_artists
    WHERE user_id = @userId
)
SELECT name, playcount, rank
FROM ranked
WHERE name ILIKE ALL(@patterns)
ORDER BY playcount DESC;";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserArtistSearchResult>(sql, new { userId, patterns })).ToList();
    }


    public static async Task<int> GetArtistPlayCountForUser(NpgsqlConnection connection, string artistName, int userId)
    {
        const string sql = "SELECT ua.playcount " +
                           "FROM user_artists AS ua " +
                           "WHERE ua.user_id = @userId AND UPPER(ua.name) = UPPER(CAST(@artistName AS CITEXT)) " +
                           "ORDER BY playcount DESC";

        return await connection.QueryFirstOrDefaultAsync<int>(sql, new
        {
            userId,
            artistName
        });
    }

    public static async Task<List<ArtistPopularity>> GetArtistsPopularity(List<string> artistNames,
        NpgsqlConnection connection)
    {
        const string getArtistsQuery = @"
        WITH artist_list(name) AS (
            SELECT unnest(@artistNames)::citext
        )
        SELECT a.name, a.popularity
        FROM public.artists a
        JOIN artist_list l ON a.name = l.name
        WHERE a.popularity IS NOT NULL
        ORDER BY a.name";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var artists = await connection.QueryAsync<ArtistPopularity>(getArtistsQuery, new
        {
            artistNames = artistNames.ToArray()
        });

        return artists.ToList();
    }

    public static async Task<Dictionary<string, int?>> GetArtistIdsForNames(List<string> artistNames,
        NpgsqlConnection connection)
    {
        const string query = @"
        WITH artist_list(name) AS (
            SELECT DISTINCT UPPER(unnest(@artistNames)::citext)
        )
        SELECT DISTINCT ON (UPPER(a.name)) a.name, a.id
        FROM public.artists a
        JOIN artist_list l ON UPPER(a.name) = l.name";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var results = await connection.QueryAsync<(string Name, int Id)>(query, new
        {
            artistNames = artistNames.ToArray()
        });

        return results.ToDictionary(r => r.Name, r => (int?)r.Id, StringComparer.OrdinalIgnoreCase);
    }
}
