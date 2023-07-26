using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

namespace FMBot.Bot.Services.WhoKnows;

public class WhoKnowsFilterService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly BotSettings _botSettings;
    private readonly TimeService _timeService;

    private const int MaxAmountOfPlaysPerDay = 600;
    private const int MaxAmountOfHoursPerPeriod = 144;
    private const int PeriodAmountOfDays = 8;

    public WhoKnowsFilterService(IDbContextFactory<FMBotDbContext> contextFactory, IOptions<BotSettings> botSettings, TimeService timeService)
    {
        this._contextFactory = contextFactory;
        this._timeService = timeService;
        this._botSettings = botSettings.Value;
    }

    public async Task UpdateGlobalFilteredUsers()
    {
        Log.Information("Updating whoknows quality filter");
        var userPlays = await GetGlobalUserPlays();

        foreach (var user in userPlays)
        {
            var timeListened = await this._timeService.GetPlayTimeForPlays(user.Value);

            if (timeListened.TotalHours >= MaxAmountOfHoursPerPeriod)
            {
                Log.Information("Found user {userId} with too much playtime", user.Key);
            }

            if ((user.Value.Count / PeriodAmountOfDays) >= MaxAmountOfPlaysPerDay)
            {
                Log.Information("Found user {userId} with too much plays", user.Key);
            }
        }
    }

    private async Task<Dictionary<int, List<UserPlay>>> GetGlobalUserPlays()
    {
        const int start = PeriodAmountOfDays + 3;
        const int end = 3;

        var sql = "SELECT up.* " +
                  "FROM user_plays AS up " +
                  $"WHERE time_played >= current_date - interval '{start}' day AND  time_played <= current_date - interval '{end}' day";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var userPlays = await connection.QueryAsync<UserPlay>(sql);

        return userPlays
            .GroupBy(g => g.UserId)
            .ToDictionary(d => d.Key, d => d.ToList());
    }
}
