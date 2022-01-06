using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Bot.Services.WhoKnows
{
    public class WhoKnowsTrackService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly BotSettings _botSettings;

        public WhoKnowsTrackService(IDbContextFactory<FMBotDbContext> contextFactory, IOptions<BotSettings> botSettings)
        {
            this._contextFactory = contextFactory;
            this._botSettings = botSettings.Value;
        }

        public async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForTrack(ICommandContext context, int guildId, string artistName, string trackName)
        {
            const string sql = "SELECT ut.user_id, " +
                               "ut.name, " +
                               "ut.artist_name, " +
                               "ut.playcount," +
                               "u.user_name_last_fm, " +
                               "u.discord_user_id, " +
                               "u.last_used, " +
                               "gu.user_name, " +
                               "gu.who_knows_whitelisted " +
                               "FROM user_tracks AS ut " +
                               "INNER JOIN users AS u ON ut.user_id = u.user_id " +
                               "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                               "WHERE gu.guild_id = @guildId AND UPPER(ut.name) = UPPER(CAST(@trackName AS CITEXT)) AND UPPER(ut.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                               "ORDER BY ut.playcount DESC";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
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
                    LastUsed = userTrack.LastUsed,
                    WhoKnowsWhitelisted = userTrack.WhoKnowsWhitelisted,
                });
            }

            return whoKnowsTrackList;
        }

        public async Task<IList<WhoKnowsObjectWithUser>> GetGlobalUsersForTrack(ICommandContext context, string artistName, string trackName)
        {
            const string sql = "SELECT * " +
                               "FROM(SELECT DISTINCT ON(UPPER(u.user_name_last_fm)) " +
                               "ut.user_id, " +
                               "ut.name, " +
                               "ut.artist_name, " +
                               "ut.playcount," +
                               "u.user_name_last_fm, " +
                               "u.discord_user_id, " +
                               "u.registered_last_fm, " +
                               "u.privacy_level " +
                               "FROM user_tracks AS ut " +
                               "INNER JOIN users AS u ON ut.user_id = u.user_id " +
                               "WHERE UPPER(ut.name) = UPPER(CAST(@trackName AS CITEXT)) AND UPPER(ut.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                               "ORDER BY UPPER(u.user_name_last_fm) DESC) ut " +
                               "ORDER BY playcount DESC";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
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
                    if (context.Guild != null)
                    {
                        var discordUser = await context.Guild.GetUserAsync(userTrack.DiscordUserId);
                        if (discordUser != null)
                        {
                            userName = discordUser.Nickname ?? discordUser.Username;
                        }
                    }
                }

                whoKnowsTrackList.Add(new WhoKnowsObjectWithUser
                {
                    Name = $"{userTrack.ArtistName} - {userTrack.Name}",
                    DiscordName = userName,
                    Playcount = userTrack.Playcount,
                    LastFMUsername = userTrack.UserNameLastFm,
                    UserId = userTrack.UserId,
                    RegisteredLastFm = userTrack.RegisteredLastFm,
                    PrivacyLevel = userTrack.PrivacyLevel,
                });
            }

            return whoKnowsTrackList;
        }

        public async Task<IList<WhoKnowsObjectWithUser>> GetFriendUsersForTrack(ICommandContext context, int guildId, int userId, string artistName, string trackName)
        {
            const string sql = "SELECT ut.user_id, " +
                               "ut.name, " +
                               "ut.artist_name, " +
                               "ut.playcount," +
                               "u.user_name_last_fm, " +
                               "u.discord_user_id, " +
                               "gu.user_name, " +
                               "gu.who_knows_whitelisted " +
                               "FROM user_tracks AS ut " +
                               "INNER JOIN users AS u ON ut.user_id = u.user_id " +
                               "INNER JOIN friends AS fr ON fr.friend_user_id = u.user_id " +
                               "LEFT JOIN guild_users AS gu ON gu.user_id = u.user_id AND gu.guild_id = @guildId " +
                               "WHERE fr.user_id = @userId AND " +
                               "UPPER(ut.name) = UPPER(CAST(@trackName AS CITEXT)) AND UPPER(ut.artist_name) = UPPER(CAST(@artistName AS CITEXT)) " +
                               "ORDER BY ut.playcount DESC ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var userArtists = (await connection.QueryAsync<WhoKnowsTrackDto>(sql, new
            {
                artistName,
                trackName,
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

        public async Task<int?> GetTrackPlayCountForUser(string artistName, string trackName, int userId)
        {
            const string sql = "SELECT ut.playcount " +
                               "FROM user_tracks AS ut " +
                               "WHERE ut.user_id = @userId AND " +
                               "UPPER(ut.name) = UPPER(CAST(@trackName AS CITEXT)) AND " +
                               "UPPER(ut.artist_name) = UPPER(CAST(@artistName AS CITEXT))";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            return await connection.QuerySingleOrDefaultAsync<int?>(sql, new
            {
                userId,
                trackName,
                artistName
            });
        }

        public async Task<ICollection<GuildTrack>> GetTopAllTimeTracksForGuild(int guildId,
            OrderType orderType, string artistName)
        {
            var dbArgs = new DynamicParameters();
            dbArgs.Add("guildId", guildId);

            var sql = "SELECT ut.name AS track_name, ut.artist_name, " +
                      "SUM(ut.playcount) AS total_playcount, " +
                      "COUNT(ut.user_id) AS listener_count " +
                      "FROM user_tracks AS ut   " +
                      "INNER JOIN users AS u ON ut.user_id = u.user_id   " +
                      "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id  " +
                      "WHERE gu.guild_id = @guildId  AND gu.bot != true " +
                      "AND NOT ut.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                      "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) ";

            if (!string.IsNullOrWhiteSpace(artistName))
            {
                sql += "AND UPPER(ut.artist_name) = UPPER(CAST(@artistName AS CITEXT)) ";
                dbArgs.Add("artistName", artistName);
            }

            sql += "GROUP BY ut.name, ut.artist_name ";

            sql += orderType == OrderType.Playcount ?
                "ORDER BY total_playcount DESC, listener_count DESC " :
                "ORDER BY listener_count DESC, total_playcount DESC ";

            sql += "LIMIT 120";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            return (await connection.QueryAsync<GuildTrack>(sql, dbArgs)).ToList();
        }

        public async Task<int> GetWeekTrackPlaycountForGuildAsync(IEnumerable<User> guildUsers, string trackName, string artistName)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-7);

            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = await this._contextFactory.CreateDbContextAsync();
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
