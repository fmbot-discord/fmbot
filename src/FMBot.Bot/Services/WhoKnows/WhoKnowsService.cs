using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services.WhoKnows;

public class WhoKnowsService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

    public WhoKnowsService(IDbContextFactory<FMBotDbContext> contextFactory)
    {
        this._contextFactory = contextFactory;
    }

    public static async Task<IList<WhoKnowsObjectWithUser>> AddOrReplaceUserToIndexList(
        IList<WhoKnowsObjectWithUser> users, User contextUser, string name, NetCord.Gateway.Guild discordGuild = null,
        long? playcount = null)
    {
        if (!playcount.HasValue)
        {
            return users;
        }

        IGuildUser discordGuildUser = null;
        if (discordGuild != null)
        {
            discordGuildUser = await discordGuild.GetUserAsync(contextUser.DiscordUserId);
        }

        var guildUser = new GuildUser
        {
            UserName = discordGuildUser != null ? discordGuildUser.DisplayName : contextUser.UserNameLastFM,
            Roles = discordGuildUser?.RoleIds?.ToArray(),
            LastMessage = DateTime.UtcNow,
            User = contextUser
        };

        var existingUsers = users
            .Where(f => f.LastFMUsername.ToLower() == guildUser.User.UserNameLastFM.ToLower());
        if (existingUsers.Any())
        {
            users = users
                .Where(f => f.LastFMUsername.ToLower() != guildUser.User.UserNameLastFM.ToLower())
                .ToList();
        }

        var userPlaycount = int.Parse(playcount.GetValueOrDefault(0).ToString());
        users.Add(new WhoKnowsObjectWithUser
        {
            UserId = guildUser.User.UserId,
            Name = name,
            Playcount = userPlaycount,
            LastFMUsername = guildUser.User.UserNameLastFM,
            LastUsed = guildUser.User.LastUsed,
            LastMessage = guildUser.LastMessage,
            DiscordName = guildUser.UserName,
            PrivacyLevel = PrivacyLevel.Global,
            Roles = guildUser.Roles
        });

        return users.OrderByDescending(o => o.Playcount).ToList();
    }

    public static (FilterStats stats, IDictionary<int, FullGuildUser> filteredGuildUsers) FilterGuildUsers(
        IDictionary<int, FullGuildUser> guildUsers,
        Persistence.Domain.Models.Guild guild,
        int contextUserId,
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

        var (stats, filteredUsers) = FilterWhoKnowsObjects(wkObjects, guildUsers, guild, contextUserId, roles);

        var userIdsLeft = filteredUsers
            .Select(s => s.UserId)
            .ToHashSet();

        var guildUsersLeft = guildUsers
            .Where(w => userIdsLeft.Contains(w.Key))
            .ToDictionary(d => d.Key, d => d.Value);

        return (stats, guildUsersLeft);
    }

    public static (FilterStats stats, List<WhoKnowsObjectWithUser> filteredUsers) FilterWhoKnowsObjects(
        ICollection<WhoKnowsObjectWithUser> users,
        IDictionary<int, FullGuildUser> guildUsers,
        Persistence.Domain.Models.Guild guild,
        int contextUserId,
        List<ulong> roles = null)
    {
        var stats = new FilterStats
        {
            StartCount = users.Count,
            Roles = roles
        };

        if (users.Select(s => s.UserId).Contains(contextUserId))
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

        if (guild.UserActivityThresholdDays.HasValue)
        {
            var preFilterCount = users.Count;

            users = users.Where(w =>
                    w.LastMessage != null &&
                    w.LastMessage >= DateTime.UtcNow.AddDays(-guild.UserActivityThresholdDays.Value))
                .ToList();

            stats.GuildActivityThresholdFiltered = preFilterCount - users.Count;
        }

        if (guildUsers.Any(w => w.Value is { BlockedFromWhoKnows: true }))
        {
            var preFilterCount = users.Count;

            var usersToFilter = guildUsers
                .DistinctBy(d => d.Value.UserId)
                .Where(w => w.Value.BlockedFromWhoKnows)
                .Select(s => s.Value.UserId)
                .ToHashSet();

            var lastFmUsersToFilter = guildUsers
                .DistinctBy(d => d.Value.UserNameLastFM, comparer: StringComparer.OrdinalIgnoreCase)
                .Where(w => w.Value.BlockedFromWhoKnows)
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

        if (guild.AllowedRoles != null && guild.AllowedRoles.Any())
        {
            var preFilterCount = users.Count;

            users = users
                .Where(w => w.Roles != null && guild.AllowedRoles.Any(a => w.Roles.Contains(a)))
                .ToList();

            stats.AllowedRolesFiltered = preFilterCount - users.Count;
        }

        if (guild.BlockedRoles != null && guild.BlockedRoles.Any())
        {
            var preFilterCount = users.Count;

            users = users
                .Where(w => w.Roles != null && !guild.BlockedRoles.Any(a => w.Roles.Contains(a)))
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

        if (stats.RequesterFiltered.HasValue &&
            !users.Select(s => s.UserId).Contains(contextUserId))
        {
            stats.RequesterFiltered = true;
        }

        return (stats, users.ToList());
    }

    public async Task<IList<WhoKnowsObjectWithUser>> FilterGlobalUsersAsync(IEnumerable<WhoKnowsObjectWithUser> users,
        bool qualityFilterDisabled = false)
    {
        if (qualityFilterDisabled)
        {
            return users.ToList();
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var bottedUsers = await db.BottedUsers
            .AsQueryable()
            .Where(w => w.BanActive)
            .ToListAsync();

        var userNamesToFilter = bottedUsers
            .DistinctBy(d => d.UserNameLastFM, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.UserNameLastFM)
            .ToHashSet();

        var insensitiveUserNames = new HashSet<string>(
            userNamesToFilter, StringComparer.OrdinalIgnoreCase);

        var userDatesToFilter = bottedUsers
            .Where(w => w.LastFmRegistered != null)
            .DistinctBy(d => d.LastFmRegistered)
            .Select(s => s.LastFmRegistered)
            .ToHashSet();

        var existingFilterDate = DateTime.UtcNow.AddMonths(-3);
        var existingRepeatOffenderFilterDate = DateTime.UtcNow.AddMonths(-6);
        var filteredUsers = await db.GlobalFilteredUsers
            .AsQueryable()
            .Where(w => w.OccurrenceEnd.HasValue
                ? w.OccurrenceEnd.Value > (w.MonthLength == null || w.MonthLength == 3
                    ? existingFilterDate
                    : existingRepeatOffenderFilterDate)
                : w.Created > (w.MonthLength == null || w.MonthLength == 3
                    ? existingFilterDate
                    : existingRepeatOffenderFilterDate))
            .ToListAsync();

        foreach (var filteredUser in filteredUsers)
        {
            insensitiveUserNames.Add(filteredUser.UserNameLastFm);

            if (filteredUser.RegisteredLastFm.HasValue &&
                !userDatesToFilter.Contains(filteredUser.RegisteredLastFm.Value))
            {
                userDatesToFilter.Add(filteredUser.RegisteredLastFm);
            }
        }

        return users
            .Where(w =>
                !insensitiveUserNames.Contains(w.LastFMUsername)
                &&
                !userDatesToFilter.Contains(w.RegisteredLastFm))
            .ToList();
    }

    public static StringBuilder GetGlobalWhoKnowsFooter(StringBuilder footer, WhoKnowsSettings settings,
        ContextModel context)
    {
        if (settings.AdminView)
        {
            footer.AppendLine("Admin view enabled - not for public channels");
        }

        if (settings.QualityFilterDisabled)
        {
            footer.AppendLine("Globally botted and filtered users are visible");
        }

        if (context.ContextUser.PrivacyLevel != PrivacyLevel.Global)
        {
            footer.AppendLine($"You're currently not globally visible - use '{context.Prefix}privacy' to enable.");
        }

        if (settings.HidePrivateUsers)
        {
            footer.AppendLine("All private users are hidden from results");
        }

        return footer;
    }

    public static IList<WhoKnowsObjectWithUser> ShowGuildMembersInGlobalWhoKnowsAsync(
        IList<WhoKnowsObjectWithUser> users, IDictionary<int, FullGuildUser> guildUsers)
    {
        foreach (var user in users.Where(w => guildUsers.ContainsKey(w.UserId)))
        {
            user.PrivacyLevel = PrivacyLevel.Global;
            user.SameServer = true;
        }

        return users;
    }

    public static string WhoKnowsListToString(IList<WhoKnowsObjectWithUser> whoKnowsObjects, int requestedUserId,
        PrivacyLevel minPrivacyLevel, NumberFormat numberFormat, CrownModel crownModel = null,
        bool hidePrivateUsers = false)
    {
        var reply = new StringBuilder();

        var whoKnowsCount = whoKnowsObjects.Count;
        if (whoKnowsCount > 14)
        {
            whoKnowsCount = 14;
        }

        var usersToShow = whoKnowsObjects
            .OrderByDescending(o => o.Playcount)
            .ToList();

        var spacer = crownModel?.Crown == null ? "" : " ";

        var indexNumber = 1;
        var timesNameAdded = 0;
        var requestedUserAdded = false;
        var addedUsers = new HashSet<int>();
        var addedLastFmUsers = new HashSet<string>();

        // Note: You might not be able to see them, but this code contains specific spacers
        // https://www.compart.com/en/unicode/category/Zs
        for (var index = 0; timesNameAdded < whoKnowsCount; index++)
        {
            if (index >= usersToShow.Count)
            {
                break;
            }

            var user = usersToShow[index];

            if (addedUsers.Any(a => a.Equals(user.UserId)) ||
                addedLastFmUsers.Any(a => a.Equals(user.LastFMUsername)))
            {
                continue;
            }

            string nameWithLink;
            if (minPrivacyLevel == PrivacyLevel.Global && user.PrivacyLevel != PrivacyLevel.Global)
            {
                nameWithLink = PrivateName();
                if (hidePrivateUsers)
                {
                    indexNumber += 1;
                    continue;
                }
            }
            else
            {
                nameWithLink = NameWithLink(user);
                if (user.UserId == requestedUserId)
                {
                    nameWithLink = $"**{nameWithLink}";
                }
            }

            var playString = StringExtensions.GetPlaysString(user.Playcount);

            var positionCounter = $"{spacer}{indexNumber}.";
            positionCounter = user.UserId == requestedUserId
                ? user.SameServer == true ? $"__**{positionCounter}** __" : $"**{positionCounter}** "
                : user.SameServer == true
                    ? $"__{positionCounter}__ "
                    : $"{positionCounter} ";

            if (crownModel?.Crown != null && crownModel.Crown.UserId == user.UserId)
            {
                positionCounter = "👑 ";
            }

            var afterPositionSpacer = index + 1 == 10 ? "" : (index + 1 == 7 || index + 1 == 9) ? " " : " ";

            reply.Append($"{positionCounter}{afterPositionSpacer}{nameWithLink}");

            if (user.UserId == requestedUserId)
            {
                reply.Append($" - {user.Playcount.Format(numberFormat)} {playString}**\n");
            }
            else
            {
                reply.Append($" - **{user.Playcount.Format(numberFormat)}** {playString}\n");
            }

            indexNumber += 1;
            timesNameAdded += 1;

            addedUsers.Add(user.UserId);
            addedLastFmUsers.Add(user.LastFMUsername);

            if (user.UserId == requestedUserId)
            {
                requestedUserAdded = true;
            }
        }

        if (!requestedUserAdded)
        {
            var requestedUser = whoKnowsObjects.FirstOrDefault(f => f.UserId == requestedUserId);
            if (requestedUser != null)
            {
                var nameWithLink = NameWithLink(requestedUser);
                var playString = StringExtensions.GetPlaysString(requestedUser.Playcount);

                reply.Append($"**{spacer}{whoKnowsObjects.IndexOf(requestedUser) + 1}.  {nameWithLink} ");

                reply.Append($" - {requestedUser.Playcount} {playString}**\n");
            }
        }

        if (crownModel?.CrownResult != null)
        {
            reply.Append($"\n{crownModel.CrownResult}");
        }

        return reply.ToString();
    }

    public static string NameWithLink(WhoKnowsObjectWithUser user)
    {
        var discordName = user.DiscordName != null
            ? StringExtensions.Sanitize(user.DiscordName
                .Replace("[", "")
                .Replace("]", "")
                .Replace(" ", "")
                .Replace("ٴ", ""))
            : null;

        if (string.IsNullOrWhiteSpace(discordName))
        {
            discordName = user.LastFMUsername;
        }

        var nameWithLink = $"[\u2066{discordName}\u2069]({LastfmUrlExtensions.GetUserUrl(user.LastFMUsername)})";
        return nameWithLink;
    }

    private static string PrivateName()
    {
        return "Private user";
    }
}
