using System.Collections.Generic;
using System.Linq;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Core;

public enum CrownEligibility
{
    Eligible,
    Crownblocked,
    MissingCrownRole
}

public static class CrownRules
{
    public static CrownEligibility GetCrownEligibility(Guild guild, bool premiumGuild,
        IDictionary<int, FullGuildUser> guildUsers, int userId, ulong[] roles = null)
    {
        if (guildUsers.TryGetValue(userId, out var guildUser) && guildUser.BlockedFromCrowns)
        {
            return CrownEligibility.Crownblocked;
        }

        if (CrownRolesActive(guild, premiumGuild) && !HasCrownRole(guild.CrownRoles.ToHashSet(), guildUsers, userId, roles))
        {
            return CrownEligibility.MissingCrownRole;
        }

        return CrownEligibility.Eligible;
    }

    public static bool CrownRolesActive(Guild guild, bool premiumGuild)
    {
        return guild.CrownRoles is { Length: > 0 } && premiumGuild;
    }

    public static bool HasCrownRole(HashSet<ulong> crownRoles, IDictionary<int, FullGuildUser> guildUsers, int userId,
        ulong[] roles)
    {
        var userRoles = roles ?? (guildUsers.TryGetValue(userId, out var guildUser) ? guildUser.Roles : null);
        return userRoles != null && userRoles.Any(crownRoles.Contains);
    }

    public static bool CrownHolderNoLongerAllowed(IDictionary<int, FullGuildUser> guildUsers, int crownHolderUserId)
    {
        if (guildUsers.Count == 0)
        {
            return false;
        }

        return !guildUsers.TryGetValue(crownHolderUserId, out var crownHolder) || crownHolder.BlockedFromCrowns;
    }
}
