using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Persistence.Repositories
{
    public class AlbumRepository
    {
        private readonly BotSettings _botSettings;

        public AlbumRepository(IOptions<BotSettings> botSettings)
        {
            this._botSettings = botSettings.Value;
        }

        public async Task<Album> GetAlbumForName(string artistName, string albumName, NpgsqlConnection connection = null)
        {
            const string getAlbumQuery = "SELECT * FROM public.albums " +
                                        "WHERE UPPER(artist_name) = UPPER(CAST(@artistName AS CITEXT)) AND " +
                                        "UPPER(name) = UPPER(CAST(@albumName AS CITEXT))";

            connection = StartConnection(connection);

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            return await connection.QueryFirstOrDefaultAsync<Album>(getAlbumQuery, new
            {
                artistName,
                albumName
            });
        }

        private NpgsqlConnection StartConnection(NpgsqlConnection connection = null)
        {
            if (connection == null)
            {
                connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
                connection.Open();
            }

            return connection;
        }
    }
}
