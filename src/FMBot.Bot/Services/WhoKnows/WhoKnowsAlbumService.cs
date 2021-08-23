using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Discord.Commands;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Bot.Services.WhoKnows
{
    public class WhoKnowsAlbumService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly BotSettings _botSettings;

        public WhoKnowsAlbumService(IDbContextFactory<FMBotDbContext> contextFactory, IOptions<BotSettings> botSettings)
        {
            this._contextFactory = contextFactory;
            this._botSettings = botSettings.Value;
        }

        public async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForAlbum(ICommandContext context, int guildId, string artistName, string albumName)
        {
            const string sql = "SELECT ub.user_id, " +
                               "ub.name, " +
                               "ub.artist_name, " +
                               "ub.playcount," +
                               "u.user_name_last_fm, " +
                               "u.discord_user_id, " +
                               "gu.user_name, " +
                               "gu.who_knows_whitelisted " +
                               "FROM user_albums AS ub " +
                               "INNER JOIN users AS u ON ub.user_id = u.user_id " +
                               "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                               "WHERE gu.guild_id = @guildId AND UPPER(ub.name) = UPPER(CAST(@albumName AS CITEXT)) AND UPPER(ub.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                               "ORDER BY ub.playcount DESC ";

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
                    WhoKnowsWhitelisted = userAlbum.WhoKnowsWhitelisted,
                });
            }

            return whoKnowsAlbumList;
        }

        public async Task<IList<WhoKnowsObjectWithUser>> GetGlobalUsersForAlbum(ICommandContext context, string artistName, string albumName)
        {
            const string sql = "SELECT * " +
                               "FROM (SELECT DISTINCT ON(UPPER(u.user_name_last_fm)) " +
                               "ub.user_id, " +
                               "ub.name, " +
                               "ub.artist_name, " +
                               "ub.playcount, " +
                               "u.user_name_last_fm, " +
                               "u.discord_user_id, " +
                               "u.registered_last_fm, " +
                               "u.privacy_level " +
                               "FROM user_albums AS ub " +
                               "INNER JOIN users AS u ON ub.user_id = u.user_id " +
                               "WHERE UPPER(ub.name) = UPPER(CAST(@albumName AS CITEXT)) AND UPPER(ub.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                               "ORDER BY UPPER(u.user_name_last_fm) DESC) ub " +
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
                    RegisteredLastFm = userAlbum.RegisteredLastFm,
                    PrivacyLevel = userAlbum.PrivacyLevel
                });
            }

            return whoKnowsAlbumList;
        }

        public async Task<IList<WhoKnowsObjectWithUser>> GetFriendUsersForAlbum(ICommandContext context, int guildId, int userId, string artistName, string albumName)
        {
            const string sql = "SELECT ub.user_id, " +
                               "ub.name, " +
                               "ub.artist_name, " +
                               "ub.playcount," +
                               "u.user_name_last_fm, " +
                               "u.discord_user_id, " +
                               "gu.user_name, " +
                               "gu.who_knows_whitelisted " +
                               "FROM user_albums AS ub " +
                               "INNER JOIN users AS u ON ub.user_id = u.user_id " +
                               "INNER JOIN friends AS fr ON fr.friend_user_id = u.user_id " +
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

        public async Task<int?> GetAlbumPlayCountForUser(string artistName, string albumName, int userId)
        {
            const string sql = "SELECT ua.playcount " +
                               "FROM user_albums AS ua " +
                               "WHERE ua.user_id = @userId AND " +
                               "UPPER(ua.name) = UPPER(CAST(@albumName AS CITEXT)) AND " +
                               "UPPER(ua.artist_name) = UPPER(CAST(@artistName AS CITEXT))";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            return await connection.QuerySingleOrDefaultAsync<int?>(sql, new
            {
                userId,
                albumName,
                artistName
            });
        }

        public async Task<IReadOnlyList<ListAlbum>> GetTopAllTimeAlbumsForGuild(int guildId,
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
                      "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                      "GROUP BY ub.name, ub.artist_name ";

            sql += orderType == OrderType.Playcount ?
                "ORDER BY total_playcount DESC, listener_count DESC " :
                "ORDER BY listener_count DESC, total_playcount DESC ";

            sql += "LIMIT 14";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
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
