using System.Threading.Tasks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Npgsql;
using Serilog;
using Dapper;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Persistence.Repositories;

public class UserRepository
{
    public static async Task<ImportUser> GetImportUserForLastFmUserName(string lastFmUserName, NpgsqlConnection connection, bool getLastImportPlayDate = false)
    {
        const string getUserQuery = "SELECT user_id, discord_user_id, user_name_last_fm, data_source " +
                                    "FROM users " +
                                    "WHERE UPPER(user_name_last_fm) = UPPER(@lastFmUserName) " +
                                    "AND last_used is not null " +
                                    "AND data_source != 1 " +
                                    "ORDER BY last_used DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var user = await connection.QueryFirstOrDefaultAsync<ImportUser>(getUserQuery, new
        {
            lastFmUserName
        });

        if (user != null && getLastImportPlayDate)
        {
            const string getLastImportedPlayDateQuery = "SELECT time_played FROM user_plays WHERE play_source != 0 AND user_id = @userId ORDER BY time_played DESC LIMIT 1";

            user.LastImportPlay = await connection.QueryFirstOrDefaultAsync<DateTime?>(getLastImportedPlayDateQuery, new
            {
                user.UserId
            });
        }

        return user;
    }

    public static async Task<ImportUser> GetImportUserForUserId(int userId, NpgsqlConnection connection, bool getLastImportPlayDate = false)
    {
        const string getUserQuery = "SELECT user_id, discord_user_id, user_name_last_fm, data_source, " +
                                    "(SELECT time_played FROM user_plays WHERE play_source != 0 AND user_id = @userId ORDER BY time_played DESC LIMIT 1) AS last_import_play " +
                                    "FROM users " +
                                    "WHERE user_id = @userId " +
                                    "AND last_used is not null " +
                                    "AND data_source != 1 " +
                                    "ORDER BY last_used DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var user = await connection.QueryFirstOrDefaultAsync<ImportUser>(getUserQuery, new
        {
            userId
        });

        return user;
    }

    public static async Task SetUserIndexTime(int userId, NpgsqlConnection connection, IEnumerable<UserPlay> plays)
    {
        Log.Information($"Setting user index time for user {userId}");
        var now = DateTime.UtcNow;

        var lastScrobble = plays?.MaxBy(o => o.TimePlayed)?.TimePlayed;

        if (lastScrobble.HasValue)
        {
            await using var setIndexTime = new NpgsqlCommand($"UPDATE public.users SET last_indexed='{now:u}', last_updated='{now:u}', last_scrobble_update = '{lastScrobble:u}' WHERE user_id = {userId};", connection);
            await setIndexTime.ExecuteNonQueryAsync();
        }
        else
        {
            await using var setIndexTime = new NpgsqlCommand($"UPDATE public.users SET last_indexed='{now:u}', last_updated='{now:u}' WHERE user_id = {userId};", connection);
            await setIndexTime.ExecuteNonQueryAsync();
        }
    }

    public static async Task<DateTime> SetUserPlayStats(User user, NpgsqlConnection connection, DataSourceUser dataSourceUser)
    {
        Log.Information($"Import: Setting user stats for {user.UserId}");

        await using var setIndexTime = new NpgsqlCommand($"UPDATE public.users SET registered_last_fm='{dataSourceUser.Registered:u}', lastfm_pro = '{dataSourceUser.Subscriber}', total_playcount = {dataSourceUser.Playcount} " +
                                                         $"WHERE user_id = {user.UserId};", connection);

        await setIndexTime.ExecuteNonQueryAsync().ConfigureAwait(false);

        return dataSourceUser.Registered;
    }
}
