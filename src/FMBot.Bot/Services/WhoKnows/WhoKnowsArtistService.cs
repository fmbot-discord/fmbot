using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Dasync.Collections;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Bot.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using Serilog;

namespace FMBot.Bot.Services.WhoKnows
{
    public class WhoKnowsArtistService
    {
        private readonly IMemoryCache _cache;
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        public WhoKnowsArtistService(IMemoryCache cache, IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._cache = cache;
            this._contextFactory = contextFactory;
        }

        public static async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForArtist(ICommandContext context, int guildId, string artistName)
        {
            const string sql = "SELECT ua.user_id, " +
                               "ua.name, " +
                               "ua.playcount, " +
                               "u.user_name_last_fm, " +
                               "u.discord_user_id, " +
                               "gu.user_name " +
                               "FROM user_artists AS ua " +
                               "INNER JOIN users AS u ON ua.user_id = u.user_id " +
                               "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                               "WHERE gu.guild_id = @guildId AND UPPER(ua.name) = UPPER(CAST(@artistName AS CITEXT))" +
                               "ORDER BY ua.playcount DESC ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(ConfigData.Data.Database.ConnectionString);
            await connection.OpenAsync();

            var userArtists = (await connection.QueryAsync<WhoKnowsArtistDto>(sql, new
            {
                guildId,
                artistName
            })).ToList();

            var whoKnowsArtistList = new List<WhoKnowsObjectWithUser>();

            for (var i = 0; i < userArtists.Count; i++)
            {
                var userArtist = userArtists[i];

                var userName = userArtist.UserName ?? userArtist.UserNameLastFm;

                if (i < 15)
                {
                    var discordUser = await context.Guild.GetUserAsync(userArtist.DiscordUserId);
                    if (discordUser != null)
                    {
                        userName = discordUser.Nickname ?? discordUser.Username;
                    }
                }

                whoKnowsArtistList.Add(new WhoKnowsObjectWithUser
                {
                    Name = userArtist.Name,
                    DiscordName = userName,
                    Playcount = userArtist.Playcount,
                    LastFMUsername = userArtist.UserNameLastFm,
                    UserId = userArtist.UserId
                });
            }

            return whoKnowsArtistList;
        }

        public static async Task<IList<WhoKnowsObjectWithUser>> GetGlobalUsersForArtists(ICommandContext context, string artistName)
        {
            const string sql = "SELECT ua.user_id, " +
                               "ua.name, " +
                               "ua.playcount, " +
                               "u.user_name_last_fm, " +
                               "u.discord_user_id, " +
                               "u.privacy_level " +
                               "FROM user_artists AS ua " +
                               "INNER JOIN users AS u ON ua.user_id = u.user_id " +
                               "WHERE UPPER(ua.name) = UPPER(CAST(@artistName AS CITEXT))" +
                               "ORDER BY ua.playcount DESC ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(ConfigData.Data.Database.ConnectionString);
            await connection.OpenAsync();

            var userArtists = (await connection.QueryAsync<WhoKnowsGlobalArtistDto>(sql, new
            {
                artistName
            })).ToList();

            var whoKnowsArtistList = new List<WhoKnowsObjectWithUser>();

            for (var i = 0; i < userArtists.Count; i++)
            {
                var userArtist = userArtists[i];

                var userName = userArtist.UserNameLastFm;

                if (i < 15)
                {
                    var discordUser = await context.Guild.GetUserAsync(userArtist.DiscordUserId);
                    if (discordUser != null)
                    {
                        userName = discordUser.Nickname ?? discordUser.Username;
                    }
                }

                whoKnowsArtistList.Add(new WhoKnowsObjectWithUser
                {
                    Name = userArtist.Name,
                    DiscordName = userName,
                    Playcount = userArtist.Playcount,
                    LastFMUsername = userArtist.UserNameLastFm,
                    UserId = userArtist.UserId,
                    PrivacyLevel = userArtist.PrivacyLevel
                });
            }

            return whoKnowsArtistList;
        }

        public static async Task<IReadOnlyList<ListArtist>> GetTopAllTimeArtistsForGuild(int guildId,
            OrderType orderType)
        {
            var sql = "SELECT ua.name AS artist_name, " +
                                "SUM(ua.playcount) AS total_playcount, " +
                                "COUNT(ua.user_id) AS listener_count " +
                                "FROM user_artists AS ua   " +
                                "INNER JOIN users AS u ON ua.user_id = u.user_id   " +
                                "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id  " +
                                "WHERE gu.guild_id = @guildId  AND gu.bot != true " +
                                "AND NOT ua.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                                "GROUP BY ua.name ";

            sql += orderType == OrderType.Playcount ?
                "ORDER BY total_playcount DESC, listener_count DESC " :
                "ORDER BY listener_count DESC, total_playcount DESC ";

            sql += "LIMIT 14";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(ConfigData.Data.Database.ConnectionString);
            await connection.OpenAsync();

            return (await connection.QueryAsync<ListArtist>(sql, new
            {
                guildId
            })).ToList();
        }

        public static async Task<int?> GetArtistPlayCountForUser(string artistName, int userId)
        {
            const string sql = "SELECT ua.playcount " +
                               "FROM user_artists AS ua " +
                               "WHERE ua.user_id = @userId AND UPPER(ua.name) = UPPER(CAST(@artistName AS CITEXT))";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(ConfigData.Data.Database.ConnectionString);
            await connection.OpenAsync();

            return await connection.QuerySingleOrDefaultAsync<int?>(sql, new
            {
                userId,
                artistName
            });
        }

