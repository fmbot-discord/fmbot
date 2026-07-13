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

            for (var i = highestUserId; i >= 0; i -= 20000)
            {
                var candidates = await GetGlobalFilterCandidates(i, i - 20000);

                foreach (var candidate in candidates)
                {
                    var totalPlayTime = TimeSpan.FromMilliseconds(candidate.TotalMsPlayed);

                    if (totalPlayTime.TotalHours >= MaxAmountOfHoursPerPeriod)
                    {
                        Log.Information(
                            "GWKFilter: Found user {userId} - {discordUserId} - {userNameLastFm} with too much playtime - {totalHours} hours",
                            candidate.UserId, candidate.DiscordUserId, candidate.UserNameLastFm, (int)totalPlayTime.TotalHours);

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
                            "GWKFilter: Found user {userId} - {discordUserId} - {userNameLastFm} with too much plays - {totalPlays} over {periodDays} days",
                            candidate.UserId, candidate.DiscordUserId, candidate.UserNameLastFm, candidate.PlayCount, PeriodAmountOfDays);

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

    public async Task<BottedCheckStats> GetBottedCheckStats(int userId)
    {
        const string windowSql = """
                                 WITH plays AS (
                                     SELECT up.time_played, up.track_name, up.artist_id, NULLIF(t.duration_ms, 0)::bigint AS duration_ms
                                     FROM user_plays AS up
                                     LEFT JOIN tracks AS t ON t.id = up.track_id
                                     WHERE up.user_id = @userId
                                       AND up.play_source = 0
                                       AND up.time_played >= now() - interval '30 days'
                                 ),
                                 artist_avgs AS (
                                     SELECT t.artist_id, AVG(t.duration_ms)::bigint AS avg_duration_ms
                                     FROM tracks AS t
                                     WHERE t.duration_ms IS NOT NULL AND t.duration_ms != 0
                                       AND t.artist_id IN (SELECT DISTINCT p.artist_id
                                                           FROM plays AS p
                                                           WHERE p.artist_id IS NOT NULL AND p.duration_ms IS NULL)
                                     GROUP BY t.artist_id
                                 ),
                                 enriched AS (
                                     SELECT p.time_played, p.track_name, p.duration_ms,
                                            COALESCE(
                                                p.duration_ms,
                                                CASE
                                                    WHEN aa.avg_duration_ms > 360000 THEN aa.avg_duration_ms - 120000
                                                    WHEN aa.avg_duration_ms > 240000 THEN aa.avg_duration_ms - 90000
                                                    WHEN aa.avg_duration_ms > 120000 THEN aa.avg_duration_ms - 60000
                                                    ELSE aa.avg_duration_ms
                                                END,
                                                60000) AS est_duration_ms,
                                            LAG(p.time_played) OVER (ORDER BY p.time_played) AS prev_time_played,
                                            LAG(p.track_name) OVER (ORDER BY p.time_played) AS prev_track_name
                                     FROM plays AS p
                                     LEFT JOIN artist_avgs AS aa ON aa.artist_id = p.artist_id
                                 ),
                                 daily AS (
                                     SELECT date_trunc('day', time_played) AS day, COUNT(*) AS day_plays
                                     FROM plays
                                     GROUP BY 1
                                 )
                                 SELECT
                                     COUNT(*)::int AS plays_month,
                                     COUNT(*) FILTER (WHERE time_played >= now() - interval '7 days')::int AS plays_week,
                                     COALESCE(SUM(est_duration_ms), 0)::bigint AS ms_month,
                                     COALESCE(SUM(est_duration_ms) FILTER (WHERE time_played >= now() - interval '7 days'), 0)::bigint AS ms_week,
                                     COUNT(*) FILTER (WHERE duration_ms IS NULL)::int AS unknown_duration_plays,
                                     COUNT(*) FILTER (WHERE track_name = prev_track_name AND time_played - prev_time_played <= interval '10 seconds')::int AS duplicate_plays,
                                     COUNT(*) FILTER (WHERE duration_ms < 90000)::int AS short_track_plays,
                                     COALESCE((SELECT MAX(day_plays) FROM daily), 0)::int AS max_plays_in_day,
                                     (SELECT day FROM daily ORDER BY day_plays DESC LIMIT 1) AS max_plays_day,
                                     (SELECT COUNT(*) FROM daily WHERE day_plays >= @maxPlaysPerDay)::int AS days_over_play_limit
                                 FROM enriched
                                 """;

        const string topTracksSql = """
                                    SELECT ut.name, ut.artist_name, ut.playcount, t.duration_ms
                                    FROM user_tracks AS ut
                                    LEFT JOIN tracks AS t ON t.id = ut.track_id
                                    WHERE ut.user_id = @userId
                                    ORDER BY ut.playcount DESC
                                    LIMIT 3
                                    """;

        const string topShortTrackSql = """
                                        SELECT ut.name, ut.artist_name, ut.playcount, t.duration_ms
                                        FROM user_tracks AS ut
                                        JOIN tracks AS t ON t.id = ut.track_id
                                        WHERE ut.user_id = @userId AND t.duration_ms > 0 AND t.duration_ms < 90000
                                        ORDER BY ut.playcount DESC
                                        LIMIT 1
                                        """;

        const string topArtistSql = """
                                    SELECT ua.name, ua.playcount
                                    FROM user_artists AS ua
                                    WHERE ua.user_id = @userId
                                    ORDER BY ua.playcount DESC
                                    LIMIT 1
                                    """;

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var stats = await connection.QueryFirstAsync<BottedCheckStats>(windowSql,
            new { userId, maxPlaysPerDay = MaxAmountOfPlaysPerDay });

        stats.TopTracks = (await connection.QueryAsync<BottedCheckTopTrack>(topTracksSql, new { userId })).ToList();
        stats.TopShortTrack = await connection.QueryFirstOrDefaultAsync<BottedCheckTopTrack>(topShortTrackSql, new { userId });

        var topArtist = await connection.QueryFirstOrDefaultAsync<BottedCheckTopTrack>(topArtistSql, new { userId });
        stats.TopArtistName = topArtist?.Name;
        stats.TopArtistPlaycount = topArtist?.Playcount ?? 0;

        return stats;
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
                   ),
                   agg AS (
                       SELECT user_id,
                              SUM(plays)::int AS play_count,
                              SUM(ms_played)::bigint AS total_ms_played,
                              MIN(first_play) AS first_play,
                              MAX(last_play) AS last_play
                       FROM enriched
                       GROUP BY user_id
                       HAVING SUM(ms_played) >= @maxMsPerPeriod OR SUM(plays) >= @maxPlaysPerPeriod
                   )
                   SELECT agg.user_id,
                          agg.play_count,
                          agg.total_ms_played,
                          agg.first_play,
                          agg.last_play,
                          u.discord_user_id,
                          u.user_name_last_fm
                   FROM agg
                   JOIN users AS u ON u.user_id = agg.user_id
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
