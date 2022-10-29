using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Persistence.Repositories;

public class AlbumRepository
{
    private readonly BotSettings _botSettings;

    public AlbumRepository(IOptions<BotSettings> botSettings)
    {
        this._botSettings = botSettings.Value;
    }

    public async Task<Album> GetAlbumForName(string artistName, string albumName, NpgsqlConnection connection)
    {
        const string getAlbumQuery = "SELECT * FROM public.albums " +
                                     "WHERE UPPER(artist_name) = UPPER(CAST(@artistName AS CITEXT)) AND " +
                                     "UPPER(name) = UPPER(CAST(@albumName AS CITEXT))";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return await connection.QueryFirstOrDefaultAsync<Album>(getAlbumQuery, new
        {
            artistName,
            albumName
        });
    }

    public async Task<IReadOnlyCollection<UserAlbum>> GetUserAlbums(int userId, NpgsqlConnection connection)
    {
        const string sql = "SELECT * FROM public.user_albums where user_id = @userId";
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserAlbum>(sql, new
        {
            userId
        })).ToList();
    }
}