        public static async Task<int> GetWeekArtistPlaycountForGuildAsync(int guildId, string artistName)
        {
            var minDate = DateTime.UtcNow.AddDays(-7);

            const string sql = "SELECT coalesce(count(up.user_play_id), 0) " +
                               "FROM user_plays AS up " +
                               "INNER JOIN users AS u ON up.user_id = u.user_id " +
                               "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                               "WHERE gu.guild_id = @guildId AND " +
                               "UPPER(up.artist_name) = UPPER(CAST(@artistName AS CITEXT)) AND " +
                               "up.time_played >= @minDate";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(ConfigData.Data.Database.ConnectionString);
            await connection.OpenAsync();

            return await connection.QuerySingleAsync<int>(sql, new
            {
                guildId,
                artistName,
                minDate
            });
        }

        // TODO: figure out how to do this
        public async Task<int> GetWeekArtistListenerCountForGuildAsync(IEnumerable<User> guildUsers, string artistName)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-7);

            var userIds = guildUsers.Select(s => s.UserId);

            try
            {
                await using var db = this._contextFactory.CreateDbContext();
                return await db.UserPlays
                    .AsQueryable()
                    .Where(w =>
                        userIds.Contains(w.UserId) &&
                        w.TimePlayed.Date <= now.Date &&
                        w.TimePlayed.Date > minDate.Date &&
                        EF.Functions.ILike(w.ArtistName, artistName))
                    .GroupBy(x => new { x.UserId, x.ArtistName, x.UserPlayId })
                    .CountAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public async Task<IReadOnlyList<AffinityArtistResultWithUser>> GetNeighbors(IEnumerable<User> guildUsers, int userId)
        {
            var userIds = guildUsers
                .Where(w => w.UserId != userId)
                .Select(s => s.UserId);

            var topArtistsForEveryoneInServer = new List<AffinityArtist>();

            await userIds.ParallelForEachAsync(async user =>
            {
                var key = $"top-affinity-artists-{user}";

                if (this._cache.TryGetValue(key, out List<AffinityArtist> topArtistsForUser))
                {
                    topArtistsForEveryoneInServer.AddRange(topArtistsForUser);
                }
                else
                {
                    await using var db = this._contextFactory.CreateDbContext();

                    var topArtist = await db.UserArtists
                        .AsQueryable()
                        .OrderByDescending(o => o.Playcount)
                        .FirstOrDefaultAsync(w => w.UserId == user);

                    var avgPlaycount = await db.UserArtists
                        .AsQueryable()
                        .Where(w => w.UserId == userId && w.Playcount > 29)
                        .AverageAsync(a => a.Playcount);

                    if (topArtist != null)
                    {
                        topArtistsForUser = await db.UserArtists
                            .AsQueryable()
                            .Where(
                                w => w.Playcount > 29 &&
                                     w.UserId == user &&
                                     w.Name != null)
                            .Select(s => new AffinityArtist
                            {
                                ArtistName = s.Name.ToLower(),
                                Playcount = s.Playcount,
                                UserId = s.UserId,
                                Weight = ((decimal)s.Playcount / (decimal)topArtist.Playcount) * (s.Playcount > (avgPlaycount * 2) ? 3 : 1)
                            })
                            .ToListAsync();

                        if (topArtistsForUser.Any())
                        {
                            this._cache.Set(key, topArtistsForUser, TimeSpan.FromHours(12));
                            topArtistsForEveryoneInServer.AddRange(topArtistsForUser);
                        }
                    }


                }
            });

            await using var db = this._contextFactory.CreateDbContext();

            var userTopArtist = await db.UserArtists
                .AsQueryable()
                .OrderByDescending(o => o.Playcount)
                .FirstOrDefaultAsync(w => w.UserId == userId);

            var userAvgPlaycount = await db.UserArtists
                .AsQueryable()
                .Where(w => w.Playcount > 29 && w.UserId == userId)
                .AverageAsync(a => a.Playcount);

            var topArtists = await db.UserArtists
                .AsQueryable()
                .Where(
                    w => w.UserId == userId &&
                         w.Playcount > 29 &&
                         w.Name != null)
                .OrderByDescending(o => o.Playcount)
                .Select(s => new AffinityArtist
                {
                    ArtistName = s.Name.ToLower(),
                    Playcount = s.Playcount,
                    UserId = s.UserId,
                    Weight = ((decimal)s.Playcount / (decimal)userTopArtist.Playcount) * (s.Playcount > (userAvgPlaycount * 2) ? 24 : 8)
                })
                .ToListAsync();

            return topArtistsForEveryoneInServer
                .Where(w =>
                    w != null &&
                    topArtists.Select(s => s.ArtistName).Contains(w.ArtistName))
                .GroupBy(g => g.UserId)
                .OrderByDescending(g => g.Sum(s => s.Weight * topArtists.First(f => f.ArtistName == s.ArtistName).Weight))
                .Select(s => new AffinityArtistResultWithUser
                {
                    UserId = s.Key,
                    MatchPercentage = Math.Min(
                        ((decimal)s.Sum(w => w.Weight * topArtists.First(f => f.ArtistName == w.ArtistName).Weight)
                        / (decimal)topArtists.Sum(w => w.Weight) * 100) * 2, 100),
                    LastFMUsername = guildUsers.First(f => f.UserId == s.Key).UserNameLastFM,
                    Name = guildUsers.First(f => f.UserId == s.Key).UserNameLastFM
                })
                .ToList();
        }
    }
}
