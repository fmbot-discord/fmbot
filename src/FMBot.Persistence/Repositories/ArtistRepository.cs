using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Persistence.Repositories;

public class ArtistRepository
{
    private readonly BotSettings _botSettings;

    public ArtistRepository(IOptions<BotSettings> botSettings)
    {
        this._botSettings = botSettings.Value;
    }

    public async Task<Artist> GetArtistForName(string artistName, NpgsqlConnection connection, bool includeGenres = false)
    {
        const string getArtistQuery = "SELECT * FROM public.artists " +
                                      "WHERE UPPER(name) = UPPER(CAST(@artistName AS CITEXT))";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var artist = await connection.QueryFirstOrDefaultAsync<Artist>(getArtistQuery, new
        {
            artistName
        });

        if (includeGenres && artist != null)
        {
            artist.ArtistGenres = await GetArtistGenres(artist.Id, connection);
        }

        return artist;
    }

    private async Task<ICollection<ArtistGenre>> GetArtistGenres(int artistId, NpgsqlConnection connection)
    {
        const string getArtistGenreQuery = "SELECT * FROM public.artist_genres " +
                                           "WHERE artist_id = @artistId";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<ArtistGenre>(getArtistGenreQuery, new
        {
            artistId
        })).ToList();
    }

    public async Task AddOrUpdateArtistAlias(int artistId, string artistNameBeforeCorrect, NpgsqlConnection connection)
    {
        const string deleteQuery = @"DELETE FROM public.artist_aliases WHERE artist_id = @artistId AND alias = @alias";
        await connection.ExecuteAsync(deleteQuery, new
        {
            artistId,
            alias = artistNameBeforeCorrect
        });

        const string insertQuery = @"INSERT INTO public.artist_aliases(artist_id, alias, corrects_in_scrobbles) " +
                                   "VALUES (@artistId, @alias, @correctsInScrobbles)";

        await connection.ExecuteAsync(insertQuery, new
        {
            artistId,
            alias = artistNameBeforeCorrect,
            correctsInScrobbles = true
        });
    }

    public async Task AddOrUpdateArtistGenres(int artistId, IEnumerable<string> genreNames, NpgsqlConnection connection)
    {
        const string deleteQuery = @"DELETE FROM public.artist_genres WHERE artist_id = @artistId";
        await connection.ExecuteAsync(deleteQuery, new { artistId });

        const string insertQuery = @"INSERT INTO public.artist_genres(artist_id, name) " +
                                   "VALUES (@artistId, @name)";

        foreach (var genreName in genreNames)
        {
            await connection.ExecuteAsync(insertQuery, new
            {
                artistId,
                name = genreName
            });
        }
    }

    public async Task<IReadOnlyCollection<UserArtist>> GetUserArtists(int userId, NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM public.user_artists where user_id = @userId";
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserArtist>(sql, new
        {
            userId
        })).ToList();
    }
}
