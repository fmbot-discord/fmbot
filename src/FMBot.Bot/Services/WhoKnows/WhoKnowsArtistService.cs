using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services.Guild;
using FMBot.Core;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
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
    private readonly AffinityService _affinityService;
    private readonly GuildService _guildService;
    private readonly IndexService _indexService;

    public WhoKnowsArtistService(IMemoryCache cache, IDbContextFactory<FMBotDbContext> contextFactory, IOptions<BotSettings> botSettings, AffinityService affinityService, GuildService guildService, IndexService indexService)
    {
        this._cache = cache;
        this._contextFactory = contextFactory;
        this._affinityService = affinityService;
        this._guildService = guildService;
        this._indexService = indexService;
        this._botSettings = botSettings.Value;
    }

    public async Task<WhoKnowsArtistContext> GetFilteredUsersForArtist(NetCord.Gateway.Guild discordGuild,
        User contextUser, string artistName, long? contextUserPlaycount, List<ulong> roles = null,
        bool filterDisabled = false)
    {
        var guild = await this._guildService.GetGuildForWhoKnows(discordGuild.Id);
        if (guild == null)
        {
            return null;
        }

        var guildUsers = await this._guildService.GetGuildUsers(discordGuild.Id);

        var usersWithArtist = await GetIndexedUsersForArtist(discordGuild, guildUsers, guild.GuildId, artistName);

        var discordGuildUser = await discordGuild.GetCachedGuildUserAsync(contextUser.DiscordUserId);
        await this._indexService.GetOrAddUserToGuild(guildUsers, guild, discordGuildUser, contextUser);
        await this._indexService.UpdateGuildUser(guildUsers, discordGuildUser, contextUser.UserId, guild);

        usersWithArtist = await WhoKnowsService.AddOrReplaceUserToIndexList(usersWithArtist, contextUser, artistName,
            discordGuild, contextUserPlaycount);

        var (filterStats, filteredUsersWithArtist) = WhoKnowsService.FilterWhoKnowsObjects(usersWithArtist, guildUsers,
            guild, contextUser.UserId, roles, filterDisabled);

        return new WhoKnowsArtistContext
        {
            Guild = guild,
            GuildUsers = guildUsers,
            FilteredUsersWithArtist = filteredUsersWithArtist,
            FilterStats = filterStats
        };
    }

    public async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForArtist(NetCord.Gateway.Guild discordGuild,
        IDictionary<int, FullGuildUser> guildUsers, int guildId, string artistName)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var whoKnowsArtistList =
            await WhoKnowsRepository.GetIndexedUsersForArtist(guildUsers, guildId, artistName, connection);

        return whoKnowsArtistList.WithDiscordDisplayNames(discordGuild, guildUsers);
    }

    public async Task<IList<WhoKnowsObjectWithUser>> GetGlobalUsersForArtists(NetCord.Gateway.Guild discordGuild, string artistName)
    {
        const string sql = "SELECT * " +
                           "FROM (SELECT DISTINCT ON(UPPER(u.user_name_last_fm)) " +
                           "ua.user_id, " +
                           "ua.playcount, " +
                           "u.user_name_last_fm, " +
                           "u.discord_user_id, " +
                           "u.registered_last_fm, " +
                           "u.privacy_level, " +
                           "u.last_used " +
                           "FROM user_artists AS ua " +
                           "FULL OUTER JOIN users AS u ON ua.user_id = u.user_id " +
                           "WHERE UPPER(ua.name) = UPPER(CAST(@artistName AS CITEXT)) " +
                           "ORDER BY UPPER(u.user_name_last_fm) DESC, ua.playcount DESC) ua " +
                           "ORDER BY last_used DESC";

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
                if (discordGuild != null && discordGuild.Users.TryGetValue(userArtist.DiscordUserId, out var discordUser))
                {
                    userName = discordUser.GetDisplayName();
                }
            }

            whoKnowsArtistList.Add(new WhoKnowsObjectWithUser
            {
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

    public async Task<IList<WhoKnowsObjectWithUser>> GetFriendUsersForArtists(NetCord.Gateway.Guild discordGuild,
        IDictionary<int, FullGuildUser> guildUsers, int guildId, int userId, string artistName)
    {
        const string sql = "SELECT * " +
                           "FROM (SELECT DISTINCT ON(UPPER(u.user_name_last_fm)) " +
                           "ua.user_id, " +
                           "ua.playcount, " +
                           "u.user_name_last_fm, " +
                           "gu.user_name AS discord_name " +
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
            var userName = userArtist.UserNameLastFm;

            guildUsers.TryGetValue(userArtist.UserId, out var guildUser);
            if (discordGuild != null && guildUser != null)
            {
                userName = guildUser.UserName;

                if (discordGuild.Users.TryGetValue(guildUser.DiscordUserId, out var discordGuildUser))
                {
                    userName = discordGuildUser.GetDisplayName();
                }
            }

            whoKnowsArtistList.Add(new WhoKnowsObjectWithUser
            {
                DiscordName = userName,
                Playcount = userArtist.Playcount,
                LastFMUsername = userArtist.UserNameLastFm,
                UserId = userArtist.UserId,
            });
        }

        return whoKnowsArtistList;
    }

    public async Task<ICollection<GuildArtist>> GetTopAllTimeArtistsForGuild(int guildId,
        OrderType orderType, int? limit = 120, int[] userIds = null)
    {
        var cacheKey = $"guild-alltime-top-artists-{guildId}-{orderType}";

        if (userIds == null)
        {
            var cachedArtistsAvailable = this._cache.TryGetValue(cacheKey, out ICollection<GuildArtist> cachedArtists);
            if (cachedArtistsAvailable)
            {
                return cachedArtists;
            }
        }

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var guildArtists =
            await WhoKnowsRepository.GetTopAllTimeArtistsForGuild(guildId, orderType, limit, userIds, connection);

        if (userIds == null)
        {
            this._cache.Set(cacheKey, guildArtists, TimeSpan.FromMinutes(10));
        }

        return guildArtists;
    }

    public async Task<ICollection<GuildArtist>> GetTopAllTimeArtistsForGuildWithListeners(int guildId,
        OrderType orderType)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var userArtists = await WhoKnowsRepository.GetGuildUserArtistsWithGenres(guildId, 0, connection);

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
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await ArtistRepository.GetArtistPlayCountForUser(connection, artistName, userId);
    }

    public Task<ICollection<AffinityItemDto>> GetAllTimeTopArtistForGuild(int guildId, bool largeGuild, bool bypassCache = false)
    {
        return this._affinityService.GetAllTimeTopArtistForGuild(guildId, largeGuild, bypassCache);
    }

    public Task<ICollection<AffinityItemDto>> GetQuarterlyTopArtistForGuild(int guildId, bool largeGuild, bool bypassCache = false)
    {
        return this._affinityService.GetQuarterlyTopArtistForGuild(guildId, largeGuild, bypassCache);
    }

    public Task<ConcurrentDictionary<int, AffinityUser>> GetAffinity(
        IEnumerable<AffinityItemDto> guildAllTimeArtists,
        List<AffinityItemDto> ownAllTime,
        IEnumerable<AffinityItemDto> guildQuarterlyArtists,
        List<AffinityItemDto> ownQuarterly)
    {
        return this._affinityService.GetAffinity(guildAllTimeArtists, ownAllTime, guildQuarterlyArtists, ownQuarterly);
    }
}
