using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Bot.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FMBot.Bot.Services.WhoKnows
{
    public class WhoKnowsAlbumService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        public WhoKnowsAlbumService(IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._contextFactory = contextFactory;
        }

        public async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForAlbum(ICommandContext context, int guildId, string artistName, string albumName)
        {
            const string sql = "SELECT ut.user_id, " +
                               "ut.name, " +
                               "ut.artist_name, " +
                               "ut.playcount," +
                               "u.user_name_last_fm, " +
                               "u.discord_user_id, " +
                               "gu.user_name " +
                               "FROM user_albums AS ut " + 
                               "INNER JOIN users AS u ON ut.user_id = u.user_id " +
                               "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                               "WHERE gu.guild_id = @guildId AND UPPER(ut.name) = UPPER(CAST(@albumName AS CITEXT)) AND UPPER(ut.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                               "ORDER BY ut.playcount DESC ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(ConfigData.Data.Database.ConnectionString);
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

                var userName = userAlbum.UserName ?? userAlbum.UserNameLastFm;

                if (i < 15)
                {
                    var discordUser = await context.Guild.GetUserAsync(userAlbum.DiscordUserId);
                    if (discordUser != null)
                    {
                        userName = discordUser.Nickname ?? discordUser.Username;
                    }
                }

                whoKnowsAlbumList.Add(new WhoKnowsObjectWithUser
                {
                    DiscordName = userName,
                    Name = $"{userAlbum.ArtistName} - {userAlbum.Name}",
                    Playcount = userAlbum.Playcount,
                    LastFMUsername = userAlbum.UserNameLastFm,
                    UserId = userAlbum.UserId,
                });
            }

            return whoKnowsAlbumList;
        }

        public async Task<IList<WhoKnowsObjectWithUser>> GetGlobalUsersForAlbum(ICommandContext context, string artistName, string albumName)
        {
            const string sql = "SELECT ut.user_id, " +
                               "ut.name, " +
                               "ut.artist_name, " +
                               "ut.playcount," +
                               "u.user_name_last_fm, " +
                               "u.discord_user_id, " +
                               "u.privacy_level " +
                               "FROM user_albums AS ut " +
                               "INNER JOIN users AS u ON ut.user_id = u.user_id " +
                               "WHERE UPPER(ut.name) = UPPER(CAST(@albumName AS CITEXT)) AND UPPER(ut.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                               "ORDER BY ut.playcount DESC ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(ConfigData.Data.Database.ConnectionString);
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
                    var discordUser = await context.Guild.GetUserAsync(userAlbum.DiscordUserId);
                    if (discordUser != null)
                    {
                        userName = discordUser.Nickname ?? discordUser.Username;
                    }
                }

                whoKnowsAlbumList.Add(new WhoKnowsObjectWithUser
                {
                    Name = $"{userAlbum.ArtistName} - {userAlbum.Name}",
                    DiscordName = userName,
                    Playcount = userAlbum.Playcount,
                    LastFMUsername = userAlbum.UserNameLastFm,
                    UserId = userAlbum.UserId,
                    PrivacyLevel = userAlbum.PrivacyLevel
                });
            }

            return whoKnowsAlbumList;
        }

        public async Task<int?> GetAlbumPlayCountForUser(string artistName, string albumName, int userId)
        {
            const string sql = "SELECT ua.playcount " +
                               "FROM user_albums AS ua " +
                               "WHERE ua.user_id = @userId AND " +
                               "UPPER(ua.name) = UPPER(CAST(@albumName AS CITEXT)) AND " +
                               "UPPER(ua.artist_name) = UPPER(CAST(@artistName AS CITEXT))";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(ConfigData.Data.Database.ConnectionString);
            await connection.OpenAsync();

            return await connection.QuerySingleOrDefaultAsync<int?>(sql, new
            {
                userId,
                albumName,
                artistName
            });
        }

        public static async Task<IReadOnlyList<ListAlbum>> GetTopAllTimeAlbumsForGuild(int guildId,
            OrderType orderType)
        {
            var sql = "SELECT ub.name AS album_name, ub.artist_name, " +
                      "SUM(ub.playcount) AS total_playcount, " +
                      "COUNT(ub.user_id) AS listener_count " +
                      "FROM user_albums AS ub   " +
                      "INNER JOIN users AS u ON ub.user_id = u.user_id   " +
                      "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id  " +
                      "WHERE gu.guild_id = @guildId AND gu.bot != true " +
                      "AND NOT ub.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                      "GROUP BY ub.name, ub.artist_name ";

            sql += orderType == OrderType.Playcount ?
                "ORDER BY total_playcount DESC, listener_count DESC " :
                "ORDER BY listener_count DESC, total_playcount DESC ";

            sql += "LIMIT 14";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(ConfigData.Data.Database.ConnectionString);
            await connection.OpenAsync();

            return (await connection.QueryAsync<ListAlbum>(sql, new
            {
                guildId
            })).ToList();
        }

        public async Task<int> GetWeekAlbumPlaycountForGuildAsync(IEnumerable<User> guildUsers, string albumName, string artistName)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-7);

            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserPlays
                .AsQueryable()
                .CountAsync(ab =>
                                userIds.Contains(ab.UserId) &&
                                ab.TimePlayed.Date <= now.Date &&
                                ab.TimePlayed.Date > minDate.Date &&
                                ab.AlbumName.ToLower() == albumName.ToLower() &&
                                ab.ArtistName.ToLower() == artistName.ToLower());
        }
    }
}
