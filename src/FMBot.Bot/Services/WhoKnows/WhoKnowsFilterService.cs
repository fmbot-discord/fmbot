using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Genius.Models.User;
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

    private const int MaxAmountOfPlaysPerDay = 800;
    private const int MaxAmountOfHoursPerPeriod = 144;
    private const int PeriodAmountOfDays = 8;

    public WhoKnowsFilterService(IDbContextFactory<FMBotDbContext> contextFactory, IOptions<BotSettings> botSettings, TimeService timeService)
    {
        this._contextFactory = contextFactory;
        this._timeService = timeService;
        this._botSettings = botSettings.Value;
    }

    public async Task<List<GlobalFilteredUser>> UpdateGlobalFilteredUsers()
    {
        Log.Information("GWKFilter: Running");

        try
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();

            var highestUserId = await GetHighestUserId();
            var newFilteredUsers = new List<GlobalFilteredUser>();

            var existingBotters = await db.BottedUsers
                .Where(w => w.BanActive)
                .ToListAsync();

            var usersToSkip = existingBotters
                .GroupBy(g => g.UserNameLastFM, StringComparer.OrdinalIgnoreCase)
                .Select(s => s.Key)
                .ToHashSet();

            for (var i = highestUserId; i >= 0; i -= 10000)
            {
                var userPlays = await GetGlobalUserPlays(i, i - 10000);

                foreach (var user in userPlays)
                {
                    var timeListened = await this._timeService.GetPlayTimeForPlays(user.Value);

                    if (timeListened.TotalHours >= MaxAmountOfHoursPerPeriod)
                    {
                        Log.Information("GWKFilter: Found user {userId} with too much playtime - {totalHours}", user.Key, (int)timeListened.TotalHours);

                        newFilteredUsers.Add(new GlobalFilteredUser
                        {
                            Created = DateTime.UtcNow,
                            OccurrenceStart = user.Value.MinBy(b => b.TimePlayed).TimePlayed,
                            OccurrenceEnd = user.Value.MaxBy(b => b.TimePlayed).TimePlayed,
                            Reason = GlobalFilterReason.PlayTimeInPeriod,
                            ReasonAmount = (int)timeListened.TotalHours,
                            UserId = user.Key
                        });
                    }
                    else if ((user.Value.Count / PeriodAmountOfDays) >= MaxAmountOfPlaysPerDay)
                    {
                        Log.Information("GWKFilter: Found user {userId} with too much plays - {totalPlays} in {totalDays}", user.Key, user.Value.Count, MaxAmountOfPlaysPerDay);

                        newFilteredUsers.Add(new GlobalFilteredUser
                        {
                            Created = DateTime.UtcNow,
                            OccurrenceStart = user.Value.MinBy(b => b.TimePlayed).TimePlayed,
                            OccurrenceEnd = user.Value.MaxBy(b => b.TimePlayed).TimePlayed,
                            Reason = GlobalFilterReason.AmountPerPeriod,
                            ReasonAmount = user.Value.Count,
                            UserId = user.Key
                        });
                    }
                }
            }

            var userIds = newFilteredUsers.Select(s => s.UserId).ToHashSet();
            var users = await db.Users
                .Where(w => userIds.Contains(w.UserId))
                .ToListAsync();

            foreach (var filteredUser in newFilteredUsers)
            {
                var user = users.First(f => f.UserId == filteredUser.UserId);

                filteredUser.UserNameLastFm = user.UserNameLastFM;
                filteredUser.RegisteredLastFm = user.RegisteredLastFm;
            }

            Log.Information("GWKFilter: Found {filterCount} users to filter", newFilteredUsers.Count);

            newFilteredUsers = newFilteredUsers
                .Where(w => !usersToSkip.Contains(w.UserNameLastFm, StringComparer.OrdinalIgnoreCase))
                .ToList();

            Log.Information("GWKFilter: Found {filterCount} users to filter after removing botted users", newFilteredUsers.Count);

            return newFilteredUsers;
        }
        catch (Exception e)
        {
            Log.Error("GWKFilter: error", e);
            throw;
        }
    }

    private async Task<Dictionary<int, List<UserPlay>>> GetGlobalUserPlays(int topUserId, int botUserId)
    {
        Log.Information("GWKFilter: Getting plays from userIds {topUserId} to {botUserId}", topUserId, botUserId);

        const int start = PeriodAmountOfDays + 3;
        const int end = 3;

        var sql = "SELECT up.* " +
                  "FROM user_plays AS up " +
                  $"WHERE time_played >= current_date - interval '{start}' day AND  time_played <= current_date - interval '{end}' day " +
                  $"AND user_id  >= {botUserId} AND user_id <= {topUserId}";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var userPlays = await connection.QueryAsync<UserPlay>(sql);

        return userPlays
            .GroupBy(g => g.UserId)
            .ToDictionary(d => d.Key, d => d.ToList());
    }

    private async Task<int> GetHighestUserId()
    {
        const string sql = "SELECT MAX (user_id) FROM public.users";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await connection.QueryFirstAsync<int>(sql);
    }
}
