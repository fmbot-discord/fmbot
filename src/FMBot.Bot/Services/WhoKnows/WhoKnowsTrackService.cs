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
    public class WhoKnowsTrackService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        public WhoKnowsTrackService(IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._contextFactory = contextFactory;
        }

        public async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForTrack(ICommandContext context, int guildId, string artistName, string trackName)
        {
            const string sql = "SELECT ut.user_id, " +
                               "ut.name, " +
                               "ut.artist_name, " +
                               "ut.playcount," +
                               "u.user_name_last_fm, " +
                               "u.discord_user_id, " +
                               "gu.user_name " +
                               "FROM user_tracks AS ut " +
                               "INNER JOIN users AS u ON ut.user_id = u.user_id " +
                               "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                               "WHERE gu.guild_id = @guildId AND UPPER(ut.name) = UPPER(CAST(@trackName AS CITEXT)) AND UPPER(ut.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                               "ORDER BY ut.playcount DESC";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(ConfigData.Data.Database.ConnectionString);
            await connection.OpenAsync();

            var userTracks = (await connection.QueryAsync<WhoKnowsTrackDto>(sql, new
            {
                guildId,
                trackName,
                artistName
            })).ToList();

            var whoKnowsTrackList = new List<WhoKnowsObjectWithUser>();

            for (var i = 0; i < userTracks.Count; i++)
            {
                var userTrack = userTracks[i];

                var userName = userTrack.UserName ?? userTrack.UserNameLastFm;

                if (i < 15)
                {
                    var discordUser = await context.Guild.GetUserAsync(userTrack.DiscordUserId);
                    if (discordUser != null)
                    {
                        userName = discordUser.Nickname ?? discordUser.Username;
                    }
                }

                whoKnowsTrackList.Add(new WhoKnowsObjectWithUser
                {
                    Name = $"{userTrack.ArtistName} - {userTrack.Name}",
                    DiscordName = userName,
                    Playcount = userTrack.Playcount,
                    LastFMUsername = userTrack.UserNameLastFm,
                    UserId = userTrack.UserId,
                });
            }

            return whoKnowsTrackList;
        }

        public async Task<IList<WhoKnowsObjectWithUser>> GetGlobalUsersForTrack(ICommandContext context, string artistName, string trackName)
        {
            const string sql = "SELECT ut.user_id, " +
                               "ut.name, " +
                               "ut.artist_name, " +
                               "ut.playcount," +
                               "u.user_name_last_fm, " +
                               "u.discord_user_id, " +
                               "u.privacy_level " +
                               "FROM user_tracks AS ut " +
                               "INNER JOIN users AS u ON ut.user_id = u.user_id " +
                               "WHERE UPPER(ut.name) = UPPER(CAST(@trackName AS CITEXT)) AND UPPER(ut.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                               "ORDER BY ut.playcount DESC";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(ConfigData.Data.Database.ConnectionString);
            await connection.OpenAsync();

            var userTracks = (await connection.QueryAsync<WhoKnowsGlobalTrackDto>(sql, new
            {
                trackName,
                artistName
            })).ToList();

            var whoKnowsTrackList = new List<WhoKnowsObjectWithUser>();

            for (var i = 0; i < userTracks.Count; i++)
            {
                var userTrack = userTracks[i];

                var userName = userTrack.UserNameLastFm;

                if (i < 15)
                {
                    var discordUser = await context.Guild.GetUserAsync(userTrack.DiscordUserId);
                    if (discordUser != null)
                    {
                        userName = discordUser.Nickname ?? discordUser.Username;
                    }
                }

                whoKnowsTrackList.Add(new WhoKnowsObjectWithUser
                {
                    Name = $"{userTrack.ArtistName} - {userTrack.Name}",
                    DiscordName = userName,
                    Playcount = userTrack.Playcount,
                    LastFMUsername = userTrack.UserNameLastFm,
                    UserId = userTrack.UserId,
                    PrivacyLevel = userTrack.PrivacyLevel
                });
            }

            return whoKnowsTrackList;
        }

        public async Task<int?> GetTrackPlayCountForUser(string artistName, string trackName, int userId)
        {
            const string sql = "SELECT ut.playcount " +
                               "FROM user_tracks AS ut " +
                               "WHERE ut.user_id = @userId AND " +
                               "UPPER(ut.name) = UPPER(CAST(@trackName AS CITEXT)) AND " +
                               "UPPER(ut.artist_name) = UPPER(CAST(@artistName AS CITEXT))";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(ConfigData.Data.Database.ConnectionString);
            await connection.OpenAsync();

            return await connection.QuerySingleOrDefaultAsync<int?>(sql, new
            {
                userId,
                trackName,
                artistName
            });
        }

        public async Task<IReadOnlyList<ListTrack>> GetTopTracksForGuild(IReadOnlyList<User> guildUsers,
            OrderType orderType)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = this._contextFactory.CreateDbContext();
            var query = db.UserTracks
                .AsQueryable()
                .Where(w => userIds.Contains(w.UserId))
                .GroupBy(g => new { g.ArtistName, g.Name });

            query = orderType == OrderType.Playcount ?
                query.OrderByDescending(o => o.Sum(s => s.Playcount)).ThenByDescending(o => o.Count()) :
                query.OrderByDescending(o => o.Count()).ThenByDescending(o => o.Sum(s => s.Playcount));

            return await query
                .Take(14)
                .Select(s => new ListTrack
                {
                    ArtistName = s.Key.ArtistName,
                    TrackName = s.Key.Name,
                    Playcount = s.Sum(su => su.Playcount),
                    ListenerCount = s.Count()
                })
                .ToListAsync();
        }

        public async Task<int> GetWeekTrackPlaycountForGuildAsync(IEnumerable<User> guildUsers, string trackName, string artistName)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-7);

            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserPlays
                .AsQueryable()
                .CountAsync(t =>
                    userIds.Contains(t.UserId) &&
                    t.TimePlayed.Date <= now.Date &&
                    t.TimePlayed.Date > minDate.Date &&
                    t.TrackName.ToLower() == trackName.ToLower() &&
                    t.ArtistName.ToLower() == artistName.ToLower()
                    );
        }
    }
}
