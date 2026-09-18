using System.Threading.Tasks;
using Dapper;
using FMBot.Persistence.Domain.Models;
using Npgsql;

namespace FMBot.Persistence.Repositories;

public static class CrownRepository
{
    public static async Task<UserCrown> GetCurrentCrownHolder(NpgsqlConnection connection, int guildId, string artistName)
    {
        const string sql = "SELECT * FROM public.user_crowns AS uc " +
                           "WHERE uc.guild_id = @guildId AND " +
                           "uc.active = true AND " +
                           "UPPER(uc.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                           "ORDER BY current_playcount desc";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return await connection.QueryFirstOrDefaultAsync<UserCrown>(sql, new
        {
            guildId,
            artistName
        });
    }
}
