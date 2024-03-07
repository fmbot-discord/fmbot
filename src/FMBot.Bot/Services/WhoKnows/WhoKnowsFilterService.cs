using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Extensions;
using FMBot.Domain.Enums;
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

    public const int MaxAmountOfPlaysPerDay = 650;
    private const int MaxAmountOfHoursPerPeriod = 144;
    public const int PeriodAmountOfDays = 8;

    public WhoKnowsFilterService(IDbContextFactory<FMBotDbContext> contextFactory, IOptions<BotSettings> botSettings, TimeService timeService)
    {
        this._contextFactory = contextFactory;
        this._timeService = timeService;
        this._botSettings = botSettings.Value;
    }

    public async Task<List<GlobalFilteredUser>> GetNewGlobalFilteredUsers()
    {
        Log.Information("GWKFilter: Running");

        try
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();

            var highestUserId = await GetHighestUserId();
            var newFilteredUsers = new List<GlobalFilteredUser>();

            for (var i = highestUserId; i >= 0; i -= 10000)
            {
                var userPlays = await GetGlobalUserPlays(i, i - 10000);

                foreach (var user in userPlays)
                {
                    var timeListened = await this._timeService.EnrichPlaysWithPlayTime(user.Value, true);

                    if (timeListened.totalPlayTime.TotalHours >= MaxAmountOfHoursPerPeriod)
                    {
                        Log.Information("GWKFilter: Found user {userId} with too much playtime - {totalHours}", user.Key, (int)timeListened.totalPlayTime.TotalHours);

                        newFilteredUsers.Add(new GlobalFilteredUser
                        {
                            Created = DateTime.UtcNow,
                            OccurrenceStart = user.Value.MinBy(b => b.TimePlayed).TimePlayed,
                            OccurrenceEnd = user.Value.MaxBy(b => b.TimePlayed).TimePlayed,
                            Reason = GlobalFilterReason.PlayTimeInPeriod,
                            ReasonAmount = (int)timeListened.totalPlayTime.TotalHours,
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

            var existingFilterData = DateTime.UtcNow.AddDays(-14);
            var existingFilteredUsers = await db.GlobalFilteredUsers
                .Where(w => w.Created >= existingFilterData && w.UserId.HasValue)
                .Select(s => s.UserId)
                .ToListAsync();

            var existingFilteredUsersHash = existingFilteredUsers
                .GroupBy(g => g.Value)
                .Select(s => s.Key)
                .ToHashSet();

            Log.Information("GWKFilter: Found {filterCount} existing filtered users to skip", existingFilteredUsersHash.Count);

            newFilteredUsers = newFilteredUsers
                .Where(w => !existingFilteredUsersHash.Contains(w.UserId.GetValueOrDefault()))
                .ToList();

            Log.Information("GWKFilter: Found {filterCount} users to filter after removing existing users", newFilteredUsers.Count);

            return newFilteredUsers;
        }
        catch (Exception e)
        {
            Log.Error("GWKFilter: error", e);
            throw;
        }
    }

    public async Task AddFilteredUsersToDatabase(List<GlobalFilteredUser> filteredUsers)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        await db.GlobalFilteredUsers.AddRangeAsync(filteredUsers);

        await db.SaveChangesAsync();

        Log.Information("GWKFilter: Added {filterCount} filtered users to database", filteredUsers.Count);
    }

    public static string FilteredUserReason(GlobalFilteredUser filteredUser)
    {
        var filterInfo = new StringBuilder();
        switch (filteredUser.Reason)
        {
            case GlobalFilterReason.PlayTimeInPeriod:
                var avgPerDay = filteredUser.ReasonAmount.Value / PeriodAmountOfDays;
                filterInfo.AppendLine($"Had `{filteredUser.ReasonAmount}` hours of listening time - Around `{avgPerDay}`hr per day");
                break;
            case GlobalFilterReason.AmountPerPeriod:
                filterInfo.AppendLine($"Had `{filteredUser.ReasonAmount}` scrobbles ");
                break;
            case GlobalFilterReason.ShortTrack:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (filteredUser.OccurrenceStart.HasValue && filteredUser.OccurrenceEnd.HasValue)
        {
            filterInfo.AppendLine($"From <t:{filteredUser.OccurrenceStart.Value.ToUnixEpochDate()}:f> to <t:{filteredUser.OccurrenceEnd.Value.ToUnixEpochDate()}:f>");
        }

        return filterInfo.ToString();
    }

    private async Task<Dictionary<int, List<UserPlay>>> GetGlobalUserPlays(int topUserId, int botUserId)
    {
        Log.Information("GWKFilter: Getting plays from userIds {topUserId} to {botUserId}", topUserId, botUserId);

        const int start = PeriodAmountOfDays + 3;
        const int end = 3;

        var sql = "SELECT up.* " +
                  "FROM user_plays AS up " +
                  $"WHERE time_played >= current_date - interval '{start}' day AND  time_played <= current_date - interval '{end}' day " +
                  $"AND user_id  >= {botUserId} AND user_id <= {topUserId} AND play_source = 0";

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
