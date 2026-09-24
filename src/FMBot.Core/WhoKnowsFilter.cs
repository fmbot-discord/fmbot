using System;
using System.Collections.Generic;
using System.Linq;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Core;

public static class WhoKnowsFilter
{
    public static (FilterStats Stats, IDictionary<int, FullGuildUser> FilteredGuildUsers) FilterGuildUsers(
        IDictionary<int, FullGuildUser> guildUsers,
        Guild guild,
        bool premiumGuild,
        int? contextUserId = null,
        List<ulong> roles = null)
    {
        var wkObjects = guildUsers.Select(s => new WhoKnowsObjectWithUser
        {
            DiscordName = s.Value.UserName,
            LastFMUsername = s.Value.UserNameLastFM,
            LastMessage = s.Value.LastMessage,
            LastUsed = s.Value.LastUsed,
            Name = s.Value.UserName,
            Roles = s.Value.Roles,
            UserId = s.Key
        }).ToList();

        var (stats, filteredUsers) = FilterWhoKnowsObjects(wkObjects, guildUsers, guild, premiumGuild, contextUserId, roles);

        var userIdsLeft = filteredUsers
            .Select(s => s.UserId)
            .ToHashSet();

        var guildUsersLeft = guildUsers
            .Where(w => userIdsLeft.Contains(w.Key))
            .ToDictionary(d => d.Key, d => d.Value);

        return (stats, guildUsersLeft);
    }

    public static (FilterStats Stats, List<WhoKnowsObjectWithUser> FilteredUsers) FilterWhoKnowsObjects(
        ICollection<WhoKnowsObjectWithUser> users,
        IDictionary<int, FullGuildUser> guildUsers,
        Guild guild,
        bool premiumGuild,
        int? contextUserId,
        List<ulong> roles = null,
        bool filterDisabled = false)
    {
        var stats = new FilterStats
        {
            StartCount = users.Count,
            Roles = roles
        };

        if (filterDisabled)
        {
            if (guildUsers.Any(w => w.Value is { BlockedFromWhoKnows: true } or { SelfBlockFromWhoKnows: true }))
            {
                var usersToFilter = guildUsers
                    .Where(w => w.Value.BlockedFromWhoKnows || w.Value.SelfBlockFromWhoKnows)
                    .Select(s => s.Value.UserId)
                    .ToHashSet();

                var lastFmUsersToFilter = guildUsers
                    .Where(w => w.Value.BlockedFromWhoKnows || w.Value.SelfBlockFromWhoKnows)
                    .Select(s => s.Value.UserNameLastFM)
                    .ToHashSet();

                var insensitiveLastFmUsersToFilter = new HashSet<string>(
                    lastFmUsersToFilter, StringComparer.OrdinalIgnoreCase);

                users = users
                    .Where(w => !usersToFilter.Contains(w.UserId) &&
                                !insensitiveLastFmUsersToFilter.Contains(w.LastFMUsername))
                    .ToList();
            }

            stats.EndCount = users.Count;
            return (stats, users.ToList());
        }

        if (contextUserId.HasValue && users.Select(s => s.UserId).Contains(contextUserId.Value))
        {
            stats.RequesterFiltered = false;
        }

        if (guild.ActivityThresholdDays.HasValue)
        {
            var preFilterCount = users.Count;

            users = users.Where(w =>
                    w.LastUsed != null &&
                    w.LastUsed >= DateTime.UtcNow.AddDays(-guild.ActivityThresholdDays.Value))
                .ToList();

            stats.ActivityThresholdFiltered = preFilterCount - users.Count;
        }

        if (premiumGuild && guild.UserActivityThresholdDays.HasValue)
        {
            var preFilterCount = users.Count;

            users = users.Where(w =>
                    w.LastMessage != null &&
                    w.LastMessage >= DateTime.UtcNow.AddDays(-guild.UserActivityThresholdDays.Value))
                .ToList();

            stats.GuildActivityThresholdFiltered = preFilterCount - users.Count;
        }

        if (guildUsers.Any(w => w.Value is { BlockedFromWhoKnows: true } or { SelfBlockFromWhoKnows: true }))
        {
            var preFilterCount = users.Count;

            var usersToFilter = guildUsers
                .DistinctBy(d => d.Value.UserId)
                .Where(w => w.Value.BlockedFromWhoKnows || w.Value.SelfBlockFromWhoKnows)
                .Select(s => s.Value.UserId)
                .ToHashSet();

            var lastFmUsersToFilter = guildUsers
                .Where(w => w.Value.BlockedFromWhoKnows || w.Value.SelfBlockFromWhoKnows)
                .Select(s => s.Value.UserNameLastFM)
                .ToHashSet();

            var insensitiveLastFmUsersToFilter = new HashSet<string>(
                lastFmUsersToFilter, StringComparer.OrdinalIgnoreCase);

            users = users
                .Where(w => !usersToFilter.Contains(w.UserId) &&
                            !insensitiveLastFmUsersToFilter.Contains(w.LastFMUsername))
                .ToList();

            stats.BlockedFiltered = preFilterCount - users.Count;
        }

        if (premiumGuild && guild.AllowedRoles != null && guild.AllowedRoles.Any())
        {
            var preFilterCount = users.Count;

            users = users
                .Where(w => w.Roles != null && guild.AllowedRoles.Any(a => w.Roles.Contains(a)))
                .ToList();

            stats.AllowedRolesFiltered = preFilterCount - users.Count;
        }

        if (premiumGuild && guild.BlockedRoles != null && guild.BlockedRoles.Any())
        {
            var preFilterCount = users.Count;

            users = users
                .Where(w => w.Roles == null || !guild.BlockedRoles.Any(a => w.Roles.Contains(a)))
                .ToList();

            stats.BlockedRolesFiltered = preFilterCount - users.Count;
        }

        if (roles != null && roles.Any())
        {
            var preFilterCount = users.Count;

            users = users
                .Where(w => w.Roles != null && roles.Any(a => w.Roles.Contains(a)))
                .ToList();

            stats.ManualRoleFilter = preFilterCount - users.Count;
        }

        stats.EndCount = users.Count;

        if (contextUserId.HasValue &&
            stats.RequesterFiltered.HasValue &&
            !users.Select(s => s.UserId).Contains(contextUserId.Value))
        {
            stats.RequesterFiltered = true;
        }

        return (stats, users.ToList());
    }

    public static IList<WhoKnowsObjectWithUser> ShowGuildMembersInGlobalWhoKnows(
        IList<WhoKnowsObjectWithUser> users, IDictionary<int, FullGuildUser> guildUsers)
    {
        foreach (var user in users.Where(w => guildUsers.ContainsKey(w.UserId)))
        {
            user.PrivacyLevel = PrivacyLevel.Global;
            user.SameServer = true;
        }

        return users;
    }
}
