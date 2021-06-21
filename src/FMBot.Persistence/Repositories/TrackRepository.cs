using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Persistence.Repositories
{
    public class TrackRepository
    {
        private readonly BotSettings _botSettings;

        public TrackRepository(IOptions<BotSettings> botSettings)
        {
            this._botSettings = botSettings.Value;
        }

        public async Task<Track> GetTrackForName(string artistName, string trackName, NpgsqlConnection connection = null)
        {
            const string getTrackQuery = "SELECT * FROM public.tracks " +
                                        "WHERE UPPER(artist_name) = UPPER(CAST(@artistName AS CITEXT)) AND " +
                                        "UPPER(name) = UPPER(CAST(@trackName AS CITEXT))";

            connection = StartConnection(connection);

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            return await connection.QueryFirstOrDefaultAsync<Track>(getTrackQuery, new
            {
                artistName,
                trackName
            });
        }

        public async Task<ICollection<Track>> GetAlbumTracks(int albumId, NpgsqlConnection connection = null)
        {
            const string getTrackQuery = "SELECT * FROM public.tracks " +
                                        "WHERE album_id = @albumId ";

            connection = StartConnection(connection);

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            return (await connection.QueryAsync<Track>(getTrackQuery, new
            {
                albumId
            })).ToList();
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
