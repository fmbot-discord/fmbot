using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Models;
using Npgsql;

namespace FMBot.Persistence.Repositories;

public static class CountryRepository
{
    public static async Task<List<TopArtist>> GetUserArtistsForCountry(int userId, string countryCode,
        NpgsqlConnection connection)
    {
        const string sql = "SELECT ua.name AS ArtistName, ua.playcount AS UserPlaycount " +
                           "FROM user_artists ua " +
                           "INNER JOIN artists a ON a.id = ua.artist_id " +
                           "WHERE ua.user_id = @userId AND ua.artist_id IS NOT NULL " +
                           "AND a.country_code = @countryCode " +
                           "ORDER BY ua.playcount DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<TopArtist>(sql, new { userId, countryCode = countryCode.ToUpperInvariant() })).ToList();
    }

    public static async Task<ICollection<WhoKnowsObjectWithUser>> GetGuildUsersForCountry(int guildId,
        string countryCode, IDictionary<int, FullGuildUser> guildUsers, NpgsqlConnection connection)
    {
        const string sql = "SELECT ua.user_id AS UserId, SUM(ua.playcount) AS Playcount " +
                           "FROM user_artists ua " +
                           "INNER JOIN guild_users gu ON gu.user_id = ua.user_id " +
                           "INNER JOIN artists a ON a.id = ua.artist_id " +
                           "WHERE gu.guild_id = @guildId AND gu.bot != true " +
                           "AND a.country_code = @countryCode " +
                           "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) " +
                           "AND NOT gu.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                           "GROUP BY ua.user_id " +
                           "ORDER BY Playcount DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var userPlaycounts = (await connection.QueryAsync<(int UserId, long Playcount)>(sql,
            new { guildId, countryCode = countryCode.ToUpperInvariant() })).ToList();

        var list = new List<WhoKnowsObjectWithUser>();
        foreach (var (userId, playcount) in userPlaycounts)
        {
            if (guildUsers != null && guildUsers.TryGetValue(userId, out var guildUser))
            {
                list.Add(new WhoKnowsObjectWithUser
                {
                    UserId = userId,
                    Playcount = (int)playcount,
                    DiscordName = guildUser.UserName,
                    LastFMUsername = guildUser.UserNameLastFM,
                    Name = guildUser.UserName,
                    LastUsed = guildUser.LastUsed,
                    LastMessage = guildUser.LastMessage,
                    Roles = guildUser.Roles
                });
            }
        }

        return list;
    }

    public static async Task<List<(string ArtistName, string CountryCode)>> GetCountryMappingsForArtists(
        string[] artistNames, NpgsqlConnection connection)
    {
        const string sql = "SELECT a.name AS ArtistName, a.country_code AS CountryCode " +
                           "FROM artists a " +
                           "WHERE a.name = ANY(@artistNames::citext[]) " +
                           "AND a.country_code IS NOT NULL";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return (await connection.QueryAsync<(string ArtistName, string CountryCode)>(sql,
            new { artistNames })).ToList();
    }
}
