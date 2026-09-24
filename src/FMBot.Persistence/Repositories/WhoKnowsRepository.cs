using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Npgsql;
using System;

namespace FMBot.Persistence.Repositories;

public static class WhoKnowsRepository
{
    public static async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForArtist(
        IDictionary<int, FullGuildUser> guildUsers, int guildId, string artistName, NpgsqlConnection connection)
    {
        const string sql = "BEGIN; " +
                           "SET LOCAL enable_nestloop = OFF; " +
                           "SELECT ua.user_id, " +
                           "ua.playcount " +
                           "FROM user_artists AS ua " +
                           "WHERE UPPER(ua.name) = UPPER(CAST(@artistName AS CITEXT)) " +
                           "AND ua.user_id = ANY(SELECT user_id FROM guild_users WHERE guild_id = @guildId) " +
                           "ORDER BY ua.playcount DESC; " +
                           "COMMIT; ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var userArtists = (await connection.QueryAsync<WhoKnowsArtistDto>(sql, new
        {
            guildId,
            artistName
        })).ToList();

        return ToWhoKnowsObjects(guildUsers, userArtists.Select(s => (s.UserId, s.Playcount)));
    }

    public static async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForAlbum(
        IDictionary<int, FullGuildUser> guildUsers, int guildId, int albumId, NpgsqlConnection connection)
    {
        const string sql = "BEGIN; " +
                           "SET LOCAL enable_nestloop = OFF; " +
                           "SELECT ub.user_id, " +
                           "ub.playcount " +
                           "FROM user_albums AS ub " +
                           "WHERE ub.album_id = @albumId " +
                           "AND ub.user_id = ANY(SELECT user_id FROM guild_users WHERE guild_id = @guildId) " +
                           "ORDER BY ub.playcount DESC; " +
                           "COMMIT; ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var userAlbums = (await connection.QueryAsync<WhoKnowsAlbumDto>(sql, new
        {
            guildId,
            albumId
        })).ToList();

        return ToWhoKnowsObjects(guildUsers, userAlbums.Select(s => (s.UserId, s.Playcount)));
    }

    public static async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForTrack(
        IDictionary<int, FullGuildUser> guildUsers, int guildId, int trackId, NpgsqlConnection connection)
    {
        const string sql = "SELECT ut.user_id, " +
                           "ut.playcount " +
                           "FROM user_tracks AS ut " +
                           "WHERE ut.track_id = @trackId " +
                           "AND ut.user_id = ANY(SELECT user_id FROM guild_users WHERE guild_id = @guildId) " +
                           "ORDER BY ut.playcount DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var userTracks = (await connection.QueryAsync<WhoKnowsTrackDto>(sql, new
        {
            guildId,
            trackId
        })).ToList();

        return ToWhoKnowsObjects(guildUsers, userTracks.Select(s => (s.UserId, s.Playcount)));
    }

    public static async Task<ICollection<GuildArtist>> GetTopAllTimeArtistsForGuild(int guildId,
        OrderType orderType, int? limit, int[] userIds, NpgsqlConnection connection)
    {
        var userFilter = userIds != null
            ? "AND ua.user_id = ANY(@userIds) "
            : "";

        var orderBy = orderType == OrderType.Playcount ?
            "ORDER BY total_playcount DESC, listener_count DESC " :
            "ORDER BY listener_count DESC, total_playcount DESC ";

        var sql = "SELECT agg.artist_name, " +
                  "agg.total_playcount, " +
                  "agg.listener_count, " +
                  "a.id AS artist_id " +
                  "FROM ( " +
                  "SELECT ua.name AS artist_name, " +
                  "SUM(ua.playcount) AS total_playcount, " +
                  "COUNT(ua.user_id) AS listener_count " +
                  "FROM user_artists AS ua   " +
                  "INNER JOIN guild_users AS gu ON gu.user_id = ua.user_id  " +
                  "WHERE gu.guild_id = @guildId  AND gu.bot != true " +
                  userFilter +
                  "AND NOT ua.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                  "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                  "GROUP BY ua.name " +
                  orderBy;

        if (limit.HasValue)
        {
            sql += $"LIMIT {limit} ";
        }

        sql += ") agg " +
               "LEFT JOIN artists AS a ON a.name = agg.artist_name " +
               orderBy;

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<GuildArtist>(sql, new
        {
            guildId,
            userIds
        })).ToList();
    }

    public static async Task<IEnumerable<UserArtist>> GetGuildUserArtistsWithGenres(int guildId, int minPlaycount,
        NpgsqlConnection connection)
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

        return await connection.QueryAsync<UserArtist>(sql, new
        {
            guildId,
            minPlaycount
        });
    }

    public static async Task<ICollection<GuildAlbum>> GetTopAllTimeAlbumsForGuild(int guildId,
        OrderType orderType, string artistName, int[] userIds, NpgsqlConnection connection)
    {
        var dbArgs = new DynamicParameters();
        dbArgs.Add("guildId", guildId);

        var orderColumn = orderType == OrderType.Playcount ? "total_playcount" : "listener_count";
        var thenByColumn = orderType == OrderType.Playcount ? "listener_count" : "total_playcount";

        var artistFilter = "";
        if (!string.IsNullOrWhiteSpace(artistName))
        {
            artistFilter = "AND ub.album_id = ANY(SELECT id FROM albums WHERE UPPER(artist_name) = UPPER(CAST(@artistName AS CITEXT))) ";
            dbArgs.Add("artistName", artistName);
        }

        var userFilter = "";
        if (userIds != null)
        {
            userFilter = "AND ub.user_id = ANY(@userIds) ";
            dbArgs.Add("userIds", userIds);
        }

        var sql = "SELECT a.name AS album_name, a.artist_name, " +
                  "agg.album_id, " +
                  "agg.total_playcount, agg.listener_count " +
                  "FROM ( " +
                  "    SELECT ub.album_id, " +
                  "           SUM(ub.playcount) AS total_playcount, " +
                  "           COUNT(ub.user_id) AS listener_count " +
                  "    FROM user_albums AS ub " +
                  "    INNER JOIN guild_users AS gu ON gu.user_id = ub.user_id " +
                  "    WHERE gu.guild_id = @guildId AND gu.bot != true " +
                  "    AND ub.album_id IS NOT NULL " +
                  $"    {artistFilter}" +
                  $"    {userFilter}" +
                  "    AND NOT ub.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                  "    AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                  "    GROUP BY ub.album_id " +
                  $"    ORDER BY {orderColumn} DESC, {thenByColumn} DESC " +
                  "    LIMIT 120 " +
                  ") agg " +
                  "INNER JOIN albums a ON a.id = agg.album_id " +
                  $"ORDER BY agg.{orderColumn} DESC, agg.{thenByColumn} DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<GuildAlbum>(sql, dbArgs)).ToList();
    }

    public static async Task<ICollection<GuildTrack>> GetTopAllTimeTracksForGuild(int guildId,
        OrderType orderType, string artistName, int[] userIds, NpgsqlConnection connection)
    {
        var dbArgs = new DynamicParameters();
        dbArgs.Add("guildId", guildId);

        var orderColumn = orderType == OrderType.Playcount ? "total_playcount" : "listener_count";
        var thenByColumn = orderType == OrderType.Playcount ? "listener_count" : "total_playcount";

        var artistFilter = "";
        if (!string.IsNullOrWhiteSpace(artistName))
        {
            artistFilter = "AND ut.track_id = ANY(SELECT id FROM tracks WHERE UPPER(artist_name) = UPPER(CAST(@artistName AS CITEXT))) ";
            dbArgs.Add("artistName", artistName);
        }

        var userFilter = "";
        if (userIds != null)
        {
            userFilter = "AND ut.user_id = ANY(@userIds) ";
            dbArgs.Add("userIds", userIds);
        }

        var sql = "SELECT t.name AS track_name, t.artist_name, " +
                  "agg.track_id, " +
                  "agg.total_playcount, agg.listener_count " +
                  "FROM ( " +
                  "    SELECT ut.track_id, " +
                  "           SUM(ut.playcount) AS total_playcount, " +
                  "           COUNT(ut.user_id) AS listener_count " +
                  "    FROM user_tracks AS ut " +
                  "    INNER JOIN guild_users AS gu ON gu.user_id = ut.user_id " +
                  "    WHERE gu.guild_id = @guildId AND gu.bot != true " +
                  "    AND ut.track_id IS NOT NULL " +
                  $"    {artistFilter}" +
                  $"    {userFilter}" +
                  "    AND NOT ut.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                  "    AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                  "    GROUP BY ut.track_id " +
                  $"    ORDER BY {orderColumn} DESC, {thenByColumn} DESC " +
                  "    LIMIT 120 " +
                  ") agg " +
                  "INNER JOIN tracks t ON t.id = agg.track_id " +
                  $"ORDER BY agg.{orderColumn} DESC, agg.{thenByColumn} DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<GuildTrack>(sql, dbArgs)).ToList();
    }

    public static async Task<ICollection<AffinityItemDto>> GetAllTimeTopArtistForGuild(int guildId, bool largeGuild,
        NpgsqlConnection connection)
    {
        var amount = largeGuild ? 125 : 300;

        var sql = "SELECT * " +
                  "FROM ( " +
                  "SELECT ua.user_id, name, playcount, " +
                  "ROW_NUMBER() OVER (PARTITION BY ua.user_id ORDER BY playcount DESC) as position " +
                  "FROM public.user_artists AS ua  " +
                  "INNER JOIN guild_users AS gu ON gu.user_id = ua.user_id  " +
                  "WHERE gu.guild_id = @guildId " +
                  ") as subquery " +
                  $"WHERE position <= {amount}; ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<AffinityItemDto>(sql, new
        {
            guildId
        })).ToList();
    }

    public static async Task<ICollection<AffinityItemDto>> GetQuarterlyTopArtistForGuild(int guildId, bool largeGuild,
        NpgsqlConnection connection)
    {
        var amount = largeGuild ? 50 : 120;
        var amountOfDays = largeGuild ? 20 : 90;

        var sql = "SELECT * " +
                  "FROM ( " +
                  "SELECT up.user_id, artist_name AS name, COUNT(*) as playcount, " +
                  " ROW_NUMBER() OVER (PARTITION BY up.user_id ORDER BY COUNT(*) DESC) as position " +
                  "FROM user_plays AS up " +
                  "INNER JOIN guild_users AS gu ON gu.user_id = up.user_id  " +
                  $"WHERE gu.guild_id = @guildId AND time_played > current_date - interval '{amountOfDays}' day AND artist_name IS NOT NULL " +
                  "GROUP BY up.user_id, artist_name " +
                  ") as subquery " +
                  $"WHERE position <= {amount}; ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<AffinityItemDto>(sql, new
        {
            guildId
        })).ToList();
    }

    private static IList<WhoKnowsObjectWithUser> ToWhoKnowsObjects(IDictionary<int, FullGuildUser> guildUsers,
        IEnumerable<(int UserId, int Playcount)> rows)
    {
        var whoKnowsList = new List<WhoKnowsObjectWithUser>();

        foreach (var row in rows)
        {
            if (!guildUsers.TryGetValue(row.UserId, out var guildUser))
            {
                continue;
            }

            whoKnowsList.Add(new WhoKnowsObjectWithUser
            {
                DiscordName = guildUser.UserName ?? guildUser.UserNameLastFM,
                Playcount = row.Playcount,
                LastFMUsername = guildUser.UserNameLastFM,
                UserId = guildUser.UserId,
                LastUsed = guildUser.LastUsed,
                LastMessage = guildUser.LastMessage,
                Roles = guildUser.Roles
            });
        }

        return whoKnowsList;
    }


    public static async Task<IList<WhoKnowsObjectWithUser>> GetGuildPeriodPlaycounts(
        IDictionary<int, FullGuildUser> guildUsers, int guildId, string artistName, string albumName, string trackName,
        DateTime since, int memberCap, NpgsqlConnection connection)
    {
        var nameFilter = "";
        if (albumName != null)
        {
            nameFilter = "AND up.album_name = CAST(@name AS CITEXT) ";
        }
        else if (trackName != null)
        {
            nameFilter = "AND up.track_name = CAST(@name AS CITEXT) ";
        }

        var sql = "WITH members AS ( " +
                  "  SELECT gu.user_id FROM guild_users gu INNER JOIN users u ON u.user_id = gu.user_id " +
                  "  WHERE gu.guild_id = @guildId AND gu.bot IS NOT TRUE " +
                  "  AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                  "  AND NOT EXISTS (SELECT 1 FROM guild_blocked_users gbu WHERE gbu.guild_id = gu.guild_id AND gbu.user_id = gu.user_id AND gbu.blocked_from_who_knows) " +
                  "  AND u.last_used > now() - interval '90 days' " +
                  "  ORDER BY u.last_used DESC NULLS LAST LIMIT @memberCap " +
                  ") " +
                  "SELECT up.user_id, COUNT(*) AS playcount " +
                  "FROM members m INNER JOIN user_plays up ON up.user_id = m.user_id AND up.time_played > @since " +
                  "WHERE up.artist_name = CAST(@artistName AS CITEXT) " + nameFilter +
                  "GROUP BY up.user_id ORDER BY playcount DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var rows = (await connection.QueryAsync<WhoKnowsArtistDto>(sql, new
        {
            guildId,
            memberCap,
            since,
            artistName,
            name = albumName ?? trackName
        }, commandTimeout: 60)).ToList();

        return ToWhoKnowsObjects(guildUsers, rows.Select(s => (s.UserId, s.Playcount)));
    }

    public static async Task<List<EntityPlaycount>> GetUserArtistPlaycounts(int userId, string[] artistNames,
        NpgsqlConnection connection)
    {
        const string sql = "SELECT name AS artist_name, name, playcount FROM user_artists " +
                           "WHERE user_id = @userId AND name = ANY(@artistNames::citext[])";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<EntityPlaycount>(sql, new { userId, artistNames })).ToList();
    }

    public static async Task<List<EntityPlaycount>> GetUserAlbumPlaycounts(int userId, string[] artistNames,
        string[] albumNames, NpgsqlConnection connection)
    {
        const string sql = "SELECT ub.artist_name, ub.name, ub.playcount FROM user_albums ub " +
                           "INNER JOIN unnest(@artistNames::citext[], @albumNames::citext[]) AS k(artist_name, name) " +
                           "ON k.artist_name = ub.artist_name AND k.name = ub.name " +
                           "WHERE ub.user_id = @userId";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<EntityPlaycount>(sql, new { userId, artistNames, albumNames })).ToList();
    }

    public static async Task<List<EntityPlaycount>> GetUserTrackPlaycounts(int userId, string[] artistNames,
        string[] trackNames, NpgsqlConnection connection)
    {
        const string sql = "SELECT ut.artist_name, ut.name, ut.playcount FROM user_tracks ut " +
                           "INNER JOIN unnest(@artistNames::citext[], @trackNames::citext[]) AS k(artist_name, name) " +
                           "ON k.artist_name = ut.artist_name AND k.name = ut.name " +
                           "WHERE ut.user_id = @userId";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<EntityPlaycount>(sql, new { userId, artistNames, trackNames })).ToList();
    }
}
