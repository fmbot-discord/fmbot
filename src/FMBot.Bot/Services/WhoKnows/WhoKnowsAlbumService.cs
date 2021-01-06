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

        public async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForAlbum(ICommandContext context,
            ICollection<GuildUser> guildUsers, int guildId, string artistName, string albumName)
        {
            const string sql = "SELECT ut.user_id, " +
                               "ut.name, " +
                               "ut.artist_name, " +
                               "ut.playcount," +
                               " u.user_name_last_fm, " +
                               "u.discord_user_id " +
                               "FROM user_albums AS ut " +
                               "INNER JOIN users AS u ON ut.user_id = u.user_id " +
                               "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                               "WHERE gu.guild_id = @guildId AND UPPER(ut.name) = UPPER(CAST(@albumName AS CITEXT)) AND UPPER(ut.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                               "ORDER BY ut.playcount DESC " +
                               "LIMIT 14";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(ConfigData.Data.Database.ConnectionString);
            await connection.OpenAsync();

            var userAlbums = await connection.QueryAsync<WhoKnowsAlbumDto>(sql, new
            {
                guildId,
                albumName,
                artistName
            });

            var whoKnowsAlbumList = new List<WhoKnowsObjectWithUser>();

            foreach (var userAlbum in userAlbums)
            {
                var discordUser = await context.Guild.GetUserAsync(userAlbum.DiscordUserId);
                var guildUser = guildUsers.FirstOrDefault(f => f.UserId == userAlbum.UserId);
                var userName = discordUser != null ?
                    discordUser.Nickname ?? discordUser.Username :
                    guildUser?.UserName ?? userAlbum.UserNameLastFm;

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

        public async Task<IReadOnlyList<ListAlbum>> GetTopAlbumsForGuild(IReadOnlyList<User> guildUsers,
            OrderType orderType)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = this._contextFactory.CreateDbContext();
            var query = db.UserAlbums
                .AsQueryable()
                .Where(w => userIds.Contains(w.UserId))
                .GroupBy(g => new { g.ArtistName, g.Name });

            query = orderType == OrderType.Playcount ?
                query.OrderByDescending(o => o.Sum(s => s.Playcount)).ThenByDescending(o => o.Count()) :
                query.OrderByDescending(o => o.Count()).ThenByDescending(o => o.Sum(s => s.Playcount));

            return await query
                .Take(14)
                .Select(s => new ListAlbum
                {
                    ArtistName = s.Key.ArtistName,
                    AlbumName = s.Key.Name,
                    Playcount = s.Sum(su => su.Playcount),
                    ListenerCount = s.Count()
                })
                .ToListAsync();
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
