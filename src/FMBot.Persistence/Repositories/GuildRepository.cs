using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Models;
using Npgsql;

namespace FMBot.Persistence.Repositories;

public static class GuildRepository
{
    public static async Task<IDictionary<int, FullGuildUser>> GetGuildUsers(ulong discordGuildId,
        NpgsqlConnection connection)
    {
        const string sql = "SELECT gu.user_id, " +
                           "gu.user_name, " +
                           "gu.bot, " +
                           "gu.last_message, " +
                           "gu.roles AS dto_roles, " +
                           "u.user_name_last_fm, " +
                           "u.discord_user_id, " +
                           "u.last_used, " +
                           "COALESCE(gbu.blocked_from_crowns, false) as blocked_from_crowns, " +
                           "COALESCE(gbu.blocked_from_who_knows, false) as blocked_from_who_knows, " +
                           "COALESCE(gbu.self_block_from_who_knows, false) as self_block_from_who_knows " +
                           "FROM public.guild_users AS gu " +
                           "LEFT JOIN users AS u ON gu.user_id = u.user_id " +
                           "LEFT JOIN guilds AS g ON gu.guild_id = g.guild_id " +
                           "LEFT OUTER JOIN guild_blocked_users AS gbu ON gu.user_id = gbu.user_id AND gbu.guild_id = gu.guild_id " +
                           "WHERE g.discord_guild_id = @discordGuildId";

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var result = (await connection.QueryAsync<FullGuildUser>(sql, new
        {
            discordGuildId = Convert.ToInt64(discordGuildId)
        })).ToList();

        foreach (var row in result.Where(w => w.DtoRoles != null))
        {
            row.Roles = row.DtoRoles.Select(s => (ulong)s).ToArray();
        }

        return result.ToDictionary(d => d.UserId, d => d);
    }
}
