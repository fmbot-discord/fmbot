using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
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

    public const int MaxAmountOfPlaysPerDay = 650;
    private const int MaxAmountOfHoursPerPeriod = 144;
    public const int PeriodAmountOfDays = 8;

    public WhoKnowsFilterService(IDbContextFactory<FMBotDbContext> contextFactory, IOptions<BotSettings> botSettings)
    {
        this._contextFactory = contextFactory;
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
                var candidates = await GetGlobalFilterCandidates(i, i - 10000);

                foreach (var candidate in candidates)
                {
                    var totalPlayTime = TimeSpan.FromMilliseconds(candidate.TotalMsPlayed);

                    if (totalPlayTime.TotalHours >= MaxAmountOfHoursPerPeriod)
                    {
                        Log.Information("GWKFilter: Found user {userId} with too much playtime - {totalHours}",
                            candidate.UserId, (int)totalPlayTime.TotalHours);

                        newFilteredUsers.Add(new GlobalFilteredUser
                        {
                            Created = DateTime.UtcNow,
                            OccurrenceStart = candidate.FirstPlay,
                            OccurrenceEnd = candidate.LastPlay,
                            Reason = GlobalFilterReason.PlayTimeInPeriod,
                            ReasonAmount = (int)totalPlayTime.TotalHours,
                            UserId = candidate.UserId,
                            MonthLength = 3
                        });
                    }
                    else if ((candidate.PlayCount / PeriodAmountOfDays) >= MaxAmountOfPlaysPerDay)
                    {
                        Log.Information(
                            "GWKFilter: Found user {userId} with too much plays - {totalPlays} in {totalDays}",
                            candidate.UserId, candidate.PlayCount, MaxAmountOfPlaysPerDay);

                        newFilteredUsers.Add(new GlobalFilteredUser
                        {
                            Created = DateTime.UtcNow,
                            OccurrenceStart = candidate.FirstPlay,
                            OccurrenceEnd = candidate.LastPlay,
                            Reason = GlobalFilterReason.AmountPerPeriod,
                            ReasonAmount = candidate.PlayCount,
                            UserId = candidate.UserId,
                            MonthLength = 3
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

            Log.Information("GWKFilter: Found {filterCount} existing filtered users to skip",
                existingFilteredUsersHash.Count);

            newFilteredUsers = newFilteredUsers
                .Where(w => !existingFilteredUsersHash.Contains(w.UserId.GetValueOrDefault()))
                .ToList();

            Log.Information("GWKFilter: Found {filterCount} users to filter after removing existing users",
                newFilteredUsers.Count);

            var newFilteredUsersHash = newFilteredUsers.Select(s => s.UserId).ToHashSet();
            var userFilterHistory = await db.GlobalFilteredUsers
                .Where(w =>  w.UserId != null && newFilteredUsersHash.Contains(w.UserId))
                .OrderBy(o => o.Created)
                .GroupBy(g => g.UserId)
                .ToDictionaryAsync(d => d.Key, d => d.ToList());

            foreach (var filteredUser in newFilteredUsers)
            {
                if (!userFilterHistory.TryGetValue(filteredUser.UserId, out var userHistory))
                {
                    filteredUser.MonthLength = 3;
                    continue;
                }

                var allViolations = userHistory.OrderBy(o => o.Created).ToList();

                var consecutiveViolations = 1;
                var lastViolationDate = allViolations[0].Created;

                // Check if at least 3 violations occurred with at least 4 weeks between each violation
                for (var i = 1; i < allViolations.Count; i++)
                {
                    var currentViolation = allViolations[i];
                    var weeksBetween = (currentViolation.Created - lastViolationDate).TotalDays / 7;

                    if (weeksBetween >= 4)
                    {
                        consecutiveViolations++;
                        lastViolationDate = currentViolation.Created;
                    }
                }

                if (consecutiveViolations >= 3)
                {
                    Log.Information("GWKFilter: Found repeat offender {userId} - {userNameLastFm} - {consecutiveViolations} prior violations",
                        filteredUser.UserId, filteredUser.UserNameLastFm, consecutiveViolations);
                    filteredUser.MonthLength = 6;
                }
            }

            return newFilteredUsers;
        }
        catch (Exception e)
        {
            Log.Error(e, "GWKFilter: error");
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
                filterInfo.AppendLine(
                    $"Had `{filteredUser.ReasonAmount}` hours of listening time - Around `{avgPerDay}`hr per day");
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
            filterInfo.AppendLine(
                $"From <t:{filteredUser.OccurrenceStart.Value.ToUnixEpochDate()}:f> to <t:{filteredUser.OccurrenceEnd.Value.ToUnixEpochDate()}:f>");
        }

        return filterInfo.ToString();
    }

    private async Task<List<GlobalFilterCandidate>> GetGlobalFilterCandidates(int topUserId, int botUserId)
    {
        Log.Information("GWKFilter: Getting filter candidates from userIds {topUserId} to {botUserId}", topUserId, botUserId);

        const int start = PeriodAmountOfDays + 3;
        const int end = 3;

        var sql = $"""
                   WITH range_plays AS (
                       SELECT up.user_id, up.track_id, up.artist_id,
                              COUNT(*) AS plays,
                              MIN(up.time_played) AS first_play,
                              MAX(up.time_played) AS last_play
                       FROM user_plays AS up
                       WHERE up.user_id >= @botUserId AND up.user_id <= @topUserId
                         AND up.time_played >= current_date - interval '{start}' day
                         AND up.time_played <= current_date - interval '{end}' day
                         AND up.play_source = 0
                       GROUP BY up.user_id, up.track_id, up.artist_id
                   ),
                   track_durations AS (
                       SELECT ti.track_id, NULLIF(t.duration_ms, 0)::bigint AS duration_ms
                       FROM (SELECT DISTINCT rp.track_id FROM range_plays AS rp WHERE rp.track_id IS NOT NULL) AS ti
                       JOIN tracks AS t ON t.id = ti.track_id
                   ),
                   track_plays AS (
                       SELECT rp.user_id, rp.artist_id, rp.plays, rp.first_play, rp.last_play, td.duration_ms
                       FROM range_plays AS rp
                       LEFT JOIN track_durations AS td ON td.track_id = rp.track_id
                   ),
                   artist_avgs AS (
                       SELECT t.artist_id, AVG(t.duration_ms)::bigint AS avg_duration_ms
                       FROM tracks AS t
                       WHERE t.duration_ms IS NOT NULL AND t.duration_ms != 0
                         AND t.artist_id IN (SELECT DISTINCT tp.artist_id
                                             FROM track_plays AS tp
                                             WHERE tp.artist_id IS NOT NULL AND tp.duration_ms IS NULL)
                       GROUP BY t.artist_id
                   ),
                   enriched AS (
                       SELECT tp.user_id, tp.plays, tp.first_play, tp.last_play,
                              tp.plays * COALESCE(
                                  tp.duration_ms,
                                  CASE
                                      WHEN aa.avg_duration_ms > 360000 THEN aa.avg_duration_ms - 120000
                                      WHEN aa.avg_duration_ms > 240000 THEN aa.avg_duration_ms - 90000
                                      WHEN aa.avg_duration_ms > 120000 THEN aa.avg_duration_ms - 60000
                                      ELSE aa.avg_duration_ms
                                  END,
                                  60000) AS ms_played
                       FROM track_plays AS tp
                       LEFT JOIN artist_avgs AS aa ON aa.artist_id = tp.artist_id
                   )
                   SELECT user_id,
                          SUM(plays)::int AS play_count,
                          SUM(ms_played)::bigint AS total_ms_played,
                          MIN(first_play) AS first_play,
                          MAX(last_play) AS last_play
                   FROM enriched
                   GROUP BY user_id
                   HAVING SUM(ms_played) >= @maxMsPerPeriod OR SUM(plays) >= @maxPlaysPerPeriod
                   """;

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var candidates = await connection.QueryAsync<GlobalFilterCandidate>(sql, new
        {
            topUserId,
            botUserId,
            maxMsPerPeriod = MaxAmountOfHoursPerPeriod * 3600000L,
            maxPlaysPerPeriod = MaxAmountOfPlaysPerDay * PeriodAmountOfDays
        });

        return candidates.ToList();
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
