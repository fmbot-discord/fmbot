using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Dasync.Collections;
using Discord;
using Discord.Commands;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Bot.Services.WhoKnows
{
    public class WhoKnowsArtistService
    {
        private readonly IMemoryCache _cache;
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly BotSettings _botSettings;

        public WhoKnowsArtistService(IMemoryCache cache, IDbContextFactory<FMBotDbContext> contextFactory, IOptions<BotSettings> botSettings)
        {
            this._cache = cache;
            this._contextFactory = contextFactory;
            this._botSettings = botSettings.Value;
        }

        public async Task<ICollection<WhoKnowsObjectWithUser>> GetIndexedUsersForArtist(IGuild discordGuild, int guildId, string artistName)
        {
            const string sql = "SELECT ua.user_id, " +
                               "ua.name, " +
                               "ua.playcount, " +
                               "u.user_name_last_fm, " +
                               "u.discord_user_id, " +
                               "u.last_used, " +
                               "gu.user_name, " +
                               "gu.who_knows_whitelisted " +
                               "FROM user_artists AS ua " +
                               "INNER JOIN users AS u ON ua.user_id = u.user_id " +
                               "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                               "WHERE gu.guild_id = @guildId AND UPPER(ua.name) = UPPER(CAST(@artistName AS CITEXT))" +
                               "ORDER BY ua.playcount DESC ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
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
                    var discordUser = await discordGuild.GetUserAsync(userArtist.DiscordUserId);
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
                    LastUsed = userArtist.LastUsed,
                    WhoKnowsWhitelisted = userArtist.WhoKnowsWhitelisted,
                });
            }

            return whoKnowsArtistList;
        }

        public async Task<IList<WhoKnowsObjectWithUser>> GetGlobalUsersForArtists(IGuild discordGuild, string artistName)
        {
            const string sql = "SELECT * " +
                               "FROM (SELECT DISTINCT ON(UPPER(u.user_name_last_fm)) " +
                               "ua.user_id, " +
                               "ua.name, " +
                               "ua.playcount, " +
                               "u.user_name_last_fm, " +
                               "u.discord_user_id, " +
                               "u.registered_last_fm, " +
                               "u.privacy_level " +
                               "FROM user_artists AS ua " +
                               "INNER JOIN users AS u ON ua.user_id = u.user_id " +
                               "WHERE UPPER(ua.name) = UPPER(CAST(@artistName AS CITEXT)) " +
                               "ORDER BY UPPER(u.user_name_last_fm) DESC) ua " +
                               "ORDER BY playcount DESC";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
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
                    if (discordGuild != null)
                    {
                        var discordUser = await discordGuild.GetUserAsync(userArtist.DiscordUserId);
                        if (discordUser != null)
                        {
                            userName = discordUser.Nickname ?? discordUser.Username;
                        }
                    }
                }

                whoKnowsArtistList.Add(new WhoKnowsObjectWithUser
                {
                    Name = userArtist.Name,
                    DiscordName = userName,
                    Playcount = userArtist.Playcount,
                    LastFMUsername = userArtist.UserNameLastFm,
                    UserId = userArtist.UserId,
                    RegisteredLastFm = userArtist.RegisteredLastFm,
                    PrivacyLevel = userArtist.PrivacyLevel
                });
            }

            return whoKnowsArtistList;
        }

        public async Task<IList<WhoKnowsObjectWithUser>> GetFriendUsersForArtists(ICommandContext context, int guildId, int userId, string artistName)
        {
            const string sql = "SELECT * " +
                               "FROM (SELECT DISTINCT ON(UPPER(u.user_name_last_fm)) " +
                               "ua.user_id, " +
                               "ua.name, " +
                               "ua.playcount, " +
                               "u.user_name_last_fm, " +
                               "u.discord_user_id, " +
                               "gu.user_name, " +
                               "gu.who_knows_whitelisted " +
                               "FROM user_artists AS ua " +
                               "INNER JOIN users AS u ON ua.user_id = u.user_id " +
                               "INNER JOIN friends AS fr ON fr.friend_user_id = u.user_id " +
                               "LEFT JOIN guild_users AS gu ON gu.user_id = u.user_id AND gu.guild_id = @guildId " +
                               "WHERE fr.user_id = @userId AND UPPER(ua.name) = UPPER(CAST(@artistName AS CITEXT)) " +
                               "ORDER BY UPPER(u.user_name_last_fm) DESC) ua " +
                               "ORDER BY playcount DESC";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var userArtists = (await connection.QueryAsync<WhoKnowsArtistDto>(sql, new
            {
                artistName,
                guildId,
                userId
            })).ToList();

            var whoKnowsArtistList = new List<WhoKnowsObjectWithUser>();

            foreach (var userArtist in userArtists)
            {
                var userName = userArtist.UserName ?? userArtist.UserNameLastFm;

                var discordUser = await context.Guild.GetUserAsync(userArtist.DiscordUserId);
                if (discordUser != null)
                {
                    userName = discordUser.Nickname ?? discordUser.Username;
                }

                whoKnowsArtistList.Add(new WhoKnowsObjectWithUser
                {
                    Name = userArtist.Name,
                    DiscordName = userName,
                    Playcount = userArtist.Playcount,
                    LastFMUsername = userArtist.UserNameLastFm,
                    UserId = userArtist.UserId,
                });
            }

            return whoKnowsArtistList;
        }

        public async Task<ICollection<GuildArtist>> GetTopAllTimeArtistsForGuild(int guildId,
            OrderType orderType, int? limit = 120)
        {
            var cacheKey = $"guild-alltime-top-artists-{guildId}-{orderType}";

            var cachedArtistsAvailable = this._cache.TryGetValue(cacheKey, out ICollection<GuildArtist> guildArtists);
            if (cachedArtistsAvailable)
            {
                return guildArtists;
            }

            var sql = "SELECT ua.name AS artist_name, " +
                                "SUM(ua.playcount) AS total_playcount, " +
                                "COUNT(ua.user_id) AS listener_count " +
                                "FROM user_artists AS ua   " +
                                "INNER JOIN users AS u ON ua.user_id = u.user_id   " +
                                "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id  " +
                                "WHERE gu.guild_id = @guildId  AND gu.bot != true " +
                                "AND NOT ua.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                                "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                                "GROUP BY ua.name ";

            sql += orderType == OrderType.Playcount ?
                "ORDER BY total_playcount DESC, listener_count DESC " :
                "ORDER BY listener_count DESC, total_playcount DESC ";

            if (limit.HasValue)
            {
                sql += $"LIMIT {limit}";
            }

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            guildArtists = (await connection.QueryAsync<GuildArtist>(sql, new
            {
                guildId
            })).ToList();

            this._cache.Set(cacheKey, guildArtists, TimeSpan.FromMinutes(10));

            return guildArtists;
        }

        public async Task<ICollection<GuildArtist>> GetTopAllTimeArtistsForGuildWithListeners(int guildId,
            OrderType orderType)
        {
            const string sql = "SELECT * " +
                               "FROM user_artists AS ua " +
                               "INNER JOIN users AS u ON ua.user_id = u.user_id " +
                               "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                               "WHERE gu.guild_id = @guildId  AND gu.bot != true  " +
                               "AND NOT ua.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                               "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                               "AND LOWER(ua.name) = ANY(SELECT LOWER(artists.name) AS artist_name " +
                               "FROM public.artist_genres AS ag " +
                               "INNER JOIN artists ON artists.id = ag.artist_id) ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var userArtists = await connection.QueryAsync<UserArtist>(sql, new
            {
                guildId
            });

            var guildArtists = userArtists
                .GroupBy(g => g.Name)
                .Select(s => new GuildArtist
                {
                    ArtistName = s.Key,
                    ListenerCount = s.Select(se => se.UserId).Distinct().Count(),
                    TotalPlaycount = s.Sum(se => se.Playcount),
                    ListenerUserIds = s.Select(se => se.UserId).ToList()
                });

            return guildArtists
                .OrderByDescending(o => orderType == OrderType.Listeners ? o.ListenerCount : o.TotalPlaycount)
                .ToList();
        }

        public async Task<int?> GetArtistPlayCountForUser(string artistName, int userId)
        {
            const string sql = "SELECT ua.playcount " +
                               "FROM user_artists AS ua " +
                               "WHERE ua.user_id = @userId AND UPPER(ua.name) = UPPER(CAST(@artistName AS CITEXT)) " +
                               "ORDER BY playcount DESC";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            return await connection.QueryFirstOrDefaultAsync<int?>(sql, new
            {
                userId,
                artistName
            });
        }

        public async Task<int> GetWeekArtistPlaycountForGuildAsync(int guildId, string artistName)
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
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            return await connection.QuerySingleAsync<int>(sql, new
            {
                guildId,
                artistName,
                minDate
            });
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
                    await using var db = await this._contextFactory.CreateDbContextAsync();

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
