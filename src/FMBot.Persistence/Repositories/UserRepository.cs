using System.Threading.Tasks;
using System;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

namespace FMBot.Persistence.Repositories;

public class UserRepository
{
    private readonly BotSettings _botSettings;

    public UserRepository(IOptions<BotSettings> botSettings)
    {
        this._botSettings = botSettings.Value;
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
