using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Discord;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Bot.Services.WhoKnows;

public class WhoKnowsAlbumService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly BotSettings _botSettings;

    public WhoKnowsAlbumService(IDbContextFactory<FMBotDbContext> contextFactory, IOptions<BotSettings> botSettings)
    {
        this._contextFactory = contextFactory;
        this._botSettings = botSettings.Value;
    }

    public async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForAlbum(IGuild discordGuild,
        IDictionary<int, FullGuildUser> guildUsers, int guildId, string artistName, string albumName)
    {
        const string sql = "BEGIN; " +
                           "SET LOCAL enable_nestloop = OFF; " +
                           "SELECT ub.user_id, " +
                           "ub.playcount " +
                           "FROM user_albums AS ub " +
                           "WHERE UPPER(ub.name) = UPPER(CAST(@albumName AS CITEXT)) AND UPPER(ub.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                           "AND ub.user_id = ANY(SELECT user_id FROM guild_users WHERE guild_id = @guildId) " +
                           "ORDER BY ub.playcount DESC;" +
                           "COMMIT; ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var userAlbums = (await connection.QueryAsync<WhoKnowsAlbumDto>(sql, new
        {
            guildId,
            albumName,
            artistName
        })).ToList();

        var whoKnowsAlbumList = new List<WhoKnowsObjectWithUser>();

        for (var i = 0; i < userAlbums.Count; i++)
        {
            var userAlbum = userAlbums[i];

            if (!guildUsers.TryGetValue(userAlbum.UserId, out var guildUser))
            {
                continue;
            }

            var userName = guildUser.UserName ?? guildUser.UserNameLastFM;

            if (i < 15 && discordGuild != null)
            {
                var discordGuildUser = await discordGuild.GetUserAsync(guildUser.DiscordUserId, CacheMode.CacheOnly);
                if (discordGuildUser != null)
                {
                    userName = discordGuildUser.DisplayName;
                }
            }

            whoKnowsAlbumList.Add(new WhoKnowsObjectWithUser
            {
                DiscordName = userName,
                Playcount = userAlbum.Playcount,
                LastFMUsername = guildUser.UserNameLastFM,
                UserId = userAlbum.UserId,
                Roles = guildUser.Roles,
                LastUsed = guildUser.LastUsed,
                LastMessage = guildUser.LastMessage
            });
        }

        return whoKnowsAlbumList;
    }

    public async Task<IList<WhoKnowsObjectWithUser>> GetGlobalUsersForAlbum(IGuild guild, string artistName, string albumName)
    {
        const string sql = "SELECT * " +
                           "FROM (SELECT DISTINCT ON(UPPER(u.user_name_last_fm)) " +
                           "ub.user_id, " +
                           "ub.playcount, " +
                           "u.user_name_last_fm, " +
                           "u.discord_user_id, " +
                           "u.registered_last_fm, " +
                           "u.privacy_level " +
                           "FROM user_albums AS ub " +
                           "FULL OUTER JOIN users AS u ON ub.user_id = u.user_id " +
                           "WHERE UPPER(ub.name) = UPPER(CAST(@albumName AS CITEXT)) AND UPPER(ub.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                           "ORDER BY UPPER(u.user_name_last_fm) DESC, ub.playcount DESC) ub " +
                           "ORDER BY playcount DESC ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var userAlbums = (await connection.QueryAsync<WhoKnowsGlobalAlbumDto>(sql, new
        {
            albumName,
            artistName
        })).ToList();

        var whoKnowsAlbumList = new List<WhoKnowsObjectWithUser>();

        for (var i = 0; i < userAlbums.Count; i++)
        {
            var userAlbum = userAlbums[i];

            var userName = userAlbum.UserNameLastFm;

            if (i < 15)
            {
                if (guild != null)
                {
                    var discordUser = await guild.GetUserAsync(userAlbum.DiscordUserId, CacheMode.CacheOnly);
                    if (discordUser != null)
                    {
                        userName = discordUser.DisplayName;
                    }
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

    public static async Task<IList<WhoKnowsObjectWithUser>> GetBasicGlobalUsersForAlbum(NpgsqlConnection connection, string artistName, string albumName)
    {
        const string sql = "SELECT * " +
                           "FROM (SELECT DISTINCT ON(UPPER(u.user_name_last_fm)) " +
                           "ub.user_id, " +
                           "ub.playcount " +
                           "FROM user_albums AS ub " +
                           "FULL OUTER JOIN users AS u ON ub.user_id = u.user_id " +
                           "WHERE UPPER(ub.name) = UPPER(CAST(@albumName AS CITEXT)) AND UPPER(ub.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                           "AND NOT UPPER(u.user_name_last_fm) = ANY(SELECT UPPER(user_name_last_fm) FROM botted_users WHERE ban_active = true) " +
                           "AND NOT UPPER(u.user_name_last_fm) = ANY(SELECT UPPER(user_name_last_fm) FROM global_filtered_users WHERE created >= NOW() - INTERVAL '3 months') " +
                           "ORDER BY UPPER(u.user_name_last_fm) DESC, ub.playcount DESC) ub " +
                           "ORDER BY playcount DESC ";

        var userAlbums = (await connection.QueryAsync<WhoKnowsGlobalAlbumDto>(sql, new
        {
            albumName,
            artistName
        })).ToList();

        return userAlbums.Select(s => new WhoKnowsObjectWithUser
        {
            UserId = s.UserId,
            Playcount = s.Playcount
        }).ToList();
    }

    public async Task<IList<WhoKnowsObjectWithUser>> GetFriendUsersForAlbum(IGuild discordGuild,
        IDictionary<int, FullGuildUser> guildUsers, int guildId, int userId, string artistName, string albumName)
    {
        const string sql = "SELECT ub.user_id, " +
                           "ub.name, " +
                           "ub.artist_name, " +
                           "ub.playcount," +
                           "u.user_name_last_fm " +
                           "FROM user_albums AS ub " +
                           "FULL OUTER JOIN users AS u ON ub.user_id = u.user_id " +
                           "INNER JOIN friends AS fr ON fr.friend_user_id = ub.user_id " +
                           "LEFT JOIN guild_users AS gu ON gu.user_id = u.user_id AND gu.guild_id = @guildId " +
                           "WHERE fr.user_id = @userId AND " +
                           "UPPER(ub.name) = UPPER(CAST(@albumName AS CITEXT)) AND UPPER(ub.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                           "ORDER BY ub.playcount DESC ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var userArtists = (await connection.QueryAsync<WhoKnowsAlbumDto>(sql, new
        {
            artistName,
            albumName,
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

                var discordGuildUser = await discordGuild.GetUserAsync(guildUser.DiscordUserId, CacheMode.CacheOnly);
                if (discordGuildUser != null)
                {
                    userName = discordGuildUser.DisplayName;
                }
            }

            whoKnowsArtistList.Add(new WhoKnowsObjectWithUser
            {
                Name = $"{albumName} by {artistName}",
                DiscordName = userName,
                Playcount = userArtist.Playcount,
                LastFMUsername = userArtist.UserNameLastFm,
                UserId = userArtist.UserId
            });
        }

        return whoKnowsArtistList;
    }

    public async Task<int?> GetAlbumPlayCountForUser(string artistName, string albumName, int userId)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await AlbumRepository.GetAlbumPlayCountForUser(connection, artistName, albumName, userId);
    }

    public async Task<ICollection<GuildAlbum>> GetTopAllTimeAlbumsForGuild(int guildId,
        OrderType orderType, string artistName)
    {
        var sql = "SELECT ub.name AS album_name, ub.artist_name, " +
                  "SUM(ub.playcount) AS total_playcount, " +
                  "COUNT(ub.user_id) AS listener_count " +
                  "FROM user_albums AS ub   " +
                  "INNER JOIN guild_users AS gu ON gu.user_id = ub.user_id  " +
                  "WHERE gu.guild_id = @guildId AND gu.bot != true " +
                  "AND NOT ub.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                  "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) ";

        if (!string.IsNullOrWhiteSpace(artistName))
        {
            sql += "AND UPPER(ub.artist_name) = UPPER(CAST(@artistName AS CITEXT)) ";
        }

        sql += "GROUP BY ub.name, ub.artist_name ";

        sql += orderType == OrderType.Playcount ?
            "ORDER BY total_playcount DESC, listener_count DESC " :
            "ORDER BY listener_count DESC, total_playcount DESC ";

        sql += "LIMIT 120";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return (await connection.QueryAsync<GuildAlbum>(sql, new
        {
            guildId,
            artistName
        })).ToList();
    }
}
