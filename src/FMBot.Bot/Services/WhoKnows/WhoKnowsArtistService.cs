using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
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

namespace FMBot.Bot.Services.WhoKnows;

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

    public async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForArtist(IGuild discordGuild, int guildId, string artistName)
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
                           "FULL OUTER JOIN users AS u ON ua.user_id = u.user_id " +
                           "INNER JOIN guild_users AS gu ON gu.user_id = ua.user_id " +
                           "WHERE gu.guild_id = @guildId AND UPPER(ua.name) = UPPER(CAST(@artistName AS CITEXT)) " +
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
                var discordUser = await discordGuild.GetUserAsync(userArtist.DiscordUserId, CacheMode.CacheOnly);
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
                           "FULL OUTER JOIN users AS u ON ua.user_id = u.user_id " +
                           "WHERE UPPER(ua.name) = UPPER(CAST(@artistName AS CITEXT)) " +
                           "ORDER BY UPPER(u.user_name_last_fm) DESC, ua.playcount DESC) ua " +
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
                    var discordUser = await discordGuild.GetUserAsync(userArtist.DiscordUserId, CacheMode.CacheOnly);
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
                           "FULL OUTER JOIN users AS u ON ua.user_id = u.user_id " +
                           "INNER JOIN friends AS fr ON fr.friend_user_id = ua.user_id " +
                           "LEFT JOIN guild_users AS gu ON gu.user_id = u.user_id AND gu.guild_id = @guildId " +
                           "WHERE fr.user_id = @userId AND UPPER(ua.name) = UPPER(CAST(@artistName AS CITEXT)) " +
                           "ORDER BY UPPER(u.user_name_last_fm) DESC, ua.playcount DESC) ua " +
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

            var discordUser = await context.Client.GetUserAsync(userArtist.DiscordUserId, CacheMode.CacheOnly);
            if (discordUser != null)
            {
                userName = discordUser.Username;
            }

            if (context.Guild != null)
            {
                var guildUser = await context.Guild.GetUserAsync(userArtist.DiscordUserId, CacheMode.CacheOnly);
                if (guildUser != null)
                {
                    userName = guildUser.DisplayName;
                }
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
                  "INNER JOIN guild_users AS gu ON gu.user_id = ua.user_id  " +
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

    private async Task<IEnumerable<UserArtist>> GetGuildUserArtists(int guildId, int minPlaycount = 0)
    {
        const string sql = "SELECT ua.* " +
                           "FROM user_artists AS ua " +
                           "INNER JOIN guild_users AS gu ON gu.user_id = ua.user_id " +
                           "WHERE gu.guild_id = @guildId  AND gu.bot != true " +
                           "AND ua.playcount > @minPlaycount " +
                           "AND NOT ua.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                           "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                           "AND LOWER(ua.name) = ANY(SELECT LOWER(artists.name) AS artist_name " +
                           "FROM public.artist_genres AS ag " +
                           "INNER JOIN artists ON artists.id = ag.artist_id) ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await connection.QueryAsync<UserArtist>(sql, new
        {
            guildId,
            minPlaycount
        });
    }

    public async Task<ICollection<GuildArtist>> GetTopAllTimeArtistsForGuildWithListeners(int guildId,
        OrderType orderType)
    {
        var userArtists = await GetGuildUserArtists(guildId);

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

    public async Task<IReadOnlyList<AffinityArtistResultWithUser>> GetNeighbors(int guildId, int userId)
    {
        var topArtistsForEveryoneInServer = new List<AffinityArtist>();

        var allUserArtists = (await GetGuildUserArtists(guildId, 29))
                .OrderByDescending(o => o.Playcount)
                .GroupBy(g => g.UserId)
                .ToList();

        foreach (var userArtists in allUserArtists)
        {
            var topArtist = userArtists
                .FirstOrDefault();

            var avgPlaycount = userArtists
                .Average(a => a.Playcount);

            if (topArtist != null)
            {
                var topArtistsForUser = userArtists
                    .Where(w => w.Name != null)
                    .Select(s => new AffinityArtist
                    {
                        ArtistName = s.Name.ToLower(),
                        Playcount = s.Playcount,
                        UserId = s.UserId,
                        Weight = ((decimal)s.Playcount / (decimal)topArtist.Playcount) * (s.Playcount > (avgPlaycount * 2) ? 3 : 1)
                    })
                    .ToList();

                if (topArtistsForUser.Any())
                {
                    topArtistsForEveryoneInServer.AddRange(topArtistsForUser);
                }
            }
        };

        await using var db = await this._contextFactory.CreateDbContextAsync();

        var currentUserArtists = await db.UserArtists
            .Where(w => w.UserId == userId)
            .ToListAsync();

        var userTopArtist = currentUserArtists
            .MaxBy(o => o.Playcount);

        var userAvgPlaycount = currentUserArtists
            .Where(w => w.Playcount > 29)
            .Average(a => a.Playcount);

        var topArtists = currentUserArtists
            .Where(
                w => w.Playcount > 29 &&
                     w.Name != null)
            .OrderByDescending(o => o.Playcount)
            .Select(s => new AffinityArtist
            {
                ArtistName = s.Name.ToLower(),
                Playcount = s.Playcount,
                UserId = s.UserId,
                Weight = ((decimal)s.Playcount / (decimal)userTopArtist.Playcount) * (s.Playcount > (userAvgPlaycount * 2) ? 24 : 8)
            })
            .ToList();

        return topArtistsForEveryoneInServer
            .Where(w => w != null &&
                        topArtists.Select(s => s.ArtistName).Contains(w.ArtistName))
            .GroupBy(g => g.UserId)
            .OrderByDescending(g => g.Sum(s => s.Weight * topArtists.First(f => f.ArtistName == s.ArtistName).Weight))
            .Select(s => new AffinityArtistResultWithUser
            {
                UserId = s.Key,
                MatchPercentage = Math.Min(
                    ((decimal)s.Sum(w => w.Weight * topArtists.First(f => f.ArtistName == w.ArtistName).Weight)
                        / (decimal)topArtists.Sum(w => w.Weight) * 100) * 2, 100)
            })
            .ToList();
    }
}
