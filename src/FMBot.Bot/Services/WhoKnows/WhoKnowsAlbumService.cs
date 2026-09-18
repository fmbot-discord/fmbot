using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Bot.Services.WhoKnows;

public class WhoKnowsAlbumService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly BotSettings _botSettings;
    private readonly IMemoryCache _cache;

    public WhoKnowsAlbumService(IDbContextFactory<FMBotDbContext> contextFactory, IOptions<BotSettings> botSettings, IMemoryCache cache)
    {
        this._contextFactory = contextFactory;
        this._botSettings = botSettings.Value;
        this._cache = cache;
    }

    public async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForAlbum(NetCord.Gateway.Guild discordGuild,
        IDictionary<int, FullGuildUser> guildUsers, int guildId, int albumId)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var whoKnowsAlbumList =
            await WhoKnowsRepository.GetIndexedUsersForAlbum(guildUsers, guildId, albumId, connection);

        return whoKnowsAlbumList.WithDiscordDisplayNames(discordGuild, guildUsers);
    }

    public async Task<IList<WhoKnowsObjectWithUser>> GetGlobalUsersForAlbum(NetCord.Gateway.Guild guild, int albumId)
    {
        const string sql = "SELECT * " +
                           "FROM (SELECT DISTINCT ON(UPPER(u.user_name_last_fm)) " +
                           "ub.user_id, " +
                           "ub.playcount, " +
                           "u.user_name_last_fm, " +
                           "u.discord_user_id, " +
                           "u.registered_last_fm, " +
                           "u.privacy_level, " +
                           "u.last_used " +
                           "FROM user_albums AS ub " +
                           "FULL OUTER JOIN users AS u ON ub.user_id = u.user_id " +
                           "WHERE ub.album_id = @albumId " +
                           "ORDER BY UPPER(u.user_name_last_fm) DESC, ub.playcount DESC) ub " +
                           "ORDER BY last_used DESC ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var userAlbums = (await connection.QueryAsync<WhoKnowsGlobalAlbumDto>(sql, new
        {
            albumId
        })).ToList();

        var whoKnowsAlbumList = new List<WhoKnowsObjectWithUser>();

        for (var i = 0; i < userAlbums.Count; i++)
        {
            var userAlbum = userAlbums[i];

            var userName = userAlbum.UserNameLastFm;

            if (i < 15)
            {
                if (guild != null && guild.Users.TryGetValue(userAlbum.DiscordUserId, out var discordUser))
                {
                    userName = discordUser.GetDisplayName();
                }
            }

            whoKnowsAlbumList.Add(new WhoKnowsObjectWithUser
            {
                DiscordName = userName,
                Playcount = userAlbum.Playcount,
                LastFMUsername = userAlbum.UserNameLastFm,
                UserId = userAlbum.UserId,
                RegisteredLastFm = userAlbum.RegisteredLastFm,
                PrivacyLevel = userAlbum.PrivacyLevel
            });
        }

        return whoKnowsAlbumList;
    }

    public async Task<IList<WhoKnowsObjectWithUser>> GetFriendUsersForAlbum(NetCord.Gateway.Guild discordGuild,
        IDictionary<int, FullGuildUser> guildUsers, int guildId, int userId, int albumId)
    {
        const string sql = "SELECT ub.user_id, " +
                           "ub.playcount, " +
                           "u.user_name_last_fm " +
                           "FROM user_albums AS ub " +
                           "FULL OUTER JOIN users AS u ON ub.user_id = u.user_id " +
                           "INNER JOIN friends AS fr ON fr.friend_user_id = ub.user_id " +
                           "LEFT JOIN guild_users AS gu ON gu.user_id = u.user_id AND gu.guild_id = @guildId " +
                           "WHERE fr.user_id = @userId AND ub.album_id = @albumId " +
                           "ORDER BY ub.playcount DESC ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var userArtists = (await connection.QueryAsync<WhoKnowsAlbumDto>(sql, new
        {
            albumId,
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
                UserId = userArtist.UserId
            });
        }

        return whoKnowsArtistList;
    }

    public async Task<int?> GetAlbumPlayCountForUser(int albumId, int userId)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await AlbumRepository.GetAlbumPlayCountForUser(connection, albumId, userId);
    }

    public async Task<ICollection<GuildAlbum>> GetTopAllTimeAlbumsForGuild(int guildId,
        OrderType orderType, string artistName, int[] userIds = null)
    {
        var cacheKey = $"guild-alltime-top-albums-{guildId}-{orderType}-{artistName}";

        if (userIds == null && this._cache.TryGetValue(cacheKey, out ICollection<GuildAlbum> cachedAlbums))
        {
            return cachedAlbums;
        }

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var results =
            await WhoKnowsRepository.GetTopAllTimeAlbumsForGuild(guildId, orderType, artistName, userIds, connection);

        if (userIds == null)
        {
            this._cache.Set(cacheKey, results, TimeSpan.FromMinutes(10));
        }

        return results;
    }
}
