using System.Threading.Tasks;
using System;
using Npgsql;
using Serilog;
using Dapper;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Persistence.Repositories;

public class UserRepository
{
    public static async Task<User> GetImportUserForLastFmUserName(string lastFmUserName, NpgsqlConnection connection)
    {   
        const string getUserQuery = "SELECT user_id, discord_user_id, user_type, user_name_last_fm, fm_embed_type, last_indexed, last_updated, last_scrobble_update, session_key_last_fm, blocked, last_used, total_playcount, privacy_level, rym_enabled, music_bot_tracking_disabled, registered_last_fm, last_small_indexed, lastfm_pro, data_source, mode, fm_footer_options " +
                                    "FROM users " +
                                      "WHERE UPPER(user_name_last_fm) = UPPER(@lastFmUserName) " +
                                      "AND last_used is not null " +
                                      "AND data_source != 1 " +
                                      "ORDER BY last_used DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var user = await connection.QueryFirstOrDefaultAsync<User>(getUserQuery, new
        {
            lastFmUserName
        });

        return user;
    }

    public static async Task SetUserIndexTime(int userId, DateTime now, DateTime lastScrobble, NpgsqlConnection connection)
    {
        Log.Information($"Setting user index time for user {userId}");

        await using var setIndexTime = new NpgsqlCommand($"UPDATE public.users SET last_indexed='{now:u}', last_updated='{now:u}', last_scrobble_update = '{lastScrobble:u}' WHERE user_id = {userId};", connection);
        await setIndexTime.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public static async Task<DateTime> SetUserSignUpTime(int userId, DateTime signUpDateTime, NpgsqlConnection connection,
        bool lastfmPro)
    {
        Log.Information($"Setting user index signup time ({signUpDateTime}) for user {userId}");

        await using var setIndexTime = new NpgsqlCommand($"UPDATE public.users SET registered_last_fm='{signUpDateTime:u}', lastfm_pro = '{lastfmPro}' WHERE user_id = {userId};", connection);
        await setIndexTime.ExecuteNonQueryAsync().ConfigureAwait(false);

        return signUpDateTime;
    }
}
